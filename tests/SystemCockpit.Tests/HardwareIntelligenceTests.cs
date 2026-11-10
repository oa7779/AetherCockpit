using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AetherOperations;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class HardwareIntelligenceTests
{
    private static int _assertions;
    private static readonly DateTimeOffset Time = DateTimeOffset.UtcNow;
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Hardware Intelligence: " + message);
    }

    private static JsonElement Row(object data) => JsonSerializer.SerializeToElement(data);
    private static HardwareFact Value(string key, string value, DataClassification privacy = DataClassification.Normal,
        SourceType source = SourceType.Direct) =>
        new(key, key, SourceObservation<string>.Present(value, "", new("fixture", source, Time,
            source == SourceType.Unconfirmed ? DataConfidence.Unconfirmed : DataConfidence.ProviderReported,
            DataAvailability.Available, classification: privacy)));

    private static HardwareCimResult[] Batches(HardwareClass category, Dictionary<string, JsonElement[]>? rows = null) =>
        HardwareCimCatalog.Queries.Where(q => q.Class == category).Select(q =>
            new HardwareCimResult(q.Key, Time, DataAvailability.Available, null, rows?.GetValueOrDefault(q.Key) ?? [])).ToArray();

    internal static IReadOnlyList<HardwareComponent> Fixture(HardwareClass category)
    {
        var q = HardwareCimCatalog.Queries.FirstOrDefault(q => q.Class == category);
        if (q is null) return HardwareSensorInventory.Read([new("sensor/0", "Fixture CPU", "Package", "Temperature", 0, "°C")], Time, true, null);
        var properties = new Dictionary<string, object?>
        {
            ["_Identity"] = "fixture/1", [q.Identity] = "fixture/1", [q.Label] = "AETHER Fixture / " + category,
            [q.Properties[0].Property] = "Fixture 0", ["Name"] = "AETHER Fixture",
            ["Size"] = 1024, ["Temperature"] = 0, ["Capacity"] = 1024
        };
        if (q.Properties.Any(p => p.Property == "PNPDeviceID"))
            properties["PNPDeviceID"] = @"PCI\VEN_1234&DEV_5678&SUBSYS_11223344&REV_02\PRIVATE";
        var rows = new Dictionary<string, JsonElement[]> { [q.Key] = [Row(properties)] };
        return HardwareIntelligenceReader.Parse(category, Batches(category, rows));
    }

    internal static int Run()
    {
        _assertions = 0;
        var zero = Value("zero", "0") with { Cadence = HardwareCadence.Live };
        Check(zero.Observation.HasValue && zero.Display == "0", "zero is available, not missing");
        var missing = HardwareFact.Missing("missing", "missing", "fixture", Time, "Fixture missing");
        Check(!missing.Observation.HasValue && missing.Display == "Nicht verfügbar", "missing is not zero");
        foreach (var source in Enum.GetValues<SourceType>())
        {
            var fact = Value("source", "1", source: source);
            Check(fact.Provenance == source.ToString(), "all source types visible");
            Check(fact.Observation.Metadata.IsCalculated == (source == SourceType.Derived), "calculated invariant");
            Check(HardwareIntelligenceView.Describe(fact).Contains(source.ToString(), StringComparison.Ordinal), "inspector shows provenance");
        }
        foreach (var state in Enum.GetValues<DataAvailability>().Where(s => s is not (DataAvailability.Available or DataAvailability.Stale)))
        {
            var fact = HardwareFact.Missing("state", "state", "fixture", Time, "reason", state);
            Check(fact.State == state.ToString() && !fact.Observation.HasValue, "missing states remain distinct");
        }
        Check(zero.At(Time.AddSeconds(11)).State == "Stale", "live expires");
        Check((zero with { Cadence = HardwareCadence.Dynamic }).At(Time.AddMinutes(2)).State == "Stale", "dynamic expires");
        Check((zero with { Cadence = HardwareCadence.Inventory }).At(Time.AddDays(10)).State == "Available", "static has no periodic polling");

        var store = new HardwareInventoryStore();
        var component = new HardwareComponent("internal-PRIVATE", HardwareClass.Cpu, "private-label",
            [zero, Value("sensitive", "PRIVATE", DataClassification.Sensitive), Value("secret", "SECRET", DataClassification.Secret),
             Value("database", "database-spec", source: SourceType.Database), Value("derived", "2", source: SourceType.Derived),
             Value("unconfirmed", "unconfirmed", source: SourceType.Unconfirmed), missing]);
        store.Replace(HardwareClass.Cpu, [component], 0);
        store.Invalidate([HardwareClass.Gpu], "GPU changed");
        Check(store.Snapshot(Time).Components[0].Facts[0].State == "Available", "unrelated invalidation preserves CPU");
        store.Invalidate([HardwareClass.Cpu], "CPU changed");
        Check(store.Snapshot(Time).Components[0].Facts[0].State == "Stale", "invalidate retains stale value");
        store.Replace(HardwareClass.Cpu, [component], 0);
        Check(store.Snapshot(Time).Components[0].Facts[0].State == "Stale", "in-flight invalidation cannot publish fresh");
        store.Replace(HardwareClass.Cpu, [component], store.Version(HardwareClass.Cpu));
        Check(store.Snapshot(Time).Components[0].Facts[0].State == "Available", "successful later refresh becomes fresh");
        store.Fail(HardwareClass.Cpu, "fixture-error", DataAvailability.ProviderError, Time);
        Check(store.Snapshot(Time).Components.Any(c => c.Facts.Any(f => f.State == "ProviderError")), "refresh failure explicit");
        Check(store.Snapshot(Time).Components[0].Facts[0].State == "Stale", "failure does not silently drop old data");
        store.Replace(HardwareClass.Cpu, [], store.Version(HardwareClass.Cpu));
        Check(store.Snapshot(Time).Components.Count == 0, "device removal replaces old inventory");
        try { store.Replace(HardwareClass.Cpu, [component, component], 0); Check(false, "duplicate should fail"); }
        catch (ArgumentException) { Check(true, "duplicate identities rejected"); }

        foreach (var format in new[] { "json", "csv", "txt" })
        {
            var inventory = new HardwareInventory(Time, [component]);
            var redacted = HardwareReport.Export(inventory, format, false);
            Check(!redacted.Contains("PRIVATE", StringComparison.Ordinal), "identity and value redaction in " + format);
            Check(!redacted.Contains("private-label", StringComparison.Ordinal), "no component label leak");
            Check(!redacted.Contains("SECRET", StringComparison.OrdinalIgnoreCase), "secret entirely omitted");
            Check(redacted.Contains("Direct") && redacted.Contains("Derived") && redacted.Contains("Database") &&
                redacted.Contains("Unconfirmed") && redacted.Contains("Unavailable"), "report states retained");
            var consent = HardwareReport.Export(inventory, format, true);
            Check(consent.Contains("PRIVATE") && !consent.Contains("SECRET"), "consent only permits Sensitive");
            Check(!consent.Contains("internal-PRIVATE"), "internal ids never exported even with consent");
        }
        var csv = HardwareReport.Export(new(Time, [component with { Facts = [Value("formula", "=1+2")] }]), "csv", false);
        Check(csv.Contains("'=1+2"), "CSV formula injection neutralized");

        foreach (var category in Enum.GetValues<HardwareClass>())
        {
            var fixtures = Fixture(category);
            Check(fixtures.Count > 0 && fixtures.All(c => c.Class == category), "fixture class " + category);
            Check(fixtures.Select(c => c.Id).Distinct().Count() == fixtures.Count, "unique instances " + category);
            Check(fixtures.SelectMany(c => c.Facts).All(f => f.Observation.Metadata.Timestamp != default), "timestamps " + category);
        }
        foreach (var category in new[] { HardwareClass.Cpu, HardwareClass.Gpu, HardwareClass.Memory, HardwareClass.Network,
                     HardwareClass.Audio, HardwareClass.Display, HardwareClass.Usb })
        {
            var q = HardwareCimCatalog.Queries.First(q => q.Class == category);
            var batches = Batches(category, new() { [q.Key] = [
                Row(new Dictionary<string, string> { ["_Identity"] = "device-1", [q.Label] = "Same model" }),
                Row(new Dictionary<string, string> { ["_Identity"] = "device-2", [q.Label] = "Same model" })] });
            var parsed = HardwareIntelligenceReader.Parse(category, batches);
            Check(parsed.Count(c => c.Name.Contains("Same model")) == 2, "same model is not identity " + category);
            foreach (var error in new[] { DataAvailability.AccessDenied, DataAvailability.ProviderError, DataAvailability.Unsupported })
            {
                var failed = Batches(category).Select(b => b with { Availability = error, Error = "fixture-error" }).ToArray();
                Check(HardwareIntelligenceReader.Parse(category, failed).Any(c => c.Facts.Any(f => f.State == error.ToString())),
                    "provider error distinct from absent " + category);
            }
        }
        Storage();
        CpuTopology();
        var all = new HardwareInventoryStore();
        var extra = new HardwareCimResult("Extra:SecureBoot", Time, DataAvailability.Available, null,
            [Row(new { Key = "SecureBoot", Name = "SecureBoot", Value = false })]);
        foreach (var category in new[] { HardwareClass.System, HardwareClass.Firmware })
            all.Replace(category, HardwareIntelligenceReader.Parse(category, [.. Batches(category), extra]), 0);
        Check(HardwareReport.Export(all.Snapshot(Time), "json", false).Contains("SecureBoot"), "cross-class source names cannot collide");
        var usbBatches = Batches(HardwareClass.Usb, new() { ["Usb"] = [
            Row(new { _Identity = "hub", PNPDeviceID = @"USB\HUB", Name = "Hub" }),
            Row(new { _Identity = "child", PNPDeviceID = @"USB\VID_1234&PID_5678", Name = "Child", Parent = @"USB\HUB" })] });
        var usb = HardwareIntelligenceReader.Parse(HardwareClass.Usb, usbBatches);
        Check(usb.Single(c => c.Name.EndsWith("Child")).ParentId == usb.Single(c => c.Name.EndsWith("Hub")).Id,
            "USB parent is exact Windows identity, not guessed port");
        var denied = HardwareIntelligenceReader.Fact(Row(new { _PropertyStates = new { DriverVersion = 2 } }),
            new("DriverVersion", "Driver"), "fixture", Time);
        Check(denied.State == "AccessDenied", "per-property access failure is not plain missing");
        var view = new HardwareIntelligenceView { Synthetic = true };
        var calls = 0;
        view.ReadInventory = (category, token) => { calls++; return Task.FromResult(Fixture(category)); };
        view.RefreshAsync().GetAwaiter().GetResult();
        Check(calls == 1 && view.Snapshot.Components.Count > 0, "shell uses injected inventory once");
        view.ReadInventory = (_, _) => throw new InvalidOperationException("fixture error");
        view.RefreshAsync().GetAwaiter().GetResult();
        Check(view.Snapshot.Components.Any(c => c.Facts.Any(f => f.State == "ProviderError")), "shell exposes provider failure");
        view.ReadInventory = async (_, token) => { await Task.Delay(TimeSpan.FromSeconds(10), token); return []; };
        var oldContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var running = view.RefreshAsync();
            var shutdown = view.ShutdownAsync();
            var deadline = Stopwatch.StartNew();
            while (!shutdown.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(2)) { Pump(); Thread.Sleep(1); }
            Check(shutdown.IsCompletedSuccessfully && running.IsCompletedSuccessfully, "unload/shutdown cancels and drains async provider");
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
        var sensors = HardwareSensorInventory.Read([new("temp", "CPU", "Package", "Temperature", 0, "°C"),
            new("missing", "CPU", "Clock", "Clock", null, "MHz")], Time, true, null);
        Check(sensors[0].Facts.Single(f => f.Key == "current").Display == "0 °C", "sensor zero");
        Check(!sensors[1].Facts.Single(f => f.Key == "current").Observation.HasValue, "sensor missing");
        Check(HardwareSensorInventory.Read([], Time, true, null)[0].Facts[0].State == "Unavailable", "no sensors supported");
        Check(HardwareSensorInventory.Read([], Time, true, "fixture-error")[0].Facts[0].State == "ProviderError", "sensor error explicit");
        Check(HardwareSensorInventory.Read([], Time, false, null)[0].Facts[0].Observation.Metadata.Error!.Contains("deaktiviert"), "disabled stays disabled");
        var duplicateSensor = new SensorReading("duplicate", "CPU", "Package", "Temperature", 1, "°C");
        Check(HardwareSensorInventory.Read([duplicateSensor, duplicateSensor], Time, true, null)[0].Facts[0].State == "ProviderError",
            "duplicate sensor identities surface as provider error, not a sampling loop exception");
        Check(HardwareIntelligenceReader.DecodeEdid(Row(new { name = new[] { 65, 66, 0, 0 } }), "name") == "AB", "EDID ASCII decoded without a database");
        Check(HardwareIntelligenceReader.DecodeEdid(Row(new { name = new[] { 999 } }), "name") is null, "invalid EDID character not guessed");
        Check(HardwareIntelligenceReader.DecodeEdid(Row(new { name = new[] { "65" } }), "name") is null, "EDID wrong type rejected");
        var entry = new HardwareDatabaseEntry("id", "fixture-source", "CC0-1.0", DateOnly.FromDateTime(Time.UtcDateTime),
            "fixture-1", "PCI:1234:5678", DataConfidence.ProviderReported, new("https://example.org/hardware"));
        Check(HardwareDatabase.IsValidMatch(entry, "PCI:1234:5678"), "exact approved license match");
        Check(!HardwareDatabase.IsValidMatch(entry, "PCI:1234:5679"), "no fuzzy database match");
        Check(!HardwareDatabase.IsValidMatch(entry with { License = "Unknown" }, entry.Match), "license gate");
        Check(!HardwareDatabase.IsValidMatch(entry with { Reviewed = default }, entry.Match), "review date mandatory");
        var logo = new HardwareLogoLicense("fixture", new("https://example.org/logo"), "owner", "terms", "purpose",
            "no changes", "credit", new string('a', 64), DateOnly.FromDateTime(Time.UtcDateTime), false);
        Check(!HardwareLogoRegistry.CanActivate(logo, logo.Sha256), "no unapproved logo");
        Check(HardwareLogoRegistry.CanActivate(logo with { Approved = true }, logo.Sha256), "full documented approval required");
        Check(!HardwareLogoRegistry.CanActivate(logo with { Approved = true }, new string('b', 64)), "asset hash gate");
        foreach (var category in new[] { HardwareClass.System, HardwareClass.Storage })
        {
            var script = HardwareIntelligenceReader.Script(HardwareCimCatalog.Queries, category);
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(script));
            var syntax = AdminShell.RunAsync(
                "$tokens=$null; $errors=$null; [void][Management.Automation.Language.Parser]::ParseInput(" +
                "[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "')), [ref]$tokens, [ref]$errors); " +
                "if($errors.Count){throw ($errors.Message -join '; ')}; 'syntax-ok'", TimeSpan.FromSeconds(15), CancellationToken.None).GetAwaiter().GetResult();
            Check(syntax.ExitCode == 0 && syntax.Output.Contains("syntax-ok"), "PowerShell read-only script syntax: " + syntax.Error);
        }
        return _assertions;
    }

    private static void Storage()
    {
        var rows = new Dictionary<string, JsonElement[]>
        {
            ["Disk"] = [Row(new { _Identity = "disk0", Number = 0, FriendlyName = "same", UniqueId = "exact0" }),
                        Row(new { _Identity = "disk1", Number = 1, FriendlyName = "same", UniqueId = "exact1" })],
            ["Physical"] = [Row(new { _Identity = "physical", FriendlyName = "same", UniqueId = "exact1" })],
            ["Partition"] = [Row(new { _Identity = "partition", DiskNumber = 1, PartitionNumber = 1, AccessPaths = new[] { @"\\?\Volume{fixture}\" } })],
            ["Volume"] = [Row(new { _Identity = "volume", Path = @"\\?\Volume{fixture}\", FileSystem = "NTFS" })]
        };
        var reliability = new HardwareCimResult("Extra:reliability", Time, DataAvailability.Available, null,
            [Row(new { Key = "Wear", Name = "Wear", Target = "physical", Value = 0, Source = "Get-StorageReliabilityCounter.Wear" })]);
        var parsed = HardwareIntelligenceReader.Parse(HardwareClass.Storage, [.. Batches(HardwareClass.Storage, rows), reliability]);
        var physical = parsed.Single(c => c.Id == HardwareIntelligenceReader.Identity("Physical", "physical"));
        var disk = parsed.Single(c => c.Id == HardwareIntelligenceReader.Identity("Disk", "disk1"));
        var partition = parsed.Single(c => c.Id == HardwareIntelligenceReader.Identity("Partition", "partition"));
        var volume = parsed.Single(c => c.Id == HardwareIntelligenceReader.Identity("Volume", "volume"));
        Check(disk.ParentId == physical.Id, "physical mapping uses exact UniqueId, not model");
        Check(partition.ParentId == disk.Id && volume.ParentId == partition.Id, "disk partition volume filesystem chain");
        Check(physical.Facts.Single(f => f.Key == "Wear").Display == "0", "health belongs to originating PhysicalDisk, zero preserved");
        Check(!disk.Facts.Any(f => f.Key == "Wear"), "no guessed health fanout");
        rows["Disk"] = [rows["Disk"][1], Row(new { _Identity = "duplicate", Number = 1, UniqueId = "exact1" })];
        parsed = HardwareIntelligenceReader.Parse(HardwareClass.Storage, Batches(HardwareClass.Storage, rows));
        rows["Physical"] = [rows["Physical"][0], Row(new { _Identity = "physical-duplicate", UniqueId = "exact1" })];
        parsed = HardwareIntelligenceReader.Parse(HardwareClass.Storage, Batches(HardwareClass.Storage, rows));
        Check(parsed.Single(c => c.Id == disk.Id).ParentId is null, "ambiguous physical mapping refused");
        Check(parsed.Single(c => c.Id == partition.Id).ParentId is null, "ambiguous disk number refused");
    }

    private static void CpuTopology()
    {
        var data = new byte[48 + 48 + 56 + 48];
        static void I32(byte[] buffer, int offset, int number) => BitConverter.GetBytes(number).CopyTo(buffer, offset);
        I32(data, 0, 0); I32(data, 4, 48); data[8] = 1; data[9] = 8; data[30] = 1; data[32] = 3;
        I32(data, 48, 3); I32(data, 52, 48); data[78] = 1; data[80] = 3;
        I32(data, 96, 2); I32(data, 100, 56); data[104] = 1; data[106] = 64;
        I32(data, 108, 32768); I32(data, 112, 2);
        I32(data, 152, 1); I32(data, 156, 48); I32(data, 160, 2);
        var topology = HardwareCpuReader.ParseTopology(data, Time);
        Check(topology.Count == 4, "core/socket/cache/NUMA fixture parsed independently");
        Check(topology[0].Facts.Any(f => f.Name.Contains("Affinity") && f.Display.EndsWith("0003")), "SMT mask preserved");
        Check(topology[0].Facts.Any(f => f.Name.Contains("Efficiency") && f.Display == "8"), "raw efficiency class not guessed P/E");
        Check(topology[2].Facts.Any(f => f.Name == "Cachegröße" && f.Display == "32768 B"), "data cache size");
        Check(topology[3].Facts[0].Display == "2", "NUMA identity");
        I32(data, 4, 1);
        try { HardwareCpuReader.ParseTopology(data, Time); Check(false, "bad topology block must fail"); }
        catch (InvalidOperationException) { Check(true, "truncated/invalid topology rejected"); }
    }

    internal static void Proof(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || Directory.Exists(directory) || !Directory.Exists(Path.GetDirectoryName(directory)))
            throw new ArgumentException("Neuer absoluter Proofordner erforderlich.", nameof(directory));
        Directory.CreateDirectory(directory);
        var start = Process.GetCurrentProcess();
        var measurements = new List<object>();
        for (var round = 0; round < 3; round++)
        {
            var cpu = start.TotalProcessorTime;
            var timer = Stopwatch.StartNew();
            var store = new HardwareInventoryStore();
            foreach (var category in Enum.GetValues<HardwareClass>())
                store.Replace(category, Fixture(category), 0);
            var coldMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            for (var i = 0; i < 100; i++) _ = store.Snapshot(Time);
            var snapshotsMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var json = HardwareReport.Export(store.Snapshot(Time), "json", false);
            var exportMs = timer.Elapsed.TotalMilliseconds;
            start.Refresh();
            measurements.Add(new { Round = round + 1, Components = store.Snapshot(Time).Components.Count,
                ColdFixtureMs = coldMs, HundredSnapshotsMs = snapshotsMs, JsonExportMs = exportMs,
                CpuMs = (start.TotalProcessorTime - cpu).TotalMilliseconds, start.WorkingSet64, start.PrivateMemorySize64,
                start.HandleCount, Threads = start.Threads.Count, JsonCharacters = json.Length });
        }
        File.WriteAllText(Path.Combine(directory, "fixture-performance.json"), JsonSerializer.Serialize(new
        { Scope = "Synthetic fixtures only, no hardware or product performance claim", Timestamp = DateTimeOffset.UtcNow, Measurements = measurements },
            new JsonSerializerOptions { WriteIndented = true }));
        ModernWindowAppearance.Enable();
        foreach (var theme in new[] { "retro-neon", "windows-modern-dark", "windows-modern-light" })
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            SkinCatalog.Apply(Application.Current, theme == "retro-neon" ? theme : "windows-modern", theme.EndsWith("light", StringComparison.Ordinal));
            var view = new HardwareIntelligenceView { Synthetic = true };
            foreach (var category in Enum.GetValues<HardwareClass>()) view.Present(category, Fixture(category));
            view.Present(HardwareClass.Cpu, Fixture(HardwareClass.Cpu));
            var window = new Window { Content = view, Width = 1440, Height = 820, ShowInTaskbar = false };
            window.SetResourceReference(Control.BackgroundProperty, "WindowBackdrop");
            // Apply resource palettes through the same product skin mechanism.
            AetherNeonFrame.SetReducedMotion(view, true);
            window.Show();
            window.UpdateLayout();
            Pump();
            var width = (int)Math.Ceiling(window.ActualWidth * scale);
            var height = (int)Math.Ceiling(window.ActualHeight * scale);
            window.Content = null;
            var root = new System.Windows.Documents.AdornerDecorator { Child = view };
            window.Content = root;
            VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
            root.Measure(new Size(1420, 780));
            root.Arrange(new Rect(0, 0, 1420, 780));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(directory, $"hardware-{theme}-{scale * 100:0}.png"))) encoder.Save(stream);
            Check(view.ActualWidth > 900 && view.ActualHeight > 500, "dense shell laid out");
            window.Close();
            view.ShutdownAsync().GetAwaiter().GetResult();
        }
        SkinCatalog.Apply(Application.Current, "retro-neon");
        Console.WriteLine("PASS: synthetic hardware proof; nine rendered root-DPI/theme samples, three fixture performance rounds.");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    internal static async Task ReadOnlyAsync(string reportPath, HardwareClass? selected = null)
    {
        if (!Path.IsPathFullyQualified(reportPath) || File.Exists(reportPath) || !Directory.Exists(Path.GetDirectoryName(reportPath)))
            throw new ArgumentException("Neuer absoluter Berichtspfad erforderlich.");
        using var listener = new TextWriterTraceListener(reportPath + ".diagnostics.log");
        Trace.Listeners.Add(listener);
        Trace.AutoFlush = true;
        var store = new HardwareInventoryStore();
        var reader = new HardwareIntelligenceReader();
        var measurements = new List<object>();
        foreach (var category in Enum.GetValues<HardwareClass>().Where(c => c != HardwareClass.Sensors && (selected is null || c == selected)))
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var components = await reader.ReadAsync(category, CancellationToken.None);
                store.Replace(category, components, 0);
                var states = components.SelectMany(c => c.Facts).GroupBy(f => f.State).ToDictionary(g => g.Key, g => g.Count());
                Console.WriteLine($"{category}: {components.Count} components; {JsonSerializer.Serialize(states)}; {watch.Elapsed.TotalMilliseconds:0} ms");
                measurements.Add(new { Class = category.ToString(), Components = components.Count, States = states, ElapsedMs = watch.Elapsed.TotalMilliseconds });
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                Trace.TraceError("Read-only {0}: {1}", category, ex);
                Console.WriteLine($"{category}: PROVIDER ERROR {ex.GetType().Name} HRESULT 0x{ex.HResult:X8}");
                store.Fail(category, "Read-only-Prüfung: " + ex.GetType().Name, DataAvailability.ProviderError, DateTimeOffset.UtcNow);
                measurements.Add(new { Class = category.ToString(), Error = ex.GetType().Name, ElapsedMs = watch.Elapsed.TotalMilliseconds });
            }
        }
        File.WriteAllText(reportPath, HardwareReport.Export(store.Snapshot(DateTimeOffset.UtcNow), "json", false));
        File.WriteAllText(reportPath + ".timings.json", JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        Trace.Listeners.Remove(listener);
        Console.WriteLine("Read-only-Prüfung abgeschlossen; Bericht redigiert, keine Sensor-/Kerneltreiber geöffnet.");
    }
}
