using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class AdminTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        Check(AdminManagement.Areas.Count == 13 && AdminManagement.Areas.All(a => a.Actions.Count > 0 && a.Description.Length > 0),
            "All administration domains expose explicit status queries, actions and explanations");
        Check(AdminManagement.Quote("O'Brien; $(x)") == "'O''Brien; $(x)'", "PowerShell arguments quote apostrophe/metacharacters as data");
        var cpu = new List<SensorReading>
        {
            new("/intelcpu/0/temperature/0", "CPU", "CPU Package", "Temperature", null, "C"),
            new("/intelcpu/0/temperature/1", "CPU", "CPU Core #1", "Temperature", 40, "C"),
            new("/intelcpu/0/temperature/2", "CPU", "CPU Core #2", "Temperature", 45, "C"),
            new("/lpc/temperature/1", "Board", "CPU", "Temperature", 65, "C")
        };
        Check(SensorSemantics.CpuTemperature(cpu)?.Value == 45, "Use actual hottest CPU core when package unavailable, never board zone");
        cpu[0] = cpu[0] with { Value = 41 };
        Check(SensorSemantics.CpuTemperature(cpu)?.Name == "CPU Package", "Readable CPU package remains preferred");
        Check(SensorSemantics.CpuTemperature([new("/amdcpu/0/temperature/0", "AMD", "Core (Tctl/Tdie)", "Temperature", 50, "C")])?.Value == 50,
            "AMD CPU telemetry is supported by semantic selector");
        Check(SensorSemantics.CpuTemperature([cpu[^1]]) is null, "Never fake CPU temperature using board or ACPI zone");
        var area = AdminManagement.Areas.Single(a => a.Id == "processes");
        var plan = AdminManagement.Plan(area, "Prioritaet BelowNormal", new("9999", "Fixture", "Normal", "", "123456"), "");
        Check(plan.Script.Contains("StartTime.ToUniversalTime().Ticks") && plan.RestoreScript is not null &&
            !plan.Script.Contains("Realtime"), "Process action uses exact identity and reversible bounded priority");
        try { AdminManagement.Plan(area, "Beenden", new("4", "System", "", "", "123"), ""); throw new InvalidOperationException("Protected PID accepted"); }
        catch (ArgumentException) { count++; }
        var adapter = AdminManagement.Areas.Single(a => a.Id == "adapters");
        try { AdminManagement.Plan(adapter, "DNS setzen", new("10", "Ethernet", "Up", "", Guid.NewGuid().ToString()), "not-a-dns"); throw new InvalidOperationException("Invalid DNS accepted"); }
        catch (ArgumentException) { count++; }
        var dns = AdminManagement.Plan(adapter, "DNS setzen", new("10", "Ethernet", "Up", "", Guid.NewGuid().ToString(),
            """[{"Family":"IPv4","Servers":""},{"Family":"IPv6","Servers":"2606:4700:4700::1111"}]"""), "1.1.1.1,2606:4700:4700::1111");
        Check(dns.Script.Contains("Set-DnsClientServerAddress") && dns.RestoreScript?.Contains("-ResetServerAddresses") == true &&
            dns.RestoreScript.Contains("2606:4700:4700::1111"), "DNS rollback preserves automatic IPv4 and static IPv6 separately");
        Check(AdminManagement.ProxyRestore("pushd winhttp\nreset proxy\npopd")?.Contains("reset proxy") == true,
            "Native WinHTTP dump produces explicit direct-proxy restoration");
        Check(AdminManagement.ProxyRestore("set proxy proxy-server=\"$(danger):8080\" bypass-list=\"O'Brien\"")?.Contains("'bypass-list=O''Brien'") == true,
            "Proxy backup arguments are escaped as data, never interpreted PowerShell");
        Check(AdminManagement.ProxyRestore("unexpected command") is null, "Unsupported WinHTTP dump is explicitly non-reversible");
        var services = AdminManagement.Areas.Single(a => a.Id == "services");
        var service = AdminManagement.Plan(services, "Starttyp Manuell", new("FixtureService", "Fixture", "Running", "", "Auto", "1"), "");
        Check(service.RestoreScript?.Contains("DelayedAutoStart -Value 1") == true, "Service restore preserves delayed automatic start");
        Check(RegistryTweaks.Catalog.Count >= 8 && RegistryTweaks.Catalog.Select(t => t.Id).Distinct().Count() == RegistryTweaks.Catalog.Count,
            "Registry settings have fixed independent IDs and backup semantics");
        var pending = RegistryTweaks.PrepareBackup("fixture", null, null, 1);
        Check(pending.Before is null && pending.Applied is null && pending.Pending == 1 &&
            RegistryTweaks.Matches(pending, null) && RegistryTweaks.Matches(pending, 1) && !RegistryTweaks.Matches(pending, 2),
            "Pending backup survives both failed-before-write and completed-write phases, rejects external values");
        var repeated = RegistryTweaks.PrepareBackup("fixture", pending with { Applied = 1, Pending = null }, 1, 0);
        Check(repeated.Before is null && repeated.Pending == 0, "Repeated toggles preserve originally absent registry value");
        Check(RegistryTweaks.PrepareBackup("fixture", new("fixture", 42, 1), 1, 0).Before == 42,
            "Repeated toggles retain exact nonstandard original DWORD");
        return count;
    }

    public static int LiveUi(string? screenshotPrefix = null)
    {
        var scripts = new List<string>();
        var folder = Path.Combine(Path.GetTempPath(), "Aether-admin-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "fixture.txt");
        File.WriteAllText(file, "fixture");
        try
        {
            var aclArea = AdminManagement.Areas.Single(a => a.Id == "acl");
            var aclRow = AdminManagement.ReadAsync(aclArea, CancellationToken.None, file).GetAwaiter().GetResult().Single();
            foreach (var area in AdminManagement.Areas)
            {
                scripts.Add(area.Query);
                foreach (var action in area.Actions)
                {
                    var row = area.Id switch
                    {
                        "processes" => new AdminEntity("9999", "Fixture", "Normal", "", "123456"),
                        "services" => new("Fixture", "Fixture", "Running", "", "Auto", "absent"),
                        "tasks" => new("\\Fixture", "Fixture", "Ready", "", "\\", "True"),
                        "features" => new("Fixture", "Fixture", "Disabled", ""),
                        "power" => new(Guid.NewGuid().ToString(), "Fixture", "True", ""),
                        "adapters" => new("10", "Fixture", "Up", "", Guid.NewGuid().ToString(), """[{"Family":"IPv4","Servers":""}]"""),
                        "users" => new("S-1-5-21-111-222-333-1000", "Fixture", "True", ""),
                        "groups" => new("S-1-5-32-544", "Fixture", "", ""),
                        "firewall" => new("Fixture", "Fixture", "True", ""),
                        "acl" => aclRow,
                        _ => new("fixture", "Fixture", "", "")
                    };
                    var parameter = area.Id switch { "adapters" => "1.1.1.1", "acl" => file, _ => "Fixture" };
                    var plan = AdminManagement.Plan(area, action, row, parameter);
                    scripts.Add(plan.Script);
                    if (plan.RestoreScript is not null) scripts.Add(plan.RestoreScript);
                }
            }
            var manifest = Path.Combine(folder, "scripts.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(scripts), Encoding.UTF8);
            var syntax = CommandRunner.PowerShellAsync(
                $"$scripts=Get-Content -Raw -Encoding UTF8 -LiteralPath {AdminManagement.Quote(manifest)} | ConvertFrom-Json; " +
                "foreach($script in $scripts){$errors=$null;[void][Management.Automation.Language.Parser]::ParseInput($script,[ref]$null,[ref]$errors);if($errors.Count){throw ($errors | Out-String)}}; 'Syntax OK'",
                CancellationToken.None).GetAwaiter().GetResult();
            if (!syntax.Contains("Syntax OK")) throw new InvalidOperationException("Admin preview script syntax check missing");
            var serviceRows = new List<AdminEntity>();
            foreach (var id in new[] { "processes", "services", "tasks", "features", "firewall", "packages", "updates", "power", "adapters", "proxy", "users", "groups", "acl" })
            {
                try
                {
                    var rows = AdminManagement.ReadAsync(AdminManagement.Areas.Single(a => a.Id == id), CancellationToken.None).GetAwaiter().GetResult();
                    if (rows.Any(r => r.Name is null || r.Status is null || r.Detail is null)) throw new InvalidOperationException("Row fields missing");
                    if (id == "services") serviceRows = rows;
                    Console.WriteLine($"ADMIN READ: {id} / {rows.Count} rows");
                }
                catch (InvalidOperationException ex) when (id is "users" or "groups" or "tasks" && ex.Message.StartsWith("PowerShell Exit"))
                {
                    Console.WriteLine($"ADMIN READ: {id} / explicit access/provider failure: {ex.Message}");
                }
                catch (InvalidOperationException ex) when (id == "features" && !SystemActions.IsAdmin() && ex.Message.StartsWith("PowerShell Exit"))
                {
                    Console.WriteLine("ADMIN READ: features / explicitly requires elevation; non-elevated test host.");
                }
            }
            var eventSnapshot = EventMonitor.ReadChannelAsync("System", 1, 10, CancellationToken.None).GetAwaiter().GetResult();
            if (eventSnapshot.Events.Count > 10) throw new InvalidOperationException("Embedded event viewer ignores requested limit");
            var main = new MainWindow();
            main.Show();
            try
            {
                var pages = (TabControl)main.FindName("Pages");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(t => (string)t.Header == "Superadmin");
                Pump();
                var workbench = (AdminWorkbench)main.FindName("Workbench");
                workbench.SelectArea("services");
                Pump();
                workbench.UpdateLayout();
                var grid = (DataGrid)workbench.FindName("Entities");
                var row = new AdminEntity("FixtureService", "Fixture service", "Running", "", "Auto", "1");
                grid.ItemsSource = new[] { row };
                grid.SelectedIndex = 0;
                ((ComboBox)workbench.FindName("Operation")).SelectedItem = "Starttyp Manuell";
                var preview = FindButton(workbench, "Aenderungsvorschau") ?? throw new InvalidOperationException("Preview button missing");
                preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!((TextBox)workbench.FindName("Console")).Text.Contains("[VORSCHAU / KEINE AUSFUEHRUNG]"))
                    throw new InvalidOperationException("GUI preview must be visible without running change");
                if (screenshotPrefix is not null)
                {
                    grid.ItemsSource = serviceRows;
                    grid.SelectedIndex = 0;
                    Capture(main, screenshotPrefix + "-superadmin.png");
                }
                workbench.SelectArea("registry");
                if (((ComboBox)workbench.FindName("Operation")).Items.Count != 3)
                    throw new InvalidOperationException("Registry AN/AUS/restore controls missing");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(t => (string)t.Header == "Ereignisse");
                Pump();
                var events = (EventBrowser)main.FindName("EmbeddedEvents");
                var eventRows = (DataGrid)events.FindName("Records");
                eventRows.ItemsSource = eventSnapshot.Events;
                if (eventSnapshot.Events.Count > 0)
                {
                    eventRows.SelectedIndex = 0;
                    Pump();
                    if (!((TextBox)events.FindName("Details")).Text.Contains(eventSnapshot.Events[0].Provider))
                        throw new InvalidOperationException("Event selection does not update embedded details");
                }
                var motion = ((CockpitModel)main.DataContext);
                motion.MotionEnabled = false;
                Pump();
                if (GlowCount(main) != 0) throw new InvalidOperationException("Disabled frame animation left glow adorners active");
                motion.MotionEnabled = true;
                Pump();
                if (SystemParameters.ClientAreaAnimation && GlowCount(main) == 0)
                    throw new InvalidOperationException("Enabled neon frames have no animated light adorner");
                if (screenshotPrefix is not null) Capture(main, screenshotPrefix + "-events.png");
                pages.SelectedItem = pages.Items.Cast<TabItem>().Single(t => (string)t.Header == "Taskmanager");
                if (screenshotPrefix is not null) Capture(main, screenshotPrefix + "-taskmanager.png");
                Console.WriteLine($"ADMIN UI: {scripts.Count} scripts parsed only; preview controls and embedded event details verified. No real system changes executed.");
            }
            finally
            {
                main.Close();
                var watch = Stopwatch.StartNew();
                while (main.IsVisible && watch.Elapsed < TimeSpan.FromSeconds(15)) { Pump(); Thread.Sleep(20); }
                if (main.IsVisible) throw new InvalidOperationException("Admin/event operations did not shut down");
            }
            return 5;
        }
        finally { Directory.Delete(folder, true); }
    }
    private static Button? FindButton(System.Windows.DependencyObject parent, string text)
    {
        if (parent is Button button && Equals(button.Content, text)) return button;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var found = FindButton(System.Windows.Media.VisualTreeHelper.GetChild(parent, i), text);
            if (found is not null) return found;
        }
        return null;
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static int GlowCount(DependencyObject root) => Descendants(root).Count(element => element.GetType().Name == "GlowAdorner");
    private static void Capture(Window window, string path)
    {
        Pump();
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
