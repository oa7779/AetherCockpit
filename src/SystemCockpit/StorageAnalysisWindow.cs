using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using SystemCockpit.Services;

namespace SystemCockpit;

internal sealed class StorageAnalysisWindow : Window
{
    private CancellationTokenSource? _stop;
    private Task _scan = Task.CompletedTask;
    internal Task ShutdownAsync() { _stop?.Cancel(); return _scan; }

    internal StorageAnalysisWindow()
    {
        Title = "AETHER | Speicherplatz-Analyse";
        Width = 1080; Height = 750; MinWidth = 850; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackdrop");
        Foreground = Brushes.White;
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Content = root;
        var header = new StackPanel();
        root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "SPEICHERPLATZ // GROSSE DATEIEN FINDEN", FontSize = 23, Foreground = Brushes.Turquoise });
        header.Children.Add(new TextBlock { Text = "Nur lesend, keine Loeschfunktion. Ordner bewusst waehlen. Maximal 100.000 Eintraege / 32 Ebenen / 1.000 groesste Treffer. " +
            "Links/Junctions werden ausgelassen; unzugaengliche Bereiche bleiben unbekannt. Das ist keine vollstaendige Datentraegerbelegung.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        var inputs = new WrapPanel();
        header.Children.Add(inputs);
        var choose = new Button { Content = "Ordner waehlen / Analyse starten", Style = (Style)FindResource("NeonButton") };
        var minimum = new TextBox { Text = "100", Width = 90, Margin = new Thickness(8, 0, 8, 0), Style = (Style)FindResource("NeonTextBox") };
        System.Windows.Automation.AutomationProperties.SetName(minimum, "Mindestgroesse in MiB");
        inputs.Children.Add(choose); inputs.Children.Add(minimum);
        inputs.Children.Add(new TextBlock { Text = "MiB Mindestgroesse", VerticalAlignment = VerticalAlignment.Center });
        var cancel = new Button { Content = "Abbrechen", Style = (Style)FindResource("NeonButton"), Margin = new Thickness(12, 0, 0, 0) };
        inputs.Children.Add(cancel); cancel.Click += (_, _) => _stop?.Cancel();
        var status = new TextBlock { Text = "Noch kein Ordner gelesen.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Turquoise, Margin = new Thickness(0, 12, 0, 12) };
        header.Children.Add(status);
        var grid = new DataGrid { Style = (Style)FindResource("NeonDataGrid"), SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridTextColumn { Header = "DATEI", Binding = new Binding(nameof(LargeFile.Path)), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "MiB", Binding = new Binding(nameof(LargeFile.MiB)) { StringFormat = "0.00" }, Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "GEAENDERT (UTC)", Binding = new Binding(nameof(LargeFile.ModifiedUtc)) { StringFormat = "yyyy-MM-dd HH:mm" }, Width = 170 });
        Grid.SetRow(grid, 1); root.Children.Add(grid);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        var actions = new WrapPanel();
        footer.Children.Add(actions);
        var show = new Button { Content = "Datei im Explorer markieren", Style = (Style)FindResource("NeonButton") };
        var export = new Button { Content = "Treffer als CSV speichern", Style = (Style)FindResource("NeonButton"), Margin = new Thickness(10, 0, 0, 0) };
        actions.Children.Add(show); actions.Children.Add(export);
        var issues = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 75,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Style = (Style)FindResource("NeonTextBox"), Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(issues);
        choose.Click += async (_, _) =>
        {
            if (_stop is not null) { status.Text = "Erst laufende Analyse beenden."; return; }
            if (!int.TryParse(minimum.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var mib) || mib is < 1 or > 1048576)
            { status.Text = "Mindestgroesse: 1..1048576 MiB."; return; }
            var dialog = new OpenFolderDialog { Title = "Eigenen Ordner fuer lesende Analyse auswaehlen" };
            if (dialog.ShowDialog(this) != true) return;
            _stop = new CancellationTokenSource();
            choose.IsEnabled = false; minimum.IsEnabled = false;
            grid.ItemsSource = null; issues.Text = "";
            status.Text = "Lesende Analyse laeuft: " + dialog.FolderName;
            _scan = ScanAsync(dialog.FolderName, (long)mib * 1048576, _stop.Token);
            await _scan;
        };
        async Task ScanAsync(string path, long bytes, CancellationToken token)
        {
            try
            {
                var result = await Task.Run(() => StorageAnalysis.Scan(path, bytes, token), token);
                grid.ItemsSource = result.Files;
                status.Text = $"{result.Inspected} Eintraege untersucht / {result.Files.Count} groesste Treffer / " +
                    $"{(result.Limited ? "LIMIT ERREICHT: unvollstaendige Auswahl" : result.IssueCount > 0
                        ? "MIT LESELUECKEN: keine vollstaendige Aussage" : "gewaehlter Bereich durchlaufen")} / {result.IssueCount} Lesehinweise.";
                issues.Text = string.Join(Environment.NewLine, result.Issues) +
                    (result.IssueCount > result.Issues.Count ? $"\nNur erste {result.Issues.Count} Hinweise angezeigt." : "");
            }
            catch (OperationCanceledException) { status.Text = "Analyse abgebrochen; keine Dateien geaendert. Keine Teilliste als vollstaendig ausgegeben."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { status.Text = "Analyse fehlgeschlagen: " + ex.Message; }
            finally { _stop?.Dispose(); _stop = null; choose.IsEnabled = true; minimum.IsEnabled = true; }
        }
        show.Click += (_, _) =>
        {
            if (grid.SelectedItem is not LargeFile file) { status.Text = "Datei auswaehlen."; return; }
            try
            {
                if (!File.Exists(file.Path)) throw new IOException("Datei ist nicht mehr vorhanden. Neu analysieren.");
                var info = new ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), UseShellExecute = false };
                info.ArgumentList.Add("/select," + file.Path);
                using var process = Process.Start(info) ?? throw new InvalidOperationException("Explorer nicht gestartet.");
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
            { status.Text = "Explorer konnte Datei nicht markieren: " + ex.Message; }
        };
        export.Click += (_, _) =>
        {
            var rows = grid.Items.OfType<LargeFile>().ToArray();
            if (rows.Length == 0) { status.Text = "Keine Treffer zum Exportieren."; return; }
            var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Aether-Grosse-Dateien.csv" };
            if (MessageBox.Show(this, "Dateipfade koennen vertrauliche Informationen enthalten. Lokal speichern und vor Weitergabe pruefen?",
                "Pfadliste exportieren", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes ||
                dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllLines(dialog.FileName, new[] { "Path,Bytes,ModifiedUtc" }.Concat(rows.Select(f => string.Join(",",
                    MainWindow.CsvCell(f.Path), f.Bytes.ToString(CultureInfo.InvariantCulture), MainWindow.CsvCell(f.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture))))), new UTF8Encoding(true));
                status.Text = $"{rows.Length} Treffer exportiert. Keine Dateien geloescht.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { status.Text = "CSV nicht gespeichert: " + ex.Message; }
        };
        Closing += (_, e) =>
        {
            if (!_scan.IsCompleted) { e.Cancel = true; _stop?.Cancel(); status.Text = "Abbruch angefordert; Analyseende abwarten, dann Fenster schliessen."; }
        };
    }
}
