using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SystemCockpit.Services;
using Forms = System.Windows.Forms;

namespace SystemCockpit;

internal sealed class DesktopController : IDisposable
{
    private readonly MainWindow _main;
    private readonly CockpitModel _model;
    private readonly Action<CockpitSettings> _save;
    private readonly Dictionary<string, GadgetWindow> _gadgets = [];
    private readonly Forms.NotifyIcon _tray;
    private readonly DispatcherTimer _desktopHealth;
    private bool _hidden, _refreshing, _disposed;
    private bool _refreshQueued;
    private DesktopSettingsWindow? _settingsWindow;
    private NetworkMonitorWindow? _networkWindow;
    public NetworkWatch Network { get; }
    internal int GadgetCount => _gadgets.Count;

    public DesktopController(MainWindow main, CockpitModel model, Action<CockpitSettings> save, Action exit)
    {
        _main = main;
        _model = model;
        _save = save;
        Network = new NetworkWatch(() => _model.Settings, Notify);
        _tray = new Forms.NotifyIcon
        {
            Text = "AETHER / Desktop und Netzwerk",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        void Menu(string text, Action action) => _tray.ContextMenuStrip.Items.Add(text, null,
            (_, _) => _main.Dispatcher.Invoke(action));
        Menu("AETHER oeffnen", () => WindowActivation.Show(_main));
        Menu("Desktop / Transparenz / Parameter", OpenSettings);
        Menu("Netzwerkueberwachung", OpenNetwork);
        Menu("Gadgets minimieren / wiederherstellen", () =>
        {
            _hidden = !_hidden;
            RefreshGadgets();
            foreach (var window in _gadgets.Values)
                if (_hidden) window.Hide(); else window.Show();
        });
        Menu("Gadgets schliessen", () => _save(_model.Settings with { GadgetsEnabled = false }));
        Menu("AETHER vollstaendig beenden", exit);
        _tray.DoubleClick += (_, _) => _main.Dispatcher.Invoke(() => WindowActivation.Show(_main));
        _model.PropertyChanged += ModelChanged;
        _model.Volumes.CollectionChanged += VolumesChanged;
        _desktopHealth = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _desktopHealth.Tick += (_, _) =>
        {
            if (_model.Settings.GadgetsEnabled) RefreshGadgets();
        };
        _desktopHealth.Start();
        ApplySettings();
        Network.StartMonitoring();
    }

    public void StartLogonCapture()
    {
        if (_model.Settings.CaptureAtLogon) Network.StartCapture(_model.Settings.CaptureDevice);
    }

    public void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new DesktopSettingsWindow(_model, _save, OpenNetwork);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        WindowActivation.Show(_settingsWindow);
    }

    public void OpenNetwork()
    {
        if (_networkWindow is null)
        {
            _networkWindow = new NetworkMonitorWindow(Network, _model, _save);
            _networkWindow.Closed += (_, _) => _networkWindow = null;
        }
        WindowActivation.Show(_networkWindow);
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CockpitModel.Settings)) ApplySettings();
    }
    private void VolumesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_refreshQueued || _disposed) return;
        _refreshQueued = true;
        _main.Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            RefreshGadgets();
        }, DispatcherPriority.Background);
    }

    private void ApplySettings()
    {
        _main.Opacity = _model.Settings.AppOpacity;
        RefreshGadgets();
    }

    private void RefreshGadgets()
    {
        if (_refreshing || _disposed) return;
        _refreshing = true;
        try
        {
            var items = GadgetCatalog.Get(_model);
            if (!_model.Settings.GadgetsEnabled) items.Clear();
            foreach (var id in _gadgets.Where(pair => !pair.Value.DesktopAlive ||
                         !items.Any(item => item.Id == pair.Key)).Select(pair => pair.Key).ToArray())
            {
                var window = _gadgets[id];
                _gadgets.Remove(id);
                window.Close();
            }
            foreach (var item in items)
            {
                if (!_model.Settings.GadgetsEnabled) break;
                if (_gadgets.TryGetValue(item.Id, out var existing))
                {
                    existing.Update(item.Data);
                    existing.SetOpacity(_model.Settings.GadgetLayouts.FirstOrDefault(layout => layout.Id == item.Id)?.Opacity
                        ?? _model.Settings.GadgetOpacity);
                    continue;
                }
                var area = SidebarDisplays.GetAll().First(display => display.IsPrimary).WorkArea;
                var layout = _model.Settings.GadgetLayouts.FirstOrDefault(layout => layout.Id == item.Id)
                    ?? new GadgetLayout(item.Id, area.Left + 20 + (_gadgets.Count % 3) * 320,
                        area.Top + 20 + (_gadgets.Count / 3) * 170, _model.Settings.GadgetOpacity);
                var gadget = new GadgetWindow(item.Id, item.Data, item.Template, layout, SaveLayout, message =>
                {
                    _save(_model.Settings with { GadgetsEnabled = false });
                    foreach (var opened in _gadgets.Values.ToArray()) opened.Close();
                    _gadgets.Clear();
                    Notify("Desktop-Gadgets deaktiviert: " + message + " In Einstellungen erneut aktivierbar.");
                });
                _gadgets.Add(item.Id, gadget);
                gadget.Closed += (_, _) => _gadgets.Remove(item.Id);
                gadget.Show();
                if (_hidden) gadget.Hide();
            }
        }
        finally { _refreshing = false; }
    }

    private void SaveLayout(GadgetLayout layout) => _save(_model.Settings with
    {
        GadgetLayouts = [.. _model.Settings.GadgetLayouts.Where(item => item.Id != layout.Id), layout]
    });

    private void Notify(string message)
    {
        if (_disposed) return;
        _tray.ShowBalloonTip(7000, "AETHER / Netzwerkhinweis", message, Forms.ToolTipIcon.Warning);
    }

    public async Task StopAsync() => await Network.StopAsync();

    public void Dispose()
    {
        _disposed = true;
        _desktopHealth.Stop();
        _model.PropertyChanged -= ModelChanged;
        _model.Volumes.CollectionChanged -= VolumesChanged;
        foreach (var window in _gadgets.Values.ToArray()) window.Close();
        _gadgets.Clear();
        _settingsWindow?.Close();
        _networkWindow?.Close();
        Network.Dispose();
        _tray.Visible = false;
        var icon = _tray.Icon;
        _tray.Dispose();
        icon?.Dispose();
    }
}

internal sealed record GadgetItem(string Id, object Data, string Template);

internal static class GadgetCatalog
{
    internal static List<GadgetItem> Get(CockpitModel model)
    {
        var result = new List<GadgetItem>();
        var settings = model.Settings;
        if (settings.SidebarWidgetCpu) result.Add(new("cpu", model.Cards[0], "MetricTemplate"));
        if (settings.SidebarWidgetMemory) result.Add(new("ram", model.Cards[1], "MetricTemplate"));
        if (settings.SidebarWidgetGpu) result.Add(new("gpu", model.Cards[2], "MetricTemplate"));
        if (settings.SidebarWidgetTemperatures)
            for (var i = 0; i < model.SidebarTemperatures.Count; i++)
                result.Add(new("temperature-" + i, model.SidebarTemperatures[i], "HistoryTemplate"));
        if (settings.SidebarWidgetNetwork)
        {
            result.Add(new("download", model.Cards[3], "MetricTemplate"));
            result.Add(new("upload", model.SidebarUpload, "HistoryTemplate"));
        }
        if (settings.SidebarWidgetDrives)
            result.AddRange(model.Volumes.Select(volume => new GadgetItem("volume-" + volume.Name, volume, "VolumeTemplate")));
        return result;
    }
}
