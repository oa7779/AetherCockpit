using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class HardwareBoardView
{
    private void DrawModernSchema(BoardSnapshot snapshot)
    {
        const double cardWidth = 330;
        const double cardHeight = 126;
        const double gapX = 22;
        const double gapY = 18;
        const double startX = 32;
        const double startY = 34;
        var columns = 3;
        var rows = (int)Math.Ceiling(Groups.Count / (double)columns);
        BoardCanvas.Width = startX * 2 + columns * cardWidth + (columns - 1) * gapX;
        BoardCanvas.Height = startY + rows * cardHeight + (rows - 1) * gapY + 28;
        BoardSurface = new(0, 0, BoardCanvas.Width, BoardCanvas.Height);
        BoardCanvas.SetResourceReference(Panel.BackgroundProperty, "SceneBackdrop");

        var primary = (Brush)FindResource("ModernTextPrimary");
        var secondary = (Brush)FindResource("ModernTextSecondary");
        var accent = (Brush)FindResource("ModernAccent");
        var border = (Brush)FindResource("ModernBorder");
        var panel = (Brush)FindResource("GlossPanel");
        var warning = (Brush)FindResource("ModernWarning");

        var hubX = BoardCanvas.Width / 2;
        var hubY = 18;
        foreach (var index in Enumerable.Range(0, Groups.Count))
        {
            var column = index % columns;
            var row = index / columns;
            var x = startX + column * (cardWidth + gapX);
            var y = startY + row * (cardHeight + gapY);
            var line = new Polyline
            {
                Points = [new Point(hubX, hubY), new Point(x + cardWidth / 2, y)],
                Stroke = accent,
                StrokeThickness = 1,
                Opacity = 0.32,
                StrokeDashArray = [2, 8],
                IsHitTestVisible = false,
                Tag = "modern-schema-flow:illustrative"
            };
            BoardCanvas.Children.Add(line);
            _flows.Add(line);
        }

        for (var index = 0; index < Groups.Count; index++)
        {
            var group = Groups[index];
            var column = index % columns;
            var row = index / columns;
            var x = startX + column * (cardWidth + gapX);
            var y = startY + row * (cardHeight + gapY);
            var card = new Border
            {
                Width = cardWidth,
                Height = cardHeight,
                Background = panel,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = "modern-schema-card:" + group.Key
            };
            card.MouseLeftButtonUp += (_, e) => { Select(group); e.Handled = true; };
            var stack = new StackPanel();
            Ellipse? activeIndicator = null;
            stack.Children.Add(new TextBlock
            {
                Text = DesktopText.Get("Board.Category." + group.Key),
                Foreground = accent,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            });
            if (group.Devices.Any(device => device.InUse == true))
            {
                activeIndicator = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = accent,
                    Opacity = 0.8,
                    ToolTip = DesktopText.Get("Board.ActiveDeviceTooltip"),
                    Tag = "modern-schema-active-device:" + group.Key
                };
                activeIndicator.IsHitTestVisible = false;
                _modernIndicators.Add(activeIndicator);
            }
            stack.Children.Add(new TextBlock
            {
                Text = string.Format(DesktopText.Get("Board.DeviceCount"), group.Devices.Count, snapshot.Devices.Length),
                Foreground = secondary,
                Margin = new Thickness(0, 2, 0, 3)
            });
            var names = group.Devices.Take(2).Select(device => device.Name).ToArray();
            var sample = names.Length == 0 ? DesktopText.Get("Board.NoDevices") :
                string.Join(" · ", names) + (group.Devices.Count > names.Length ? " · …" : "");
            stack.Children.Add(new TextBlock
            {
                Text = sample,
                Foreground = group.Devices.Any(device => device.Status.Equals("Error", StringComparison.OrdinalIgnoreCase))
                    ? warning : primary,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 45,
                Margin = new Thickness(0, 3, 0, 0)
            });
            card.Child = stack;
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            BoardCanvas.Children.Add(card);
            if (activeIndicator is not null)
            {
                Canvas.SetLeft(activeIndicator, x + cardWidth - 24);
                Canvas.SetTop(activeIndicator, y + 19);
                BoardCanvas.Children.Add(activeIndicator);
            }
        }

    }

    private void ApplyModernChrome()
    {
        if (!SkinCatalog.IsModern) return;
        BoardTitleText.Text = DesktopText.Get("Board.SystemSchemaTitle");
        BoardTitleText.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextPrimary");
        BoardTitleText.FontFamily = new FontFamily("Segoe UI");
        BoardTitleText.FontSize = 23;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextSecondary");
        BoardDescriptionText.Text = DesktopText.Get("Board.SystemSchemaDescription");
        BoardDescriptionText.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextSecondary");
        BoardDescriptionText.FontFamily = new FontFamily("Segoe UI");
        DetailTitle.FontFamily = new FontFamily("Segoe UI");
        LiveDataNotice.FontFamily = new FontFamily("Segoe UI");
        StatusText.Text = DesktopText.Get("Board.InitialStatus");
        RefreshButton.Content = DesktopText.Get("Board.Refresh");
        HeaderLegend.Visibility = Visibility.Collapsed;
        MotionToggle.Content = DesktopText.Get("Board.MotionToggle");
        BoardZoom.ToolTip = DesktopText.Get("Board.ZoomTooltip");
        ((ComboBoxItem)BoardZoom.Items[0]).Content = DesktopText.Get("Board.ZoomFit");
        ((ComboBoxItem)BoardZoom.Items[1]).Content = DesktopText.Get("Board.Zoom75");
        ((ComboBoxItem)BoardZoom.Items[2]).Content = DesktopText.Get("Board.Zoom100");
        ((ComboBoxItem)BoardZoom.Items[3]).Content = DesktopText.Get("Board.Zoom125");
        LiveDataNotice.Text = DesktopText.Get("Board.LiveMetricsNotice");
        UploadMetricLabel.Text = " Mbit/s    " + DesktopText.Get("Board.Upload") + " ";
        CpuTemperatureMetricLabel.Text = "    " + DesktopText.Get("Board.CpuTemperature") + " ";
        BoardZoom.SetResourceReference(Control.ForegroundProperty, "ModernTextPrimary");
        DetailTitle.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextPrimary");
        InternetText.SetResourceReference(TextBlock.ForegroundProperty, "ModernTextSecondary");
        DeviceList.SetResourceReference(ListBox.ForegroundProperty, "ModernTextPrimary");
    }
}
