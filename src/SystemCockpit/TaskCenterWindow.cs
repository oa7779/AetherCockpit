using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SystemCockpit.Services;

namespace SystemCockpit;

public sealed class TaskCenterWindow : Window
{
    public CockpitTask? SelectedTask { get; private set; }
    internal TextBox SearchBox { get; } = new() { Margin = new Thickness(0, 8, 0, 8) };
    internal ComboBox CategoryBox { get; } = new() { Margin = new Thickness(0, 0, 0, 8) };
    internal ListBox Results { get; } = new();
    private readonly TextBlock _count = new(), _title = new(), _effect = new(), _steps = new();
    private readonly Button _open = new() { Content = DesktopUiText.Get("TaskCenter.Open"), Margin = new Thickness(0, 16, 0, 0) };

    public TaskCenterWindow()
    {
        Title = DesktopUiText.Get("TaskCenter.Title");
        Width = 1040; Height = 720; MinWidth = 760; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackdrop");
        Foreground = Brushes.White;
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Content = grid;
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = DesktopUiText.Get("TaskCenter.Heading"),
            Foreground = Brushes.Turquoise, FontSize = 21, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = DesktopUiText.Get("TaskCenter.Notice"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        SearchBox.Style = (Style)FindResource("NeonTextBox");
        System.Windows.Automation.AutomationProperties.SetName(SearchBox, DesktopUiText.Get("TaskCenter.Search"));
        header.Children.Add(SearchBox);
        CategoryBox.ItemsSource = new[] { DesktopUiText.Get("TaskCenter.AllCategories") }.Concat(TaskCatalog.All.Select(t => t.Category).Distinct()).ToArray();
        CategoryBox.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetName(CategoryBox, DesktopUiText.Get("TaskCenter.Category"));
        header.Children.Add(CategoryBox); header.Children.Add(_count);
        grid.Children.Add(header);
        var body = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1.3, GridUnitType.Star) });
        Grid.SetRow(body, 1); grid.Children.Add(body);
        Results.Background = new SolidColorBrush(Color.FromRgb(21, 16, 45));
        Results.Foreground = Brushes.Turquoise;
        Results.BorderBrush = Brushes.MediumPurple;
        Results.FontSize = 14;
        var resultText = new FrameworkElementFactory(typeof(TextBlock));
        resultText.SetBinding(TextBlock.TextProperty, new Binding(nameof(CockpitTask.Title)));
        resultText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        Results.ItemTemplate = new DataTemplate { VisualTree = resultText };
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(49, 35, 75))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Turquoise));
        itemStyle.Triggers.Add(selected);
        Results.ItemContainerStyle = itemStyle;
        Results.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        System.Windows.Automation.AutomationProperties.SetName(Results, DesktopUiText.Get("TaskCenter.Results"));
        body.Children.Add(Results);
        var details = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        foreach (var text in new[] { _title, _effect, _steps })
        { text.TextWrapping = TextWrapping.Wrap; text.Margin = new Thickness(0, 0, 0, 14); details.Children.Add(text); }
        _title.FontSize = 23; _title.Foreground = Brushes.Orchid;
        _effect.Foreground = Brushes.Gold; _steps.FontSize = 15; _steps.LineHeight = 25;
        _open.Style = (Style)FindResource("NeonButton");
        _open.Click += (_, _) => OpenSelection();
        details.Children.Add(_open);
        var scroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        SearchBox.TextChanged += (_, _) => Refresh();
        CategoryBox.SelectionChanged += (_, _) => Refresh();
        Results.SelectionChanged += (_, _) => ShowSelection();
        Results.MouseDoubleClick += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) is ListBoxItem) OpenSelection();
        };
        Results.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OpenSelection(); e.Handled = true; } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Loaded += (_, _) => SearchBox.Focus();
        Refresh();
    }

    private void Refresh()
    {
        var tasks = TaskCatalog.Search(SearchBox.Text, CategoryBox.SelectedIndex > 0 ? CategoryBox.SelectedItem as string : null);
        Results.ItemsSource = tasks;
        Results.SelectedIndex = tasks.Count > 0 ? 0 : -1;
        _count.Text = DesktopUiText.Format("TaskCenter.Count", tasks.Count);
        ShowSelection();
    }

    private void ShowSelection()
    {
        var task = Results.SelectedItem as CockpitTask;
        _title.Text = task?.Title ?? DesktopUiText.Get("TaskCenter.NoResults");
        _effect.Text = task is null ? "" : $"{task.Category} / {task.Surface} / {task.Destination}\n{task.Effect}";
        _steps.Text = task?.Steps ?? DesktopUiText.Get("TaskCenter.NoResultsHint");
        _open.IsEnabled = task is not null;
    }

    private void OpenSelection()
    {
        if (Results.SelectedItem is not CockpitTask task) return;
        SelectedTask = task;
        DialogResult = true;
    }
}
