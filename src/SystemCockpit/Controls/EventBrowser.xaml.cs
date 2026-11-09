using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class EventBrowser : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource? _stop;
    private Task _pending = Task.CompletedTask;
    private bool _shutdown;
    public Task Pending => _pending;
    public void Cancel() => _stop?.Cancel();
    public void Shutdown() { _shutdown = true; _timer.Stop(); Cancel(); }
    public EventBrowser()
    {
        InitializeComponent();
        Channel.ItemsSource = new[] { "System", "Application", "Security", "Setup", "Microsoft-Windows-Windows Defender/Operational", "Microsoft-Windows-PowerShell/Operational" };
        Channel.SelectedIndex = 0;
        Loaded += (_, _) => { if (!_shutdown) _timer.Start(); };
        Unloaded += (_, _) => { _timer.Stop(); Cancel(); };
        _timer.Tick += async (_, _) => { if (!_shutdown && AutoRefresh.IsChecked == true && IsVisible && _stop is null) await ReadAsync(); };
    }
    private async void ReadClicked(object sender, RoutedEventArgs e) => await ReadAsync();
    private Task ReadAsync() => RunAsync(async token =>
    {
        var channel = Channel.SelectedItem as string ?? throw new ArgumentException(DesktopUiText.Get("EventBrowser.SelectChannel"));
        if (!int.TryParse(Days.Text, out var days) || days is < 1 or > 30 ||
            !int.TryParse(Limit.Text, out var limit) || limit is < 1 or > 2000)
            throw new ArgumentException(DesktopUiText.Get("EventBrowser.InvalidRange"));
        var snapshot = await EventMonitor.ReadChannelAsync(channel, days, limit, token);
        Records.ItemsSource = snapshot.Events.OrderByDescending(r => r.Timestamp).ToList();
        Filter();
        Status.Text = DesktopUiText.Format("EventBrowser.Status", channel, snapshot.Events.Count, limit, days, DateTime.Now) +
            (snapshot.Issues.Count > 0 ? DesktopUiText.Format("EventBrowser.Issues", string.Join(" | ", snapshot.Issues)) : "");
        Details.Clear();
    });
    private async void ChannelsClicked(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        var json = await CommandRunner.PowerShellAsync(
            "$errors=@(); $logs=@(Get-WinEvent -ListLog * -ErrorAction SilentlyContinue -ErrorVariable errors | Select-Object -ExpandProperty LogName); " +
            "@{Logs=@($logs);Issues=@($errors | ForEach-Object {$_.Exception.Message})} | ConvertTo-Json -Compress", token);
        using var doc = JsonDocument.Parse(json);
        var logs = doc.RootElement.GetProperty("Logs").Deserialize<List<string>>() ?? throw new InvalidOperationException(DesktopUiText.Get("EventBrowser.InvalidChannels"));
        Channel.ItemsSource = logs.Order().ToList();
        Channel.SelectedIndex = 0;
        var issues = doc.RootElement.GetProperty("Issues").Deserialize<List<string>>() ?? [];
        Status.Text = DesktopUiText.Format("EventBrowser.ChannelsStatus", logs.Count, issues.Count);
        Details.Text = string.Join("\n", issues);
    });
    private void FilterChanged(object sender, TextChangedEventArgs e) => Filter();
    private void SeverityChanged(object sender, SelectionChangedEventArgs e) => Filter();
    private void Filter()
    {
        if (Records?.ItemsSource is null) return;
        CollectionViewSource.GetDefaultView(Records.ItemsSource).Filter = o => o is MonitorEvent r &&
            r.SearchText.Contains(Search.Text, StringComparison.OrdinalIgnoreCase) &&
            (Severity.SelectedIndex == 0 || Severity.SelectedIndex == 1 && r.Level is 1 or 2 ||
             Severity.SelectedIndex == 2 && r.Level == 3 || Severity.SelectedIndex == 3 && r.Level == 4);
    }
    private void Selected(object sender, SelectionChangedEventArgs e)
    {
        if (Records.SelectedItem is MonitorEvent r) Details.Text = $"{r.LocalTime:yyyy-MM-dd HH:mm:ss} | {r.Log} | {r.Provider} | ID {r.Id} | Record {r.RecordId}\n{r.Message}";
    }
    private async void FullDetailsClicked(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        if (Records.SelectedItem is not MonitorEvent record || record.RecordId < 0) throw new ArgumentException(DesktopUiText.Get("EventBrowser.SelectEvent"));
        var result = await CommandRunner.PowerShellAsync(
            $"$e=Get-WinEvent -LogName {AdminManagement.Quote(record.Log)} -FilterXPath '*[System[EventRecordID={record.RecordId}]]' -MaxEvents 1; " +
            "$e | Format-List TimeCreated,Id,RecordId,ProviderName,LevelDisplayName,Message; $e.ToXml()", token);
        Details.Text = result;
        Status.Text = DesktopUiText.Get("EventBrowser.DetailsRead");
    });
    private void CancelClicked(object sender, RoutedEventArgs e) => Cancel();
    private Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_stop is not null) { Status.Text = DesktopUiText.Get("EventBrowser.AlreadyRunning"); return Task.CompletedTask; }
        _pending = Execute();
        return _pending;
        async Task Execute()
        {
            _stop = new();
            Status.Text = DesktopUiText.Get("EventBrowser.Reading");
            Channel.IsEnabled = Days.IsEnabled = Limit.IsEnabled = false;
            try { await action(_stop.Token); }
            catch (OperationCanceledException) { Status.Text = DesktopUiText.Get("EventBrowser.Cancelled"); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException or TimeoutException or
                System.ComponentModel.Win32Exception or System.IO.IOException or System.Xml.XmlException)
            {
                Records.ItemsSource = null;
                Status.Text = DesktopUiText.Format("EventBrowser.Failed", ex.Message);
                Details.Text = ex.Message;
                Trace.TraceError("Eventbrowser: {0}", ex);
            }
            finally { _stop.Dispose(); _stop = null; Channel.IsEnabled = Days.IsEnabled = Limit.IsEnabled = true; }
        }
    }
}
