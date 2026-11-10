using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AetherOperations;

namespace SystemCockpit.Services;

internal enum ProcessActionState { Exited, AlreadyExited, CloseRequested, Reconfirm, Blocked, AccessDenied, Failed, Timeout, Cancelled, Partial }
internal sealed record ProcessActionResult(ProcessIdentity Identity, ProcessActionState State, string Message, int? NativeCode = null);
internal sealed record ProcessTarget(ProcessIdentity Identity, string Name, string Path, string User, int? Session, string Protection);
internal sealed record ProcessActionPlan(ProcessTarget Root, IReadOnlyList<ProcessTarget> Targets, bool Tree, DateTimeOffset CreatedAt);
internal interface IProcessTargetHandle : IDisposable
{
    ProcessTarget Target { get; }
    bool Exited { get; }
    void Terminate();
}
internal interface IProcessActionSource
{
    Task<IReadOnlyList<ProcessMetric>> ReadAsync(CancellationToken token);
    Task<IReadOnlySet<ProcessIdentity>> ProtectedAsync(CancellationToken token);
    IProcessTargetHandle Open(ProcessIdentity identity, bool terminate);
}

internal sealed class NativeProcessActionSource : IProcessActionSource
{
    public Task<IReadOnlyList<ProcessMetric>> ReadAsync(CancellationToken token) => Task.Run<IReadOnlyList<ProcessMetric>>(() =>
    {
        var parents = ProcessNative.Parents();
        var rows = new List<ProcessMetric>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    rows.Add(new(process.ProcessName, process.Id, null, null, null, null, null, null, "Identitätsprobe",
                        process.StartTime.ToUniversalTime().Ticks) { ParentPid = parents.GetValueOrDefault(process.Id) });
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    Trace.TraceWarning("Prozessbaum PID {0}: {1}", process.Id, ex);
                    rows.Add(new("Nicht lesbar", process.Id, null, null, null, null, null, null, ex.Message)
                        { ParentPid = parents.GetValueOrDefault(process.Id) });
                }
            }
        }
        return rows;
    }, token);

    public async Task<IReadOnlySet<ProcessIdentity>> ProtectedAsync(CancellationToken token)
    {
        var response = await AdminShell.RunAsync("""
            $ErrorActionPreference='Stop'
            $rows=@(Get-CimInstance Win32_Service -OperationTimeoutSec 5 | Where-Object {
                $_.Name -in @('AetherClient','AetherManager','AETHER Managed Client','AETHER Management Server') -or
                $_.DisplayName -in @('AETHER Managed Client','AETHER Management Server')
            } | Select-Object ProcessId)
            ConvertTo-Json -InputObject $rows -Compress
            """, TimeSpan.FromSeconds(12), token).ConfigureAwait(false);
        if (response.ExitCode != 0 || response.OutputTruncated)
            throw new InvalidOperationException("Eigene Dienstkomponenten konnten nicht sicher geprüft werden; Aktion gesperrt.");
        using var json = System.Text.Json.JsonDocument.Parse(response.Output);
        if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidOperationException("Ungültige Eigenkomponentenliste; Aktion gesperrt.");
        var result = new HashSet<ProcessIdentity>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var pid = item.GetProperty("ProcessId").GetInt32();
            if (pid == 0) continue;
            using var process = Process.GetProcessById(pid);
            result.Add(new(pid, process.StartTime.ToUniversalTime().Ticks));
        }
        return result;
    }

    public IProcessTargetHandle Open(ProcessIdentity identity, bool terminate) => new NativeTarget(identity, terminate);

    private sealed class NativeTarget : IProcessTargetHandle
    {
        private readonly ProcessHandle _handle;
        public ProcessTarget Target { get; }
        public bool Exited => ProcessNative.Exited(_handle);
        public NativeTarget(ProcessIdentity identity, bool terminate)
        {
            _handle = ProcessNative.Open(identity, terminate);
            try
            {
                var path = ProcessNative.Path(_handle);
                var protection = ProcessNative.Protection(_handle);
                var ownDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
                if (identity.Pid == Environment.ProcessId) protection = "AETHER selbst ist geschützt.";
                if (Path.GetDirectoryName(path)?.Equals(ownDirectory, StringComparison.OrdinalIgnoreCase) == true &&
                    Path.GetFileNameWithoutExtension(path) is "AetherClient.Service" or "AetherManager.Server" or "AetherScreensaver" or "SystemCockpit")
                    protection = "Notwendige eigene AETHER-Komponente ist geschützt.";
                var user = ProcessNative.Token(_handle);
                using var process = Process.GetProcessById(identity.Pid);
                Target = new(identity, Path.GetFileNameWithoutExtension(path), path, user.User, process.SessionId, protection);
            }
            catch { _handle.Dispose(); throw; }
        }
        public void Terminate()
        {
            if (!ProcessNative.TerminateProcess(_handle, 1)) throw ProcessNative.Error();
        }
        public void Dispose() => _handle.Dispose();
    }
}

