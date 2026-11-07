using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text;
using SystemCockpit.Services;

namespace SystemCockpit.Controls;

public partial class HardwareBoardView
{
    internal static string BinarySignature => string.Join(" ", Encoding.UTF8.GetBytes("AETHER / Olaf Weins / ")
        .Select(value => Convert.ToString(value, 2).PadLeft(8, '0'))) + " ";
    private static readonly double BinaryPeriod = new FormattedText(BinarySignature,
        System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface("Consolas"), 14, Brushes.White, 1).WidthIncludingTrailingWhitespace;
    // Board coordinates are projected, not measured physical component positions.
    private double _boardOffsetX;
    private double _boardScale = 1;
    private double _boardOriginX;
    private Point Project(double u, double v, double z = 0) =>
        new(_boardOriginX + _boardScale * (.72 * u - .72 * v),
            285 + _boardScale * (.30 * u + .38 * v - z));

    internal static Rect DriveSlot(int index) => new(580 + index % 4 * 94, 40 + index / 4 * 118, 80, 100);
    internal Rect BoardSurface { get; private set; }

    private static SolidColorBrush Ink(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void DrawBoard(BoardSnapshot snapshot)
    {
        var storage = Find("storage");
        DriveCount = storage.Devices.Count(IsDrive);
        var depth = Math.Max(560, 65 + Math.Ceiling(DriveCount / 4.0) * 118);
        BoardSurface = new(0, 0, DriveCount > 0 ? 960 : 620, depth);
        _boardOffsetX = Math.Max(0, depth - 560) * .72;
        BoardCanvas.Width = 1600 + _boardOffsetX;
        // Reserve a gutter between the complete projected PCB and both legend columns.
        _boardScale = Math.Min(1, (BoardCanvas.Width - 570) / (.72 * (BoardSurface.Width + depth)));
        _boardOriginX = 285 + .72 * depth * _boardScale;
        var boardBottom = Project(BoardSurface.Width, depth).Y;
        const double calloutHeight = 155;
        var calloutStep = calloutHeight + 16;
        var groundTop = Math.Max(240 + calloutStep * 3 + calloutHeight + 200, boardBottom + 280);
        var peripheralTop = groundTop + 300;
        var peripherals = Groups.SelectMany(group => group.Devices.Select(device => (group, device)))
            .Where(item => HardwareTopology.IsIllustratedPeripheral(item.device))
            .OrderByDescending(item => item.device.InUse == true)
            .ThenByDescending(item => item.device.Preferred)
            .ThenBy(item => item.group.Key, StringComparer.Ordinal)
            .ThenBy(item => item.device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        BoardCanvas.Height = _screensaverLayout ? groundTop + 285 :
            peripheralTop + Math.Ceiling(peripherals.Length / 5.0) * 210 + 40;
        if (_screensaverLayout && _viewportAspect > 0)
            BoardCanvas.Height = Math.Max(BoardCanvas.Height, BoardCanvas.Width / _viewportAspect);
        PrepareSky(groundTop);
        Text("AETHER / VIRTUAL HARDWARE", 32, 24, "#24FFE3", 23, 950);
        Text(snapshot.Board, 32, 57, "#AFBED5", 14, 1050);
        Text("ISOMETRIC SERVICE DRAWING  /  REV. 1986", 32, groundTop + 260, "#73658F", 13, 800);
        Box(0, 0, BoardSurface.Width, depth, -8, 8, "#231049", "#23EFFF");
        Face([Project(8, 8), Project(BoardSurface.Width - 8, 8), Project(BoardSurface.Width - 8, depth - 8),
            Project(8, depth - 8)], "#111332", "#7960D9");
        Route([Project(0, 0), Project(BoardSurface.Width, 0), Project(BoardSurface.Width, depth),
            Project(0, depth), Project(0, 0)], "#23EFFF", false, "board-neon-rim");
        for (var lane = 0; lane < 14; lane++)
        {
            var u = 35 + lane * 40;
            Route([Project(u, 24, 1), Project(u, 235, 1), Project(320, 235, 1),
                Project(320, 520, 1)], "#49327C", false);
        }
        foreach (var (u, v) in new[] { (20.0, 20.0), (BoardSurface.Width - 20, 20), (20, depth - 20), (BoardSurface.Width - 20, depth - 20) })
            Disc(u, v, 2, 8, "#08101E", "#94B8BC");

        var anchors = new Dictionary<string, Point>
        {
            ["cpu"] = Project(235, 190, 43), ["ram"] = Project(450, 155, 50),
            ["gpu"] = Project(290, 385, 65), ["controller"] = Project(440, 290, 24),
            ["network"] = Project(25, 85, 24), ["usb"] = Project(25, 200, 24),
            ["storage"] = Project(530, 445, 14), ["sound"] = Project(95, 445, 14),
            ["other"] = Project(240, 515, 13)
        };
        foreach (var group in Groups.Where(g => g.Devices.Count > 0 && g.Key is not ("controller" or "monitor")))
        {
            var source = group.Key switch
            {
                "cpu" => new Point(235, 190), "ram" => new Point(450, 155), "gpu" => new Point(290, 385),
                "network" => new Point(25, 85), "usb" => new Point(25, 200),
                "storage" => new Point(530, 445), "sound" => new Point(95, 445), _ => new Point(240, 515)
            };
            var target = group.Key is "ram" or "gpu" ? new Point(235, 190) : new Point(440, 290);
            Route([Project(source.X, source.Y, 3), Project(target.X, source.Y, 3),
                Project(target.X, target.Y, 3)], group.Accent, true);
        }
        DrawInternetSymbol(1210, groundTop - 235, snapshot.Internet.StartsWith("Internet laut Windows verbunden", StringComparison.Ordinal));
        var cpu = Find("cpu"); var ram = Find("ram"); var gpu = Find("gpu");
        if (cpu.Devices.Count > 0)
        {
            Box(140, 105, 175, 160, 1, 10, "#263646", cpu.Accent, cpu);
            Box(163, 128, 130, 116, 11, 12, "#C0C9CA", cpu.Accent, cpu);
            for (var fin = 0; fin < 10; fin++)
                Box(164 + fin * 12, 129, 5, 113, 23, 15, "#3D6470", "#9FCED1", cpu);
            Disc(225, 180, 41, 43, "#102533", "#A4EADE", cpu);
            Fan(225, 180, 42, 34, "#39FF68");
        }
        if (ram.Devices.Count > 0)
        {
            for (var bank = 0; bank < Math.Min(8, ram.Devices.Count); bank++)
            {
                var u = 370 + bank * 24;
                Box(u, 42, 15, 205, 2, 6, "#18232C", "#668E96", ram);
                Box(u + 3, 45, 7, 195, 8, 44, "#264F48", ram.Accent, ram);
                for (var chip = 0; chip < 7; chip++)
                    Box(u + 2, 56 + chip * 25, 11, 17, 47, 6, "#172A31", "#B9CF75", ram);
            }
        }
        var controller = Find("controller");
        if (controller.Devices.Count > 0)
        {
            Box(385, 260, 100, 92, 1, 12, "#375662", controller.Accent, controller);
            for (var fin = 0; fin < 8; fin++)
                Box(390 + fin * 11, 265, 4, 82, 13, 11, "#446C77", "#8FD5D5", controller);
        }
        // Empty sockets are board furniture, never an assertion of installed hardware.
        for (var slot = 0; slot < 3; slot++)
            Box(128, 335 + slot * 58, 350, 12, 1, 7, "#18252F", "#657B8D");
        if (gpu.Devices.Count > 0)
        {
            Box(130, 330, 350, 68, 11, 62, "#20243D", gpu.Accent, gpu);
            for (var fan = 0; fan < 3; fan++)
            {
                Disc(195 + fan * 105, 363, 74, 28, "#101A28", "#C7B4DA", gpu);
                Fan(195 + fan * 105, 363, 75, 23, gpu.Accent);
            }
            Box(119, 328, 12, 74, 5, 74, "#68818C", "#C4D7DC", gpu);
        }
        DrawPorts(Find("network"), 22, 38, 42, 62, 30);
        DrawPorts(Find("usb"), 22, 135, 42, 98, 26);
        var sound = Find("sound");
        if (sound.Devices.Count > 0)
        {
            Box(70, 422, 55, 55, 1, 10, "#20283D", sound.Accent, sound);
            for (var jack = 0; jack < 3; jack++)
            {
                Box(16, 320 + jack * 30, 30, 23, 1, 22, "#334553", sound.Accent, sound);
                Disc(27, 331 + jack * 30, 24, 6, "#10121C", sound.Accent, sound);
            }
        }
        if (storage.Devices.Count > 0)
        {
            if (storage.Devices.Any(d => d.Name.Contains("SATA", StringComparison.OrdinalIgnoreCase) ||
                d.Name.Contains("AHCI", StringComparison.OrdinalIgnoreCase)))
                for (var port = 0; port < 3; port++)
                    Box(490 + port * 28, 500, 24, 28, 1, 23, "#354050", storage.Accent, storage);
        }
        DrawIndividualDrives(storage);
        var other = Find("other");
        if (other.Devices.Count > 0)
            for (var header = 0; header < 4; header++)
            {
                Box(185 + header * 40, 510, 28, 18, 1, 6, "#24363E", other.Accent, other);
                for (var pin = 0; pin < 3; pin++)
                    Box(190 + header * 40 + pin * 7, 515, 2, 2, 7, 9, "#C6B788", "#E1D2A9", other);
            }
        for (var capacitor = 0; capacitor < 12; capacitor++)
        {
            var u = 80 + capacitor * 35;
            Disc(u, 285, 2, 7, "#426B70", "#9AD2D0");
            Disc(u, 285, 16, 7, "#AFC7CB", "#D7EBEA");
            Segment(Project(u - 4, 285, 16), Project(u + 4, 285, 16), "#436B70", 1);
        }
        // Callouts stay unprojected so type names and status remain readable.
        Callout(cpu, anchors["cpu"], 320, 105);
        Callout(ram, anchors["ram"], 840, 105);
        Callout(Find("network"), anchors["network"], 24, 240);
        Callout(Find("usb"), anchors["usb"], 24, 240 + calloutStep);
        Callout(sound, anchors["sound"], 24, 240 + calloutStep * 2);
        Callout(controller, anchors["controller"], BoardCanvas.Width - 255, 250);
        Callout(storage, Project(580, 40, 14), BoardCanvas.Width - 255, 250 + calloutStep);
        Callout(Find("monitor"), anchors["gpu"], BoardCanvas.Width - 255, 250 + calloutStep * 2);
        Callout(gpu, anchors["gpu"], 1100, 105);
        Callout(other, anchors["other"], 24, 240 + calloutStep * 3);
        Text(snapshot.Internet, 1345, groundTop - 245, "#B4F7FF", 12, 245);
        Text("SCHEMATISCHER DATENFLUSS >>>\nKeine gemessenen Verbindungen/RPM.\nAktiv/Standard: Momentaufnahme.", 850, groundTop - 235, "#D5C9ED", 11, 320);
        if (!_screensaverLayout)
            for (var index = 0; index < peripherals.Length; index++)
                DrawPeripheral(peripherals[index].group, peripherals[index].device,
                    24 + index % 5 * 316, peripheralTop + index / 5 * 210, index);
        if (_screensaverLayout && _viewportAspect > 0)
        {
            var width = Math.Max(BoardCanvas.Width, BoardCanvas.Height * _viewportAspect);
            var offset = (width - BoardCanvas.Width) / 2;
            foreach (UIElement child in BoardCanvas.Children)
            {
                var left = Canvas.GetLeft(child);
                Canvas.SetLeft(child, (double.IsFinite(left) ? left : 0) + offset);
            }
            BoardCanvas.Width = width;
        }
        DrawSynthwaveLandscape(groundTop);
    }

    private static bool IsSolidState(BoardDevice device) =>
        device.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
        device.Name.Contains("SSD", StringComparison.OrdinalIgnoreCase);

    private static bool IsDrive(BoardDevice device) =>
        device.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase) ||
        device.Class.Equals("CDROM", StringComparison.OrdinalIgnoreCase);

    private void DrawIndividualDrives(BoardGroup storage)
    {
        var drives = storage.Devices.Where(IsDrive).ToArray();
        for (var index = 0; index < drives.Length; index++)
        {
            var device = drives[index];
            var group = storage with { Devices = new[] { device } };
            var slot = DriveSlot(index);
            var u = slot.X; var v = slot.Y;
            if (Find("controller").Devices.Count > 0)
                Route([Project(u, v + 80, 1), Project(565, v + 80, 1), Project(565, 300, 1),
                    Project(440, 300, 1)], storage.Accent, true);
            Box(u, v, slot.Width, slot.Height, 2, 9, "#234349", storage.Accent, group);
            if (IsSolidState(device))
                for (var chip = 0; chip < 3; chip++)
                    Box(u + 6 + chip * 24, v + 7, 17, 18, 11, 4, "#122034", "#9FC7D2", group);
            else
            {
                Disc(u + 40, v + 40, 12, 28, "#516075", "#9FC7D2", group);
                Disc(u + 40, v + 40, 13, 7, "#122034", storage.Accent, group);
            }
            var corner = Project(u, v + 28, 14);
            var label = new Button
            {
                Tag = "drive:" + device.Id, Width = 80, Height = 70,
                Style = (Style)FindResource("BoardLabel"), BorderBrush = Ink(storage.Accent),
                ToolTip = device.Name + "\n" + device.Id + "\n" + WindowsStatus(group),
                RenderTransform = new MatrixTransform(.72 * _boardScale, .30 * _boardScale,
                    -.72 * _boardScale, .38 * _boardScale, 0, 0),
                Content = new TextBlock { Text = $"{index + 1:00} / {device.Name}\n{WindowsStatus(group)}",
                    TextWrapping = TextWrapping.Wrap, Foreground = Ink("#DEE6EC"), FontSize = 11, MaxHeight = 68 }
            };
            label.Click += (_, _) => Select(group);
            Place(label, corner.X, corner.Y);
            Panel.SetZIndex(label, 5);
        }
    }

    private void DrawPeripheral(BoardGroup family, BoardDevice device, double x, double y, int index)
    {
        var group = family with { Devices = new[] { device } };
        var color = device.InUse == true ? "#FFCE54" : device.Preferred ? "#39FF68" : family.Accent;
        var source = family.Key == "monitor" ? Project(460, 365, 60) :
            family.Key == "sound" ? Project(95, 445, 14) : Project(25, 200, 24);
        var connector = new Polyline { Points = new PointCollection
            { source, new(source.X, y - 18), new(x + 100, y - 18), new(x + 100, y) },
            Stroke = Ink(family.Accent), StrokeThickness = 1, Opacity = .22, IsHitTestVisible = false };
        BoardCanvas.Children.Add(connector);
        Panel.SetZIndex(connector, -1);
        if (family.Key == "monitor")
        {
            Face([new(x + 30, y), new(x + 150, y), new(x + 150, y + 66), new(x + 30, y + 66)],
                "#152539", color, group);
            Face([new(x + 37, y + 7), new(x + 143, y + 7), new(x + 143, y + 57), new(x + 37, y + 57)],
                "#092B33", color, group);
            Segment(new(x + 90, y + 66), new(x + 90, y + 77), color, 3);
            Segment(new(x + 65, y + 77), new(x + 115, y + 77), color, 2);
        }
        else if (family.Key == "sound")
        {
            Face([new(x + 35, y), new(x + 125, y + 9), new(x + 125, y + 65), new(x + 35, y + 56)],
                "#1A2339", color, group);
            Place(new Ellipse { Width = 32, Height = 32, Stroke = Ink(color), Fill = Ink("#0C1726"),
                IsHitTestVisible = false }, x + 64, y + 20);
            Text("AUDIO", x + 150, y + 25, color, 12, 150);
        }
        else if (device.Class.Equals("Keyboard", StringComparison.OrdinalIgnoreCase))
        {
            Face([new(x + 25, y + 20), new(x + 175, y + 20), new(x + 155, y + 65), new(x + 5, y + 65)],
                "#17253A", color, group);
            for (var row = 0; row < 3; row++)
                for (var key = 0; key < 10; key++)
                    Segment(new(x + 28 + key * 13 - row * 5, y + 29 + row * 11),
                        new(x + 36 + key * 13 - row * 5, y + 29 + row * 11), color, 3);
        }
        else
        {
            Face([new(x + 45, y + 5), new(x + 120, y + 5), new(x + 133, y + 60), new(x + 32, y + 60)],
                "#17253A", color, group);
            Text(device.Class.ToUpperInvariant(), x + 150, y + 25, color, 12, 145);
        }
        var label = new Button { Width = 294, Height = 122, Tag = "device:" + device.Id,
            Style = (Style)FindResource("BoardLabel"), BorderBrush = Ink(color),
            ToolTip = device.Name + "\n" + device.Id + "\n" + device.Usage + "\n" + WindowsStatus(group),
            Content = new TextBlock { Text = $"{index + 1:00} / {device.Name}\n{device.Usage}\n{WindowsStatus(group)}",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Ink("#DEE6EC") } };
        label.Click += (_, _) => Select(group);
        Place(label, x, y + 80);
    }

    private BoardGroup Find(string key) => Groups.Single(g => g.Key == key);

    private void DrawInternetSymbol(double x, double y, bool connected)
    {
        Place(new Ellipse { Width = 54, Height = 54, Stroke = Ink("#24FFE3"), StrokeThickness = 1.5 }, x, y);
        Place(new Ellipse { Width = 22, Height = 54, Stroke = Ink("#24FFE3") }, x + 16, y);
        Place(new Ellipse { Width = 54, Height = 18, Stroke = Ink("#24FFE3") }, x, y + 18);
        Segment(new(x, y + 27), new(x + 54, y + 27), "#24FFE3", 1);
        if (connected && Find("network").Devices.Count > 0)
            Route([Project(25, 85, 30), new(x - 20, y - 25), new(x + 27, y)], "#24FFE3", true);
        Text("WAN / INTERNET\nWindows-Meldestatus", x - 76, y - 40, "#24FFE3", 11, 170);
    }

    internal static string WindowsStatus(BoardGroup group)
    {
        if (group.Devices.Count == 0) return "NICHT GEMELDET";
        var states = group.Devices.Select(d => string.IsNullOrWhiteSpace(d.Status) ? "Unknown" : d.Status)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return "Windows: " + string.Join(" / ", states);
    }

    private void Callout(BoardGroup group, Point anchor, double x, double y)
    {
        var color = group.Devices.Count > 0 ? group.Accent : "#667185";
        const double width = 235;
        const double height = 155;
        var bounds = new Rect(x, y, width, height);
        var end = anchor.Y > bounds.Bottom ? new Point(Math.Clamp(anchor.X, x, bounds.Right), bounds.Bottom) :
            anchor.Y < y ? new Point(Math.Clamp(anchor.X, x, bounds.Right), y) :
            new Point(anchor.X < x ? x : bounds.Right, Math.Clamp(anchor.Y, y, bounds.Bottom));
        Route([anchor, new(anchor.X, end.Y), end], color, group.Devices.Count > 0, "callout-route:" + group.Key);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = group.Title, Foreground = Ink(color),
            FontFamily = new FontFamily("Consolas"), FontSize = 14, FontWeight = FontWeights.Bold });
        var textDetails = new StackPanel();
        var names = group.Devices.Select(d => d.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        textDetails.Children.Add(new TextBlock { Text = names.Length == 0 ? "Nicht vorhanden / nicht gemeldet" :
            string.Join(" / ", group.Devices.Select(d => d.Name +
                (d.Preferred || d.InUse == true ? " [" + d.Usage + "]" : ""))),
            Foreground = Ink("#DEE6EC"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.WordEllipsis, LineHeight = 15,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 45, Tag = "component-name:" + group.Key });
        textDetails.Children.Add(new TextBlock { Text = WindowsStatus(group), Foreground = Ink("#FFCE54"),
            FontFamily = new FontFamily("Consolas"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        textDetails.Children.Add(new TextBlock { Text = $"{group.Devices.Count} Windows-Eintraege / Details",
            Foreground = Ink("#8D9AAE"), FontSize = 11 });
        {
            var readout = new Grid { Height = 110, Margin = new Thickness(0, 3, 0, 0) };
            var live = new TextBlock { Foreground = Ink("#39FF68"), FontFamily = new FontFamily("Consolas"),
                FontSize = 13, TextWrapping = TextWrapping.Wrap, Tag = "component-live:" + group.Key };
            _componentReadings[group.Key] = live;
            textDetails.Children.Add(live);
            readout.Children.Add(textDetails);
            var dial = new Grid { Visibility = Visibility.Collapsed, Tag = "component-dial:" + group.Key };
            dial.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(87) });
            dial.ColumnDefinitions.Add(new ColumnDefinition());
            var gauge = new NeonGauge { Width = 87, Height = 87, Accent = Ink(color) };
            dial.Children.Add(gauge);
            var name = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ink("#DEE6EC"),
                MaxHeight = 48, TextTrimming = TextTrimming.WordEllipsis };
            var value = new TextBlock { FontSize = 16, Foreground = Ink("#39FF68"), FontFamily = new FontFamily("Consolas") };
            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            detail.Children.Add(name); detail.Children.Add(value);
            Grid.SetColumn(detail, 1); dial.Children.Add(detail);
            readout.Children.Add(dial);
            _componentDials[group.Key] = (textDetails, dial, gauge, name, value);
            content.Children.Add(readout);
        }
        var button = new Button { Content = content, Width = width, Height = height, Tag = group.Key,
            Background = LabelBackground(y),
            ClipToBounds = true, VerticalContentAlignment = VerticalAlignment.Top,
            Style = (Style)FindResource("BoardLabel"), BorderBrush = Ink(color),
            ToolTip = string.Join("\n", names) + "\n" + WindowsStatus(group) };
        button.Click += (_, _) => Select(group);
        Place(button, x, y);
        Panel.SetZIndex(button, 10);
    }

    private void DrawPorts(BoardGroup group, double u, double v, double w, double d, double h)
    {
        if (group.Devices.Count == 0) return;
        Box(u, v, w, d, 1, h, "#567F8C", group.Accent, group);
        for (var port = 0; port < 2; port++)
            Box(u - 3, v + 10 + port * d / 2, 4, d / 2 - 12, 6, h - 10, "#071421", group.Accent, group);
    }

    private void Fan(double u, double v, double z, double radius, string color)
    {
        var center = Project(u, v, z);
        var rotation = new RotateTransform();
        _fans.Add(rotation);
        var plane = new Canvas { RenderTransform = new MatrixTransform(.72 * _boardScale, .30 * _boardScale,
            -.72 * _boardScale, .38 * _boardScale, center.X, center.Y),
            IsHitTestVisible = false };
        var rotor = new Canvas { RenderTransform = rotation };
        for (var blade = 0; blade < 7; blade++)
        {
            var angle = blade * Math.PI * 2 / 7;
            rotor.Children.Add(new Polygon { Points = new PointCollection
                { new(5 * Math.Cos(angle), 5 * Math.Sin(angle)),
                  new(radius * Math.Cos(angle + .3), radius * Math.Sin(angle + .3)),
                  new(radius * Math.Cos(angle + .7), radius * Math.Sin(angle + .7)) },
                Fill = Ink("#395866"), Stroke = Ink(color), StrokeThickness = 1.2 });
        }
        plane.Children.Add(rotor);
        BoardCanvas.Children.Add(plane);
        Disc(u, v, z + 1, 7, "#B8D8DE", color);
    }

    private void Disc(double u, double v, double z, double radius, string fill, string stroke, BoardGroup? group = null) =>
        Face(Enumerable.Range(0, 25).Select(i => Project(u + radius * Math.Cos(i * Math.PI / 12),
            v + radius * Math.Sin(i * Math.PI / 12), z)).ToArray(), fill, stroke, group);

    private void Box(double u, double v, double w, double d, double z, double h, string fill, string stroke, BoardGroup? group = null)
    {
        Face([Project(u, v + d, z), Project(u + w, v + d, z), Project(u + w, v + d, z + h),
            Project(u, v + d, z + h)], "#0C1726", stroke, group);
        Face([Project(u + w, v, z), Project(u + w, v + d, z), Project(u + w, v + d, z + h),
            Project(u + w, v, z + h)], "#172538", stroke, group);
        Face([Project(u, v, z + h), Project(u + w, v, z + h), Project(u + w, v + d, z + h),
            Project(u, v + d, z + h)], fill, stroke, group);
    }

    private void Face(Point[] points, string fill, string stroke, BoardGroup? group = null)
    {
        var face = new Polygon { Points = new PointCollection(points), Fill = Ink(fill), Stroke = Ink(stroke),
            StrokeThickness = 1.2, SnapsToDevicePixels = false, Tag = group?.Key };
        if (group is not null)
        {
            face.Cursor = Cursors.Hand;
            face.ToolTip = group.Title + "\n" + WindowsStatus(group) + "\n" +
                string.Join("\n", group.Devices.Select(d => d.Name).Distinct());
            face.MouseLeftButtonUp += (_, e) => { Select(group); e.Handled = true; };
        }
        BoardCanvas.Children.Add(face);
    }

    private void Route(Point[] points, string color, bool animate, string? tag = null)
    {
        var line = new Polyline { Points = new PointCollection(points), Stroke = Ink(color),
            StrokeThickness = animate ? 2 : 1, Opacity = animate ? .8 : .45, IsHitTestVisible = false, Tag = tag };
        if (animate || tag == "board-neon-rim")
        {
            line.Points.Freeze();
            foreach (var (spread, opacity) in new[] { (8.0, .08), (4.0, .18) })
            {
                var halo = new Polyline { Points = line.Points, Stroke = line.Stroke,
                    StrokeThickness = line.StrokeThickness + spread, Opacity = opacity,
                    IsHitTestVisible = false, Tag = "neon-halo:" + tag };
                if (animate)
                {
                    halo.StrokeDashArray = new DoubleCollection { 10 / halo.StrokeThickness, 10 / halo.StrokeThickness };
                    _flows.Add(halo);
                }
                BoardCanvas.Children.Add(halo);
            }
            line.Opacity = .95;
        }
        if (animate)
        {
            line.StrokeDashArray = new DoubleCollection { 5, 5 };
            _flows.Add(line);
        }
        if (tag?.StartsWith("callout-route:", StringComparison.Ordinal) == true)
            _routeColors.Add(((SolidColorBrush)line.Stroke, Ink(color == "#F56AFF" ? "#24FFE3" : "#F56AFF").Color));
        BoardCanvas.Children.Add(line);
    }

    private void Segment(Point a, Point b, string color, double width) =>
        BoardCanvas.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
            Stroke = Ink(color), StrokeThickness = width, IsHitTestVisible = false });

    private void Text(string text, double x, double y, string color, int size, double width) =>
        Place(new TextBlock { Text = text, Foreground = Ink(color), FontFamily = new FontFamily("Consolas"),
            FontSize = size, Width = width, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false }, x, y);

    private void Place(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x); Canvas.SetTop(element, y); BoardCanvas.Children.Add(element);
    }
}
