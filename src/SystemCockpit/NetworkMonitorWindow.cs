using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using SystemCockpit.Services;

namespace SystemCockpit;

internal sealed class NetworkMonitorWindow : Window
{
    internal const string ConsentText = "Paketmitschnitt: IP-Adressen und DNS-/Protokollmetadaten, trotz Begrenzung auch vertrauliche Inhalte. " +
        "Nur eigene/berechtigte Schnittstelle verwenden. 128 Bytes/Paket, maximal 100 MB oder 15 Minuten pro Start; " +
        "lokale PCAP-Dateien, sieben Tage Aufbewahrung, insgesamt maximal ca. 500 MB. " +
        "Keine Cloud, keine Entschluesselung, kein automatisches Blockieren. Jetzt freigeben?";

    internal NetworkMonitorWindow(NetworkWatch watch, CockpitModel model, Action<CockpitSettings> save)
    {
        Title = "AETHER / Netzwerkbeobachtung und Paketmitschnitt";
        Width = 1100; Height = 880; MinWidth = 850; MinHeight = 660;
        Background = new SolidColorBrush(Color.FromRgb(12, 9, 32));
        Foreground = Brushes.White;
        var buttonStyle = (Style)FindResource("NeonButton");
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Content = root;
        var header = new StackPanel();
        root.Children.Add(header);
        var panel = new StackPanel { Margin = new Thickness(12) };
        var live = new StackPanel { Margin = new Thickness(12) };
        var alertsPanel = new StackPanel { Margin = new Thickness(12) };
        var tabs = new TabControl { Background = Background, BorderBrush = Brushes.MediumPurple, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (title, content) in new[] { ("Live-Analyse", live), ("Erfassung / Alarme", panel), ("Warnungen", alertsPanel) })
            tabs.Items.Add(new TabItem { Header = title, Style = (Style)FindResource("NeonTabItem"),
                Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        header.Children.Add(new TextBlock { Text = "NETZWERK / Npcap optional", FontSize = 23, Foreground = Brushes.Turquoise });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(NetworkWatch.Status)) { Source = watch });
        header.Children.Add(status);
        var counts = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        counts.SetBinding(TextBlock.TextProperty, new Binding(nameof(NetworkWatch.Packets))
            { Source = watch, StringFormat = "Erfasste Pakete: {0}" });
        header.Children.Add(counts);
        var dropped = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        dropped.SetBinding(TextBlock.TextProperty, new Binding(nameof(NetworkWatch.DisplayDropped))
            { Source = watch, StringFormat = "Wegen Anzeigelimit nicht dargestellt: {0} (kein Indikator fuer Treiberverluste)" });
        header.Children.Add(dropped);
        var monitor = new CheckBox { Content = "Lokale TCP-Verbindungszahl ueberwachen (ohne Mitschnitt)", IsChecked = model.Settings.NetworkMonitorEnabled };
        monitor.Click += (_, _) => save(model.Settings with { NetworkMonitorEnabled = monitor.IsChecked == true });
        panel.Children.Add(monitor);
        var connectionCount = new TextBlock { Margin = new Thickness(0, 8, 0, 8) };
        connectionCount.SetBinding(TextBlock.TextProperty, new Binding(nameof(NetworkWatch.Connections))
            { Source = watch, StringFormat = "Aktive/aufbauende TCP-Verbindungen: {0}" });
        panel.Children.Add(connectionCount);
        var thresholds = new WrapPanel();
        panel.Children.Add(thresholds);
        TextBox Threshold(string title, int value)
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, 12, 8), Width = 250 };
            group.Children.Add(new TextBlock { Text = title });
            var text = new TextBox { Text = value.ToString(), Style = (Style)FindResource("NeonTextBox") };
            group.Children.Add(text); thresholds.Children.Add(group); return text;
        }
        var connections = Threshold("TCP-Verbindungsschwelle", model.Settings.ConnectionAlertThreshold);
        var ports = Threshold("Verschiedene SYN-Zielports / Minute", model.Settings.SynPortAlertThreshold);
        var dns = Threshold("DNS-Fehlerantworten / Minute", model.Settings.DnsFailureAlertThreshold);
        var apply = new Button { Content = "Alarmschwellen speichern", Style = buttonStyle, Padding = new Thickness(8) };
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(connections.Text, out var c) || !int.TryParse(ports.Text, out var p) || !int.TryParse(dns.Text, out var d) ||
                c is < 5 or > 10000 || p is < 5 or > 10000 || d is < 5 or > 10000)
            {
                MessageBox.Show(this, "Alle Schwellen muessen zwischen 5 und 10000 liegen."); return;
            }
            save(model.Settings with { ConnectionAlertThreshold = c, SynPortAlertThreshold = p, DnsFailureAlertThreshold = d });
        };
        panel.Children.Add(apply);
        var device = new ComboBox { DisplayMemberPath = "Description", Margin = new Thickness(0, 12, 0, 8) };
        panel.Children.Add(device);
        var devices = new Button { Content = "Npcap-Schnittstellen lesen (kein Mitschnitt)", Style = buttonStyle, Padding = new Thickness(8) };
        devices.Click += (_, _) =>
        {
            try
            {
                var all = watch.Devices();
                device.ItemsSource = all;
                device.SelectedItem = all.FirstOrDefault(item => item.Name == model.Settings.CaptureDevice) ?? all.FirstOrDefault();
                if (all.Count == 0) MessageBox.Show(this, "Npcap meldet keine Schnittstelle.");
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or EntryPointNotFoundException or BadImageFormatException)
            {
                MessageBox.Show(this, ex.Message, "Npcap nicht verfuegbar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        panel.Children.Add(devices);
        var buttons = new WrapPanel();
        var start = new Button { Content = "Mitschnitt starten (Bestaetigung)", Style = buttonStyle, Padding = new Thickness(8) };
        start.Click += (_, _) =>
        {
            if (device.SelectedItem is not CaptureDevice selected) { MessageBox.Show(this, "Zuerst eine Npcap-Schnittstelle auswaehlen."); return; }
            if (MessageBox.Show(this, ConsentText, "Paketmitschnitt freigeben", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { watch.StartCapture(selected.Name); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { MessageBox.Show(this, ex.Message); }
        };
        var stop = new Button { Content = "Mitschnitt stoppen", Style = buttonStyle, Padding = new Thickness(8) };
        stop.Click += async (_, _) => await watch.StopCaptureAsync();
        buttons.Children.Add(start); buttons.Children.Add(stop); panel.Children.Add(buttons);
        var auto = new CheckBox { Content = "Ein Mitschnitt pro Windows-Anmeldung (nur installierter Autostart)", IsChecked = model.Settings.CaptureAtLogon, Margin = new Thickness(0, 10, 0, 10) };
        auto.Click += (_, _) =>
        {
            if (auto.IsChecked == true)
            {
                if (device.SelectedItem is not CaptureDevice selected) { auto.IsChecked = false; MessageBox.Show(this, "Zuerst eine Schnittstelle auswaehlen."); return; }
                if (MessageBox.Show(this, ConsentText + "\nGilt auch fuer zukuenftige Anmeldungen, bis hier deaktiviert.",
                        "Automatischen Mitschnitt freigeben", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                { auto.IsChecked = false; return; }
                save(model.Settings with { CaptureAtLogon = true, CaptureDevice = selected.Name });
            }
            else save(model.Settings with { CaptureAtLogon = false });
        };
        panel.Children.Add(auto);
        live.Children.Add(new TextBlock { Text = "Unter Erfassung / Alarme Schnittstelle bewusst freigeben. Begrenzte Protokollanalyse: Ethernet/VLAN, IPv4/IPv6, TCP/UDP/ICMP und DNS-Fehler. Keine Wireshark-Vollanalyse, TLS-Entschluesselung oder Angriffsgarantie. Letzte 200 Pakete; PCAP bleibt vom Anzeigefilter unberuehrt.", TextWrapping = TextWrapping.Wrap });
        var filter = PacketFilter.Create("", "Alle", "", false, false);
        var view = new CollectionViewSource { Source = watch.RecentPackets }.View;
        view.Filter = item => item is PacketSummary packet && filter.Matches(packet);
        var filters = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var search = new TextBox { Width = 210, Style = (Style)FindResource("NeonTextBox"), ToolTip = "Adresse / Text, mehrere Begriffe mit Leerzeichen; kein Wireshark-Displayfilter" };
        var protocol = new ComboBox { Width = 100, ItemsSource = PacketFilter.Protocols, SelectedIndex = 0 };
        var port = new TextBox { Width = 90, Style = (Style)FindResource("NeonTextBox"), ToolTip = "Quell- oder Zielport, leer oder 1..65535" };
        var syn = new CheckBox { Content = "SYN", Foreground = Brushes.White, Margin = new Thickness(8) };
        var dnsErrors = new CheckBox { Content = "DNS-Fehler", Foreground = Brushes.White, Margin = new Thickness(8) };
        var applyFilter = new Button { Content = "Filter anwenden", Style = buttonStyle };
        System.Windows.Automation.AutomationProperties.SetName(search, "Pakettextfilter");
        System.Windows.Automation.AutomationProperties.SetName(protocol, "Paketprotokoll");
        System.Windows.Automation.AutomationProperties.SetName(port, "Paketport");
        foreach (var (label, control) in new (string, Control)[] { ("Adresse / Text", search), ("Protokoll", protocol), ("Port", port) })
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            group.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            group.Children.Add(control);
            filters.Children.Add(group);
        }
        foreach (var control in new Control[] { syn, dnsErrors, applyFilter })
        { control.VerticalAlignment = VerticalAlignment.Bottom; filters.Children.Add(control); }
        live.Children.Add(filters);
        var filterStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Turquoise };
        live.Children.Add(filterStatus);
        var packets = new DataGrid { ItemsSource = view, Style = (Style)FindResource("NeonDataGrid"),
            Height = 220, Margin = new Thickness(0, 10, 0, 10) };
        foreach (var (columnHeader, property) in new[]
        {
            ("Zeit", nameof(PacketSummary.Time)), ("Quelle", nameof(PacketSummary.Source)),
            ("Ziel", nameof(PacketSummary.Destination)), ("Protokoll", nameof(PacketSummary.Protocol)),
            ("Quellport", nameof(PacketSummary.SourcePort)), ("Zielport", nameof(PacketSummary.DestinationPort)),
            ("Paketbytes", nameof(PacketSummary.WireLength)), ("SYN", nameof(PacketSummary.Syn)),
            ("DNS-Fehler", nameof(PacketSummary.DnsFailure))
        })
            packets.Columns.Add(new DataGridTextColumn { Header = columnHeader,
                Binding = property == nameof(PacketSummary.Time) ? new Binding(property) { StringFormat = "HH:mm:ss.fff zzz" } : new Binding(property),
                Width = property is nameof(PacketSummary.Source) or nameof(PacketSummary.Destination) ? 160
                    : property == nameof(PacketSummary.Time) ? 150 : 105 });
        live.Children.Add(packets);
        var details = new TextBlock { Text = "Paket markieren fuer Metadaten und Analysegrenzen.", TextWrapping = TextWrapping.Wrap };
        void RefreshDetails() => details.Text = packets.SelectedItem is PacketSummary packet
            ? PacketFilter.Describe(packet) : "Kein Paket markiert.";
        packets.SelectionChanged += (_, _) => RefreshDetails();
        RefreshDetails();
        live.Children.Add(details);
        void RefreshCounts()
        {
            var visible = view.Cast<PacketSummary>().ToArray();
            filterStatus.Text = $"{visible.Length} / {watch.RecentPackets.Count} Live-Zeilen sichtbar, {visible.Sum(p => (long)p.WireLength)} gemeldete Paketbytes. " +
                $"Aktiver Filter: '{filter.Text}' / {filter.Protocol} / Port {filter.Port?.ToString() ?? "alle"} / SYN {filter.SynOnly} / DNS-Fehler {filter.DnsErrorsOnly}. " +
                "Nur begrenztes Live-Fenster, keine Gesamtstatistik und keine PCAP-Filterung.";
        }
        applyFilter.Click += (_, _) =>
        {
            try
            {
                var next = PacketFilter.Create(search.Text, protocol.SelectedItem as string ?? "", port.Text,
                    syn.IsChecked == true, dnsErrors.IsChecked == true);
                filter = next;
                view.Refresh();
                RefreshDetails();
                RefreshCounts();
            }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message + "\nDer bisherige Filter bleibt aktiv.", "Filter nicht angewendet"); }
        };
        NotifyCollectionChangedEventHandler changed = (_, _) => RefreshCounts();
        watch.RecentPackets.CollectionChanged += changed;
        Closed += (_, _) => watch.RecentPackets.CollectionChanged -= changed;
        RefreshCounts();
        var exportButtons = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        var csv = new Button { Content = "Sichtbare Paketmetadaten als CSV", Style = buttonStyle };
        csv.Click += (_, _) =>
        {
            var snapshot = view.Cast<PacketSummary>().ToArray();
            if (snapshot.Length == 0) { MessageBox.Show(this, "Keine sichtbaren Paketzeilen fuer den Export."); return; }
            var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Aether-Paketmetadaten.csv" };
            if (MessageBox.Show(this, "CSV enthaelt IP-Adressen und Kommunikationsmetadaten. Vor Weitergabe pruefen. Lokal speichern?",
                    "Sensible Netzwerkdaten", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes ||
                dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, PacketFilter.Csv(snapshot), new UTF8Encoding(true));
                MessageBox.Show(this, $"{snapshot.Length} sichtbare Paketmetadaten gespeichert; keine PCAP-Nutzlast exportiert.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { MessageBox.Show(this, "CSV nicht gespeichert: " + ex.Message); }
        };
        var wireshark = new Button { Content = "PCAP in installiertem Wireshark oeffnen", Style = buttonStyle };
        wireshark.Click += (_, _) =>
        {
            if (watch.IsCapturing) { MessageBox.Show(this, "Zuerst den laufenden Mitschnitt stoppen."); return; }
            var captureDialog = new OpenFileDialog { Filter = "Paketmitschnitt (*.pcap;*.pcapng)|*.pcap;*.pcapng",
                InitialDirectory = Directory.Exists(NetworkWatch.CaptureDirectory) ? NetworkWatch.CaptureDirectory : "" };
            if (captureDialog.ShowDialog(this) != true) return;
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Wireshark", "Wireshark.exe");
            if (!File.Exists(executable))
            {
                var exeDialog = new OpenFileDialog { Filter = "Wireshark (Wireshark.exe)|Wireshark.exe", Title = "Vertrauenswuerdiges installiertes Wireshark auswaehlen" };
                if (exeDialog.ShowDialog(this) != true) return;
                executable = exeDialog.FileName;
            }
            if (MessageBox.Show(this, $"Externes Programm starten:\n{executable}\n\nDatei uebergeben:\n{captureDialog.FileName}\n\n" +
                    "Die Datei kann vertrauliche Kommunikation enthalten. Nur eine vertrauenswuerdige Wireshark-Installation verwenden. " +
                    "AETHER startet keinen Mitschnitt und laedt nichts hoch; das externe Programm hat eigene Einstellungen.",
                    "Wireshark-Uebergabe", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try
            {
                using var process = Process.Start(CaptureHandoff.Wireshark(executable, captureDialog.FileName))
                    ?? throw new InvalidOperationException("Windows hat keinen Wireshark-Prozess gestartet.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or InvalidOperationException)
            { MessageBox.Show(this, "Wireshark nicht gestartet: " + ex.Message); }
        };
        exportButtons.Children.Add(csv); exportButtons.Children.Add(wireshark); live.Children.Add(exportButtons);
        alertsPanel.Children.Add(new TextBlock { Text = "LOKALE WARNUNGEN / Indizien, keine Angriffsnachweise. Die Verbindungsschwelle erfordert freigegebene lokale Ueberwachung; SYN-/DNS-Regeln erfordern einen bewusst gestarteten Mitschnitt.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Turquoise });
        alertsPanel.Children.Add(new ListBox { ItemsSource = watch.Alerts, Height = 400,
            Background = new SolidColorBrush(Color.FromRgb(16, 12, 36)), Foreground = Brushes.Turquoise,
            BorderBrush = Brushes.MediumPurple, FontFamily = new FontFamily("Consolas") });
    }
}
