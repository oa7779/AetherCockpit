using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SystemCockpit.Services;

internal static class GuardianContinuationTests
{
    private static int _checks;
    private static readonly GuardianContext Context = new("continuation-fixture-host", "continuation-fixture-user");
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException("Guardian continuation: " + message); }
    private sealed class StateAction : IGuardianAction
    {
        internal GuardianTarget Target = new("fixture", "controlled", EvidenceFile.Hash("controlled-identity"), "Enabled", new Dictionary<string, string>());
        internal int Calls;
        internal bool Unavailable;
        internal Action? OnExecute;
        public Task<GuardianTarget> ReadAsync(GuardianTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Unavailable) throw new IOException("Synthetic provider failure");
            return Task.FromResult(Target);
        }
        public Task<string> ExecuteAsync(GuardianPlan plan, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++; Target = Target with { State = plan.ExpectedState }; OnExecute?.Invoke();
            return Task.FromResult("Synthetic fixture only");
        }
    }
    private static GuardianPlan Plan(StateAction action) => new("disable", action.Target, "Disabled", "Own fixture", "None",
        "Fixture state", "No external target", "Manual restart boundary; no automatic restart", "Exact own fixture state journalled",
        "Read identity and state", RollbackCapability.Available, "Restore exact prior state");
    private static IntelligenceSnapshot Snapshot() => new(1, Guid.NewGuid(), "Synthetic continuation", DateTimeOffset.UtcNow, [], [], [], Context.Host);
    internal static int Run() => Task.Run(RunAsync).GetAwaiter().GetResult();
    private static async Task<int> RunAsync()
    {
        _checks = 0;
        var root = Path.Combine(Path.GetTempPath(), "GdnContinuation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await ConfigurationAsync(root);
            await MaintenanceAsync(root);
            await JournalAsync(root);
            await ProcessJournalAsync(root);
            await TraceAsync(root);
            Evidence();
            return _checks;
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task ConfigurationAsync(string root)
    {
        var initial = GuardianConfiguration.Capture(new CockpitSettings { ManagerServerUrl = "https://example.invalid", SmartctlPath = "C:\\not-a-secret-fixture.exe" },
            [], new(1, false, new()), TaskCenterPreferences.Empty);
        var desired = GuardianConfiguration.Capture(new CockpitSettings { CpuWarningC = 82 }, [], new(1, false, new()), TaskCenterPreferences.Empty);
        var backup = GuardianConfiguration.Backup(desired, Context);
        backup.Validate();
        Check(!backup.Fields["payload"].Contains("ManagerServerUrl", StringComparison.Ordinal) &&
            !backup.Fields["payload"].Contains("SmartctlPath", StringComparison.Ordinal), "configuration uses an allowlist, not incidental paths/connections");
        var merged = GuardianConfiguration.Merge(new CockpitSettings { ManagerServerUrl = "https://example.invalid" }, desired);
        Check(merged.CpuWarningC == 82 && merged.ManagerServerUrl == "https://example.invalid", "configuration restore preserves excluded settings");
        var invalid = desired with { Settings = new Dictionary<string, JsonElement>(desired.Settings) { ["Token"] = JsonSerializer.SerializeToElement("synthetic-secret") } };
        try { GuardianConfiguration.Validate(invalid); Check(false, "unknown configuration property accepted"); }
        catch (JsonException) { Check(true, "secret-like unknown properties are rejected"); }
        try { (backup with { Fields = new Dictionary<string, string>(backup.Fields) { ["payload"] = "{}" } }).Validate();
            Check(false, "configuration readiness accepted an invalid payload"); }
        catch (JsonException) { Check(true, "configuration artifact validates its actual allowlisted recovery payload"); }
        var path = Path.Combine(root, "own-config.json");
        await EvidenceFile.WriteAsync(path, EvidenceFile.Encode("own-configuration-fixture", initial), default);
        GuardianConfigurationData Read() => EvidenceFile.Decode<GuardianConfigurationData>("own-configuration-fixture", File.ReadAllText(path));
        var adapter = new GuardianConfigurationAction(Read, (data, token) => EvidenceFile.WriteAsync(path,
            EvidenceFile.Encode("own-configuration-fixture", data), token));
        var vault = new GuardianVault(Path.Combine(root, "config-vault"));
        var core = new GuardianCore(vault, () => Context);
        var target = adapter.Target(Context, GuardianConfiguration.Serialize(desired));
        var tx = await core.ExecuteAsync(core.Plan("Restore own configuration", GuardianConfigurationAction.Plan(target)), adapter, true, default);
        Check(tx.State == ChangeState.Completed && GuardianConfiguration.Serialize(Read()) == GuardianConfiguration.Serialize(desired),
            "own configuration file really changes and is reread");
        var rollback = await core.ExecuteAsync(core.Rollback(tx), adapter, true, default);
        Check(rollback.State == ChangeState.RolledBack && GuardianConfiguration.Serialize(Read()) == GuardianConfiguration.Serialize(initial),
            "configuration rollback rereads exact prior allowed content");
        var scope = GuardianVault.Readiness([backup], Context);
        foreach (var area in new[] { "AETHER-Einstellungen", "Watch-Regeln", "System-Intelligence-Einstellungen", "Gespeicherte Ansichten",
            "Baselines", "Snapshots", "Diagnoseaufnahmen", "Guardian-Transaktionen", "Software", "Treiberbestand", "Tasks", "Autostarts",
            "Dienstkonfigurationen", "Rescue Packages", "Recovery Points", "Persönliche Dateien" })
            Check(scope.Any(r => r.Area == area && r.Evidence.Length > 0 && r.Limitation.Length > 0), "explicit readiness category " + area);
        Check(scope.Single(r => r.Area == "Persönliche Dateien").State == ReadinessKind.NotProtected, "personal files remain unprotected");
    }
    private static async Task ProcessJournalAsync(string root)
    {
        var row = ProcessCommandTests.Row(1001, 100);
        var source = new ProcessCommandTests.FakeSource { Rows = [row] };
        var vault = new GuardianVault(Path.Combine(root, "process-vault"));
        var core = new GuardianCore(vault, () => Context);
        var actions = new ProcessActions(source, (_, _) => { }, true, core);
        var planned = await actions.PrepareAsync(row.Identity, false, default);
        var results = await actions.ExecuteAsync(planned, default);
        var tx = (await vault.LoadAsync(default)).Single().Transaction!;
        Check(results.Single().State == ProcessActionState.Exited && source.Terminations == 1, "Guardian wrapper reuses the process core exactly once");
        Check(tx.State == ChangeState.Completed && tx.Plan.Before.Kind == "process" && tx.After?.State == "VerifiedEnd",
            "process outcome is durably journalled from the same-handle end-state proof");
        Check(tx.Journal.Count == 7 && source.Opened == source.Disposed, "process journal stages and all process handles are retained/released correctly");
        var snapshot = Snapshot() with { Entities = [IntelligenceSources.Entity("process:" + row.Identity, IntelligenceKind.Process, "Fixture",
            "Process", "Synthetic process identity", DateTimeOffset.UtcNow, new Dictionary<string, string>())] };
        var projection = GuardianWorkflows.Project(snapshot, [GuardianVault.Wrap(tx)]);
        Check(projection.Edges.Any(e => e.To == "process:" + row.Identity), "process transaction graph uses exact PID/creation-time identity");
        Check(!GuardianWorkflows.Project(snapshot with { HostScope = "other" }, [GuardianVault.Wrap(tx)]).Entities.Any(e => e.Kind == IntelligenceKind.Guardian),
            "foreign-host transactions never correlate with current Windows names");
        var invocation = new GuardianTarget("invocation", Guid.NewGuid().ToString("D"), EvidenceFile.Hash("fixture"), "Invoked", new Dictionary<string, string>());
        Check((await new GuardianActions().ReadAsync(invocation, default)).State == "SystemStateUnverified",
            "manual invocation reanalysis never invents system-state verification");
        try { await new GuardianActions().ReadAsync(new("process", "fixture", EvidenceFile.Hash("fixture"), "Alive",
            new Dictionary<string, string> { ["targets"] = "[]" }), default); Check(false, "empty process identity set accepted"); }
        catch (JsonException) { Check(true, "process reanalysis rejects unknown targets before reading Windows"); }
    }
    private static async Task MaintenanceAsync(string root)
    {
        var action = new StateAction(); var snapshot = Snapshot();
        var vault = new GuardianVault(Path.Combine(root, "maintenance-vault"));
        var core = new GuardianCore(vault, () => Context);
        var item = GuardianMaintenance.Create("Controlled maintenance", Plan(action), snapshot, Context);
        await vault.SaveAsync(item, default);
        try { GuardianMaintenance.Reserve(item, Context, core, true); Check(false, "unanalyzed workflow executed"); }
        catch (InvalidOperationException) { Check(action.Calls == 0, "fresh analysis is required"); }
        item = await GuardianMaintenance.ReanalyseAsync(item, Context, snapshot, [], action, default);
        Check(GuardianMaintenance.Steps(item).Single().State == "Ready", "fresh target preconditions are recorded");
        try { GuardianMaintenance.Reserve(item, Context, core, false); Check(false, "unconfirmed next step executed"); }
        catch (InvalidOperationException) { Check(action.Calls == 0, "each next action needs fresh confirmation"); }
        var reserved = GuardianMaintenance.Reserve(item, Context, core, true);
        await vault.SaveAsync(reserved.Pending, default);
        var interrupted = await GuardianMaintenance.ReanalyseAsync(reserved.Pending, Context, snapshot, [], action, default);
        Check(GuardianMaintenance.Steps(interrupted).Single().State == "Unknown" && action.Calls == 0, "reserved interrupted action is never blindly replayed");
        var tx = await core.ExecuteAsync(reserved.Transaction, action, true, default);
        var completed = GuardianMaintenance.Complete(reserved.Pending, tx);
        await vault.SaveAsync(completed, default);
        var verified = await GuardianMaintenance.ReanalyseAsync(completed, Context, snapshot, await vault.LoadAsync(default), action, default);
        Check(!verified.Pinned && verified.Fields["state"] == "Verified", "completed steps are verified against actual current state");
        Check(GuardianMaintenance.Steps(verified).Single().TransactionId == tx.Id && verified.Dependencies.Contains(tx.Id), "step retains its durable transaction dependency");
        action.Target = action.Target with { Fingerprint = EvidenceFile.Hash("replacement") };
        var conflict = await GuardianMaintenance.ReanalyseAsync(completed, Context, snapshot, await vault.LoadAsync(default), action, default);
        Check(conflict.Pinned && GuardianMaintenance.Steps(conflict).Single().State == "Unknown", "changed identity cannot be accepted as completed step");
        action.Unavailable = true;
        var unavailable = await GuardianMaintenance.ReanalyseAsync(completed, Context, snapshot, await vault.LoadAsync(default), action, default);
        Check(GuardianMaintenance.Steps(unavailable).Single().Verification.Contains("Quelle nicht lesbar", StringComparison.Ordinal), "provider failure is explicit unknown state");
        try { await GuardianMaintenance.ReanalyseAsync(completed, new("wrong", "user"), snapshot, [], action, default); Check(false, "wrong restart context accepted"); }
        catch (InvalidOperationException) { Check(true, "restart host and user are checked"); }

        var request = Path.Combine(root, "restart-fixture.json");
        await EvidenceFile.WriteAsync(request, EvidenceFile.Encode("guardian-restart-fixture", new RestartRequest(item, snapshot, new StateAction().Target)), default);
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "SystemCockpit.Tests.exe")) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--guardian-restart-child"); info.ArgumentList.Add(request);
        using var child = Process.Start(info) ?? throw new InvalidOperationException("Own restart testhost did not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } throw; }
        Check(child.ExitCode == 0 && File.Exists(request + ".response"), "separate testhost process reloads persisted maintenance");
        var response = EvidenceFile.Decode<GuardianArtifact>("guardian-restart-response", await File.ReadAllTextAsync(request + ".response"));
        Check(GuardianMaintenance.Steps(response).Single().State == "Ready", "new process explicitly reanalyses, without executing");
    }
    internal sealed record RestartRequest(GuardianArtifact Maintenance, IntelligenceSnapshot Snapshot, GuardianTarget Target);
    internal static int RestartChild(string path)
    {
        var request = EvidenceFile.Decode<RestartRequest>("guardian-restart-fixture", File.ReadAllText(path));
        var action = new StateAction { Target = request.Target };
        var core = new GuardianCore(new(Path.Combine(Path.GetDirectoryName(path)!, "not-created-by-restart")), () => Context);
        var refused = false;
        try { GuardianMaintenance.Reserve(request.Maintenance, Context, core, true); }
        catch (InvalidOperationException) { refused = true; }
        if (!refused) throw new InvalidOperationException("Old process analysis was accepted after restart.");
        var result = GuardianMaintenance.ReanalyseAsync(request.Maintenance, Context, request.Snapshot, [], action, default).GetAwaiter().GetResult();
        if (action.Calls != 0) throw new InvalidOperationException("Restart replayed a system effect.");
        EvidenceFile.WriteAsync(path + ".response", EvidenceFile.Encode("guardian-restart-response", result), default).GetAwaiter().GetResult();
        return 0;
    }
    private sealed class JumpClock : TimeProvider
    {
        internal DateTimeOffset Utc = DateTimeOffset.UtcNow;
        private long _stamp;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => Interlocked.Add(ref _stamp, TimestampFrequency);
    }
    private static async Task JournalAsync(string root)
    {
        var clock = new JumpClock();
        var action = new StateAction { OnExecute = () => clock.Utc -= TimeSpan.FromDays(2) };
        var vault = new GuardianVault(Path.Combine(root, "clock-vault"));
        var core = new GuardianCore(vault, () => Context, clock);
        var tx = await core.ExecuteAsync(core.Plan("Clock jump fixture", Plan(action)), action, true, default);
        tx.Validate();
        Check(tx.State == ChangeState.Completed && tx.Ended < tx.Started, "UTC jump is retained honestly, not rewritten");
        Check(tx.Journal.Zip(tx.Journal.Skip(1)).All(p => p.Second.ElapsedMilliseconds >= p.First.ElapsedMilliseconds), "elapsed times remain monotonic across UTC jump");
        var invocationVault = new GuardianVault(Path.Combine(root, "invocation-vault"));
        var returned = await GuardianInvocation.RunAsync("Synthetic invocation", _ => Task.FromResult("synthetic-private-output"), invocationVault, Context, default);
        var receipt = (await invocationVault.LoadAsync(default)).Single();
        Check(returned == "synthetic-private-output" && receipt.Transaction?.State == ChangeState.Partial, "invocation exit never impersonates verified system success");
        Check(!JsonSerializer.Serialize(receipt).Contains("synthetic-private-output", StringComparison.Ordinal), "invocation does not persist raw output");
        var limited = new GuardianVault(Path.Combine(root, "limited"), new(Files: 8));
        for (var index = 0; index < 8; index++)
            await limited.SaveAsync(GuardianVault.Artifact(RecoveryKind.ConfigurationBackup, "Pinned own configuration", Context, "Own fixture", pinned: true), default);
        var denied = new StateAction(); var limitedCore = new GuardianCore(limited, () => Context);
        try { await limitedCore.ExecuteAsync(limitedCore.Plan("Full protected budget", Plan(denied)), denied, true, default); Check(false, "needed protection discarded"); }
        catch (IOException) { Check(denied.Calls == 0 && (await limited.LoadAsync(default)).Count == 8, "budget exhaustion blocks effects without removing protected data"); }
        var groups = new[] { RecoveryKind.Transaction, RecoveryKind.RecoveryPoint, RecoveryKind.ConfigurationBackup, RecoveryKind.Maintenance, RecoveryKind.RescuePackage, RecoveryKind.CrashCase };
        Check(groups.Select(GuardianVault.BudgetGroup).Distinct().Count() == groups.Length, "artifact classes use separate retention groups");
    }
    private sealed class TraceApi(string directory, string mode) : IGuardianTraceNative
    {
        internal bool Active;
        internal int Starts, Stops, Queries;
        public uint Start(out ulong session, string name, IntPtr properties)
        {
            Starts++; session = 1;
            if (mode == "denied") return 5;
            Active = true;
            var file = Path.Combine(directory, name["AETHER-Guardian-".Length..] + ".etl");
            File.WriteAllBytes(file, new byte[mode == "size" ? 4 * 1024 * 1024 : 1024]);
            return 0;
        }
        public uint Enable(ulong session, Guid provider, byte level, ulong keywords) => mode == "provider" ? 87u : 0u;
        public uint Control(ulong session, string name, IntPtr properties, uint control)
        {
            if (!Active) return 4201;
            if (control == 1) { Stops++; Active = false; return 0; }
            Queries++; return 0;
        }
    }
    private static async Task TraceAsync(string root)
    {
        Check(!GuardianTrace.ProductionReleased, "production trace stays gated without real acceptance");
        try { await new GuardianTrace().RunAsync(Path.Combine(root, "must-not-exist"), 1, default); Check(false, "production trace started"); }
        catch (InvalidOperationException) { Check(!Directory.Exists(Path.Combine(root, "must-not-exist")), "production gate precedes all side effects"); }
        foreach (var mode in new[] { "denied", "provider", "time", "size", "cancel", "shutdown" })
        {
            var directory = Path.Combine(root, "trace-" + mode); var api = new TraceApi(directory, mode);
            using var cancel = new CancellationTokenSource();
            if (mode is "cancel" or "shutdown") cancel.CancelAfter(80);
            var balance = 0; var steps = new List<(string Name, uint Code)>();
            try
            {
                var result = await new GuardianTrace(api).RunAcceptanceAsync(directory, 1, cancel.Token, (name, code) =>
                {
                    if (name == "PropertiesBufferAllocated") balance++;
                    if (name == "PropertiesBufferFreed") balance--;
                    steps.Add((name, code));
                });
                Check(mode is "time" or "size" or "cancel" or "shutdown", "no success for injected native failure");
                Check(result.State == (mode == "size" ? "Incomplete" : mode is "cancel" or "shutdown" ? "Cancelled" : "Completed"), mode + " has honest state");
                Check(result.EndReason == (mode == "size" ? "Größenlimit" : mode == "time" ? "Zeitlimit" : "Benutzerabbruch oder AETHER-Shutdown"), mode + " records stop boundary");
            }
            catch (Win32Exception ex) when (mode is "denied" or "provider")
            { Check(ex.NativeErrorCode == (mode == "denied" ? 5 : 87), mode + " preserves native result"); }
            Check(!api.Active && balance == 0, mode + " releases own session and native property buffer");
            Check(api.Starts == 1 && api.Stops == (mode == "denied" ? 0 : 1), mode + " has exactly one own lifecycle");
            Check(steps.Any(s => s.Name == "StartTrace") && steps.Any(s => s.Name == "PropertiesBufferFreed"), mode + " records lifecycle stages");
        }
    }
    private static void Evidence()
    {
        var package = IntelligenceSources.Entity("pkg:fixture", IntelligenceKind.Software, "Package fixture", "Softwarepaket",
            "Synthetic exact package full name", DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["value"] = "Fixture.App_1.2.3.4_x64__publisher" });
        var snapshot = Snapshot() with { Entities = [package], Coverage = [new("Softwarepaket", true, package.Metadata)] };
        var manifest = GuardianWorkflows.SoftwareManifest(snapshot, Context);
        var packageItem = manifest.Software?.Single() ?? throw new InvalidOperationException("Manifest item missing.");
        Check(packageItem.PackageId == "Fixture.App_1.2.3.4_x64__publisher" &&
            packageItem.Architecture == "x64" && packageItem.Version == "1.2.3.4", "AppX exact fullname supplies version/architecture, not names");
        var winget = GuardianWorkflows.PackageManifest(new("upgrade", DateTimeOffset.UtcNow,
            [new("Fixture", "Fixture.App", "1.0", "2.0", "fixture-source")], false), Context);
        var wingetItem = winget.Software?.Single() ?? throw new InvalidOperationException("Package item missing.");
        Check(wingetItem.AvailableVersion == "2.0" && wingetItem.Source == "fixture-source",
            "available version is retained only with exact package source/ID");
        try { GuardianWorkflows.PackageManifest(new("upgrade", DateTimeOffset.UtcNow, [], true), Context); Check(false, "truncated package evidence accepted"); }
        catch (InvalidOperationException) { Check(true, "truncated package evidence is rejected"); }
        var driver = IntelligenceSources.Entity("driver:fixture", IntelligenceKind.Driver, "Fixture driver", "Driver", "Synthetic",
            DateTimeOffset.UtcNow, new Dictionary<string, string> { ["deviceId"] = "fixture-device", ["version"] = "1.0" });
        snapshot = Snapshot() with { Entities = [driver], Coverage = [new("Driver", true, driver.Metadata)] };
        var before = GuardianWorkflows.DriverState(snapshot, Context);
        var after = snapshot with { Entities = [driver with { Fields = new Dictionary<string, string>(driver.Fields) { ["version"] = "2.0" } }] };
        var verified = GuardianWorkflows.VerifyDrivers(before, after, Context); verified.Validate();
        var driverCheck = verified.DriverChecks?.Single() ?? throw new InvalidOperationException("Driver comparison missing.");
        Check(driverCheck.Before["version"] == "1.0" && driverCheck.After!["version"] == "2.0",
            "driver before/after retains exact registered versions");
        Check(driverCheck.PnPProblem == "Nicht lesbar", "missing PnP status is not a fabricated functional success");
        var incomplete = GuardianWorkflows.VerifyDrivers(before, after with { Entities = [], Coverage = [new("Driver", false, driver.Metadata)] }, Context);
        Check(incomplete.Fields["comparison"].Contains("Incomplete", StringComparison.Ordinal), "incomplete driver source cannot imply removal");
        var secret = "synthetic-excluded-secret";
        var privateArtifact = GuardianVault.Artifact(RecoveryKind.DiagnosticEvidence, secret, Context, secret,
            new Dictionary<string, string> { ["private"] = secret });
        Check(!System.Text.Encoding.UTF8.GetString(GuardianRescue.Support([privateArtifact])).Contains(secret, StringComparison.Ordinal), "support privacy remains strict allowlist");
        Check(!GuardianDumpPlan.EnabledByDefault, "dump remains honestly disabled architecture, no memory capture");
    }
}
