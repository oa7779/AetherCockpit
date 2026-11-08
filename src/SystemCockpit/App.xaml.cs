using System.Windows;

namespace SystemCockpit;

public partial class App : Application
{
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _activationWait;

    protected override async void OnStartup(StartupEventArgs e)
    {
        try { Services.DesktopText.ConfigureSupportedUi(Services.CockpitSettings.Load().Language); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException)
        {
            Services.DesktopText.Configure("de");
            System.Diagnostics.Trace.TraceError("AETHER Sprache: {0}", ex);
        }
        if (!Services.SystemActions.IsAdmin())
        {
            MessageBox.Show(Services.DesktopText.Get("App.AdminRequired.Body"),
                Services.DesktopText.Get("App.AdminRequired.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        base.OnStartup(e);
        var trayStart = e.Args.Contains("--tray", StringComparer.Ordinal);
        if (trayStart && !System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "desktop-defaults.json")))
        {
            MessageBox.Show(Services.DesktopUiText.Get("App.TrayUnavailable"),
                Services.DesktopUiText.Get("App.TrayUnavailableTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Windows-Benutzerkennung nicht verfuegbar.");
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AetherCockpit-" + identity, out var first);
        if (!first)
        {
            if (!trayStart) _activation.Set();
            Shutdown();
            return;
        }
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window) Services.WindowActivation.Show(window);
            });
        }, null, Timeout.Infinite, false);
        try { Services.SetupProfile.ApplyInstalledProfile(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceError("AETHER Setup-Profil: {0}", ex);
            MessageBox.Show(ex.Message, Services.DesktopUiText.Get("App.SetupFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Services.CockpitSettings startupSettings;
        try { startupSettings = Services.CockpitSettings.Load(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException)
        {
            startupSettings = new();
            System.Diagnostics.Trace.TraceError("AETHER Einstellungen vor UI-Start: {0}", ex);
        }
        Services.DesktopText.ConfigureSupportedUi(startupSettings.Language);
        try { Services.SkinCatalog.Apply(this, startupSettings.Skin); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException or System.Windows.Markup.XamlParseException)
        {
            System.Diagnostics.Trace.TraceError("AETHER Design: {0}", ex);
        }
        Services.ModernWindowAppearance.Enable();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += WindowsPreferencesChanged;
        // Erststart: Funktionsuebersicht, EULA-Pflichtzustimmung und Rechner-Inventar.
        // Sprache und Design werden vor dem ersten Programmfenster gesetzt.
        var consent = Services.EulaConsent.Default;
        if (!consent.IsAccepted())
        {
            if (trayStart)
            {
                System.Diagnostics.Trace.TraceError("AETHER Autostart: EULA noch nicht bestaetigt. App einmal manuell starten.");
                Shutdown(1);
                return;
            }
            var firstRun = new FirstRunWindow();
            firstRun.Loaded += (_, _) => firstRun.Activate();
            if (firstRun.ShowDialog() != true) { Shutdown(); return; }
            try { consent.Accept(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceError("AETHER EULA: {0}", ex);
            }
        }
        if (trayStart)
        {
            MainWindow? background = null;
            try
            {
                background = new MainWindow();
                MainWindow = background;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                await background.InitializeHardwareAsync((_, _) => { }, CancellationToken.None);
                background.StartBackgroundServices();
                background.InitializeDesktop(true);
                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.ComponentModel.Win32Exception or
                InvalidOperationException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                System.Diagnostics.Trace.TraceError("AETHER Hintergrundstart: {0}", ex);
                MessageBox.Show(ex.Message, Services.DesktopUiText.Get("App.BackgroundFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
                if (background is not null) background.RequestExit();
                else Shutdown(1);
            }
            return;
        }
        var boot = new BootWindow();
        MainWindow? cockpit = null;
        Services.WindowActivation.Show(boot);
        try
        {
            boot.Report(Services.DesktopUiText.Get("Boot.AccessGranted"), 3);
            cockpit = new MainWindow();
            var warnings = await cockpit.InitializeHardwareAsync(boot.Report, boot.Token);
            await boot.FinishAsync(warnings);
            cockpit.SetBootTranscript(boot.Transcript);
            MainWindow = cockpit;
            Services.WindowActivation.Show(cockpit);
            cockpit.InitializeDesktop(false);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            boot.CloseAfterStartup();
        }
        catch (OperationCanceledException) when (boot.Token.IsCancellationRequested)
        {
            if (cockpit is not null) await cockpit.ReleaseBeforeStartupAsync();
            boot.CloseAfterStartup();
            Shutdown();
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.ComponentModel.Win32Exception or
            InvalidOperationException or UnauthorizedAccessException or System.Windows.Markup.XamlParseException or
            NotSupportedException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceError("AETHER Boot: {0}", ex);
            MessageBox.Show(boot, ex.Message, Services.DesktopUiText.Get("App.InitializationFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            if (cockpit is not null) await cockpit.ReleaseBeforeStartupAsync();
            boot.CloseAfterStartup();
            Shutdown(1);
        }

    }

    private void WindowsPreferencesChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.Color || !Services.SkinCatalog.IsModern ||
            Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!Dispatcher.HasShutdownStarted && Services.SkinCatalog.IsModern)
            {
                try { Services.SkinCatalog.Apply(this, "windows-modern"); }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
                    System.Windows.Markup.XamlParseException or InvalidOperationException)
                {
                    System.Diagnostics.Trace.TraceError("AETHER Windows Modern appearance refresh: {0}", ex);
                }
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= WindowsPreferencesChanged;
        if (_activationWait is { } wait)
        {
            using var callbacksFinished = new EventWaitHandle(false, EventResetMode.ManualReset);
            if (wait.Unregister(callbacksFinished)) callbacksFinished.WaitOne();
            _activationWait = null;
        }
        _activation?.Dispose();
        _activation = null;
        base.OnExit(e);
        Services.DesktopRestart.StartReplacementAfterExit();
    }
}
