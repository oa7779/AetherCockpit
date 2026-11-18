using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using AetherOperations.Intelligence;

namespace AetherLocalAiSetup;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _stop = new();
    private readonly AetherProductModel _phi = AetherProductModelCatalog.Current.Models.Single(model => model.ModelId == "phi4-mini:3.8b");
    private AetherProductModel? _selectedModel;
    private AetherSetupHardware? _hardware;
    private AetherLocalAiCapabilityResult? _capability;
    private CancellationTokenSource? _setupStop;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = "AETHER " + (typeof(App).Assembly.GetCustomAttributes(typeof(
            System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Select(attribute => attribute.InformationalVersion.Split('+')[0])
            .FirstOrDefault() ?? "0.7.0 Beta 53");
        ModelStorePathText.Text = AetherLocalAiStoreContract.ModelStorePath;
        StoreConfigurationAccepted.Content =
            $"Ich bestätige die Konfiguration des AETHER-Modellstores und des Loopback-Endpunkts {AetherLocalAiStoreContract.Host}. Vorhandene Fremdmodelle bleiben unberührt.";
        Loaded += Window_Loaded;
        Closed += (_, _) =>
        {
            _stop.Cancel();
            _setupStop?.Cancel();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (SystemParameters.ClientAreaAnimation)
            BrandLogo.BeginAnimation(OpacityProperty,
                new DoubleAnimation(.84, 1, TimeSpan.FromSeconds(2.8))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        else
            BrandLogo.BeginAnimation(OpacityProperty, null);
        SetupButton.IsEnabled = false;
        ClearError();
        try
        {
            _hardware = await new AetherSetupHardwareProbe().ReadAsync(_stop.Token);
            _capability = AetherSetupCapabilityGate.Evaluate(AetherProductModelCatalog.Current, _hardware);
            CapabilityLabel.Text = _capability.Label;
            CapabilityDetails.Text = string.Join(Environment.NewLine, _capability.Reasons);
            HardwareFacts.Text = DescribeHardware(_hardware);
            ModelQualification.Text = DescribeModel(_phi);
            UpdateSetupAvailability();
            var inspectionModel = _selectedModel ?? _phi;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var runtime = await new OllamaSetupOperations().InspectRuntimeAsync(inspectionModel, timeout.Token);
            RuntimeFacts.Text = DescribeRuntime(runtime, inspectionModel);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or
            HttpRequestException or Win32Exception or SecurityException or TimeoutException or System.Text.Json.JsonException or
            System.Runtime.InteropServices.COMException)
        {
            _capability = new(AetherLocalAiCapability.LocalAiLimited, null,
                ["Die System- oder Katalogprüfung konnte nicht abgeschlossen werden."]);
            CapabilityLabel.Text = _capability.Label;
            CapabilityDetails.Text = "Die System- oder Katalogprüfung konnte nicht abgeschlossen werden: " + ex.Message;
            RuntimeFacts.Text = "Ollama-Kompatibilität konnte nicht bestätigt werden.";
            ProgressStatus.Text = "Lokale KI bleibt deaktiviert; AETHER kann weiterhin regelbasiert arbeiten.";
            ShowError("Die Prüfung konnte nicht abgeschlossen werden. AETHER bleibt verfügbar. Prüfen Sie die Systeminformationen und starten Sie den Assistenten erneut.", ex);
        }
    }

    private static string DescribeHardware(AetherSetupHardware hardware)
    {
        var lines = new List<string>
        {
            $"Windows: {hardware.WindowsVersion?.ToString() ?? "nicht verfügbar"} · {(hardware.Is64BitOperatingSystem ? "x64" : "nicht x64")}",
            $"CPU: {hardware.CpuName ?? "nicht verfügbar"} · Kerne: {hardware.CpuCores?.ToString() ?? "nicht verfügbar"} · logische Prozessoren: {hardware.LogicalProcessors?.ToString() ?? "nicht verfügbar"}",
            $"Arbeitsspeicher: installiert {FormatBytes(hardware.InstalledRamBytes)} · verfügbar {FormatBytes(hardware.AvailableRamBytes)}",
            $"Freier Speicher am Modellstore: {FormatBytes(hardware.FreeModelStoreBytes)}"
        };
        if (hardware.Gpus.IsEmpty)
            lines.Add("GPU / dedizierter VRAM: keine GPU erkannt");
        else
            lines.AddRange(hardware.Gpus.Select(gpu =>
                $"GPU: {gpu.Name} · dedizierter VRAM {FormatBytes(gpu.DedicatedVramBytes)} · verfügbar {FormatBytes(gpu.AvailableVramBytes)} · Treiber {gpu.DriverVersion ?? "nicht verfügbar"}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeModel(AetherProductModel model)
    {
        var download = model.DownloadSizeBytes is { } bytes
            ? FormatBytes(bytes) + " (Manifest-Payload; lokale Cachetreffer reduzieren den Transfer)"
            : "nicht belegt";
        var layers = model.ExpectedLayers.IsEmpty
            ? "keine belegten Layer"
            : string.Join(", ", model.ExpectedLayers.Select(layer => $"{layer.MediaType}: {layer.Digest}"));
        var missing = new List<string>();
        if (model.Revision is null) missing.Add("Revision");
        if (model.MinimumRamBytes is null) missing.Add("Mindest-RAM");
        if (model.MinimumAvailableRamBytes is null) missing.Add("verfügbarer Mindest-RAM");
        if (model.MinimumVramBytes is null) missing.Add("Mindest-VRAM");
        if (model.MinimumStorageBytes is null) missing.Add("Mindest-Datenträger");
        if (model.InstallationSizeBytes is null) missing.Add("Installationsgröße");
        if (model.QualificationStatus != AetherModelQualificationStatus.Qualified)
            missing.Add("Qualifikationsnachweis");
        return $"{model.DisplayName} · {model.ModelId} · Publisher {model.Publisher ?? "nicht belegt"} · " +
            $"Variante {model.Variant ?? "nicht belegt"} · Revision {model.Revision ?? "nicht belegt"} · " +
            $"Runtime {model.Runtime ?? "nicht belegt"} · Provider {model.Provider?.ToString() ?? "nicht belegt"} · " +
            $"Quantisierung {model.Quantization ?? "nicht belegt"} · Lizenz {model.LicenseName ?? "nicht belegt"}." +
            Environment.NewLine + $"Downloadquelle: {model.DownloadSource?.AbsoluteUri ?? "nicht belegt"}." +
            Environment.NewLine + $"Manifest-Digest: {model.ExpectedOllamaDigest ?? "nicht belegt"}." +
            Environment.NewLine + $"Layer: {layers}." +
            Environment.NewLine + $"Rollen: {(model.SupportedRoles.IsEmpty ? "nicht belegt" : string.Join(", ", model.SupportedRoles))} · " +
            $"AETHER-Kontext: {(model.ContextLimitTokens?.ToString() ?? "nicht belegt")} Token · Download-Payload: {download}." +
            Environment.NewLine + (missing.Count == 0
                ? "Qualifikation abgeschlossen."
                : "Automatische Freigabe gesperrt: " + string.Join(", ", missing) + " nicht belegt.");
    }

    private static string DescribeRuntime(AetherOllamaRuntimeStatus runtime, AetherProductModel model)
    {
        if (!runtime.Installed)
            return "Ollama ist nicht installiert. Setup lädt keinen Installer, bevor die Modellfreigabe und Hardwareeignung bestätigt sind.";
        var path = runtime.ExecutablePath ?? "Pfad nicht verfügbar";
        var version = runtime.Version ?? "Version nicht verfügbar";
        var listener = !runtime.ListenerAvailable ? "kein Listener" :
            runtime.LoopbackOnly ? "nur Loopback" : "Listener außerhalb des Loopbacks";
        var store = runtime.ModelStorePath ?? "nicht ermittelbar";
        var modelStatus = runtime.ModelPresent
            ? runtime.ModelOwned
                ? $"freigegebenes {model.DisplayName} im AETHER-Store vorhanden"
                : $"{model.DisplayName} vorhanden, Herkunft nicht bestätigt"
            : $"kein freigegebenes {model.DisplayName} vorhanden";
        return $"Version: {version} · Pfad: {path}" + Environment.NewLine +
            $"Signatur: {(runtime.OfficialSignatureValid ? "Ollama Inc. bestätigt" : "nicht bestätigt")} · " +
            $"API-Kompatibilität: {(runtime.ApiCompatible ? "bestätigt" : "nicht bestätigt")} · Listener: {listener} · Store: {store}" + Environment.NewLine +
            $"AETHER-Store: {(runtime.ModelStoreOwned && runtime.StoreConfigurationVerified ? "bestätigt" : "nicht bestätigt")} · Modell: {modelStatus}";
    }

    private static string FormatBytes(long? value) => value is { } bytes
        ? $"{bytes:N0} Bytes ({bytes / 1073741824d:N1} GiB)"
        : "nicht verfügbar";

    private void UpdateSetupAvailability()
    {
        _selectedModel = _capability?.ModelId == _phi.ModelId ? _phi : null;
        var requirementsSatisfied = _selectedModel is
            { QualificationStatus: AetherModelQualificationStatus.Qualified, ApprovedForAutomaticSelection: true } &&
            _capability?.CanInstall == true;
        LicenseAccepted.IsEnabled = requirementsSatisfied && _phi.LicenseAcceptanceRequired;
        StoreConfigurationAccepted.IsEnabled = requirementsSatisfied;
        var licenseAccepted = !_phi.LicenseAcceptanceRequired || LicenseAccepted.IsChecked == true;
        SetupButton.IsEnabled = _busy || requirementsSatisfied &&
            licenseAccepted &&
            StoreConfigurationAccepted.IsChecked == true;
    }

    private void AcceptanceChanged(object sender, RoutedEventArgs e) => UpdateSetupAvailability();

    private async void SetupButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _setupStop?.Cancel();
            return;
        }
        if (_hardware is null || _capability is null) return;
        var selectedModel = _selectedModel;
        if (selectedModel is null) return;
        _busy = true;
        using var operationStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _setupStop = operationStop;
        UpdateSetupAvailability();
        SetupButton.Content = "Abbrechen";
        SetupProgress.Visibility = Visibility.Visible;
        SetupProgress.IsIndeterminate = true;
        SuccessNotice.Visibility = Visibility.Collapsed;
        ClearError();
        try
        {
            ProgressStatus.Text = "System wird geprüft …";
            var currentHardware = await new AetherSetupHardwareProbe().ReadAsync(operationStop.Token);
            _hardware = currentHardware;
            _capability = AetherSetupCapabilityGate.Evaluate(AetherProductModelCatalog.Current, currentHardware);
            CapabilityLabel.Text = _capability.Label;
            CapabilityDetails.Text = string.Join(Environment.NewLine, _capability.Reasons);
            HardwareFacts.Text = DescribeHardware(currentHardware);
            UpdateSetupAvailability();
            if (!_capability.CanInstall || _capability.ModelId != selectedModel.ModelId)
            {
                ProgressStatus.Text = "Die aktuelle Systemprüfung hat die Modellauswahl geändert oder erfüllt den Vertrag nicht. Bitte Lizenz und Einrichtung erneut prüfen; es wurde nichts heruntergeladen.";
                return;
            }
            var progress = new Progress<AetherLocalAiSetupProgress>(value =>
            {
                ProgressStatus.Text = DescribeProgress(value, selectedModel);
                SetupProgress.IsIndeterminate = value.TotalBytes is null or <= 0;
                if (!SetupProgress.IsIndeterminate && value.TotalBytes is { } total && total > 0)
                    SetupProgress.Value = Math.Clamp(value.CompletedBytes.GetValueOrDefault() * 100d / total, 0, 100);
            });
            await new AetherLocalAiSetupWorkflow(new OllamaSetupOperations()).RunAsync(
                AetherProductModelCatalog.Current, selectedModel, currentHardware,
                LicenseAccepted.IsChecked == true,
                StoreConfigurationAccepted.IsChecked == true, progress, operationStop.Token);
            ProgressStatus.Text = "Lokale KI wurde geprüft, eingerichtet und im AETHER-Katalog freigegeben.";
            CapabilityLabel.Text = "AETHER Intelligence ist bereit";
            CapabilityLabel.Foreground = (Brush)FindResource("Success");
            ResultAccent.Background = (Brush)FindResource("Success");
            CapabilityDetails.Text = "Phi 4 Mini wurde für die lokale Verwendung durch AETHER eingerichtet und geprüft.";
            SuccessNotice.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (operationStop.IsCancellationRequested)
        {
            ProgressStatus.Text = "Einrichtung abgebrochen. Der Modellkatalog wurde nicht freigegeben.";
            CapabilityLabel.Text = "Einrichtung abgebrochen";
            CapabilityLabel.Foreground = (Brush)FindResource("Warning");
            ResultAccent.Background = (Brush)FindResource("Warning");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or UnauthorizedAccessException or
            HttpRequestException or Win32Exception or SecurityException or TimeoutException or System.Text.Json.JsonException or
            System.Runtime.InteropServices.COMException or AggregateException)
        {
            CapabilityLabel.Text = "Einrichtung nicht abgeschlossen";
            CapabilityLabel.Foreground = (Brush)FindResource("Error");
            ResultAccent.Background = (Brush)FindResource("Error");
            CapabilityDetails.Text = "Der aktuelle Schritt ist fehlgeschlagen. Sie können die Ursache in den technischen Details prüfen und die Einrichtung anschließend erneut starten.";
            ProgressStatus.Text = "Einrichtung fehlgeschlagen. AETHER bleibt ohne eine unbestätigte Modellfreigabe verfügbar.";
            ShowError("Einrichtung nicht abgeschlossen. Prüfen Sie die technischen Details, beheben Sie die Ursache und starten Sie die Einrichtung erneut.", ex);
        }
        finally
        {
            _busy = false;
            _setupStop = null;
            SetupButton.Content = "Lokale KI einrichten";
            SetupProgress.IsIndeterminate = false;
            SetupProgress.Visibility = Visibility.Collapsed;
            UpdateSetupAvailability();
        }
    }

    private static string DescribeProgress(AetherLocalAiSetupProgress progress, AetherProductModel model)
    {
        var phase = progress.Phase switch
        {
            AetherLocalAiSetupPhase.CheckingSystem => "System wird geprüft",
            AetherLocalAiSetupPhase.InstallingAether => "AETHER wird installiert",
            AetherLocalAiSetupPhase.PreparingLocalAi => "Lokale KI wird vorbereitet",
            AetherLocalAiSetupPhase.ConfiguringOllama => "Ollama wird eingerichtet",
            AetherLocalAiSetupPhase.DownloadingModel => $"{model.DisplayName} wird heruntergeladen",
            AetherLocalAiSetupPhase.VerifyingModel => "Modelldaten werden geprüft",
            AetherLocalAiSetupPhase.ConfiguringLocalAi => "Lokale KI wird konfiguriert",
            AetherLocalAiSetupPhase.Completing => "Installation wird abgeschlossen",
            _ => throw new InvalidOperationException("Unbekannte Setup-Phase.")
        };
        if (progress.CompletedBytes is not { } completed)
            return phase + " …";
        return progress.TotalBytes is { } total && total > 0
            ? $"{phase} · {completed:N0} / {total:N0} Bytes"
            : $"{phase} · {completed:N0} Bytes";
    }

    private void LicenseLink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void WindowHeader_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed ||
            e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
            return;
        DragMove();
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void ShowError(string message, Exception exception)
    {
        ProgressStatus.Foreground = (Brush)FindResource("Error");
        ProgressStatus.Text = message;
        ErrorDetailsHeading.Visibility = Visibility.Visible;
        ErrorDetails.Visibility = Visibility.Visible;
        ErrorDetails.Text = exception.ToString();
    }

    private void ClearError()
    {
        ProgressStatus.Foreground = (Brush)FindResource("Muted");
        ErrorDetailsHeading.Visibility = Visibility.Collapsed;
        ErrorDetails.Visibility = Visibility.Collapsed;
        ErrorDetails.Text = string.Empty;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
