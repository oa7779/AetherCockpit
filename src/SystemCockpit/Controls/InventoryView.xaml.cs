using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class InventoryView : UserControl
{
    private InventorySnapshot? _snapshot;
    private bool _busy;
    internal bool IsBusy => _busy;
    internal bool HasCompleted { get; private set; }
    internal CancellationToken LifetimeToken { get; set; }
    internal event EventHandler? StateChanged;
    internal Func<CancellationToken, IProgress<string>, Task<InventorySnapshot>> BuildInventory { get; set; } =
        (token, progress) => SystemInventory.BuildAsync(token, progress);

    public InventoryView()
    {
        InitializeComponent();
    }

    public async Task<bool> LoadAsync(CancellationToken token = default)
    {
        if (_busy) return false;
        _busy = true;
        HasCompleted = false;
        _snapshot = null;
        HardwareGrid.ItemsSource = null;
        SoftwareGrid.ItemsSource = null;
        RefreshButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        Investigation.Visibility = Visibility.Visible;
        InvestigationProgress.IsIndeterminate = true;
        Status.Text = DesktopUiText.Get("Inventory.Reading");
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var progress = new Progress<string>(message =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => { if (_busy) Status.Text = message; });
            });
            var snapshot = await BuildInventory(token, progress);
            token.ThrowIfCancellationRequested();
            _snapshot = snapshot;
            HardwareGrid.ItemsSource = snapshot.Hardware;
            SoftwareGrid.ItemsSource = snapshot.Software;
            SoftwareHeader.Text = DesktopUiText.Format("Inventory.SoftwareCount", snapshot.Software.Count);
            Status.Text = DesktopUiText.Format("Inventory.Completed", snapshot.GeneratedAt, snapshot.Hardware.Count, snapshot.Software.Count);
            SaveButton.IsEnabled = true;
            HasCompleted = true;
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Status.Text = DesktopUiText.Get("Inventory.Canceled");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or TimeoutException or System.Text.Json.JsonException)
        {
            System.Diagnostics.Trace.TraceError("AETHER Inventarisierung: {0}", ex);
            Status.Text = DesktopUiText.Format("Inventory.Failed", ex.Message);
            return false;
        }
        finally
        {
            _busy = false;
            InvestigationProgress.IsIndeterminate = false;
            Investigation.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async void RefreshClicked(object sender, RoutedEventArgs e) => await LoadAsync(LifetimeToken);

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = DesktopUiText.Get("Common.TextFileFilter"),
            FileName = $"AETHER-Inventar-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, SystemInventory.ToText(_snapshot)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, DesktopUiText.Get("Inventory.SaveFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
