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
            Title = DesktopUiText.Get("Package.ExternalSelect") };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, DesktopUiText.Format("Package.ExternalConsent", dialog.FileName),
                DesktopUiText.Get("Package.ExternalTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            using var process = Process.Start(PackageCenter.ExternalManager(dialog.FileName))
                ?? throw new InvalidOperationException(DesktopUiText.Get("Package.ExternalNoProcess"));
            PackagesInfo.Text = DesktopUiText.Get("Package.ExternalStarted");
            _packageKind = "stale";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or InvalidOperationException)
        { PackagesInfo.Text = DesktopUiText.Format("Package.ExternalFailed", ex.Message); }
    }

    private async void PackageDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (PackagesGrid.SelectedItems.Count != 1 || PackagesGrid.SelectedItem is not WingetRow row)
        { PackagesInfo.Text = DesktopUiText.Get("Package.SelectOne"); return; }
        string script;
        try { script = PackageCenter.TargetCommandScript("show", row); }
        catch (ArgumentException ex) { PackagesInfo.Text = ex.Message; return; }
        await RunAsync(DesktopUiText.Format("Package.DetailsTitle", row.Id), async token =>
        {
            var result = await AdminShell.RunAsync(script, TimeSpan.FromMinutes(3), token, ProgressOutput(raw: true));
            if (result.ExitCode != 0) throw new InvalidOperationException(DesktopUiText.Format("Package.DetailsExit", result.ExitCode));
            return DesktopUiText.Get("Package.DetailsSource");
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
                    DesktopUiText.Format("Package.ExportConsent", eligible.Count, excluded),
                    DesktopUiText.Get("Package.ExportTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var dialog = new SaveFileDialog { Filter = DesktopUiText.Get("Package.BundleFilter"),
                FileName = "Aether-Software.aether-packages.json" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
            PackagesInfo.Text = DesktopUiText.Format("Package.Exported", eligible.Count, excluded);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { PackagesInfo.Text = DesktopUiText.Format("Package.ExportFailed", ex.Message); }
    }

    private void PackageImportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = DesktopUiText.Get("Package.BundleFilter") + "|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bundle = PackageCenter.ReadBundle(dialog.FileName);
            _packageRows = bundle.Packages.Select(p => new WingetRow(p.Name, p.Id, p.Version, "", p.Source)).ToList();
            _packageKind = "import";
            PackagesGrid.ItemsSource = _packageRows;
            PackagesInfo.Text = DesktopUiText.Format("Package.Imported", _packageRows.Count);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or DecoderFallbackException)
        { PackagesInfo.Text = DesktopUiText.Format("Package.ImportFailed", ex.Message); }
    }

    internal async Task<string> RunPackageQueueAsync(string verb, IReadOnlyList<WingetRow> rows, CancellationToken token,
        Func<WingetRow, CancellationToken, Task<CommandResult>> execute, Action<string, object> journal)
    {
        if (rows.Count is < 1 or > PackageCenter.MaxIds ||
            rows.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
            throw new ArgumentException(DesktopUiText.Get("Package.QueueIds"));
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
                AppendTerminal(DesktopUiText.Format("Package.QueueHeading", verb, row.Id,
                    row.Source.Length == 0 ? DesktopUiText.Get("Package.SourceUnknown") : row.Source));
                try
                {
                    var result = await execute(row, token);
                    if (result.ExitCode == 0)
                    { job.State = "ABGESCHLOSSEN"; completed++; }
                    else if (verb == "upgrade" && result.ExitCode == -1978335189)
                    { job.State = "KEIN UPGRADE"; skipped++; }
                    else { job.State = "FEHLGESCHLAGEN"; failed++; }
                    job.Detail = DesktopUiText.Format("Package.QueueExit", result.ExitCode);
                }
                catch (OperationCanceledException)
                {
                    job.State = "ABGEBROCHEN";
                    job.Detail = DesktopUiText.Get("Package.QueuePartialChanges");
                    journal("package-result", new { verb, row.Id, row.Source, job.State, job.Detail });
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or TimeoutException)
                {
                    job.State = "FEHLGESCHLAGEN"; job.Detail = ex.Message; failed++;
                    AppendTerminal(DesktopUiText.Format("Package.QueueError", row.Id, ex.Message));
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
                job.Detail = DesktopUiText.Get(started ? "Package.QueueIncomplete" : "Package.QueueNotStarted");
            }
            PackagesInfo.Text = DesktopUiText.Get("Package.QueueStale");
            _packageKind = "stale";
        }
        var summary = DesktopUiText.Format("Package.QueueSummary", completed, skipped, failed);
        if (failed > 0) throw new InvalidOperationException(summary + DesktopUiText.Get("Package.QueueFindings"));
        return summary;
    }
}
