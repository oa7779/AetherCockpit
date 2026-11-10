using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using AetherOperations;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class HardwareIntelligenceView : UserControl
{
    private readonly HardwareInventoryStore _store = new();
    private readonly SessionStatistics _sensorStatistics = new();
    private CancellationTokenSource? _lifetime;
    private Task _pending = Task.CompletedTask;
    private Window? _window;
    private HwndSource? _source;
    private bool _busy, _subscribed, _shutdown;
    private readonly DispatcherTimer _changeDelay;
    private readonly DispatcherTimer _freshness;
    internal bool Synthetic { get; set; }
    internal Task Pending => _pending;
    internal Func<HardwareClass, CancellationToken, Task<IReadOnlyList<HardwareComponent>>> ReadInventory { get; set; } =
        new HardwareIntelligenceReader().ReadAsync;
    internal HardwareInventory Snapshot => _store.Snapshot(DateTimeOffset.UtcNow);
    internal event Action<HardwareComponent>? ProcessLinksRequested;
    internal bool SelectExisting(string id)
    {
        var component = Snapshot.Components.FirstOrDefault(c => c.Id == id);
        if (component is null) return false;
        Categories.SelectedValue = component.Class;
        Render();
        Components.SelectedItem = Components.Items.Cast<HardwareComponent>().FirstOrDefault(c => c.Id == id);
        return true;
    }
    internal void RequestExport()
    {
        if (ExportButton.IsEnabled) ExportClicked(this, new RoutedEventArgs());
        else Status.Text = "Zuerst eine Hardwareklasse lesen; danach vorhandene Daten exportieren.";
    }
    private void ProcessLinksClicked(object sender, RoutedEventArgs e)
    {
        if (Components.SelectedItem is HardwareComponent component) ProcessLinksRequested?.Invoke(component);
        else Status.Text = "Zuerst eine Komponente auswählen.";
    }

    private static readonly IReadOnlyDictionary<HardwareClass, string> Captions = new Dictionary<HardwareClass, string>
    {
        [HardwareClass.System] = "SYSTEM", [HardwareClass.Cpu] = "CPU", [HardwareClass.Gpu] = "GPU", [HardwareClass.Npu] = "NPU / KLASSENHINWEISE",
        [HardwareClass.Mainboard] = "MAINBOARD", [HardwareClass.Firmware] = "BIOS / UEFI", [HardwareClass.Chipset] = "CHIPSATZ",
        [HardwareClass.Memory] = "RAM", [HardwareClass.Storage] = "STORAGE", [HardwareClass.Pci] = "PCI / PCIe",
        [HardwareClass.Network] = "NETZWERK", [HardwareClass.Wlan] = "WLAN", [HardwareClass.Bluetooth] = "BLUETOOTH",
        [HardwareClass.Audio] = "AUDIO", [HardwareClass.Display] = "DISPLAY", [HardwareClass.Usb] = "USB",
        [HardwareClass.Security] = "SECURITY HARDWARE", [HardwareClass.Power] = "BATTERIE / POWER",
        [HardwareClass.Sensors] = "SENSOREN", [HardwareClass.Peripheral] = "PERIPHERIE"
    };

    public HardwareIntelligenceView()
    {
        InitializeComponent();
        _changeDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(750), DispatcherPriority.Background, ChangeDelayElapsed, Dispatcher);
        _changeDelay.Stop();
        _freshness = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, FreshnessTick, Dispatcher);
        _freshness.Stop();
        Categories.ItemsSource = Captions;
        Categories.SelectedValue = HardwareClass.System;
        Loaded += ViewLoaded;
        Unloaded += ViewUnloaded;
    }

    private HardwareClass Category => Categories.SelectedValue is HardwareClass category ? category : HardwareClass.System;

    private async void ViewLoaded(object sender, RoutedEventArgs e)
    {
        if (_shutdown) return;
        await _pending;
        if (!IsLoaded || _shutdown) return;
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();
        _freshness.Start();
        if (!Synthetic) Subscribe();
        if (_store.NeedsRefresh(Category, DateTimeOffset.UtcNow) && !Synthetic) await RefreshAsync();
        else Render();
    }

    private void ViewUnloaded(object sender, RoutedEventArgs e)
    {
        Unsubscribe();
        _lifetime?.Cancel();
    }

    public async Task ShutdownAsync()
    {
        _shutdown = true;
        Unsubscribe();
        _lifetime?.Cancel();
        await _pending;
        _lifetime?.Dispose();
        _lifetime = null;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        NetworkChange.NetworkAddressChanged += NetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChanged;
        _window = Window.GetWindow(this);
        _source = _window is null ? null : HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WindowMessage);
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        _changeDelay.Stop();
        _freshness.Stop();
        if (!_subscribed) return;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        NetworkChange.NetworkAddressChanged -= NetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChanged;
        _source?.RemoveHook(WindowMessage);
        _source = null;
        _window = null;
        _subscribed = false;
        // Changes while hidden are not observed; retained values cannot be asserted current on reopen.
        _store.Invalidate(Enum.GetValues<HardwareClass>(), "Hardwareansicht war geschlossen; Aktualität erneut prüfen.");
    }

    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Invalidate(Enum.GetValues<HardwareClass>(), "Windows Resume");
    }
    private void DisplayChanged(object? sender, EventArgs e) => Invalidate([HardwareClass.Display, HardwareClass.Gpu], "Displayänderung");
    private void NetworkChanged(object? sender, EventArgs e) => Invalidate([HardwareClass.Network, HardwareClass.Wlan], "Netzwerkänderung");
    private void NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => NetworkChanged(sender, e);
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0219 && wParam.ToInt64() is 7 or 0x8000 or 0x8004)
            Invalidate(Enum.GetValues<HardwareClass>().Where(c => c is not (HardwareClass.Cpu or HardwareClass.Firmware)),
                "PnP-Änderung; Geräteklassen bei Auswahl neu lesen");
        return IntPtr.Zero;
    }

    private void Invalidate(IEnumerable<HardwareClass> categories, string reason)
    {
        var affected = categories.ToArray();
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_subscribed) return;
            _store.Invalidate(affected, reason);
            Status.Text = reason + ". Betroffene Werte sind veraltet; ausgewählte Klasse aktualisieren.";
            Render();
            if (affected.Contains(Category))
            {
                _changeDelay.Stop();
                _changeDelay.Start();
            }
        });
    }

    private async void ChangeDelayElapsed(object? sender, EventArgs e)
    {
        if (_busy) return;
        _changeDelay.Stop();
        if (_subscribed && IsVisible && Category != HardwareClass.Sensors) await RefreshAsync();
    }

    private void FreshnessTick(object? sender, EventArgs e)
    {
        if (!IsVisible || Window.GetWindow(this)?.WindowState == WindowState.Minimized) return;
        var now = DateTimeOffset.UtcNow;
        if (Facts.ItemsSource is IReadOnlyList<HardwareFact> facts &&
            facts.Any(f => !ReferenceEquals(f, f.At(now))))
            Render();
    }

    internal void Present(HardwareClass category, IReadOnlyList<HardwareComponent> components)
    {
        _store.Replace(category, components, _store.Version(category));
        Categories.SelectedValue = category;
        Render();
    }

    internal void UpdateSensors(IReadOnlyList<SensorReading> readings, DateTimeOffset time, bool enabled, string? error)
    {
        if (_shutdown) return;
        if (!enabled) _sensorStatistics.Reset();
        if (enabled && error is null && readings.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == readings.Count)
        {
            try { _sensorStatistics.AddSensors(time, readings, retainAbsent: false); }
            catch (InvalidOperationException ex)
            {
                Trace.TraceError("Hardware-Sensorstatistik: {0}", ex);
                error = ex.Message;
            }
        }
        var components = HardwareSensorInventory.Read(readings, time, enabled, error, _sensorStatistics);
        if (enabled && error is null)
            components = HardwareSensorCorrelation.Apply(components, readings, Snapshot, time);
        _store.Replace(HardwareClass.Sensors, components, _store.Version(HardwareClass.Sensors));
        if (IsLoaded && Category == HardwareClass.Sensors) Render();
    }

    private async void CategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Components is null || !IsLoaded) return;
        Filter.Text = "";
        Render();
        if (!Synthetic && !_busy && _store.NeedsRefresh(Category, DateTimeOffset.UtcNow)) await RefreshAsync();
    }

    private async void RefreshClicked(object sender, RoutedEventArgs e) => await RefreshAsync();

    internal Task RefreshAsync()
    {
        if (_busy || _shutdown) return _pending;
        if (_lifetime is null || _lifetime.IsCancellationRequested)
        {
            _lifetime?.Dispose();
            _lifetime = new CancellationTokenSource();
        }
        return _pending = LoadAsync(Category, _lifetime.Token);
    }

    private async Task LoadAsync(HardwareClass category, CancellationToken token)
    {
        _busy = true;
        RefreshButton.IsEnabled = false;
        Categories.IsEnabled = false;
        var version = _store.Version(category);
        Status.Text = Captions[category] + " / lesende Abfrage läuft; keine Systemänderung.";
        try
        {
            var components = await ReadInventory(category, token);
            token.ThrowIfCancellationRequested();
            _store.Replace(category, components, version);
            Status.Text = $"{Captions[category]} / {components.Count} Einträge. Quellenstatus je Wert beachten. Export enthält nur bereits gelesene Klassen.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status.Text = "Hardwareabfrage abgebrochen."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            Win32Exception or TimeoutException or JsonException or COMException or NetworkInformationException)
        {
            Trace.TraceError("Hardware Intelligence: {0}", ex);
            var state = ex is UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.ProviderError;
            var reason = $"Hardwareabfrage fehlgeschlagen ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}).";
            _store.Fail(category, reason, state, DateTimeOffset.UtcNow);
            Status.Text = reason + " Vorherige Werte sind veraltet.";
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
            Categories.IsEnabled = true;
            if (!_shutdown) Render();
        }
    }

    private void FilterChanged(object sender, TextChangedEventArgs e) { if (Components is not null) Render(); }
    private void Render()
    {
        var old = (Components.SelectedItem as HardwareComponent)?.Id;
        var current = Snapshot;
        var selected = current.Components.Where(c => c.Class == Category &&
            c.Name.Contains(Filter.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        Components.ItemsSource = selected;
        Components.SelectedItem = selected.FirstOrDefault(c => c.Id == old) ?? selected.FirstOrDefault();
        ExportButton.IsEnabled = current.Components.Count > 0;
        if (selected.Length == 0)
        {
            ComponentTitle.Text = "Keine geladenen Komponenten";
            Facts.ItemsSource = null;
            Inspector.Text = "Klasse auswählen und lesen. Keine Information wird aus einem Produktnamen geraten.";
        }
    }

    private void ComponentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Components.SelectedItem is not HardwareComponent component) return;
        var old = (Facts.SelectedItem as HardwareFact)?.Key;
        ComponentTitle.Text = component.Name + (component.ParentId is { } parent
            ? "\n↳ " + (Snapshot.Components.FirstOrDefault(c => c.Id == parent)?.Name ?? "Elterngerät nicht geladen") : "");
        Facts.ItemsSource = component.Facts;
        Facts.SelectedItem = component.Facts.FirstOrDefault(f => f.Key == old) ?? component.Facts.FirstOrDefault();
    }

    private void FactChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Facts.SelectedItem is HardwareFact fact) Inspector.Text = Describe(fact.At(DateTimeOffset.UtcNow));
    }

    internal static string Describe(HardwareFact fact)
    {
        var m = fact.Observation.Metadata;
        return $"{fact.Name}\n\nWert: {fact.Display}\nStatus: {m.Availability}\nQuelle: {m.Source}\nSourceType: {m.SourceType}\n" +
            $"Erfasst: {m.Timestamp:O}\nConfidence: {m.Confidence}\nKlasse: {fact.Cadence}\n" +
            $"RequiresElevation: {m.RequiresElevation}\nIsEstimated: {m.IsEstimated}\nIsCalculated: {m.IsCalculated}\n" +
            $"Classification: {m.Classification}\nIsSensitive: {m.IsSensitive}\n\nUrsache: {m.Error ?? "Keine gemeldet"}\n\n" +
            $"Dokumentation: {m.DocumentationLink?.AbsoluteUri ?? "Nicht verfügbar"}\n\n" +
            $"Berechnung / Eingangsquelle: {fact.Calculation ?? "Nicht berechnet"}\n" +
            $"Datenbank: {fact.DatabaseVersion ?? "Keine"}\nMatch: {fact.Match ?? "Keiner"}";
    }

    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        var consent = MessageBox.Show(Window.GetWindow(this),
            "Sensitive Hardwarekennungen (z. B. Seriennummern, Gerätepfade, MAC und IP) ausdrücklich mit exportieren?\n\n" +
            "Nein: redigierter Bericht. Abbrechen: kein Export. Secret wird niemals exportiert. Es erfolgt kein Upload.",
            "AETHER / Hardwarebericht", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
        if (consent == MessageBoxResult.Cancel) return;
        var dialog = new SaveFileDialog { Filter = "JSON|*.json|CSV|*.csv|Text|*.txt", FileName = "AETHER-Hardware", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, HardwareReport.Export(Snapshot, dialog.FilterIndex switch { 1 => "json", 2 => "csv", _ => "txt" },
                consent == MessageBoxResult.Yes));
            Status.Text = "Hardwarebericht lokal gespeichert. Nur gelesene Klassen enthalten; keine externen Uploads.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceError("Hardwarebericht: {0}", ex);
            Status.Text = "Hardwarebericht nicht gespeichert: " + ex.Message;
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Hardwarebericht fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
