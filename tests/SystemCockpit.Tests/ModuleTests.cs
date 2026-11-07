using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Services;

internal static class ModuleTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }

        // ---- Paket-Center: Tabellenparser ----
        static string Row(string name, string id, string version, string available, string source) =>
            name.PadRight(25) + id.PadRight(28) + version.PadRight(11) + available.PadRight(12) + source;
        var table = "Irrelevante Vorabzeile\n" +
            Row("Name", "Id", "Version", "Verfügbar", "Quelle") + "\n" + new string('-', 82) + "\n" +
            Row("Microsoft PowerToys", "Microsoft.PowerToys", "0.84.1", "0.85.0", "winget") + "\n" +
            Row("Langer Anwendungsname m\u2026", "Publisher.SehrLangeId.A\u2026", "1.0", "2.0", "winget") + "\n" +
            Row("NVIDIA Grafiktreiber", "ARP\\Machine\\X64\\NVIDIA G\u2026", "560.94", "", "") + "\n" +
            Row("7-Zip", "7zip.7zip", "24.08", "24.09", "winget") + "\n";
        var rows = PackageCenter.ParseTable(table);
        Check(rows.Count == 4, "Parser liest alle Tabellenzeilen unter der Trennlinie");
        Check(rows[0].Id == "Microsoft.PowerToys" && rows[0].Available == "0.85.0" && rows[0].Source == "winget",
            "Spalten werden aus den Kopfzeilen-Offsets geschnitten");
        Check(rows[0].Actionable && rows[3].Actionable, "Vollstaendige IDs sind aktionierbar");
        Check(!rows[1].Actionable && !rows[2].Actionable, "Abgeschnittene IDs (U+2026) bleiben ohne Aktion");
        Check(PackageCenter.ParseTable("Es wurden keine installierten Pakete gefunden.").Count == 0,
            "Ohne Tabellenkopf werden keine Zeilen erfunden");
        Check(PackageCenter.ValidId("9NBLGGH4NNS1") && PackageCenter.ValidId("Git.Git"),
            "Uebliche Store-/Manifest-IDs sind gueltig");
        foreach (var bad in new[] { "", "a'b", "x$y", "mit\"quote", "back`tick", "endet.", new string('a', 201), "abc\u2026" })
            Check(!PackageCenter.ValidId(bad), $"ID '{bad}' darf nicht aktionierbar sein");

        // ---- Paket-Center: Skript-Builder (nur geparst, nie ausgefuehrt) ----
        var upgrade = PackageCenter.ActionScript("upgrade", ["Microsoft.PowerToys", "7zip.7zip"]);
        Check(upgrade.Contains("--id 'Microsoft.PowerToys' --exact") && upgrade.Contains("--id '7zip.7zip' --exact"),
            "Jede ID laeuft einzeln und exakt");
        Check(upgrade.Contains("--silent --accept-package-agreements") && upgrade.Contains("$script:aetherPkgFails"),
            "Upgrade laeuft silent; Fehler werden pro ID gezaehlt statt den Lauf abzubrechen");
        var uninstall = PackageCenter.ActionScript("uninstall", ["7zip.7zip"]);
        Check(!uninstall.Contains("--silent") && !uninstall.Contains("--accept-package-agreements"),
            "Deinstallation erzwingt kein silent und akzeptiert keine Paketlizenzen");
        foreach (var (verb, ids, reason) in new (string, string[], string)[]
        {
            ("delete", ["Git.Git"], "unbekanntes Verb"),
            ("upgrade", [], "leere Liste"),
            ("upgrade", ["Git.Git", "git.git"], "doppelte IDs"),
            ("upgrade", ["abc\u2026"], "abgeschnittene ID"),
            ("upgrade", Enumerable.Range(0, 101).Select(i => $"Id.Nr{i}").ToArray(), "mehr als 100 IDs")
        })
        {
            try { PackageCenter.ActionScript(verb, ids); throw new InvalidOperationException("Validierung fehlt: " + reason); }
            catch (ArgumentException) { count++; }
        }

        Check(PackageCenter.SearchScript("power toys").Contains("search 'power toys'"), "Suche quotet den Begriff");
        try { PackageCenter.SearchScript("a'b"); throw new InvalidOperationException("Suchvalidierung fehlt"); }
        catch (ArgumentException) { count++; }
        Check(PackageCenter.UpgradeListScript.Contains("winget.exe upgrade") &&
            PackageCenter.UpgradeListScript.Contains("-1978335189"), "Updateliste behandelt 'keine Upgrades' als Ergebnis");

        // ---- Shellbag-Analyzer: Dekodierung synthetischer Elemente ----
        var guid = Guid.NewGuid();
        var root = new byte[20];
        root[2] = 0x1F;
        guid.ToByteArray().CopyTo(root, 4);
        var decodedRoot = ShellbagAnalyzer.DecodeItem(root);
        Check(decodedRoot.Name == "{" + guid + "}" && decodedRoot.Kind == "Systemordner-GUID" && decodedRoot.Decoded,
            "GUID-Wurzeln werden als Systemordner gemeldet");
        var drive = new byte[8];
        drive[2] = 0x2F; drive[3] = (byte)'D'; drive[4] = (byte)':';
        Check(ShellbagAnalyzer.DecodeItem(drive) is ("D:\\", "Laufwerk", true), "Laufwerkselemente liefern den Buchstaben");
        var folder = new byte[120];
        folder[2] = 0x31;
        System.Text.Encoding.ASCII.GetBytes("DOKUME~1").CopyTo(folder, 14);
        var block = 32; // BEEF0004 v9: Langname bei Blockoffset 46
        folder[block] = 60; folder[block + 2] = 9;
        folder[block + 4] = 0x04; folder[block + 5] = 0x00; folder[block + 6] = 0xEF; folder[block + 7] = 0xBE;
        System.Text.Encoding.Unicode.GetBytes("Dokumente").CopyTo(folder, block + 46);
        var decodedFolder = ShellbagAnalyzer.DecodeItem(folder);
        Check(decodedFolder is ("Dokumente", "Ordner", true), "UTF-16-Langname aus BEEF0004 v9 wird gelesen");
        var shortOnly = new byte[32];
        shortOnly[2] = 0x32;
        System.Text.Encoding.ASCII.GetBytes("ALT~1").CopyTo(shortOnly, 14);
        var decodedShort = ShellbagAnalyzer.DecodeItem(shortOnly);
        Check(decodedShort.Name == "ALT~1" && decodedShort.Kind.Contains("Kurzname"), "Ohne Langname bleibt der Kurzname");
        var unknown = ShellbagAnalyzer.DecodeItem([0x04, 0x00, 0x74, 0x00]);
        Check(!unknown.Decoded && unknown.Name.Contains("0x74"), "Unbekannte Typen werden explizit gemeldet, nie erfunden");
        var real = ShellbagAnalyzer.Read();
        Check(real.Count <= ShellbagAnalyzer.MaxEntries, "Lesende HKCU-Analyse respektiert das Eintragslimit");

        // ---- Kernelzugriffs-Diagnose: erklaert fehlende CPU-Werte, ohne etwas zu veraendern ----
        Check(HardwareSensors.KernelAccessHint.StartsWith("Kernelzugriff:") && HardwareSensors.KernelAccessHint.Length > 40,
            "Kernelzugriffs-Status wird als klare Diagnosezeile geliefert");
        Check(HardwareSensors.KernelAccessHint.Contains("PawnIO"),
            "Diagnose nennt den HVCI-kompatiblen Treiberweg beim Namen");

        // ---- GodMode: Verfuegbarkeit wird lesend erkannt, nichts wird geraten ----
        Check(MainWindow.GodModeClsid == "{ED7BA470-8E54-465E-825C-99712043E26C}", "GodMode-CLSID ist die dokumentierte Alle-Aufgaben-ID");
        using (var clsid = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\" + MainWindow.GodModeClsid))
            Check(MainWindow.GodModeAvailable() == (clsid is not null),
                "GodMode-Erkennung folgt exakt der Registrierung dieses Windows-Builds");

        // ---- Erststart / EULA-Zustimmung: lokal, pflichtig, versioniert ----
        var consentDir = Path.Combine(Path.GetTempPath(), "Aether-eula-" + Guid.NewGuid().ToString("N"));
        try
        {
            var consent = new EulaConsent(consentDir);
            Check(!consent.IsAccepted(), "Ohne Marker gilt die EULA als nicht akzeptiert");
            consent.Accept();
            Check(consent.IsAccepted() && new EulaConsent(consentDir).IsAccepted(),
                "Zustimmung wird lokal gespeichert und erneut gelesen");
            File.WriteAllText(Path.Combine(consentDir, "eula-consent.json"),
                "{\"version\":\"0.0\",\"accepted\":true}");
            Check(!new EulaConsent(consentDir).IsAccepted(),
                "Zustimmung zu einer aelteren EULA-Version zaehlt nicht");
            Check(EulaConsent.Text.Contains("ENDNUTZER-LIZENZVEREINBARUNG") &&
                EulaConsent.Text.Contains("KEINE Gewaehrleistung") && EulaConsent.Text.Contains("MPL 2.0"),
                "EULA nennt Gewaehrleistungsausschluss und Fremdlizenzen");
        }
        finally { if (Directory.Exists(consentDir)) Directory.Delete(consentDir, recursive: true); }

        // ---- Inventar: lesende Software-Erfassung und Textausgabe ----
        var software = SystemInventory.ReadInstalledSoftware();
        Check(software.Count > 5 && software.All(s => s.Name.Length > 0),
            "Installierte Software wird aus der Registry gelesen (nicht leer)");
        Check(software.Zip(software.Skip(1)).All(p =>
            string.Compare(p.First.Name, p.Second.Name, StringComparison.CurrentCultureIgnoreCase) <= 0),
            "Softwareliste ist nach Name sortiert");
        var fixtureSnapshot = new InventorySnapshot(DateTimeOffset.Now,
            [new InventoryItem("Prozessor", "CPU", "Test-CPU"), new InventoryItem("System", "Windows", "Windows 11")],
            [new InstalledSoftware("Beispiel App", "1.2.3", "Beispiel GmbH")]);
        var text = SystemInventory.ToText(fixtureSnapshot);
        Check(text.Contains("RECHNER-INVENTAR") && text.Contains("== HARDWARE") &&
            text.Contains("Beispiel App | 1.2.3 | Beispiel GmbH") && text.Contains("INSTALLIERTE SOFTWARE (1)"),
            "Inventar-Textausgabe enthaelt Hardware- und Softwareabschnitt");

        // ---- Erststart-Fenster: EULA sichtbar, Zustimmung erzwingt Freigabe ----
        var firstRun = new FirstRunWindow();
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var eulaBox = (TextBox)firstRun.FindName("EulaText");
            var acceptCheck = (CheckBox)firstRun.FindName("AcceptCheck");
            var nextButton = (Button)firstRun.FindName("NextButton");
            Check(eulaBox.Text.Contains("ENDNUTZER-LIZENZVEREINBARUNG"), "Erststart zeigt den EULA-Text");
            var showStep = typeof(FirstRunWindow).GetMethod("ShowStep", flags)!;
            showStep.Invoke(firstRun, [1]);
            Check(!nextButton.IsEnabled, "Weiter ist ohne Zustimmung gesperrt");
            acceptCheck.IsChecked = true;
            Check(nextButton.IsEnabled, "Zustimmung gibt den Weiter-Knopf frei");
        }
        finally { firstRun.Close(); }

        var inventoryWizard = new FirstRunWindow();
        try
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var inventory = (SystemCockpit.Controls.InventoryView)inventoryWizard.FindName("Inventory");
            var next = (Button)inventoryWizard.FindName("NextButton");
            var back = (Button)inventoryWizard.FindName("BackButton");
            typeof(FirstRunWindow).GetMethod("ShowStep", flags)!.Invoke(inventoryWizard, [2]);
            Check(!next.IsEnabled, "First inventory cannot be skipped before it starts");
            var delayed = new TaskCompletionSource<InventorySnapshot>();
            inventory.BuildInventory = (_, _) => delayed.Task;
            var pending = inventory.LoadAsync();
            Check(inventory.IsBusy && !next.IsEnabled && !back.IsEnabled &&
                ((ProgressBar)inventory.FindName("InvestigationProgress")).IsIndeterminate &&
                ((Border)inventory.FindName("Investigation")).Visibility == Visibility.Visible,
                "Pending first inventory shows progress and blocks both navigation directions");
            typeof(FirstRunWindow).GetMethod("NextClicked", flags)!.Invoke(inventoryWizard, [next, new RoutedEventArgs()]);
            Check(inventoryWizard.DialogResult is null && inventory.IsBusy, "Reentrant/programmatic advance cannot bypass inventory");
            delayed.SetResult(new InventorySnapshot(DateTimeOffset.Now, [], []));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(pending.GetAwaiter().GetResult() && inventory.HasCompleted && next.IsEnabled && back.IsEnabled &&
                !((ProgressBar)inventory.FindName("InvestigationProgress")).IsIndeterminate,
                "Only a completed inventory enables cockpit start and stops progress");
            inventory.BuildInventory = (_, _) => Task.FromException<InventorySnapshot>(new IOException("fixture read failure"));
            Check(!inventory.LoadAsync().GetAwaiter().GetResult() && !next.IsEnabled && !inventory.HasCompleted &&
                ((TextBlock)inventory.FindName("Status")).Text.Contains("fixture read failure"),
                "Failed inventory invalidates previous success and keeps start locked with an explicit error");
            inventory.BuildInventory = (_, _) => Task.FromResult(new InventorySnapshot(DateTimeOffset.Now, [], []));
            Check(inventory.LoadAsync().GetAwaiter().GetResult() && next.IsEnabled, "Explicit inventory retry unlocks start after success");
        }
        finally { inventoryWizard.Close(); }

        // ---- Speedtest / Bewertung: Formatierung und Validierung (ohne Netz) ----
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Check(SpeedTest.FormatResult(1048576, 1) == "1.0 MiB in 1.0 s = 8.4 Mbit/s", "Mbit/s-Umrechnung aus Bytes und Sekunden");
            var assess = CacheCatalog.Assess(new("Chrome / Default / Cache", @"C:\x", 1, "Browser schliessen"), 12, 2097152);
            Check(assess.Contains("B / App-Cache") && assess.Contains("12 Dateien") && assess.Contains("2.00 MiB") &&
                assess.Contains("Browser schliessen"), "Bewertung nennt Stufe, Summen und Katalog-Hinweis");
            Check(CacheCatalog.Assess(new("Benutzer Temp", @"C:\x", 0, "r"), 0, 0).Contains("A / risikoarm") &&
                CacheCatalog.Assess(new("Prefetch", @"C:\x", 3, "r"), 1, 1).Contains("D / systemnah"),
                "Stufen folgen dem Katalog-Level");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        try { SpeedTest.FormatResult(10, 0); throw new InvalidOperationException("FormatResult-Validierung fehlt"); }
        catch (ArgumentOutOfRangeException) { count++; }
        foreach (var size in new[] { 0, SpeedTest.MaxMiB + 1 })
        {
            try
            {
                SpeedTest.DownloadAsync(size, CancellationToken.None).GetAwaiter().GetResult();
                throw new InvalidOperationException("Speedtest-Groessenvalidierung fehlt");
            }
            catch (ArgumentException) { count++; }
        }

        // ---- Overlay: Modellwerte, Grenzwertkanaele und Warnuebergang ----
        var model = new CockpitModel();
        var hot = new LiveSample(DateTimeOffset.Now, 10, 30, 10, 32, null, null, [],
        [
            new("/intelcpu/0/temperature/9", "CPU", "CPU Package", "Temperature", 95, "C"),
            new("/nvme/0/temperature/0", "SSD", "Composite Temperature", "Temperature", 41, "C"),
            new("/nvme/0/temperature/10", "SSD", "Warning Temperature", "Temperature", 90, "C")
        ]);
        model.Apply(hot, true);
        Check(model.StorageTemperature.StartsWith("41"), "Overlay zeigt den heissesten echten Storage-Messkanal");
        Check(model.CpuTempWarning && !model.StorageTempWarning && !model.GpuTempWarning,
            "Warnflags folgen den konfigurierten Schwellen");
        Check(model.OverlayAlert.Contains("CPU") && !model.OverlayAlert.Contains("STORAGE"),
            "Warnbanner nennt nur ueberschrittene Komponenten");
        var sidebar = new SidebarWindow(model, new CockpitSettings(), _ => { });
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(ReferenceEquals(sidebar.CpuChart.Content, model.Cards[0]) &&
                  ReferenceEquals(sidebar.MemoryChart.Content, model.Cards[1]) &&
                  ReferenceEquals(sidebar.GpuChart.Content, model.Cards[2]),
                "xBar reuses the live CPU, RAM and GPU instruments and curves");
            Check(ReferenceEquals(sidebar.TemperatureCharts.ItemsSource, model.SidebarTemperatures) &&
                  ReferenceEquals(sidebar.VolumePies.ItemsSource, model.Volumes),
                "xBar binds temperature histories and volume pie charts");
            Check(model.SidebarTemperatures[0].Trace.Count == 1 &&
                  model.SidebarTemperatures[2].NumericValue == 41d.ToString("0.0"),
                "Temperature histories receive real CPU and SSD samples, not temperature limits");
        }
        finally { sidebar.Close(); }
        foreach (var free in new[] { 0d, 25, 50, 75, 100 })
        {
            var sector = SystemCockpit.Controls.DrivePie.UsedSector(free);
            var circleArea = new System.Windows.Media.EllipseGeometry(new Point(0.5, 0.5), 0.5, 0.5)
                .GetArea(0.00001, System.Windows.Media.ToleranceType.Absolute);
            Check(Math.Abs(sector.GetArea(0.00001, System.Windows.Media.ToleranceType.Absolute) -
                           circleArea * (100 - free) / 100) < circleArea * 0.0005,
                $"Drive pie sector area matches {100 - free}% used: actual " +
                $"{sector.GetArea(0.00001, System.Windows.Media.ToleranceType.Absolute):G17}, circle {circleArea:G17}");
            Check(new Check("C:", "", "ok", "", free).UsedPercent == 100 - free,
                $"Drive usage label matches pie for {free}% free");
        }
        foreach (var free in new double?[] { null, double.NaN, double.PositiveInfinity, -1, 101 })
            Check(new Check("C:", "", "unknown", "", free).UsedPercent is null &&
                  new Check("C:", "", "unknown", "", free).UsageSummary == "Nicht verfuegbar",
                "Unknown or invalid drive space is unavailable, not zero or full");
        var leftWorkArea = new System.Drawing.Rectangle(-1920, 0, 1920, 1080);
        var docked = OverlayDocking.Snap(leftWorkArea, new System.Drawing.Rectangle(-900, 400, 250, 300));
        Check(docked.Side == "right" && docked.Left == -250 && docked.Top == 400,
            "Overlay dockt nach dem Ziehen an die naechste Monitorseite");
        var restored = OverlayDocking.Restore(leftWorkArea, new System.Drawing.Size(250, 300), "left", 0.5);
        Check(restored.Left == -1920 && restored.Top == 390,
            "Gespeicherte Monitorseite und relative Hoehe stellen sich passend wieder her");
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        foreach (var area in new[]
                 {
                     new System.Drawing.Rectangle(0, 0, 1920, 1040),
                     new System.Drawing.Rectangle(-2560, -180, 2560, 1400),
                     new System.Drawing.Rectangle(48, 0, 800, 600)
                 })
        {
            foreach (var side in new[] { "left", "right" })
            {
                var pinned = SidebarPlacement.Restore(area, scale, true, side, 0.4, 0.6);
                Check(area.Contains(pinned) && pinned.Height == area.Height &&
                      pinned.Width == Math.Min(area.Width, (int)Math.Round(350 * scale)) &&
                      (side == "left" ? pinned.Left == area.Left : pinned.Right == area.Right),
                    $"Pinned xBar fits the physical work area at scale {scale} on {side}");
            }
            var floating = SidebarPlacement.Restore(area, scale, false, "right", 0.37, 0.46);
            Check(area.Contains(floating) && floating.Left > area.Left && floating.Right < area.Right,
                $"Floating xBar remains inside the work area without snapping at scale {scale}");
            var offsets = SidebarPlacement.Capture(area, floating);
            Check(SidebarPlacement.Restore(area, scale, false, "left",
                    offsets.Horizontal, offsets.Vertical) == floating,
                $"Floating position restores independently of dock side at scale {scale}");
            var outside = new System.Drawing.Rectangle(area.Right + 30, area.Bottom + 30,
                floating.Width, floating.Height);
            var clamped = SidebarPlacement.Capture(area, outside);
            Check(area.Contains(SidebarPlacement.Restore(area, scale, false, "right",
                    clamped.Horizontal, clamped.Vertical)),
                $"Off-screen floating positions are clamped at scale {scale}");
        }
        var overlay = new OverlayWindow(model, new CockpitSettings(), _ => { });
        try
        {
            Check(overlay.AlertBanner.Visibility == Visibility.Visible, "Banner uebernimmt den aktiven Warnzustand beim Oeffnen");
            var cool = hot with
            {
                Sensors = [new SensorReading("/nvme/0/temperature/10", "SSD", "Warning Temperature", "Temperature", 90, "C")]
            };
            model.Apply(cool, true);
            Check(model.StorageTemperature == "Nicht verfuegbar",
                "Nur Grenzwertkanaele ergeben keinen Storage-Messwert; Grenzwerte sind keine Temperaturen");
            Check(model.SidebarTemperatures.All(graph => graph.Trace.Count == 0 && graph.Value == "--"),
                "Missing temperatures clear the chart rather than leaving stale curves");
            Check(overlay.AlertBanner.Visibility == Visibility.Collapsed && model.OverlayAlert.Length == 0,
                "Banner verschwindet, wenn keine Schwelle mehr ueberschritten ist");
            model.MeasurementFailed("Testfall");
            Check(model.StorageTemperature == "Nicht verfuegbar" && !model.CpuTempWarning && model.OverlayAlert.Length == 0,
                "Fehlgeschlagene Messung loescht Overlay-Werte statt alte Werte zu zeigen");
            Check(model.SidebarTemperatures.All(graph => graph.Trace.Count == 0) &&
                  model.SidebarUpload.Trace.Count == 0 && model.SidebarUpload.Value == "--",
                "Measurement failures clear all sidebar temperature and upload histories");
            for (var i = 0; i < 95; i++) model.Apply(hot with { UploadMbps = 1 }, true);
            Check(model.SidebarTemperatures[0].Trace.Count == 90 &&
                  model.SidebarTemperatures[2].Trace.Count == 90 &&
                  model.SidebarUpload.Trace.Count == 90,
                "Temperature and upload histories are bounded to 90 samples");
        }
        finally { overlay.Close(); }
        return count;
    }

    public static int LiveSidebarUi(string? screenshotPrefix = null)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        var saved = new List<CockpitSettings>();
        var model = new CockpitModel();
        for (var i = 0; i < 12; i++)
            model.Apply(new(DateTimeOffset.Now, 20 + i * 2, 45, 14.4, 32, 3 + i, 1 + i * 0.2,
            [
                new("C:", "250 GB frei", "ok", "Testlaufwerk", 25),
                new("D:", "750 GB frei", "ok", "Testlaufwerk", 75),
                new("E:", "Nicht verfuegbar", "unknown", "Testlaufwerk")
            ],
            [
                new("/intelcpu/0/temperature/0", "CPU", "CPU Package", "Temperature", 40 + i, "C"),
                new("/gpu-nvidia/0/load/0", "GPU", "GPU Core", "Load", 30 + i, "%"),
                new("/gpu-nvidia/0/temperature/0", "GPU", "GPU Core", "Temperature", 50 + i, "C"),
                new("/nvme/0/temperature/0", "SSD", "Composite Temperature", "Temperature", 35 + i, "C")
            ]), true);
        var sidebar = new SidebarWindow(model, new CockpitSettings { SidebarAutoHide = true },
            saved.Add);
        try
        {
            sidebar.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(sidebar.IsVisible && SidebarDisplays.GetAll().Count > 0,
                "xBar opens on a Windows display");
            Check(sidebar.MonitorSelect.Items.Count > 0 && sidebar.TemperatureCharts.Items.Count == 3,
                "xBar monitor selector and temperature charts are populated without a launcher");
            var handle = new System.Windows.Interop.WindowInteropHelper(sidebar).Handle;
            var display = SidebarDisplays.ForWindow(handle);
            var pinnedBounds = SidebarDisplays.GetWindowBounds(handle);
            Check(display.WorkArea.Contains(pinnedBounds) && pinnedBounds.Right == display.WorkRight &&
                  pinnedBounds.Height == display.WorkHeight,
                "Pinned native window fits the actual work area including scaling and taskbar");
            Check(sidebar.OptionsButton.PointToScreen(new Point(sidebar.OptionsButton.ActualWidth,
                    sidebar.OptionsButton.ActualHeight)).X <= display.WorkRight,
                "Options button is fully inside the display");
            Check(!Descendants(sidebar).OfType<Button>().Any(button =>
                    button.Content is string title && (title.Contains("hinzufuegen") || title.Contains("Herunterfahren"))) &&
                  !Descendants(sidebar).Any(element => element is ListView or DataGrid),
                "Diagram-only sidebar contains no launcher, power-action or data lists");
            var pies = Descendants(sidebar).OfType<SystemCockpit.Controls.DrivePie>().ToArray();
            Check(pies.Length == 3 && pies[0].FreePercent == 25 && pies[1].FreePercent == 75 &&
                  pies[2].FreePercent is null,
                "Each volume has a bound pie chart, including an explicit unavailable volume");
            Check(Descendants(sidebar).OfType<System.Windows.Shapes.Polyline>().Count(p => p.StrokeThickness == 2) == 8,
                "CPU, RAM, GPU, three temperatures and both network directions render live curves");
            sidebar.OptionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(sidebar.OptionsPopup.IsOpen, "xBar options popup opens");
            foreach (var (toggle, chart) in new (CheckBox, FrameworkElement)[]
                     {
                         (sidebar.CpuWidgetToggle, sidebar.CpuChart),
                         (sidebar.MemoryWidgetToggle, sidebar.MemoryChart),
                         (sidebar.GpuWidgetToggle, sidebar.GpuChart),
                         (sidebar.TemperatureWidgetToggle, sidebar.TemperatureCharts),
                         (sidebar.NetworkWidgetToggle, sidebar.NetworkCharts),
                         (sidebar.DrivesWidgetToggle, sidebar.DriveCharts)
                     })
            {
                toggle.IsChecked = false;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Check(chart.Visibility == Visibility.Collapsed && saved.Count > 0,
                    $"Diagram group {chart.Name} can be hidden and saved");
                var roundtrip = System.Text.Json.JsonSerializer.Deserialize<CockpitSettings>(
                    System.Text.Json.JsonSerializer.Serialize(saved[^1]))!;
                roundtrip.Validate();
                Check(new[]
                    {
                        roundtrip.SidebarWidgetCpu, roundtrip.SidebarWidgetMemory, roundtrip.SidebarWidgetGpu,
                        roundtrip.SidebarWidgetTemperatures, roundtrip.SidebarWidgetNetwork, roundtrip.SidebarWidgetDrives
                    }.Count(enabled => !enabled) == 1,
                    $"Diagram group {chart.Name} configuration survives serialization");
                toggle.IsChecked = true;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Check(chart.Visibility == Visibility.Visible,
                    $"Diagram group {chart.Name} can be shown again");
            }
            sidebar.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!model.Settings.SidebarPinned, "xBar can switch to a floating window");
            Check(sidebar.PinnedToggle.IsChecked == false && !sidebar.AutoHideToggle.IsEnabled,
                "FLOAT synchronizes pin options and disables auto-hide even when previously enabled");
            sidebar.OptionsPopup.IsOpen = false;
            var bounds = SidebarDisplays.GetWindowBounds(handle);
            var left = display.WorkLeft + (int)Math.Round((display.WorkWidth - bounds.Width) * 0.37);
            var top = display.WorkTop + (int)Math.Round((display.WorkHeight - bounds.Height) * 0.46);
            Check(SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, 0x0015),
                "Test can move the native floating window");
            sidebar.CaptureFloatingPosition();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var moved = SidebarDisplays.GetWindowBounds(handle);
            Check(Math.Abs(moved.Left - left) <= 1 && Math.Abs(moved.Top - top) <= 1 &&
                  display.WorkArea.Contains(moved),
                "Releasing FLOAT preserves the chosen interior position instead of docking");
            Check(saved[^1].SidebarHorizontalOffset > 0 && saved[^1].SidebarHorizontalOffset < 1,
                "Horizontal floating position is persisted");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
            var frame = new DispatcherFrame();
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Check(SidebarDisplays.GetWindowBounds(handle) == moved,
                "Behavior timer never auto-hides or re-docks FLOAT");
            var reopened = new SidebarWindow(model, saved[^1], saved.Add);
            try
            {
                reopened.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var reopenedBounds = SidebarDisplays.GetWindowBounds(
                    new System.Windows.Interop.WindowInteropHelper(reopened).Handle);
                Check(reopenedBounds == moved, "Reopening xBar restores the saved floating position");
            }
            finally { reopened.Close(); }
            if (screenshotPrefix is not null)
            {
                SaveScreenshot(sidebar, screenshotPrefix + "-graphs.png");
                sidebar.DriveCharts.BringIntoView();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                SaveScreenshot(sidebar, screenshotPrefix + "-drives.png");
            }
        }
        finally { sidebar.Close(); }
        return assertions;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    internal static void SaveScreenshot(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // Nur mit --winget-live: liest die reale winget-Ausgabe (list/upgrade) und prueft den Parser.
    internal static int InventoryUi(string? screenshot)
    {
        var window = new FirstRunWindow { ShowInTaskbar = false };
        var inventory = (SystemCockpit.Controls.InventoryView)window.FindName("Inventory");
        using var stop = new CancellationTokenSource();
        Task<bool>? pending = null;
        var count = 0;
        try
        {
            inventory.BuildInventory = async (token, progress) =>
            {
                progress.Report("Synthetische Abfragestufe: Rechner wird untersucht ...");
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("The pending fixture must be cancelled.");
            };
            typeof(FirstRunWindow).GetMethod("ShowStep", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [2]);
            window.Show();
            pending = Dispatcher.CurrentDispatcher.Invoke(() => inventory.LoadAsync(stop.Token));
            foreach (var size in new[] { new Size(1000, 760), new Size(820, 640) })
            {
                window.Width = size.Width; window.Height = size.Height;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var progress = (Border)inventory.FindName("Investigation");
                var bottom = progress.TranslatePoint(new Point(progress.ActualWidth, progress.ActualHeight), window);
                if (!progress.IsVisible || progress.ActualHeight < 60 || bottom.Y >= window.ActualHeight - 60 ||
                    ((Button)window.FindName("NextButton")).IsEnabled ||
                    !((TextBlock)inventory.FindName("Status")).Text.Contains("untersucht"))
                    throw new InvalidOperationException("First inventory must visibly explain the investigation and block navigation at both window sizes.");
                count++;
            }
            if (screenshot is not null) SaveScreenshot(window, screenshot);
            return count;
        }
        finally
        {
            stop.Cancel();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.Close();
            if (pending is not null && !pending.IsCompleted) throw new InvalidOperationException("Inventory cancellation did not release its pending UI task.");
        }
    }

    // Es wird nichts installiert, aktualisiert oder entfernt.
    public static async Task<int> WingetLive()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        var list = await AdminShell.RunAsync(PackageCenter.ListScript, TimeSpan.FromMinutes(5), CancellationToken.None);
        Check(list.ExitCode == 0, $"winget list Exit {list.ExitCode}: {list.Error}");
        var rows = PackageCenter.ParseTable(list.Output);
        Console.WriteLine($"WINGET LIVE: list {rows.Count} Zeilen / {rows.Count(r => r.Actionable)} aktionierbar / " +
            $"{rows.Count(r => !r.Actionable)} ohne eindeutige ID");
        Check(rows.Count > 10 && rows.Count(r => r.Actionable) > 0, "Reale winget-list-Tabelle wird geparst");
        Check(rows.All(r => r.Name.Length > 0 && r.Id.Length > 0), "Kein leerer Name/keine leere ID aus realer Tabelle");
        var upgrade = await AdminShell.RunAsync(PackageCenter.UpgradeListScript, TimeSpan.FromMinutes(5), CancellationToken.None);
        Check(upgrade.ExitCode == 0, $"winget upgrade Exit {upgrade.ExitCode}: {upgrade.Error}");
        var updates = PackageCenter.ParseTable(upgrade.Output);
        Console.WriteLine($"WINGET LIVE: upgrade {updates.Count} Zeilen / {updates.Count(r => r.Actionable)} aktionierbar; " +
            "nur gelesen, nichts installiert");
        Check(updates.All(r => r.Available.Length > 0 || !r.Actionable || updates.Count == 0),
            "Updatezeilen tragen eine Zielversion oder sind nicht aktionierbar");
        return count;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