internal sealed class ProcessActions(IProcessActionSource source, Action<string, object> journal)
{
    internal static ProcessActions Create() => new(new NativeProcessActionSource(), ActionJournal.Append);
    internal static IReadOnlyList<ProcessMetric> Tree(ProcessIdentity root, IReadOnlyList<ProcessMetric> rows)
    {
        var byPid = rows.GroupBy(r => r.Id).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        if (!byPid.TryGetValue(root.Pid, out var first) || first.Identity != root)
            throw new InvalidOperationException("Ziel wurde beendet oder PID wiederverwendet.");
        var result = new List<ProcessMetric> { first };
        var seen = new HashSet<int> { first.Id };
        for (var index = 0; index < result.Count; index++)
        {
            var parent = result[index];
            foreach (var child in rows.Where(r => r.ParentPid == parent.Id && !seen.Contains(r.Id)))
            {
                if (!child.Identity.IsKnown) throw new InvalidOperationException("Kindidentität nicht prüfbar; Baumaktion gesperrt.");
                if (child.StartTicks <= parent.StartTicks) continue;
                if (seen.Add(child.Id)) result.Add(child);
            }
        }
        return result;
    }

    internal async Task<ProcessActionPlan> PrepareAsync(ProcessIdentity identity, bool tree, CancellationToken token)
    {
        var rows = await source.ReadAsync(token).ConfigureAwait(false);
        var protectedIds = await source.ProtectedAsync(token).ConfigureAwait(false);
        var own = rows.SingleOrDefault(r => r.Id == Environment.ProcessId);
        if (own is { Identity.IsKnown: true })
            protectedIds = protectedIds.Concat(Tree(own.Identity, rows).Select(r => r.Identity)).ToHashSet();
        var targets = tree ? Tree(identity, rows) : rows.Where(r => r.Identity == identity).ToArray();
        if (targets.Count == 0) throw new InvalidOperationException("Prozess wurde bereits beendet.");
        var details = new List<ProcessTarget>();
        foreach (var row in targets)
        {
            token.ThrowIfCancellationRequested();
            using var handle = source.Open(row.Identity, false);
            if (handle.Exited) throw new InvalidOperationException("Prozess wurde bereits beendet.");
            var target = handle.Target;
            if (protectedIds.Contains(target.Identity)) target = target with { Protection = "Eigene aktive AETHER-Komponente." };
            if (!string.IsNullOrEmpty(target.Protection)) throw new InvalidOperationException(target.Protection);
            details.Add(target);
        }
        return new(details.Single(t => t.Identity == identity), details, tree, DateTimeOffset.UtcNow);
    }

    internal async Task<IReadOnlyList<ProcessActionResult>> ExecuteAsync(ProcessActionPlan plan, CancellationToken token)
    {
        var results = new List<ProcessActionResult>();
        var handles = new List<IProcessTargetHandle>();
        var requested = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var current = await PrepareAsync(plan.Root.Identity, plan.Tree, token).ConfigureAwait(false);
            if (!current.Targets.Select(t => t.Identity).ToHashSet().SetEquals(plan.Targets.Select(t => t.Identity)))
                return [new(plan.Root.Identity, ProcessActionState.Reconfirm, "Prozessbaum verändert. Erneute Bestätigung erforderlich.")];
            foreach (var target in plan.Targets)
            {
                var handle = source.Open(target.Identity, true);
                handles.Add(handle);
                if (handle.Exited) return [new(target.Identity, ProcessActionState.AlreadyExited, "Prozess bereits beendet; keine Aktion ausgeführt.")];
                if (!string.IsNullOrEmpty(handle.Target.Protection))
                    return [new(target.Identity, ProcessActionState.Blocked, handle.Target.Protection)];
            }
            journal("process-terminate-intent", new { targets = plan.Targets.Select(t => t.Identity).ToArray(), plan.Tree });
            foreach (var handle in handles.AsEnumerable().Reverse())
            {
                token.ThrowIfCancellationRequested();
                if (handle.Exited) { results.Add(new(handle.Target.Identity, ProcessActionState.AlreadyExited, "Bereits beendet.")); continue; }
                handle.Terminate();
                requested = true;
                var watch = Stopwatch.StartNew();
                while (!handle.Exited && watch.Elapsed < TimeSpan.FromSeconds(5))
                    await Task.Delay(40, token).ConfigureAwait(false);
                results.Add(new(handle.Target.Identity, handle.Exited ? ProcessActionState.Exited : ProcessActionState.Timeout,
                    handle.Exited ? "Prozessende an derselben Handleinstanz bestätigt." : "Beendigung angefordert, Ende nicht innerhalb 5 s bestätigt."));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            results.Add(new(plan.Root.Identity, requested ? ProcessActionState.Partial : ProcessActionState.Cancelled,
                requested ? "Abgebrochen; bereits angeforderte Beendigungen sind nicht rückgängig." : "Abgebrochen; kein Eingriff angefordert."));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            Trace.TraceError("Prozessaktion: {0}", ex);
            var code = (ex as Win32Exception)?.NativeErrorCode;
            results.Add(new(plan.Root.Identity, requested ? ProcessActionState.Partial : code == 5 ? ProcessActionState.AccessDenied : ProcessActionState.Failed,
                code == 5 ? "Zugriff verweigert. Windows-Schutz und Berechtigungen bleiben unverändert." : ex.Message, code));
        }
        finally { foreach (var handle in handles) handle.Dispose(); }
        journal("process-terminate-result", new { results });
        return results;
    }
}
