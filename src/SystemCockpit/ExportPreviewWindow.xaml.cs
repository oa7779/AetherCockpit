using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SystemCockpit;

public partial class ExportPreviewWindow : Window
{
    private readonly string _raw;
    public string? SavedPath { get; private set; }

    public ExportPreviewWindow(string report)
    {
        InitializeComponent();
        _raw = report;
        RedactChanged(this, new RoutedEventArgs());
    }

    private void RedactChanged(object sender, RoutedEventArgs e)
    {
        if (Preview is null) return;
        Preview.Text = RedactToggle.IsChecked == true ? DiagnosticExport.Redact(_raw) : _raw;
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Textdatei (*.txt)|*.txt",
            FileName = $"AETHER-Diagnose-{DateTime.Now:yyyyMMdd-HHmmss}{(RedactToggle.IsChecked == true ? "-geschwaerzt" : "")}.txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, Preview.Text);
            SavedPath = dialog.FileName;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Bericht nicht gespeichert", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
