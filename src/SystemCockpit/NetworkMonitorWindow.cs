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
    internal static string ConsentText => DesktopUiText.Get("Network.Consent");

    internal NetworkMonitorWindow(NetworkWatch watch, CockpitModel model, Action<CockpitSettings> save)
    {
        Title = DesktopUiText.Get("Network.Title");
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
        foreach (var (title, content) in new[] { ("Network.LiveTab", live), ("Network.CaptureTab", panel), ("Network.AlertsTab", alertsPanel) })
            tabs.Items.Add(new TabItem { Header = DesktopUiText.Get(title), Style = (Style)FindResource("NeonTabItem"),
                Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        header.Children.Add(new TextBlock { Text = DesktopUiText.Get("Network.Header"), FontSize = 23, Foreground = Brushes.Turquoise });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(NetworkWatch.Status)) { Source = watch });
        header.Children.Add(status);
        var counts = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        counts.SetBinding(TextBlock.TextProperty, new Binding(nameof(NetworkWatch.Packets))
            { Source = watch, StringFormat = DesktopUiText.Get("Network.PacketCount") });
        header.Children.Add(counts);
        var dropped = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        dropped.SetBinding(TextBlock.TextProperty, new Binding(nameof(NetworkWatch.DisplayDropped))
            { Source = watch, StringFormat = DesktopUiText.Get("Network.DroppedCount") });
        header.Children.Add(dropped);
        var monitor = new CheckBox { Content = DesktopUiText.Get("Network.Monitor"), IsChecked = model.Settings.NetworkMonitorEnabled };
        monitor.Click += (_, _) => save(model.Settings with { NetworkMonitorEnabled = monitor.IsChecked == true });
        panel.Children.Add(monitor);
        var connectionCount = new TextBlock { Margin = new Thickness(0, 8, 0, 8) };
        connectionCount.SetBinding(TextBlock.TextProperty, new Binding(nameof(NetworkWatch.Connections))
            { Source = watch, StringFormat = DesktopUiText.Get("Network.ConnectionCount") });
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
        var connections = Threshold(DesktopUiText.Get("Network.ConnectionThreshold"), model.Settings.ConnectionAlertThreshold);
        var ports = Threshold(DesktopUiText.Get("Network.PortThreshold"), model.Settings.SynPortAlertThreshold);
        var dns = Threshold(DesktopUiText.Get("Network.DnsThreshold"), model.Settings.DnsFailureAlertThreshold);
        var apply = new Button { Content = DesktopUiText.Get("Network.SaveThresholds"), Style = buttonStyle, Padding = new Thickness(8) };
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(connections.Text, out var c) || !int.TryParse(ports.Text, out var p) || !int.TryParse(dns.Text, out var d) ||
                c is < 5 or > 10000 || p is < 5 or > 10000 || d is < 5 or > 10000)
            {
                MessageBox.Show(this, DesktopUiText.Get("Network.InvalidThresholds")); return;
            }
            save(model.Settings with { ConnectionAlertThreshold = c, SynPortAlertThreshold = p, DnsFailureAlertThreshold = d });
        };
        panel.Children.Add(apply);
        var device = new ComboBox { DisplayMemberPath = "Description", Margin = new Thickness(0, 12, 0, 8) };
        panel.Children.Add(device);
        var devices = new Button { Content = DesktopUiText.Get("Network.ReadDevices"), Style = buttonStyle, Padding = new Thickness(8) };
        devices.Click += (_, _) =>
        {
            try
            {
                var all = watch.Devices();
                device.ItemsSource = all;
                device.SelectedItem = all.FirstOrDefault(item => item.Name == model.Settings.CaptureDevice) ?? all.FirstOrDefault();
                if (all.Count == 0) MessageBox.Show(this, DesktopUiText.Get("Network.NoDevices"));
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or EntryPointNotFoundException or BadImageFormatException)
            {
                MessageBox.Show(this, ex.Message, DesktopUiText.Get("Network.NpcapUnavailable"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        panel.Children.Add(devices);
        var download = new Button
        {
            Content = DesktopUiText.Get("Network.Download"),
            Style = buttonStyle,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 8)
        };
        download.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                    DesktopUiText.Get("Network.DownloadConsent"),
                    DesktopUiText.Get("Network.InstallNpcap"), MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            try
            {
                Process.Start(new ProcessStartInfo(NpcapCapture.OfficialDownloadPage) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(this, ex.Message, DesktopUiText.Get("Network.DownloadFailed"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        panel.Children.Add(download);
        var buttons = new WrapPanel();
        var start = new Button { Content = DesktopUiText.Get("Network.StartCapture"), Style = buttonStyle, Padding = new Thickness(8) };
        start.Click += (_, _) =>
        {
            if (device.SelectedItem is not CaptureDevice selected) { MessageBox.Show(this, DesktopUiText.Get("Network.SelectDevice")); return; }
            if (MessageBox.Show(this, ConsentText, DesktopUiText.Get("Network.CaptureConsentTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { watch.StartCapture(selected.Name); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { MessageBox.Show(this, ex.Message); }
        };
        var stop = new Button { Content = DesktopUiText.Get("Network.StopCapture"), Style = buttonStyle, Padding = new Thickness(8) };
        stop.Click += async (_, _) => await watch.StopCaptureAsync();
        buttons.Children.Add(start); buttons.Children.Add(stop); panel.Children.Add(buttons);
        var auto = new CheckBox { Content = DesktopUiText.Get("Network.CaptureAtLogon"), IsChecked = model.Settings.CaptureAtLogon, Margin = new Thickness(0, 10, 0, 10) };
        auto.Click += (_, _) =>
        {
            if (auto.IsChecked == true)
            {
                if (device.SelectedItem is not CaptureDevice selected) { auto.IsChecked = false; MessageBox.Show(this, DesktopUiText.Get("Network.SelectDevice")); return; }
                if (MessageBox.Show(this, ConsentText + "\n" + DesktopUiText.Get("Network.FutureLogons"),
                        DesktopUiText.Get("Network.AutoConsentTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                { auto.IsChecked = false; return; }
                save(model.Settings with { CaptureAtLogon = true, CaptureDevice = selected.Name });
            }
            else save(model.Settings with { CaptureAtLogon = false });
        };
        panel.Children.Add(auto);
        live.Children.Add(new TextBlock { Text = DesktopUiText.Get("Network.LiveNotice"), TextWrapping = TextWrapping.Wrap });
        var filter = PacketFilter.Create("", "Alle", "", false, false);
        var view = new CollectionViewSource { Source = watch.RecentPackets }.View;
        view.Filter = item => item is PacketSummary packet && filter.Matches(packet);
        var filters = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var search = new TextBox { Width = 210, Style = (Style)FindResource("NeonTextBox"), ToolTip = DesktopUiText.Get("Network.SearchTooltip") };
        var protocol = new ComboBox { Width = 100, DisplayMemberPath = "Value", SelectedValuePath = "Key",
            ItemsSource = PacketFilter.Protocols.Select(id => new KeyValuePair<string, string>(id,
                id == "Alle" ? DesktopUiText.Get("Common.All") : id)).ToArray(), SelectedIndex = 0 };
        var port = new TextBox { Width = 90, Style = (Style)FindResource("NeonTextBox"), ToolTip = DesktopUiText.Get("Network.PortTooltip") };
        var syn = new CheckBox { Content = "SYN", Foreground = Brushes.White, Margin = new Thickness(8) };
        var dnsErrors = new CheckBox { Content = DesktopUiText.Get("Network.DnsErrors"), Foreground = Brushes.White, Margin = new Thickness(8) };
        var applyFilter = new Button { Content = DesktopUiText.Get("Network.ApplyFilter"), Style = buttonStyle };
        System.Windows.Automation.AutomationProperties.SetName(search, DesktopUiText.Get("Network.AccessibleSearch"));
        System.Windows.Automation.AutomationProperties.SetName(protocol, DesktopUiText.Get("Network.AccessibleProtocol"));
        System.Windows.Automation.AutomationProperties.SetName(port, DesktopUiText.Get("Network.AccessiblePort"));
        foreach (var (label, control) in new (string, Control)[] { (DesktopUiText.Get("Network.AddressText"), search), (DesktopUiText.Get("Common.Protocol"), protocol), ("Port", port) })
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
            (DesktopUiText.Get("Common.Time"), nameof(PacketSummary.Time)), (DesktopUiText.Get("Common.Source"), nameof(PacketSummary.Source)),
            (DesktopUiText.Get("Common.Destination"), nameof(PacketSummary.Destination)), (DesktopUiText.Get("Common.Protocol"), nameof(PacketSummary.Protocol)),
            (DesktopUiText.Get("Network.SourcePort"), nameof(PacketSummary.SourcePort)), (DesktopUiText.Get("Network.DestinationPort"), nameof(PacketSummary.DestinationPort)),
            (DesktopUiText.Get("Network.PacketBytes"), nameof(PacketSummary.WireLength)), ("SYN", nameof(PacketSummary.Syn)),
            (DesktopUiText.Get("Network.DnsErrors"), nameof(PacketSummary.DnsFailure))
        })
            packets.Columns.Add(new DataGridTextColumn { Header = columnHeader,
                Binding = property == nameof(PacketSummary.Time) ? new Binding(property) { StringFormat = "HH:mm:ss.fff zzz" } : new Binding(property),
                Width = property is nameof(PacketSummary.Source) or nameof(PacketSummary.Destination) ? 160
                    : property == nameof(PacketSummary.Time) ? 150 : 105 });
        live.Children.Add(packets);
        var details = new TextBlock { Text = DesktopUiText.Get("Network.SelectPacket"), TextWrapping = TextWrapping.Wrap };
        void RefreshDetails() => details.Text = packets.SelectedItem is PacketSummary packet
            ? PacketFilter.Describe(packet) : DesktopUiText.Get("Network.NoPacket");
        packets.SelectionChanged += (_, _) => RefreshDetails();
        RefreshDetails();
        live.Children.Add(details);
        void RefreshCounts()
        {
            var visible = view.Cast<PacketSummary>().ToArray();
            filterStatus.Text = DesktopUiText.Format("Network.VisibleCounts", visible.Length, watch.RecentPackets.Count,
                visible.Sum(p => (long)p.WireLength), filter.Text,
                filter.Protocol == "Alle" ? DesktopUiText.Get("Common.All") : filter.Protocol,
                filter.Port?.ToString() ?? DesktopUiText.Get("Common.All"), filter.SynOnly, filter.DnsErrorsOnly);
        }
        applyFilter.Click += (_, _) =>
        {
            try
            {
                var next = PacketFilter.Create(search.Text, protocol.SelectedValue as string ?? "", port.Text,
                    syn.IsChecked == true, dnsErrors.IsChecked == true);
                filter = next;
                view.Refresh();
                RefreshDetails();
                RefreshCounts();
            }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message + "\n" + DesktopUiText.Get("Network.FilterUnchanged"), DesktopUiText.Get("Network.FilterFailed")); }
        };
        NotifyCollectionChangedEventHandler changed = (_, _) => RefreshCounts();
        watch.RecentPackets.CollectionChanged += changed;
        Closed += (_, _) => watch.RecentPackets.CollectionChanged -= changed;
        RefreshCounts();
        var exportButtons = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        var csv = new Button { Content = DesktopUiText.Get("Network.ExportCsv"), Style = buttonStyle };
        csv.Click += (_, _) =>
        {
            var snapshot = view.Cast<PacketSummary>().ToArray();
            if (snapshot.Length == 0) { MessageBox.Show(this, DesktopUiText.Get("Network.ExportEmpty")); return; }
            var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Aether-Paketmetadaten.csv" };
            if (MessageBox.Show(this, DesktopUiText.Get("Network.ExportConsent"),
                    DesktopUiText.Get("Network.SensitiveData"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes ||
                dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, PacketFilter.Csv(snapshot), new UTF8Encoding(true));
                MessageBox.Show(this, DesktopUiText.Format("Network.ExportComplete", snapshot.Length));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { MessageBox.Show(this, DesktopUiText.Format("Network.ExportFailed", ex.Message)); }
        };
        var wireshark = new Button { Content = DesktopUiText.Get("Network.OpenWireshark"), Style = buttonStyle };
        wireshark.Click += (_, _) =>
        {
            if (watch.IsCapturing) { MessageBox.Show(this, DesktopUiText.Get("Network.StopFirst")); return; }
            var captureDialog = new OpenFileDialog { Filter = DesktopUiText.Get("Network.CaptureFilter"),
                InitialDirectory = Directory.Exists(NetworkWatch.CaptureDirectory) ? NetworkWatch.CaptureDirectory : "" };
            if (captureDialog.ShowDialog(this) != true) return;
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Wireshark", "Wireshark.exe");
            if (!File.Exists(executable))
            {
                var exeDialog = new OpenFileDialog { Filter = "Wireshark (Wireshark.exe)|Wireshark.exe", Title = DesktopUiText.Get("Network.SelectWireshark") };
                if (exeDialog.ShowDialog(this) != true) return;
                executable = exeDialog.FileName;
            }
            if (MessageBox.Show(this, DesktopUiText.Format("Network.WiresharkConsent", executable, captureDialog.FileName),
                    DesktopUiText.Get("Network.WiresharkTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try
            {
                using var process = Process.Start(CaptureHandoff.Wireshark(executable, captureDialog.FileName))
                    ?? throw new InvalidOperationException(DesktopUiText.Get("Network.WiresharkNoProcess"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or InvalidOperationException)
            { MessageBox.Show(this, DesktopUiText.Format("Network.WiresharkFailed", ex.Message)); }
        };
        exportButtons.Children.Add(csv); exportButtons.Children.Add(wireshark); live.Children.Add(exportButtons);
        alertsPanel.Children.Add(new TextBlock { Text = DesktopUiText.Get("Network.AlertsNotice"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Turquoise });
        alertsPanel.Children.Add(new ListBox { ItemsSource = watch.Alerts, Height = 400,
            Background = new SolidColorBrush(Color.FromRgb(16, 12, 36)), Foreground = Brushes.Turquoise,
            BorderBrush = Brushes.MediumPurple, FontFamily = new FontFamily("Consolas") });
    }
}
