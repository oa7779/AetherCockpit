using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using SystemCockpit;
using SystemCockpit.Services;

internal static class DesktopFeatureTests
{
    internal static int Run()
    {
        var assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
        void Reject(CockpitSettings settings)
        {
            try { settings.Validate(); }
            catch (ArgumentException) { assertions++; return; }
            throw new InvalidOperationException("Invalid desktop/network setting was accepted.");
        }
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, 0.19, 1.01 })
        {
            Reject(new() { AppOpacity = value });
            Reject(new() { SidebarOpacity = value });
            Reject(new() { GadgetOpacity = value });
        }
        Reject(new() { GadgetLayouts = [new("cpu", double.NaN, 0, 0.5)] });
        Reject(new() { GadgetLayouts = [new("cpu", 0, 0, 0.5), new("cpu", 1, 1, 0.7)] });
        Reject(new() { CaptureAtLogon = true });
        Reject(new() { ConnectionAlertThreshold = 4 });
        var original = new CockpitSettings { AppOpacity = 0.7, SidebarOpacity = 0.6,
            GadgetLayouts = [new("cpu", -1200, 30, 0.5)], CpuWarningC = 95 };
        var profile = new SetupProfile("test", true, true, false, false, true, true, false, false, "");
        var merged = SetupProfile.Merge(original, profile);
        Check(merged.GadgetsEnabled && !merged.SidebarWidgetMemory && !merged.SidebarWidgetGpu &&
              merged.AppOpacity == 0.7 && merged.CpuWarningC == 95 && merged.GadgetLayouts[0].X == -1200,
            "Setup choices merge without losing existing unrelated settings or desktop layouts");
        var restored = JsonSerializer.Deserialize<CockpitSettings>(JsonSerializer.Serialize(merged))!;
        restored.Validate();
        Check(restored.GadgetLayouts[0].Opacity == 0.5 && restored.SidebarOpacity == 0.6,
            "Desktop opacity and negative monitor coordinates survive serialization");
        var model = new CockpitModel { Settings = merged };
        var items = GadgetCatalog.Get(model);
        Check(items.Select(item => item.Id).SequenceEqual(new[]
            { "cpu", "temperature-0", "temperature-1", "temperature-2", "download", "upload" }),
            "Gadget selection matches configured diagram groups without launcher items");
        model.Settings = model.Settings with { SidebarWidgetDrives = true };
        model.Volumes.Add(new("C:", "", "ok", "", 25));
        Check(GadgetCatalog.Get(model).Single(item => item.Id == "volume-C:").Template == "VolumeTemplate",
            "Every volume can become its own freely placed pie gadget");
        var packet = TcpPacket(443);
        var now = DateTimeOffset.UtcNow;
        var parsed = PacketAnalysis.Parse(packet, 1600, now);
        Check(parsed.Source == "192.168.1.2" && parsed.Destination == "192.168.1.1" &&
              parsed.SourcePort == 12345 && parsed.DestinationPort == 443 && parsed.Syn && parsed.WireLength == 1600,
            "Ethernet/IPv4/TCP metadata is decoded without mistaking captured length for wire length");
        for (var length = 0; length < packet.Length; length++)
        {
            PacketAnalysis.Parse(packet.AsSpan(0, length), 1600, now);
            assertions++;
        }
        var random = new Random(777);
        for (var i = 0; i < 1000; i++)
        {
            var bytes = new byte[random.Next(129)];
            random.NextBytes(bytes);
            PacketAnalysis.Parse(bytes, bytes.Length, now);
        }
        Check(true, "Random/truncated headers never read outside captured data");
        var fragment = packet.ToArray();
        fragment[20] = 0; fragment[21] = 1;
        Check(!PacketAnalysis.Parse(fragment, 54, now).Syn &&
              PacketAnalysis.Parse(fragment, 54, now).Protocol == "IP-Fragment",
            "Non-initial IP fragments are not interpreted as TCP SYN headers");
        var vlan = new byte[packet.Length + 4];
        packet.AsSpan(0, 12).CopyTo(vlan);
        vlan[12] = 0x81; vlan[13] = 0; vlan[16] = 8; vlan[17] = 0;
        packet.AsSpan(14).CopyTo(vlan.AsSpan(18));
        Check(PacketAnalysis.Parse(vlan, 100, now).DestinationPort == 443, "VLAN headers preserve transport offsets");
        var ipv6 = new byte[14 + 40 + 20];
        ipv6[12] = 0x86; ipv6[13] = 0xDD; ipv6[14] = 0x60; ipv6[19] = 20; ipv6[20] = 6;
        ipv6[37] = 1; ipv6[53] = 2;
        packet.AsSpan(34, 20).CopyTo(ipv6.AsSpan(54));
        Check(PacketAnalysis.Parse(ipv6, 74, now).Source == "::1" &&
              PacketAnalysis.Parse(ipv6, 74, now).Destination == "::2",
            "IPv6 addresses and TCP headers are decoded");
        var rules = new PacketAlertRules();
        var thresholds = new CockpitSettings { SynPortAlertThreshold = 5, DnsFailureAlertThreshold = 5 };
        for (var i = 1; i < 5; i++)
            Check(rules.Observe(parsed with { DestinationPort = i }, thresholds) is null,
                "SYN alerts do not trigger below configured threshold");
        Check(rules.Observe(parsed with { DestinationPort = 5 }, thresholds) is not null &&
              rules.Observe(parsed with { DestinationPort = 6 }, thresholds) is null,
            "SYN alert triggers once per source/target and time window");
        Check(rules.Observe(parsed with { Time = now.AddMinutes(2), DestinationPort = 7 }, thresholds) is null,
            "Alarm window expires and does not preserve old port counts");
        var udpDns = packet.ToArray();
        udpDns[23] = 17;
        BinaryPrimitives.WriteUInt16BigEndian(udpDns.AsSpan(34), 53);
        udpDns[44] = 0x81; udpDns[45] = 0x83;
        var dnsPacket = PacketAnalysis.Parse(udpDns, 54, now);
        Check(dnsPacket.DnsFailure && dnsPacket.Protocol == "DNS / UDP",
            "DNS response failures are recognized from the DNS header");
        var dnsRules = new PacketAlertRules();
        for (var i = 0; i < 4; i++) Check(dnsRules.Observe(dnsPacket, thresholds) is null, "DNS rule respects configured threshold");
        Check(dnsRules.Observe(dnsPacket, thresholds) is not null &&
              dnsRules.Observe(dnsPacket, thresholds) is null, "DNS errors emit one alarm per window");
        var tinyIp = packet.ToArray();
        tinyIp[17] = 20;
        Check(!PacketAnalysis.Parse(tinyIp, 54, now).Syn,
            "Ethernet padding after a short IP payload is not mistaken for TCP traffic");
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) NpcapCapture.WriteHeader(writer);
        var header = buffer.ToArray();
        Check(header.Length == 24 && BinaryPrimitives.ReadUInt32LittleEndian(header) == 0xa1b2c3d4 &&
              BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16)) == 128 &&
              NpcapCapture.MaxFileBytes == 100_000_000 && NpcapCapture.MaxDuration == TimeSpan.FromMinutes(15),
            "Capture format and explicit size/duration/snapshot bounds match consent");
        using (var records = new MemoryStream())
        {
            using var writer = new CaptureFile(records);
            Check(writer.TryWrite(123, 456, 1600, packet, TimeSpan.FromMinutes(14)),
                "Capture writer accepts packets below the time limit");
            Check(!writer.TryWrite(124, 456, 1600, packet, TimeSpan.FromMinutes(15)) &&
                  records.Length == 24 + 16 + packet.Length,
                "Capture writer stops exactly at 15 minutes without adding a record");
            var bytes = records.ToArray();
            Check(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)) == 123 &&
                  BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32)) == packet.Length &&
                  BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(36)) == 1600 &&
                  bytes.AsSpan(40).SequenceEqual(packet),
                "PCAP packet records preserve timestamp, captured/wire lengths and exact bytes");
        }
        var directory = Path.Combine(Path.GetTempPath(), "AetherCaptureTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var old = Path.Combine(directory, "aether-old.pcap");
            var recent = Path.Combine(directory, "aether-new.pcap");
            var unrelated = Path.Combine(directory, "unrelated.pcap");
            File.WriteAllText(old, "old"); File.WriteAllText(recent, "new"); File.WriteAllText(unrelated, "unrelated");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
            File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-8));
            NpcapCapture.Prune(directory, DateTime.UtcNow);
            Check(!File.Exists(old) && File.Exists(recent) && File.Exists(unrelated),
                "Retention prunes only scoped capture files, not unrelated data");
            using var limited = new FileStream(Path.Combine(directory, "size-test.pcap"), FileMode.CreateNew);
            using var capture = new CaptureFile(limited);
            limited.Position = NpcapCapture.MaxFileBytes - 16 - packet.Length;
            Check(capture.TryWrite(1, 0, 100, packet, TimeSpan.Zero) &&
                  limited.Length == NpcapCapture.MaxFileBytes,
                "Capture writer permits exactly 100 MB");
            Check(!capture.TryWrite(2, 0, 100, packet, TimeSpan.Zero) &&
                  limited.Length == NpcapCapture.MaxFileBytes,
                "Capture writer refuses the next packet instead of exceeding 100 MB");
        }
        finally { Directory.Delete(directory, true); }
        return assertions;
    }

    internal static int LiveUi(string? screenshotPrefix = null)
    {
        var assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
        var main = new MainWindow();
        var model = main.Model;
        model.Cards[0].Value = "42.0";
        model.Cards[0].Level = 42;
        model.Cards[0].Trace = Sparkline.Create(new double[] { 20, 35, 25, 30, 42 }, 100);
        model.Settings = new CockpitSettings
        {
            GadgetsEnabled = true, SidebarWidgetMemory = false, SidebarWidgetGpu = false,
            SidebarWidgetTemperatures = false, SidebarWidgetNetwork = false, SidebarWidgetDrives = false,
            AppOpacity = 0.7, GadgetOpacity = 0.65
        };
        var saved = new List<CockpitSettings>();
        void Save(CockpitSettings settings) { saved.Add(settings); model.Settings = settings; }
        using var trace = new System.Diagnostics.TextWriterTraceListener(Console.Error);
        System.Diagnostics.Trace.Listeners.Add(trace);
        using var controller = new DesktopController(main, model, Save, () => { });
        controller.StopAsync().GetAwaiter().GetResult();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(controller.GadgetCount == 1 && main.Opacity == 0.7 && !main.IsVisible,
                $"Desktop gadget and tray run while main window is hidden: count={controller.GadgetCount}, opacity={main.Opacity}, visible={main.IsVisible}");
            var gadget = Application.Current.Windows.OfType<GadgetWindow>().Single();
            Check(gadget.IsVisible && gadget.Opacity == 0.65 &&
                  GetParent(new WindowInteropHelper(gadget).Handle) != IntPtr.Zero,
                $"Native gadget is parented to desktop: visible={gadget.IsVisible}, opacity={gadget.Opacity}, parent={GetParent(new WindowInteropHelper(gadget).Handle)}");
            model.Settings = model.Settings with { GadgetOpacity = 0.5 };
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(gadget.Opacity == 0.5, "Global gadget transparency applies to open desktop windows");
            var slider = ((StackPanel)((Border)gadget.Content).Child).Children.OfType<Slider>().Single();
            slider.Value = 0.4;
            Check(saved[^1].GadgetLayouts.Single().Opacity == 0.4,
                "Individual gadget transparency persists its position and opacity");
            var storedLayout = saved[^1].GadgetLayouts.Single();
            var bounds = SidebarDisplays.GetWindowBounds(new WindowInteropHelper(gadget).Handle);
            Check(storedLayout.X == bounds.Left && storedLayout.Y == bounds.Top,
                "Persisted gadget positions use actual screen pixels rather than desktop-client coordinates");
            gadget.Hide();
            Check(!gadget.IsVisible, "A desktop gadget can be minimized without destroying its configuration");
            gadget.Show();
            Check(gadget.IsVisible && gadget.Opacity == 0.4,
                "Desktop gadget restore preserves individual transparency");
            if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(gadget, screenshotPrefix + "-gadget.png");
            var settingsWindow = new DesktopSettingsWindow(model, Save, () => { });
            try
            {
                settingsWindow.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var panel = (StackPanel)((ScrollViewer)settingsWindow.Content).Content;
                var controls = panel.Children.OfType<Slider>().ToArray();
                Check(controls.Length == 3, "App settings expose separate app, bar and gadget opacity controls");
                controls[0].Value = 0.8;
                controls[1].Value = 0.55;
                Check(main.Opacity == 0.8 && model.Settings.SidebarOpacity == 0.55,
                    "App settings immediately apply main transparency and persist bar transparency");
                if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(settingsWindow, screenshotPrefix + "-settings.png");
            }
            finally { settingsWindow.Close(); }
            var networkWindow = new NetworkMonitorWindow(controller.Network, model, Save);
            try
            {
                networkWindow.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Check(networkWindow.IsVisible && !controller.Network.IsCapturing,
                    "Opening network configuration never starts packet capture or adapter discovery");
                if (screenshotPrefix is not null) ModuleTests.SaveScreenshot(networkWindow, screenshotPrefix + "-network.png");
            }
            finally { networkWindow.Close(); }
            model.Settings = model.Settings with { GadgetsEnabled = false };
            Check(controller.GadgetCount == 0, "Closing gadgets disposes independent windows without quitting the hidden main host");
        }
        finally { System.Diagnostics.Trace.Listeners.Remove(trace); main.Close(); }
        return assertions;
    }

    private static byte[] TcpPacket(int port)
    {
        var bytes = new byte[54];
        bytes[12] = 8; bytes[13] = 0; bytes[14] = 0x45; bytes[23] = 6;
        bytes[17] = 40;
        bytes[26] = 192; bytes[27] = 168; bytes[28] = 1; bytes[29] = 2;
        bytes[30] = 192; bytes[31] = 168; bytes[32] = 1; bytes[33] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(34), 12345);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(36), (ushort)port);
        bytes[46] = 0x50; bytes[47] = 0x02;
        return bytes;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
}
