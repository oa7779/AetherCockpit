using System.Windows;
using SystemCockpit.Services;

namespace SystemCockpit;

/// <summary>
/// Erststart-Assistent: Funktionsuebersicht, Pflicht-Zustimmung zur EULA und
/// lokale Rechner-Inventarisierung. DialogResult true = akzeptiert und fertig,
/// false = abgelehnt (Programm wird beendet).
/// </summary>
public partial class FirstRunWindow : Window
{
    private int _step;
    private readonly CancellationTokenSource _stop = new();

    public FirstRunWindow()
    {
        InitializeComponent();
        EulaText.Text = EulaConsent.Text;
        Inventory.LifetimeToken = _stop.Token;
        Inventory.StateChanged += (_, _) => UpdateInventoryGate();
        Closed += (_, _) => _stop.Cancel();
    }

    private void ShowStep(int step)
    {
        _step = step;
        StepIntro.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        StepEula.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepInventory.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = step switch
        {
            0 => "Schritt 1 von 3 - Willkommen",
            1 => "Schritt 2 von 3 - Lizenzvereinbarung",
            _ => "Schritt 3 von 3 - Rechner-Inventar"
        };
        BackButton.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
        DeclineButton.Visibility = step == 2 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = step switch { 0 => "Weiter zur Lizenz", 1 => "Akzeptieren und weiter", _ => "Cockpit starten" };
        if (step == 1) NextButton.IsEnabled = AcceptCheck.IsChecked == true;
        else NextButton.IsEnabled = step != 2 || Inventory.HasCompleted && !Inventory.IsBusy;
        UpdateInventoryGate();
    }

    private void UpdateInventoryGate()
    {
        BackButton.IsEnabled = !Inventory.IsBusy;
        if (_step == 2)
        {
            NextButton.IsEnabled = Inventory.HasCompleted && !Inventory.IsBusy;
            NextButton.Content = Inventory.IsBusy ? "Rechner wird untersucht ..." : "Cockpit starten";
        }
    }

    private void AcceptChanged(object sender, RoutedEventArgs e)
    {
        if (_step == 1) NextButton.IsEnabled = AcceptCheck.IsChecked == true;
    }

    private async void NextClicked(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 0:
                ShowStep(1);
                break;
            case 1:
                if (AcceptCheck.IsChecked != true) return;
                ShowStep(2);
                await Inventory.LoadAsync(_stop.Token);
                break;
            default:
                if (!Inventory.HasCompleted || Inventory.IsBusy) return;
                DialogResult = true;
                break;
        }
    }

    private void BackClicked(object sender, RoutedEventArgs e)
    {
        if (_step > 0 && !Inventory.IsBusy) ShowStep(_step - 1);
    }

    private void DeclineClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
