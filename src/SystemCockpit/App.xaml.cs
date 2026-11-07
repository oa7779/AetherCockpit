using System.Windows;

namespace SystemCockpit;

public partial class App : Application
{
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _activationWait;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!Services.SystemActions.IsAdmin())
        {
            MessageBox.Show("AETHER benoetigt Administratorrechte. Bitte Windows-UAC beim Programmstart freigeben.",
                "AETHER / Administratorrechte erforderlich", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        base.OnStartup(e);
        var trayStart = e.Args.Contains("--tray", StringComparer.Ordinal);
        if (trayStart && !System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "desktop-defaults.json")))
        {
            MessageBox.Show("Der Anmeldestart ist nur fuer die installierte Ausgabe konfiguriert.",
                "AETHER / Kein installierter Autostart", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(ex.Message, "Setup-Einstellungen nicht uebernommen", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        // Erststart: Funktionsuebersicht, EULA-Pflichtzustimmung und Rechner-Inventar.
        // Der Assistent laeuft bewusst IMMER im Original-Design (Retro-Neon);
        // der gespeicherte Skin wird erst danach angewendet und wirkt dann sofort.
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
        try { Services.SkinCatalog.Apply(this, Services.CockpitSettings.Load().Skin); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceError("AETHER Skin: {0}", ex);
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
                MessageBox.Show(ex.Message, "AETHER / Hintergrundstart fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
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
            boot.Report("[ACCESS] ADMINISTRATOR / Windows-UAC freigegeben", 3);
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
            MessageBox.Show(boot, ex.Message, "AETHER / Initialisierung fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            if (cockpit is not null) await cockpit.ReleaseBeforeStartupAsync();
            boot.CloseAfterStartup();
            Shutdown(1);
        }

    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        _activation?.Dispose();
        base.OnExit(e);
    }
}
