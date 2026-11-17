using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using SystemCockpit.Services;

internal static class GuardianReversibleProof
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static int Run(string output) => Task.Run(() => RunAsync(output)).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string output)
    {
        if (!Path.IsPathFullyQualified(output) || File.Exists(output)) throw new ArgumentException("Neue absolute Ergebnisdatei erforderlich.");
        var id = "GdnProof-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), id);
        Directory.CreateDirectory(root);
        var taskId = "\\" + id;
        var steps = new List<object>();
        var context = IntelligenceSources.GuardianContext();
        var vault = new GuardianVault(Path.Combine(root, "vault"));
        var adapter = new GuardianActions(Path.Combine(root, "startup-backup"));
        var core = new GuardianCore(vault, () => context);
        var command = "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") + "\" /c exit 0";
        var startupCreated = false; var taskAttempted = false; var taskCreated = false; var cleanup = true;
        string? failure = null;
        try
        {
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.CreateSubKey(RunKey))
            {
                if (key.GetValue(id) is not null) throw new InvalidOperationException("Eigene GUID-Testidentität unerwartet bereits vorhanden.");
                startupCreated = true;
                key.SetValue(id, command, RegistryValueKind.String);
            }
            await Roundtrip("startup", "HKCU\\" + id);
            var script = """
                $ErrorActionPreference='Stop'
                $scheduler=New-Object -ComObject 'Schedule.Service'; $scheduler.Connect()
                $folder=$scheduler.GetFolder('\')
                $definition=$scheduler.NewTask(0)
                $definition.RegistrationInfo.Description=MARKER
                $definition.Settings.Enabled=$true
                $definition.Settings.AllowDemandStart=$false
                $definition.Principal.UserId=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
                $definition.Principal.LogonType=3
                $action=$definition.Actions.Create(0); $action.Path=$env:WINDIR+'\System32\cmd.exe'; $action.Arguments='/c exit 0'
                [void]$folder.RegisterTaskDefinition(TASKNAME,$definition,2,$definition.Principal.UserId,$null,3)
                'CREATED'
                """.Replace("MARKER", AdminManagement.Quote(id), StringComparison.Ordinal)
                .Replace("TASKNAME", AdminManagement.Quote(id), StringComparison.Ordinal);
            taskAttempted = true;
            var created = await AdminShell.RunAsync(script, TimeSpan.FromSeconds(25), default);
            if (created.ExitCode != 0 || created.OutputTruncated) throw new InvalidOperationException("Eigene Test-Task nicht angelegt: " + created.Error);
            taskCreated = true;
            await Roundtrip("task", taskId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or JsonException)
        {
            failure = ex.Message;
            steps.Add(new { Step = "Failure", Error = failure, NativeCode = (ex as Win32Exception)?.NativeErrorCode });
        }
        finally
        {
            if (startupCreated)
            {
                try
                {
                    using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                    using var key = hive.OpenSubKey(RunKey, true) ?? throw new IOException("Eigener Testautostart nicht zur Bereinigung lesbar.");
                    var current = key.GetValue(id);
                    if (current is not null && (key.GetValueKind(id) != RegistryValueKind.String || current is not string text || text != command))
                        throw new InvalidOperationException("Eigener Testautostart extern geändert; nicht gelöscht.");
                    key.DeleteValue(id, false);
                    var verified = key.GetValue(id) is null;
                    cleanup &= verified;
                    steps.Add(new { Step = "StartupCleanup", Verified = verified });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    cleanup = false; failure ??= "StartupCleanup: " + ex.Message;
                    steps.Add(new { Step = "StartupCleanup", Verified = false, Error = ex.Message });
                }
            }
            if (taskAttempted)
            {
                try
                {
                    var script = "$ErrorActionPreference='Stop'; $s=New-Object -ComObject 'Schedule.Service'; $s.Connect(); $f=$s.GetFolder('\\'); " +
                        $"$t=$null; try{{$t=$f.GetTask({AdminManagement.Quote(id)})}}catch{{if($_.Exception.HResult -ne -2147024894){{throw}}}}; " +
                        $"if($null -ne $t){{if($t.Definition.RegistrationInfo.Description -cne {AdminManagement.Quote(id)}){{throw 'Eigentumsmarker geändert; keine Löschung'}}; " +
                        $"$f.DeleteTask({AdminManagement.Quote(id)},0)}}; if(@($f.GetTasks(1) | Where-Object Name -eq {AdminManagement.Quote(id)}).Count){{throw 'Eigene Task noch vorhanden'}}; 'CLEAN'";
                    var removed = await AdminShell.RunAsync(script, TimeSpan.FromSeconds(25), default);
                    var verified = removed.ExitCode == 0 && !removed.OutputTruncated && removed.Output.Trim() == "CLEAN";
                    cleanup &= verified;
                    steps.Add(new { Step = "TaskCleanup", Verified = verified, Error = removed.Error });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
                {
                    cleanup = false; failure ??= "TaskCleanup: " + ex.Message;
                    steps.Add(new { Step = "TaskCleanup", Verified = false, Error = ex.Message });
                }
            }
            if (cleanup)
            {
                try { Directory.Delete(root, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    cleanup = false; failure ??= "FixtureCleanup: " + ex.Message;
                    steps.Add(new { Step = "FixtureCleanup", Verified = false, Error = ex.Message });
                }
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { Scope = "Eigener Registry64-Testautostart und eigene Task ohne Trigger / Ausführung gesperrt",
                Steps = steps, Passed = failure is null && cleanup && taskCreated, Error = failure,
                CleanupVerified = cleanup, RetainedDirectory = cleanup ? null : root },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        return failure is null && cleanup && taskCreated ? 0 : 2;

        async Task Roundtrip(string kind, string target)
        {
            var before = await adapter.InspectAsync(kind, target, default);
            var changed = await core.ExecuteAsync(core.Plan("Own reversible " + kind, GuardianActions.MakePlan(before, "disable")), adapter, true, default);
            if (changed.State != ChangeState.Completed || changed.After?.State != "Disabled")
                throw new InvalidOperationException(kind + " disable verification failed.");
            var rollback = await core.ExecuteAsync(core.Rollback(changed), adapter, true, default);
            var after = await adapter.InspectAsync(kind, target, default);
            if (rollback.State != ChangeState.RolledBack || !GuardianCore.Matches(before, after))
                throw new InvalidOperationException(kind + " rollback verification failed.");
            steps.Add(new { Step = kind + "Roundtrip", Before = before.State, Applied = changed.State.ToString(),
                VerifiedAfter = changed.After.State, Rollback = rollback.State.ToString(), Restored = after.State,
                SameIdentity = GuardianCore.SameIdentity(before, after), Transaction = changed.Id, RollbackTransaction = rollback.Id });
        }
    }
}
