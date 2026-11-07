using System.Windows;

namespace SystemCockpit;

public partial class InventoryWindow : Window
{
    private readonly CancellationTokenSource _stop = new();
    public InventoryWindow()
    {
        InitializeComponent();
        Inventory.LifetimeToken = _stop.Token;
        Loaded += async (_, _) => await Inventory.LoadAsync(_stop.Token);
        Closed += (_, _) => _stop.Cancel();
    }
}
