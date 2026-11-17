using System.Collections.Immutable;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SystemCockpit.Controls;
using AetherOperations;
using AetherOperations.Platform;

internal static class PlatformTests
{
    internal static int Contracts()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception ex) when (ex is ArgumentException or System.IO.InvalidDataException or UnauthorizedAccessException or JsonException)
            { count++; return; }
            throw new InvalidOperationException(message);
        }
        var target = TargetInventory.Create("Testziel", "fixture.invalid", TargetKind.WindowsClient, TransportKind.Simulated, true);
        var inventory = new PlatformInventory(1, [target], []);
        TargetInventory.Validate(inventory);
        Check(TargetInventory.Read(target, "protection") is { Value: null, Metadata.Availability: DataAvailability.Unknown },
            "Unobserved protection remains unknown rather than healthy");
        Reject(() => TargetInventory.Create("bad", "host;whoami", TargetKind.WindowsClient, TransportKind.WinRM), "Command-shaped host accepted");
        Reject(() => TargetInventory.Create("bad", "host.invalid", TargetKind.LocalComputer, TransportKind.Local, true), "Simulation/transport mismatch accepted");
        Reject(() => TargetInventory.Validate(inventory with { Targets = [target, target] }), "Duplicate target identities accepted");
        Check(TargetInventory.Resolve(new(Guid.NewGuid(), "Leer", []), [target]).Length == 0,
            "Empty manual group never expands to all targets");
        Check(TargetInventory.Resolve(new(Guid.NewGuid(), "Simuliert", [], true, Transport: TransportKind.Simulated), [target]).Length == 1,
            "Explicit typed dynamic filter selects only matching targets");
        Reject(() => TargetInventory.Resolve(new(Guid.NewGuid(), "Unbegrenzt", [], true), [target]), "Unrestricted dynamic group accepted");
        var csv = TargetInventory.ImportCsv("Name,Host\r\n\"Client, Test\",client.invalid\r\n");
        Check(csv.Length == 1 && csv[0].Name == "Client, Test" && csv[0].Transport == TransportKind.WinRM &&
            TargetInventory.Read(csv[0], "reachability").Value is null, "CSV quoting is parsed without contacting imported hosts");
        Reject(() => TargetInventory.ImportCsv("Name,Host\r\na,same.invalid\r\nb,same.invalid"), "Duplicate CSV endpoints accepted");
        Reject(() => TargetInventory.ImportCsv("Name,Host\r\n\"unfinished,client.invalid"), "Malformed CSV escaped the import error contract");
        Reject(() => TargetInventory.ImportJson(JsonSerializer.Serialize(new[] { target }, PlatformJson.Options)), "Import claimed a simulation identity");
        var claimed = TargetInventory.Create("Import", "import.invalid", TargetKind.WindowsClient, TransportKind.WinRM) with
        { Facts = target.Facts.Add("protection", new("Healthy", new("Claimed provider", SourceType.Direct,
            DateTimeOffset.UtcNow, DataConfidence.ProviderReported, DataAvailability.Available))) };
        Reject(() => TargetInventory.ImportJson(JsonSerializer.Serialize(new[] { claimed }, PlatformJson.Options)),
            "Imported metadata claimed an observed protection state");
        var decoded = JsonSerializer.Deserialize<PlatformInventory>(JsonSerializer.Serialize(inventory, PlatformJson.Options), PlatformJson.Options)!;
        Check(PlatformJson.Hash(inventory) == PlatformJson.Hash(decoded), "Inventory and canonical hash survive a storage roundtrip");
        Check(PlatformJson.Hash(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }) ==
            PlatformJson.Hash(new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }), "Object key order does not invalidate approval hashes");
        var reader = new PlatformPrincipal("fixture-reader", PlatformRole.Reader, [target.Id]);
        PlatformActions.Authorize(reader, target, PlatformActions.Get("inventory.basic"));
        Reject(() => PlatformActions.Authorize(reader, target, PlatformActions.Get("test.marker")), "Reader was allowed a change");
        Reject(() => PlatformActions.Authorize(reader with { Scope = [] }, target, PlatformActions.Get("inventory.basic")), "Out-of-scope target was authorized");
        var original = PlatformActions.Get("test.marker");
        Reject(() => PlatformActions.Authorize(reader with { Role = PlatformRole.Support }, target,
            original with { Safety = original.Safety with { Risk = 0 } }), "Tampered action risk was accepted");
        Reject(() => PlatformActions.Validate(new(CanaryCount: 0), 10, true), "Mass change without canary accepted");
        Reject(() => PlatformActions.Validate(new(Workers: 99), 1, false), "Unbounded workers accepted");
        var manifest = new ScriptPackage(1, "fixture", "1", "Self claimed", "Fixture only", new string('A', 64), "Signed", PackageTrust.BuiltIn, []);
        Check(PlatformActions.InspectPackage(manifest).Trust == PackageTrust.Blocked, "Imported trust and signature claims never grant execution");
        Check(TargetInventory.Topology(new(1, [target], [new(Guid.NewGuid(), "Test", [target.Id])])).All(e =>
            !string.IsNullOrWhiteSpace(e.Metadata.Source) && e.Metadata.Classification == DataClassification.Sensitive),
            "Each topology relationship has provenance and does not imply a discovered connection");
        var now = DateTimeOffset.UtcNow;
        Check(TargetInventory.Evaluate(target, new("freeBytes", 1, "Bytes"), now).State == "Nicht bewertbar", "Missing inventory did not fail closed");
        var observed = target with { Facts = target.Facts.Add("freeBytes", new("20", new("Fixture simulation",
            SourceType.Unconfirmed, now, DataConfidence.Unconfirmed, DataAvailability.Available))) };
        Check(TargetInventory.Evaluate(observed, new("freeBytes", 30, "Bytes"), now).State == "Abweichung", "Observed free-space deviation not detected");
        Check(TargetInventory.Evaluate(observed, new("freeBytes", 10, "Bytes"), now.AddHours(1)).State == "Nicht bewertbar", "Old evidence was treated as compliance");
        return count;
    }

    internal static async Task<int> Execution()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        async Task Reject(Func<Task> action, string message)
        {
            try { await action(); }
            catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { count++; return; }
            throw new Exception(message);
        }
        var root = Path.Combine(Path.GetTempPath(), "Aether.Platform.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var targets = Enumerable.Range(0, 1000).Select(i => TargetInventory.Create($"Simulation {i}", $"fixture-{i}.invalid",
                TargetKind.WindowsClient, TransportKind.Simulated, true)).ToImmutableArray();
            var principal = new PlatformPrincipal("fixture-support", PlatformRole.Support, targets.Select(t => t.Id).ToImmutableArray());
            var store = new PlatformStore(root);
            var simulation = new SimulatedPlatformAdapter { Delay = TimeSpan.FromMilliseconds(1) };
            var engine = new PlatformEngine(store, principal, [simulation]);
            using (store.Acquire())
                await Reject(() => Task.Run(() => { using var other = store.Acquire(); }), "Concurrent workspace lease accepted");
            foreach (var size in new[] { 1, 10, 100, 1000 })
            {
                var preview = await engine.PreviewAsync("inventory.basic", targets[..size], new(), CancellationToken.None);
                await Reject(() => engine.RunAsync(preview.Preview.Id, CancellationToken.None), "Run without approval accepted");
                await Reject(() => Task.Run(() => engine.Approve(preview.Preview.Id, "wrong", TimeSpan.FromMinutes(5))), "Wrong preview hash approved");
                var granted = engine.Approve(preview.Preview.Id, preview.PreviewHash, TimeSpan.FromHours(1));
                Check(granted.Approvals[0].Expires - granted.Approvals[0].At == TimeSpan.FromHours(1), "Maximum approval lifetime drifted");
                var result = await engine.RunAsync(preview.Preview.Id, CancellationToken.None);
                Check(result.State == PlatformJobState.Succeeded && result.Results.Length == size &&
                    result.Results.All(r => r.ExitCode == 0 && r.State == TargetResultState.Succeeded), $"Scaled result shape failed for {size}");
                Check(store.Load(result.Preview.Id).PreviewHash == preview.PreviewHash, "Persisted approval hash changed");
                Check(simulation.PeakWorkers <= 4, "Worker bound exceeded");
            }
            var plan = await engine.PreviewAsync("test.marker", targets[..10], new(WaveSize: 3), CancellationToken.None);
            engine.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            var done = await engine.RunAsync(plan.Preview.Id, CancellationToken.None);
            Check(done.Results.All(r => r.MarkerHash?.Length == 64), "Each changing target needs its own verified marker hash");
            var undone = await engine.RollbackAsync(plan.Preview.Id, plan.PreviewHash, CancellationToken.None);
            Check(undone.State == PlatformJobState.RolledBack && undone.Results.All(r => r.State == TargetResultState.RolledBack),
                "Multi-target own-marker rollback failed");
            await Reject(() => Task.Run(() => store.Save(done with { PreviewHash = "changed" })), "Tampered frozen plan accepted");
            await Reject(() => Task.Run(() => store.Save(done with { Audit = done.Audit.SetItem(0, done.Audit[0] with { Event = "changed" }) })),
                "Tampered audit chain accepted");
            await Reject(() => Task.Run(() => store.Save(done with { Results = [null!] })), "Null deserialized result accepted");
            var named = targets[0] with { Name = "=1+1<script>" };
            var exported = done with { Preview = done.Preview with { Targets = done.Preview.Targets.SetItem(0,
                done.Preview.Targets[0] with { Target = named }) } };
            exported = exported with { PreviewHash = exported.Preview.Hash, Approvals = [] };
            Check(!PlatformReports.Json(exported).Contains(named.Name, StringComparison.Ordinal) &&
                !PlatformReports.Json(exported).Contains("fixture-support", StringComparison.Ordinal), "Default report exposed sensitive identity");
            Check(PlatformReports.Csv(exported, true).Contains("\"'=1+1<script>\"", StringComparison.Ordinal), "Spreadsheet formula was not neutralized");
            Check(!PlatformReports.Html(exported, true).Contains("<script>", StringComparison.Ordinal), "HTML report contains unescaped input");
            var off = new SimulatedPlatformAdapter { Offline = [targets[0].Id] };
            Check((await off.ProbeAsync(targets[0], CancellationToken.None)).Facts.Count == 0,
                "Offline simulation manufactured current inventory facts");
            var offEngine = new PlatformEngine(store, principal, [off]);
            plan = await offEngine.PreviewAsync("test.marker", targets[..10], new(MaximumFailures: 1000), CancellationToken.None);
            offEngine.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            var stopped = await offEngine.RunAsync(plan.Preview.Id, CancellationToken.None);
            Check(stopped.State == PlatformJobState.Paused && stopped.Results.Length == 1 && off.Executed == 0,
                "Failed canary allowed a later wave");
            var partial = new SimulatedPlatformAdapter { Offline = [targets[1].Id, targets[3].Id] };
            var partialEngine = new PlatformEngine(store, principal, [partial]);
            plan = await partialEngine.PreviewAsync("inventory.basic", targets[..10], new(WaveSize: 2, CanaryCount: 0, MaximumFailures: 1), CancellationToken.None);
            partialEngine.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            stopped = await partialEngine.RunAsync(plan.Preview.Id, CancellationToken.None);
            Check(stopped.State == PlatformJobState.Paused && stopped.Results.Length == 4, "Failure budget was per-wave rather than cumulative");
            var crashSim = new SimulatedPlatformAdapter();
            var crashEngine = new PlatformEngine(store, principal, [crashSim]);
            plan = await crashEngine.PreviewAsync("test.marker", targets[..2], new(), CancellationToken.None);
            crashEngine.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            crashEngine.TargetFinished += _ => throw new InvalidOperationException("Fixture crash after effect");
            await Reject(() => crashEngine.RunAsync(plan.Preview.Id, CancellationToken.None), "Crash fixture did not interrupt");
            Check(store.Load(plan.Preview.Id).Results[0].State == TargetResultState.Running, "Pre-effect durable checkpoint missing");
            var restart = new PlatformEngine(store, principal, [crashSim]);
            await Reject(() => restart.RunAsync(plan.Preview.Id, CancellationToken.None), "Uncertain crash was automatically redispatched");
            var reconciled = await restart.ReconcileAsync(plan.Preview.Id, CancellationToken.None);
            Check(reconciled.Results[0].State == TargetResultState.Succeeded && reconciled.Approvals.IsEmpty, "Own marker reconciliation failed");
            restart.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            done = await restart.RunAsync(plan.Preview.Id, CancellationToken.None);
            Check(done.State == PlatformJobState.Succeeded && crashSim.Executed == 2, "Resume repeated an already successful target");
            await restart.RollbackAsync(plan.Preview.Id, plan.PreviewHash, CancellationToken.None);
            var slow = new PlatformEngine(store, principal, [new SimulatedPlatformAdapter { Delay = TimeSpan.FromSeconds(5) }]);
            plan = await slow.PreviewAsync("inventory.basic", targets[..1], new(TargetTimeoutSeconds: 1), CancellationToken.None);
            slow.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            done = await slow.RunAsync(plan.Preview.Id, CancellationToken.None);
            Check(done.Results[0].State == TargetResultState.TimedOut, "Target timeout was not distinguished");
            plan = await slow.PreviewAsync("inventory.basic", targets[..1], new(), CancellationToken.None);
            var approved = slow.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            store.Save(approved with { Approvals = [approved.Approvals[0] with { At = DateTimeOffset.UtcNow.AddMinutes(-2), Expires = DateTimeOffset.UtcNow.AddMinutes(-1) }] });
            await Reject(() => slow.RunAsync(plan.Preview.Id, CancellationToken.None), "Expired approval accepted");
            slow.Revoke(plan.Preview.Id);
            await Reject(() => slow.RunAsync(plan.Preview.Id, CancellationToken.None), "Revoked approval accepted");
            slow.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
            done = await slow.RunAsync(plan.Preview.Id, cancel.Token);
            Check(done.State == PlatformJobState.Cancelled && done.Results[0].State == TargetResultState.Cancelled, "Cancellation was not persisted");
            var local = TargetInventory.Create("Lokale Testinventur", Environment.MachineName, TargetKind.LocalComputer, TransportKind.Local);
            var localRoot = Path.Combine(root, "markers");
            var localEngine = new PlatformEngine(store, principal with { Scope = [local.Id] }, [new LocalPlatformAdapter(localRoot, local.Id)]);
            plan = await localEngine.PreviewAsync("test.marker", [local], new(), CancellationToken.None);
            localEngine.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            done = await localEngine.RunAsync(plan.Preview.Id, CancellationToken.None);
            var markerPath = Path.Combine(localRoot, local.Id.ToString("N") + ".marker");
            Check(done.State == PlatformJobState.Succeeded && File.Exists(markerPath), "Real local test action did not run");
            var own = File.ReadAllText(markerPath);
            File.AppendAllText(markerPath, "fixture conflict");
            undone = await localEngine.RollbackAsync(plan.Preview.Id, plan.PreviewHash, CancellationToken.None);
            Check(undone.State == PlatformJobState.RollbackFailed && File.ReadAllText(markerPath).EndsWith("fixture conflict", StringComparison.Ordinal),
                "Rollback deleted a modified marker");
            File.WriteAllText(markerPath, own);
            undone = await localEngine.RollbackAsync(plan.Preview.Id, plan.PreviewHash, CancellationToken.None);
            Check(undone.State == PlatformJobState.RolledBack && !File.Exists(markerPath), "Explicit conflict retry did not verify absence");
            Check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Atomic save left temporary files");
            var durableRoot = Path.Combine(root, "durable-simulation");
            var durable = new SimulatedPlatformAdapter(durableRoot);
            plan = await new PlatformEngine(store, principal, [durable]).PreviewAsync("test.marker", targets[..1], new(), CancellationToken.None);
            var durableEngine = new PlatformEngine(store, principal, [durable]);
            durableEngine.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            done = await durableEngine.RunAsync(plan.Preview.Id, CancellationToken.None);
            var durableRestart = new SimulatedPlatformAdapter(durableRoot);
            Check((await durableRestart.ProbeAsync(targets[0], CancellationToken.None)).MarkerHash == done.Results[0].MarkerHash,
                "Simulation state disappeared on adapter restart");
            await new PlatformEngine(store, principal, [durableRestart]).RollbackAsync(plan.Preview.Id, plan.PreviewHash, CancellationToken.None);
            Check(done.Preview.RunbookHash == PlatformActions.Runbook("test.marker").Hash, "Preview did not freeze the trusted runbook");
            await Reject(() => Task.Run(() => store.Save(done with { Preview = done.Preview with { RunbookId = "imported.free.script" } })),
                "Unqualified runbook accepted");
            plan = await slow.PreviewAsync("inventory.basic", targets[..1], new(MaximumRuntimeSeconds: 1), CancellationToken.None);
            slow.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            done = await slow.RunAsync(plan.Preview.Id, CancellationToken.None);
            Check(done.State == PlatformJobState.Cancelled && done.Audit[^1].Event.Contains("Gesamtzeitlimit", StringComparison.Ordinal),
                "Aggregate runtime bound was not enforced");
            var rollbackSim = new SimulatedPlatformAdapter(Path.Combine(root, "rollback-restart"));
            var interruptedRollback = new PlatformEngine(store, principal, [new InterruptedRollbackAdapter(rollbackSim)]);
            plan = await interruptedRollback.PreviewAsync("test.marker", targets[..1], new(), CancellationToken.None);
            interruptedRollback.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            await interruptedRollback.RunAsync(plan.Preview.Id, CancellationToken.None);
            await Reject(() => interruptedRollback.RollbackAsync(plan.Preview.Id, plan.PreviewHash, CancellationToken.None),
                "Rollback interruption fixture failed");
            Check(store.Load(plan.Preview.Id).Results[0].State == TargetResultState.RollingBack,
                "Rollback had no durable per-target pending state");
            var afterRollbackCrash = new PlatformEngine(store, principal, [rollbackSim]);
            done = await afterRollbackCrash.ReconcileAsync(plan.Preview.Id, CancellationToken.None);
            Check(done.State == PlatformJobState.RolledBack && done.Results[0].State == TargetResultState.RolledBack,
                "Interrupted rollback could not verify actual absence");
            var diskStore = new PlatformStore(Path.Combine(root, "disk-failure"));
            var noEffects = new SimulatedPlatformAdapter();
            var diskEngine = new PlatformEngine(diskStore, principal, [noEffects]);
            plan = await diskEngine.PreviewAsync("test.marker", targets[..1], new(), CancellationToken.None);
            diskEngine.Approve(plan.Preview.Id, plan.PreviewHash, TimeSpan.FromMinutes(5));
            var journal = Path.Combine(diskStore.Root, "jobs", plan.Preview.Id.ToString("N") + ".json");
            File.SetAttributes(journal, FileAttributes.ReadOnly);
            try
            {
                await Reject(() => diskEngine.RunAsync(plan.Preview.Id, CancellationToken.None), "Unwritable journal did not prevent execution");
                Check(noEffects.Executed == 0, "Adapter effects occurred before durable journal write");
            }
            finally { File.SetAttributes(journal, FileAttributes.Normal); }
            return count;
        }

        finally { Directory.Delete(root, true); }
    }

    private sealed class InterruptedRollbackAdapter(IPlatformAdapter inner) : IPlatformAdapter
    {
        public TransportKind Transport => inner.Transport;
        public Task<AdapterProbe> ProbeAsync(AdminTarget target, CancellationToken token) => inner.ProbeAsync(target, token);
        public Task<string?> ExecuteAsync(AdminTarget target, string action, Guid job, string expected, CancellationToken token) =>
            inner.ExecuteAsync(target, action, job, expected, token);
        public async Task RollbackAsync(AdminTarget target, Guid job, string expected, CancellationToken token)
        {
            await inner.RollbackAsync(target, job, expected, token);
            throw new OperationCanceledException("Fixture process exit after rollback effect");
        }
    }

    internal static int Interface()
    {
        var assertions = 0;
        foreach (var scale in new[] { 1.0, 1.25, 1.5 })
        {
            var view = new AdminPlatformView();
            var host = new Window { Content = view, Width = 1280, Height = 920, ShowInTaskbar = false };
            try
            {
                host.Show();
                VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                view.Measure(new Size(1280, 900)); view.Arrange(new Rect(0, 0, 1280, 900)); view.UpdateLayout();
                if (Math.Abs(VisualTreeHelper.GetDpi(view).DpiScaleX - scale) > 0.001)
                    throw new InvalidOperationException("Requested root DPI was not applied");
                assertions++;
                foreach (var name in new[] { "SetupPanel", "PlanPanel", "ActionPanel", "Areas", "Status" })
                {
                    if (view.FindName(name) is not FrameworkElement item || item.ActualHeight <= 0 ||
                        item.TranslatePoint(new Point(), view).Y + item.ActualHeight > view.ActualHeight + 1)
                        throw new InvalidOperationException($"Platform layout clipped {name} at DPI {scale}");
                    assertions++;
                }
                var targets = (DataGrid)view.FindName("Targets");
                if (!targets.EnableRowVirtualization || targets.Items.Count != 0 ||
                    ((CheckBox)view.FindName("SensitiveConsent")).IsChecked != false)
                    throw new InvalidOperationException("Workspace construction acquired targets or enabled sensitive export");
                assertions++;
                var target = TargetInventory.Create("Inventurfixture", "inventory-fixture.invalid",
                    TargetKind.WindowsClient, TransportKind.Simulated, true);
                var facts = target.Facts.Add("freeBytes", new("1234", new("Inventurproof source", SourceType.Unconfirmed,
                    DateTimeOffset.UtcNow, DataConfidence.Unconfirmed, DataAvailability.Available)));
                var preview = new PlatformPreview(Guid.NewGuid(), "inventory.basic", 1, PlatformActions.Get("inventory.basic").Hash,
                    "fixture", DateTimeOffset.UtcNow, [new(target, ConnectionState.Available, "", new string('A', 64))], new(),
                    PlatformActions.Runbook("inventory.basic").Id, PlatformActions.Runbook("inventory.basic").Hash);
                var audit = new PlatformAudit(0, DateTimeOffset.UtcNow, "fixture", "UI fixture", null, "", "");
                var job = new PlatformJob(1, preview, preview.Hash, PlatformJobState.Succeeded, [],
                    [new(target.Id, TargetResultState.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, facts, "Fixture")],
                    [audit with { Hash = PlatformStore.AuditHash(audit) }]);
                PlatformStore.Validate(job);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(AdminPlatformView).GetField("_inventory", flags)!.SetValue(view, new PlatformInventory(1, [target], []));
                typeof(AdminPlatformView).GetField("_job", flags)!.SetValue(view, job);
                typeof(AdminPlatformView).GetMethod("Refresh", flags)!.Invoke(view, null);
                var evidence = targets.Items[0].GetType().GetProperty("Evidence")!.GetValue(targets.Items[0]) as string;
                typeof(AdminPlatformView).GetMethod("EvaluateClicked", flags)!.Invoke(view, [view, new RoutedEventArgs()]);
                if (evidence != "Freier Speicher (Bytes): 1234" ||
                    !((TextBox)view.FindName("Details")).Text.Contains("Inventurproof source", StringComparison.Ordinal) ||
                    !((TextBox)view.FindName("Topology")).Text.Contains("Inventurfixture → SIMULIERT", StringComparison.Ordinal))
                    throw new InvalidOperationException("Completed inventory facts are absent from target table or source details");
                assertions++;
                var rows = Enumerable.Range(0, 1000).Select(i => new
                {
                    Name = $"Simulation {i}", Host = $"fixture-{i}.invalid", Transport = "SIMULIERT",
                    Evidence = "Künstliches Layoutfixture, keine Messung"
                }).ToArray();
                targets.ItemsSource = rows;
                targets.UpdateLayout();
                var realized = Enumerable.Range(0, 1000).Count(i => targets.ItemContainerGenerator.ContainerFromIndex(i) is not null);
                if (targets.Items.Count != 1000 || realized is <= 0 or >= 1000)
                    throw new InvalidOperationException("1000-row platform grid did not virtualize actual containers");
                targets.ScrollIntoView(rows[^1]); targets.UpdateLayout();
                if (targets.ItemContainerGenerator.ContainerFromIndex(999) is not DataGridRow { ActualHeight: > 0 })
                    throw new InvalidOperationException("Last target in a virtualized 1000-row grid is inaccessible");
                assertions++;
                var areas = (TabControl)view.FindName("Areas");
                foreach (var tab in areas.Items.Cast<TabItem>())
                {
                    areas.SelectedItem = tab; host.UpdateLayout();
                    if (tab.Content is not FrameworkElement content || content.ActualHeight <= 0)
                        throw new InvalidOperationException("Platform tab has no usable content");
                    assertions++;
                }
            }
            finally { host.Close(); }
        }
        return assertions;
    }
}
