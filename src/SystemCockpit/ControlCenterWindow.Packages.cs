using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using SystemCockpit.Services;

namespace SystemCockpit;

public partial class ControlCenterWindow
{
    private void ExternalPackagesClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "UniGetUI / WingetUI (*.exe)|UniGetUI.exe;WingetUI.exe",
            Title = "Bereits installiertes UniGetUI / WingetUI bewusst auswaehlen" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, $"Externes Programm starten:\n{dialog.FileName}\n\n" +
                "Fuer weitere Paketmanager (z.B. Scoop/Chocolatey). Keine native AETHER-Verwaltung dieser Quellen. " +
                "Nur vertrauenswuerdige Installation verwenden. Das externe Programm hat eigene Einstellungen, Freigaben und Protokolle; " +
                "seine Aktionen werden nicht in AETHERs Auftraegen erfasst. AETHER installiert oder aktualisiert dabei selbst keine Pakete.",
                "Externe Paketverwaltung", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            using var process = Process.Start(PackageCenter.ExternalManager(dialog.FileName))
                ?? throw new InvalidOperationException("Windows hat keinen Paketmanager-Prozess gestartet.");
            PackagesInfo.Text = "Externe Paketverwaltung gestartet. AETHER-Liste nach externen Aenderungen neu laden.";
            _packageKind = "stale";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or InvalidOperationException)
        { PackagesInfo.Text = "Externe Paketverwaltung nicht gestartet: " + ex.Message; }
    }

    private async void PackageDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (PackagesGrid.SelectedItems.Count != 1 || PackagesGrid.SelectedItem is not WingetRow row)
        { PackagesInfo.Text = "Genau ein Paket fuer Details markieren."; return; }
        string script;
        try { script = PackageCenter.TargetCommandScript("show", row); }
        catch (ArgumentException ex) { PackagesInfo.Text = ex.Message; return; }
        await RunAsync("Paketdetails / " + row.Id, async token =>
        {
            var result = await AdminShell.RunAsync(script, TimeSpan.FromMinutes(3), token, ProgressOutput(raw: true));
            if (result.ExitCode != 0) throw new InvalidOperationException($"Paketdetails: WinGet Exit {result.ExitCode}; Ausgabe beachten.");
            return "Hersteller, Lizenz und Installerangaben stammen von der konfigurierten Quelle; vor Installation pruefen.";
        });
    }

    private void PackageExportClicked(object sender, RoutedEventArgs e)
    {
        var rows = PackagesGrid.SelectedItems.Count > 0
            ? PackagesGrid.SelectedItems.Cast<WingetRow>().ToList() : _packageRows;
        var eligible = rows.Where(r => r.Actionable && PackageCenter.ValidSource(r.Source)).ToList();
        try
        {
            var json = PackageCenter.SerializeBundle(eligible);
            var excluded = rows.Count - eligible.Count;
            if (MessageBox.Show(this,
                    $"{eligible.Count} Paket-IDs mit Quelle exportieren; {excluded} Eintraege ohne eindeutige ID/Quelle ausgeschlossen.\n" +
                    "Auswahl, sonst geladene Liste. Kein vollstaendiges Softwarebackup, keine Installer, Einstellungen oder Lizenzen. " +
                    "Versionen sind nur Hinweise; Import installiert spaeter die verfuegbare Version der Quelle.",
                    "Paketliste pruefen", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var dialog = new SaveFileDialog { Filter = "AETHER-Paketliste (*.aether-packages.json)|*.aether-packages.json",
                FileName = "Aether-Software.aether-packages.json" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
            PackagesInfo.Text = $"{eligible.Count} Pakete exportiert; {excluded} ausgeschlossen. Keine Systemaenderung.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { PackagesInfo.Text = "Paketexport fehlgeschlagen: " + ex.Message; }
    }

    private void PackageImportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "AETHER-Paketliste (*.aether-packages.json)|*.aether-packages.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bundle = PackageCenter.ReadBundle(dialog.FileName);
            _packageRows = bundle.Packages.Select(p => new WingetRow(p.Name, p.Id, p.Version, "", p.Source)).ToList();
            _packageKind = "import";
            PackagesGrid.ItemsSource = _packageRows;
            PackagesInfo.Text = $"{_packageRows.Count} IMPORTIERTE WUNSCHPAKETE / nichts installiert. " +
                "Keine Ist-Liste! Quellen/IDs pruefen, gewuenschte Zeilen markieren und separat installieren. " +
                "Quellen muessen lokal bereits eingerichtet sein. Versionen sind keine Versionssperren.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or DecoderFallbackException)
        { PackagesInfo.Text = "Paketimport fehlgeschlagen; bisherige Liste bleibt erhalten: " + ex.Message; }
    }

    internal async Task<string> RunPackageQueueAsync(string verb, IReadOnlyList<WingetRow> rows, CancellationToken token,
        Func<WingetRow, CancellationToken, Task<CommandResult>> execute, Action<string, object> journal)
    {
        if (rows.Count is < 1 or > PackageCenter.MaxIds ||
            rows.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
            throw new ArgumentException("Paketlauf braucht 1..100 eindeutige IDs.");
        foreach (var row in rows) _ = PackageCenter.TargetCommandScript(verb, row);
        var jobs = rows.Select(row => (Row: row, Job: _jobs.Add($"{verb} / {row.Id} / {row.Source}"))).ToArray();
        var failed = 0;
        var completed = 0;
        var skipped = 0;
        try
        {
            foreach (var (row, job) in jobs)
            {
                token.ThrowIfCancellationRequested();
                journal("package-start", new { verb, row.Id, row.Source });
                job.State = "LAEUFT";
                AppendTerminal($"\n[PAKET] {verb} / {row.Id} / Quelle: {(row.Source.Length == 0 ? "nicht gemeldet / WinGet-Zuordnung" : row.Source)}\n");
                try
                {
                    var result = await execute(row, token);
                    if (result.ExitCode == 0)
                    { job.State = "ABGESCHLOSSEN"; completed++; }
                    else if (verb == "upgrade" && result.ExitCode == -1978335189)
                    { job.State = "KEIN UPGRADE"; skipped++; }
                    else { job.State = "FEHLGESCHLAGEN"; failed++; }
                    job.Detail = $"WinGet Exit {result.ExitCode}; Ausgabe und anschliessend Programmliste pruefen.";
                }
                catch (OperationCanceledException)
                {
                    job.State = "ABGEBROCHEN";
                    job.Detail = "Installer kann bereits Aenderungen vorgenommen haben.";
                    journal("package-result", new { verb, row.Id, row.Source, job.State, job.Detail });
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or TimeoutException)
                {
                    job.State = "FEHLGESCHLAGEN"; job.Detail = ex.Message; failed++;
                    AppendTerminal($"\n[PAKETFEHLER] {row.Id}: {ex.Message}\n");
                }
                journal("package-result", new { verb, row.Id, row.Source, job.State, job.Detail });
            }
        }
        finally
        {
            foreach (var (_, job) in jobs.Where(j => j.Job.State is "NICHT GESTARTET" or "LAEUFT"))
            {
                var started = job.State == "LAEUFT";
                job.State = started ? "UNVOLLSTAENDIG" : "NICHT AUSGEFUEHRT";
                job.Detail = started ? "Ausfuehrung ohne bestaetigtes Ende; Teilaktionen koennen bestehen bleiben."
                    : "Warteschlange vor Ausfuehrung gestoppt; keine automatische Wiederholung.";
            }
            PackagesInfo.Text = "Paketlauf beendet/gestoppt. Liste ist jetzt eine alte Momentaufnahme; vor weiteren Aktionen neu laden. Einzelstatus unter Auftraege.";
            _packageKind = "stale";
        }
        var summary = $"{completed} ohne gemeldeten Fehler / {skipped} ohne passendes Upgrade / {failed} fehlgeschlagen. " +
            "Kein Funktionsnachweis und kein automatischer Neustart. Liste neu laden.";
        if (failed > 0) throw new InvalidOperationException(summary + " Einzelbefunde unter Auftraege.");
        return summary;
    }
}
