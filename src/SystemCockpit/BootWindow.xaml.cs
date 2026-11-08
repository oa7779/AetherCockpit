using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SystemCockpit.Services;

namespace SystemCockpit;

public partial class BootWindow : Window
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _allowClose;
    private string _stage = "START";
    public CancellationToken Token => _stop.Token;
    public string Transcript => BootOutput.Text;

    public BootWindow()
    {
        InitializeComponent();
        if (SkinCatalog.IsModern)
        {
            PerspectiveDecoration.Visibility = Visibility.Collapsed;
            ScanlineDecoration.Visibility = Visibility.Collapsed;
            ConsoleReflection.Visibility = Visibility.Collapsed;
            Sweep.Visibility = Visibility.Collapsed;
        }
        else if (SystemParameters.ClientAreaAnimation)
            ((System.Windows.Media.TranslateTransform)Sweep.RenderTransform).BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(-120, 980, TimeSpan.FromSeconds(2.8)) { RepeatBehavior = RepeatBehavior.Forever });
        else Sweep.Visibility = Visibility.Collapsed;
        _timer.Tick += (_, _) => BootStatus.Text = $"{_stage} / {_elapsed.Elapsed:hh\\:mm\\:ss}";
        _timer.Start();
        Closing += OnClosing;
        Closed += (_, _) => { _timer.Stop(); _stop.Dispose(); };
    }

    public void Report(string message, int progress)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Report(message, progress)); return; }
        _stage = message;
        BootStatus.Text = $"{_stage} / {_elapsed.Elapsed:hh\\:mm\\:ss}";
        BootOutput.AppendText($"[{_elapsed.Elapsed:mm\\:ss\\.fff}] {message}\n");
        BootOutput.ScrollToEnd();
        BootProgress.Value = progress;
    }

    public async Task FinishAsync(bool hasWarnings)
    {
        Report(DesktopUiText.Get(hasWarnings ? "Boot.ReadyWarnings" : "Boot.Ready"), 100);
        if (hasWarnings)
        {
            ContinueButton.Visibility = Visibility.Visible;
            await _continue.Task.WaitAsync(Token);
        }
        else
        {
            var remaining = TimeSpan.FromSeconds(3.6) - _elapsed.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, Token);
        }
    }

    public void CloseAfterStartup()
    {
        _allowClose = true;
        Close();
    }

    private void ContinueClicked(object sender, RoutedEventArgs e) => _continue.TrySetResult();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        _stop.Cancel();
        _timer.Stop();
        BootStatus.Text = DesktopUiText.Get("Boot.Cancel");
    }
}
