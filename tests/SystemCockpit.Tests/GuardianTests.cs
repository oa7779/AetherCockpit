using System.IO;
using System.Text.Json;
using AetherOperations;
using SystemCockpit.Services;

internal static class GuardianTests
{
    private static int _assertions;
    private static readonly GuardianContext Context = new("test-host", "test-user");
    private static void Check(bool value, string message)
    { _assertions++; if (!value) throw new InvalidOperationException("Guardian: " + message); }
    internal static int Run() => Task.Run(RunAsync).GetAwaiter().GetResult();
    private sealed class Fixture : IGuardianAction
    {
        internal GuardianTarget Target = new("fixture", "own-test-target", EvidenceFile.Hash("test-identity"), "Enabled", new Dictionary<string, string>());
        internal int Calls;
        internal bool FailExecute, FailRead, WrongAfter, CancelExecution;
        internal Action? OnExecute;
        public Task<GuardianTarget> ReadAsync(GuardianTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailRead && Calls > 0) throw new IOException("Controlled verification failure");
            return Task.FromResult(Target);
        }
        public Task<string> ExecuteAsync(GuardianPlan plan, CancellationToken token)
        {
            Calls++; OnExecute?.Invoke();
            if (FailExecute) throw new IOException("Controlled execution failure");
            if (CancelExecution) throw new OperationCanceledException("Controlled interruption");
            if (!WrongAfter) Target = Target with { State = plan.ExpectedState };
            return Task.FromResult("API accepted, not proof of success");
        }
    }
    private static GuardianPlan Plan(Fixture f) => new("disable", f.Target, "Disabled", "Own fixture only", "None", "Disable fixture",
        "Controlled fixture", "None", "Exact state stored", "Read exact state", RollbackCapability.Available, "Restore exact prior state");
    private static async Task<int> RunAsync()
    {
        _assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "AetherGuardianTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var vault = new GuardianVault(root);
            var core = new GuardianCore(vault, () => Context);
            var fixture = new Fixture();
            var planned = core.Plan("Own reversible fixture", Plan(fixture));
            planned.Validate();
            Check(planned.State == ChangeState.Planned && fixture.Calls == 0, "planning does not change anything");
            var tx = await core.ExecuteAsync(planned, fixture, true, CancellationToken.None);
            Check(tx.State == ChangeState.Completed && tx.After?.State == "Disabled", "completion requires actual matching state");
            Check(tx.Journal.Select(e => e.State).SequenceEqual(new[] { ChangeState.Planned, ChangeState.Preflight, ChangeState.Ready,
                ChangeState.Protected, ChangeState.Executing, ChangeState.Verifying, ChangeState.Completed }), "journal contains full ordered lifecycle");
            Check(tx.Journal.All(e => e.Hash.Length == 64), "every journal entry has an integrity hash");
            Check((await vault.LoadAsync(CancellationToken.None)).Single().Transaction?.Journal.Count == tx.Journal.Count, "journal survives reload");
            var rollback = core.Rollback(tx);
            var restored = await core.ExecuteAsync(rollback, fixture, true, CancellationToken.None);
            Check(restored.State == ChangeState.RolledBack && fixture.Target.State == "Enabled" && restored.RollbackOf == tx.Id,
                "rollback is a separately verified transaction");
            Check(restored.Journal.Any(e => e.State == ChangeState.RollingBack), "rollback has its own lifecycle");
            try
            {
                var again = core.Rollback(tx);
                await core.ExecuteAsync(again, fixture, true, default); Check(false, "second verified rollback accepted");
            }
            catch (InvalidOperationException) { Check(fixture.Calls == 2, "already completed rollback cannot be replayed as another transaction"); }
            var replacementAfter = tx with { State = ChangeState.Partial, After = tx.After! with { Fingerprint = EvidenceFile.Hash("replacement") },
                Journal = tx.Journal.Take(tx.Journal.Count - 1).ToArray() };
            var partialEntry = tx.Journal[^1] with { State = ChangeState.Partial, Hash = "" };
            partialEntry = partialEntry with { Hash = GuardianCore.JournalHash(partialEntry) };
            replacementAfter = replacementAfter with { Journal = replacementAfter.Journal.Append(partialEntry).ToArray() };
            try { core.Rollback(replacementAfter); Check(false, "rollback into replacement identity"); }
            catch (InvalidOperationException) { Check(true, "partial result cannot roll back an unrelated replacement"); }
            try { await core.ExecuteAsync(planned, fixture, true, CancellationToken.None); Check(false, "replay accepted"); }
            catch (InvalidOperationException) { Check(fixture.Calls == 2, "persisted plan cannot be replayed"); }
            var saved = (await vault.LoadAsync(CancellationToken.None)).Single(a => a.Id == tx.Id);
            Check(saved.Transaction?.State == ChangeState.Completed, "replay cannot rewrite original result");
            try { await vault.DeleteAsync(tx.Id, CancellationToken.None); Check(false, "dependency deletion accepted"); }
            catch (InvalidOperationException) { Check(true, "rollback dependency protects original"); }
            foreach (var mode in new[] { "identity", "state", "preflight", "execute", "verification", "wrong", "cancel", "context" })
            {
                var test = new Fixture();
                var currentContext = Context;
                var engine = new GuardianCore(vault, () => currentContext);
                var plan = engine.Plan("Fixture " + mode, Plan(test));
                switch (mode)
                {
                    case "identity": test.Target = test.Target with { Fingerprint = EvidenceFile.Hash("replacement") }; break;
                    case "state": test.Target = test.Target with { State = "Disabled" }; break;
                    case "preflight": test.Target = test.Target with { Id = "missing-target" }; break;
                    case "execute": test.FailExecute = true; break;
                    case "verification": test.FailRead = true; break;
                    case "wrong": test.WrongAfter = true; break;
                    case "cancel": test.CancelExecution = true; break;
                    case "context": currentContext = new("other-host", "other-user"); break;
                }
                if (mode == "context")
                {
                    try { await engine.ExecuteAsync(plan, test, true, CancellationToken.None); Check(false, "wrong host accepted"); }
                    catch (InvalidOperationException) { Check(test.Calls == 0, "context change blocks mutation"); }
                    continue;
                }
                var result = await engine.ExecuteAsync(plan, test, true, CancellationToken.None);
                Check(result.State == (mode is "identity" or "state" or "preflight" ? ChangeState.Failed : ChangeState.Partial), mode + " never reports success");
                Check(test.Calls == (mode is "identity" or "state" or "preflight" ? 0 : 1), mode + " has expected mutation count");
                result.Validate();
            }
            var denied = new Fixture();
            try { await core.ExecuteAsync(core.Plan("No consent", Plan(denied)), denied, false, CancellationToken.None); Check(false, "no consent accepted"); }
            catch (InvalidOperationException) { Check(denied.Calls == 0, "consent is required"); }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { await core.ExecuteAsync(core.Plan("Cancelled", Plan(denied)), denied, true, cancellation.Token); Check(false, "cancel ignored"); }
                catch (OperationCanceledException) { Check(denied.Calls == 0, "pre-execution cancellation cannot mutate"); }
            }
            var inFlight = core.Plan("Interrupted on restart", Plan(new Fixture()));
            await vault.CreateTransactionAsync(inFlight, CancellationToken.None);
            Check((await new GuardianVault(root).LoadAsync(CancellationToken.None)).Single(a => a.Id == inFlight.Id).Transaction!.InFlight,
                "restart retains unfinished workflow without replay");
            try { await vault.DeleteAsync(inFlight.Id, CancellationToken.None); Check(false, "active record deleted"); }
            catch (InvalidOperationException) { Check(true, "active transaction remains protected"); }
            var bad = saved with { Id = Guid.NewGuid(), Transaction = tx with { Journal = tx.Journal.Select((e, i) => i == 1 ? e with { Detail = "changed" } : e).ToArray() } };
            try { bad.Validate(); Check(false, "tampered journal accepted"); }
            catch (JsonException) { Check(true, "tampered journal is rejected"); }
            var damagedId = Guid.NewGuid();
            await File.WriteAllTextAsync(Path.Combine(root, damagedId.ToString("N") + ".guardian"), "{\"Version\":1}");
            var all = await vault.LoadAsync(CancellationToken.None);
            Check(all.All(a => a.Id != damagedId) && vault.Issues.Count == 1, "damaged object is isolated with an explicit issue");
            Check(Directory.GetFiles(root, "*.corrupt-*").Length == 1, "damaged bytes are preserved in quarantine");
            Check(GuardianVault.Readiness(all, Context).Single(r => r.Area == "Persönliche Dateien").State == ReadinessKind.NotProtected,
                "system evidence is never a personal-file backup");
            Check(GuardianVault.Readiness(all, new("wrong-host", "wrong-user")).First().State == ReadinessKind.NotProtected,
                "wrong host evidence does not imply protection");
            var noWay = tx with { Plan = tx.Plan with { Rollback = RollbackCapability.NotAvailable } };
            try { core.Rollback(noWay); Check(false, "invented rollback"); }
            catch (InvalidOperationException) { Check(true, "missing rollback is explicit"); }
            var blockedRoot = Path.Combine(root, "blocked");
            Directory.CreateDirectory(blockedRoot);
            var blockedVault = new GuardianVault(blockedRoot);
            await using (var lease = new FileStream(Path.Combine(blockedRoot, "vault.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    var engine = new GuardianCore(blockedVault, () => Context);
                    await engine.ExecuteAsync(engine.Plan("Locked journal", Plan(denied)), denied, true, CancellationToken.None);
                    Check(false, "journal lock ignored");
                }
                catch (IOException) { Check(denied.Calls == 0, "unwritable protection blocks all system execution"); }
            }
            Check(EvidenceFile.Decode<GuardianArtifact>("guardian", System.Text.Encoding.UTF8.GetString(EvidenceFile.Encode("guardian", saved))).Id == saved.Id,
                "shared evidence format roundtrips");
            var failureRoot = Path.Combine(root, "post-change-failure");
            var failureVault = new GuardianVault(failureRoot);
            var failureCore = new GuardianCore(failureVault, () => Context);
            FileStream? blockedJournal = null;
            var affected = new Fixture { OnExecute = () => blockedJournal = new FileStream(
                Path.Combine(failureRoot, "vault.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None) };
            try
            {
                await failureCore.ExecuteAsync(failureCore.Plan("Journal failure after change", Plan(affected)), affected, true, default);
                Check(false, "post-change journal failure reported as success");
            }
            catch (GuardianJournalException ex)
            {
                Check(affected.Calls == 1 && affected.Target.State == "Disabled", "post-change journal failure does not hide actual side effect");
                Check(ex.Transaction.State == ChangeState.Verifying, "persist failure carries exact unpersisted stage");
            }
            finally { blockedJournal?.Dispose(); }
            Check((await failureVault.LoadAsync(default)).Single().Transaction?.State == ChangeState.Executing,
                "durable interrupted state remains available after failed verification write");
            await WorkflowsAsync(root);
            return _assertions;
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task WorkflowsAsync(string root)
    {
        var now = DateTimeOffset.UtcNow;
        var software = IntelligenceSources.Entity("software:fixture", IntelligenceKind.Software, "Sensitive application",
            "Software", "Own registry fixture", now, new Dictionary<string, string> { ["version"] = "1.0", ["architecture"] = "fixture" });
        var driver = IntelligenceSources.Entity("driver:fixture", IntelligenceKind.Driver, "Fixture driver", "Driver",
            "Own fixture / no real driver", now, new Dictionary<string, string> { ["deviceId"] = "fixture", ["version"] = "1.0", ["inf"] = "fixture.inf" });
        var snapshot = new IntelligenceSnapshot(1, Guid.NewGuid(), "Fixture snapshot", now, [software, driver], [],
            [new("Software", true, software.Metadata), new("Driver", true, driver.Metadata)], Context.Host);
        var point = GuardianWorkflows.RecoveryPoint(snapshot, Context, "Own evidence only");
        point.Validate();
        Check(point.Snapshot?.Id == snapshot.Id && point.Summary.Contains("persönliche Dateien", StringComparison.Ordinal), "recovery point reuses snapshot model and states excluded scope");
        var manifest = GuardianWorkflows.SoftwareManifest(snapshot, Context);
        manifest.Validate();
        Check(manifest.Software?.Single().Version == "1.0", "software manifest keeps actual version");
        Check(manifest.Software?.Single().Restore == RollbackCapability.ExternalSourceRequired &&
            manifest.Software.Single().Method.Contains("unbekannt", StringComparison.Ordinal), "unknown classic installer is not assigned to a package source");
        var observer = GuardianWorkflows.BeginObservation(snapshot, Context);
        Check(observer.Pinned, "active observation is protected from retention");
        foreach (var mode in new[] { "same", "new", "version", "removed", "incomplete", "host", "driver" })
        {
            var after = mode switch
            {
                "new" => snapshot with { Entities = snapshot.Entities.Append(software with { Id = "software:new" }).ToArray() },
                "version" => snapshot with { Entities = [software with { Fields = new Dictionary<string, string>(software.Fields) { ["version"] = "2.0" } }, driver] },
                "removed" => snapshot with { Entities = [driver] },
                "incomplete" => snapshot with { Entities = [driver], Coverage = [new("Software", false, software.Metadata), new("Driver", true, driver.Metadata)] },
                "host" => snapshot with { HostScope = "other" },
                "driver" => snapshot with { Entities = [software, driver with { Fields = new Dictionary<string, string>(driver.Fields) { ["version"] = "2.0" } }] },
                _ => snapshot
            };
            var completed = GuardianWorkflows.CompleteObservation(observer, after, Context);
            completed.Validate();
            var changes = GuardianWorkflows.ObservationChanges(observer, completed);
            var expected = mode switch { "new" => ChangeKind.New, "version" or "driver" => ChangeKind.Changed,
                "removed" => ChangeKind.Removed, "incomplete" => ChangeKind.Incomplete, "host" => ChangeKind.NotComparable, _ => ChangeKind.Unchanged };
            Check(changes.Any(c => c.Kind == expected), mode + " uses shared coverage-aware comparison");
            Check(completed.Summary.Contains("kein Beweis", StringComparison.Ordinal), "observation never invents causality: " + mode);
        }
        var drivers = GuardianWorkflows.DriverState(snapshot, Context);
        drivers.Validate();
        Check(drivers.Snapshot?.Entities.Count == 1 && drivers.Fields["rollback"] == "Unknown", "driver guard does not fabricate rollback from version");
        var maintenance = GuardianMaintenance.Create("Own maintenance", Plan(new Fixture()), snapshot, Context);
        var paused = GuardianWorkflows.MaintenanceState(maintenance, "Paused", Context);
        var resumed = await GuardianMaintenance.ReanalyseAsync(paused, Context, snapshot, [], new Fixture(), default);
        Check(paused.Pinned && resumed.Pinned && GuardianMaintenance.Steps(resumed).Single().State == "Ready", "maintenance survives pause and requires step confirmation after fresh analysis");
        var aborted = GuardianWorkflows.MaintenanceState(paused, "Cancelled", Context);
        Check(aborted.Fields["verification"].Contains("erfolgte Schritte", StringComparison.Ordinal), "cancel does not claim changes were undone");
        try { await GuardianMaintenance.ReanalyseAsync(paused, new("wrong", "user"), snapshot, [], new Fixture(), default); Check(false, "restart host mismatch accepted"); }
        catch (InvalidOperationException) { Check(true, "restart validates host and user"); }
        var crash = new TimelineEntry("event:fixture", now, now, "ApplicationCrash", "Own crash fixture", "event:fixture",
            software.Metadata, "Not a cause", new("Application", 1, "Application Error", 1000, "fixture.exe"));
        var caseFile = GuardianWorkflows.CrashCase(crash, snapshot, Context, [crash], []);
        caseFile.Validate();
        Check(caseFile.Fields["processIdentity"].Contains("Keine", StringComparison.Ordinal), "crash without process edge never invents a PID identity");
        Check(caseFile.Fields["dump"].Contains("Ausgeschaltet", StringComparison.Ordinal) && !GuardianDumpPlan.EnabledByDefault, "crash case never starts a dump");
        try { GuardianWorkflows.CrashCase(crash with { Category = "WindowsEvent" }, snapshot, Context, [], []); Check(false, "unqualified event treated as crash"); }
        catch (InvalidOperationException) { Check(true, "only qualified events can create crash cases"); }
        var support = System.Text.Encoding.UTF8.GetString(GuardianRescue.Support([point, manifest, caseFile]));
        foreach (var secret in new[] { Context.Host, Context.User, software.Name, driver.Id, "fixture.exe", "Own crash fixture", "Not a cause" })
            Check(!support.Contains(secret, StringComparison.Ordinal), "support allowlist excludes sensitive detail: " + secret);
        var encrypted = GuardianRescue.Private([point, manifest]);
        var decoded = GuardianRescue.OpenPrivate(encrypted, Context);
        Check(decoded.Count == 2 && decoded[0].Id == point.Id, "Windows DPAPI private package decrypts and validates");
        try { GuardianRescue.OpenPrivate(encrypted, new("wrong", "user")); Check(false, "private host mismatch accepted"); }
        catch (InvalidOperationException) { Check(true, "private package context is checked after decryption"); }
        var output = Path.Combine(root, "own-support.aethersupport");
        var result = await GuardianRescue.WriteAsync(output, [point], false, true, default);
        Check(result.Bytes == new FileInfo(output).Length && result.Sha256.Length == 64, "real own support file is verified and hashed");
        try { await GuardianRescue.WriteAsync(output, [point], false, true, default); Check(false, "rescue overwrite allowed"); }
        catch (IOException) { Check(true, "existing rescue package is not overwritten"); }
        var projection = GuardianWorkflows.Project(snapshot, [point, manifest]);
        IntelligenceModel.Validate(projection);
        Check(projection.Entities.Count(e => e.Kind == IntelligenceKind.Guardian) == 2, "guardian reuses existing entity graph");
        Check(!projection.Coverage.Single(c => c.Domain == "Guardian").Complete, "limited vault is not a complete system history");
        Check(GuardianTrace.Owns(Guid.NewGuid(), Guid.NewGuid(), "AETHER-Guardian-other") == false, "trace name prefix never proves ownership");
        var ownTrace = Guid.NewGuid();
        Check(GuardianTrace.Owns(ownTrace, ownTrace, "AETHER-Guardian-" + ownTrace.ToString("N")), "trace requires exact session GUID and name");
        foreach (var invalid in new[] { 0, -1, 301, int.MaxValue })
        {
            try { GuardianTrace.ValidateRequest(invalid); Check(false, "invalid trace duration"); }
            catch (ArgumentOutOfRangeException) { Check(true, "bounded trace duration: " + invalid); }
        }
        foreach (var invalid in new[] { @"\Microsoft\Windows\Task", @"\Aether\Task", @"\Folder\*", @"\Folder\..\Task" })
        {
            try { GuardianActions.TaskScript(invalid); Check(false, "unsafe task path accepted"); }
            catch (ArgumentException) { Check(true, "protected/wildcard task blocked: " + invalid); }
        }
        var task = new GuardianTarget("task", @"\OwnFixture", EvidenceFile.Hash("xml"), "Enabled",
            new Dictionary<string, string> { ["xml"] = "<Task>fixture's exact XML</Task>" });
        var taskPlan = GuardianActions.MakePlan(task, "disable");
        var script = GuardianActions.TaskScript(task.Id, taskPlan);
        Check(script.Contains("fixture''s", StringComparison.Ordinal) && !script.Contains("Unregister-ScheduledTask", StringComparison.Ordinal),
            "task XML is literal-quoted and no deletion command is generated");
        Check(taskPlan.Rollback == RollbackCapability.Available && taskPlan.ExpectedState == "Disabled", "task plan has explicit state and reverse capability");
        var retentionRoot = Path.Combine(root, "retention");
        var small = new GuardianVault(retentionRoot, new(1, 8, 1048576));
        for (var i = 0; i < 10; i++) await small.SaveAsync(GuardianVault.Artifact(RecoveryKind.ConfigurationBackup, "Own fixture", Context,
            "Temporary evidence") with { Created = DateTimeOffset.UtcNow.AddDays(-3) }, default);
        Check((await small.LoadAsync(default)).Count <= 8 && Directory.GetFiles(retentionRoot, "*.guardian").Length <= 8, "vault count and age retention stay bounded");
        var pinned = point with { Pinned = true, Created = DateTimeOffset.UtcNow.AddDays(-3) };
        await small.SaveAsync(pinned, default);
        for (var i = 0; i < 10; i++) await small.SaveAsync(GuardianVault.Artifact(RecoveryKind.ConfigurationBackup, "Own fixture", Context, "Bounded"), default);
        Check((await small.LoadAsync(default)).Any(a => a.Id == pinned.Id), "retention preserves old active recovery dependencies");
    }
}
