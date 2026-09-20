using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.Vda5050.Enums;

namespace AgvDispatch.MasterControl.Controls;

/// <summary>
/// Map canvas rendering grid, planned route nodes/edges, and AGVs with heading & battery indicators.
/// Uses a 100ms DispatcherTimer for smooth redraw without per-message UI thrashing (§4, §19).
/// Allows clicking to add waypoints for the order editor.
/// </summary>
public class MapCanvas : FrameworkElement
{
    public static readonly DependencyProperty AgvsSourceProperty =
        DependencyProperty.Register(nameof(AgvsSource), typeof(ObservableCollection<AgvEntry>), typeof(MapCanvas),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty PlannedWaypointsSourceProperty =
        DependencyProperty.Register(nameof(PlannedWaypointsSource), typeof(ObservableCollection<WaypointItem>), typeof(MapCanvas),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SelectedAgvProperty =
        DependencyProperty.Register(nameof(SelectedAgv), typeof(AgvEntry), typeof(MapCanvas),
            new FrameworkPropertyMetadata(null));

    public ObservableCollection<AgvEntry>? AgvsSource
    {
        get => (ObservableCollection<AgvEntry>?)GetValue(AgvsSourceProperty);
        set => SetValue(AgvsSourceProperty, value);
    }

    public ObservableCollection<WaypointItem>? PlannedWaypointsSource
    {
        get => (ObservableCollection<WaypointItem>?)GetValue(PlannedWaypointsSourceProperty);
        set => SetValue(PlannedWaypointsSourceProperty, value);
    }

    public AgvEntry? SelectedAgv
    {
        get => (AgvEntry?)GetValue(SelectedAgvProperty);
        set => SetValue(SelectedAgvProperty, value);
    }

    public double ExtentMinX { get; set; } = -1.0;
    public double ExtentMaxX { get; set; } = 21.0;
    public double ExtentMinY { get; set; } = -1.0;
    public double ExtentMaxY { get; set; } = 21.0;

    public event Action<double, double>? MapClicked;

    private readonly DispatcherTimer _renderTimer;

    // Cached brushes and pens
    private readonly Brush _bgBrush = new SolidColorBrush(Color.FromRgb(24, 28, 36));
    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.0);
    private readonly Pen _majorGridPen = new(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1.5);
    private readonly Pen _axisPen = new(new SolidColorBrush(Color.FromArgb(120, 100, 180, 255)), 2.0);

    private readonly Pen _releasedEdgePen = new(new SolidColorBrush(Color.FromRgb(34, 197, 94)), 3.0);
    private readonly Pen _horizonEdgePen = new(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 2.5)
    {
        DashStyle = DashStyles.Dash
    };

    private readonly Brush _releasedNodeBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
    private readonly Brush _horizonNodeBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
    private readonly Pen _nodeBorderPen = new(Brushes.White, 1.5);

    private readonly Brush _textBrush = new SolidColorBrush(Color.FromRgb(200, 210, 225));
    private readonly Brush _releasedTextBrush = new SolidColorBrush(Color.FromRgb(220, 252, 231));
    private readonly Brush _horizonTextBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199));
    private readonly Typeface _typeface = new("Segoe UI");

    // Chip and legend styling
    private readonly Brush _chipBgBrush = new SolidColorBrush(Color.FromArgb(220, 18, 24, 34));
    private readonly Brush _gridChipBrush = new SolidColorBrush(Color.FromArgb(190, 16, 20, 28));
    private readonly Brush _legendBgBrush = new SolidColorBrush(Color.FromArgb(225, 20, 26, 38));
    private readonly Pen _legendBorderPen = new(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1.0);
    private readonly Pen _chipBorderPen = new(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 0.8);
    private readonly Pen _releasedChipBorderPen = new(new SolidColorBrush(Color.FromArgb(180, 34, 197, 94)), 1.0);
    private readonly Pen _horizonChipBorderPen = new(new SolidColorBrush(Color.FromArgb(180, 245, 158, 11)), 1.0);
    private readonly Pen _selectedAgvChipPen = new(new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)), 1.2);
    private readonly Pen _agvChipPen = new(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 0.8);
    private readonly Brush _batteryTrackBrush = new SolidColorBrush(Color.FromArgb(220, 30, 36, 46));
    private readonly Pen _batteryTrackPen = new(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 0.5);
    private readonly Brush _cyanBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));

    // Collision avoidance tracking for current frame
    private readonly List<Rect> _placedLabels = new(64);

    public MapCanvas()
    {
        ClipToBounds = true;

        _bgBrush.Freeze();
        _gridPen.Freeze();
        _majorGridPen.Freeze();
        _axisPen.Freeze();
        _releasedEdgePen.Freeze();
        _horizonEdgePen.Freeze();
        _releasedNodeBrush.Freeze();
        _horizonNodeBrush.Freeze();
        _nodeBorderPen.Freeze();
        _textBrush.Freeze();
        _releasedTextBrush.Freeze();
        _horizonTextBrush.Freeze();
        _chipBgBrush.Freeze();
        _gridChipBrush.Freeze();
        _legendBgBrush.Freeze();
        _legendBorderPen.Freeze();
        _chipBorderPen.Freeze();
        _releasedChipBorderPen.Freeze();
        _horizonChipBorderPen.Freeze();
        _selectedAgvChipPen.Freeze();
        _agvChipPen.Freeze();
        _batteryTrackBrush.Freeze();
        _batteryTrackPen.Freeze();
        _cyanBrush.Freeze();

        // §4 / §19: Redraw from latest snapshot via 100ms DispatcherTimer
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _renderTimer.Tick += (s, e) => InvalidateVisual();
        _renderTimer.Start();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pt = e.GetPosition(this);
        var (worldX, worldY) = ScreenToWorld(pt.X, pt.Y);
        MapClicked?.Invoke(worldX, worldY);
    }

    private (double sx, double sy) WorldToScreen(double wx, double wy)
    {
        double margin = 40.0;
        double w = Math.Max(ActualWidth - 2 * margin, 10.0);
        double h = Math.Max(ActualHeight - 2 * margin, 10.0);

        double rangeX = ExtentMaxX - ExtentMinX;
        double rangeY = ExtentMaxY - ExtentMinY;

        double scaleX = w / rangeX;
        double scaleY = h / rangeY;
        double scale = Math.Min(scaleX, scaleY);

        double sx = margin + (wx - ExtentMinX) * scale;
        double sy = (ActualHeight - margin) - (wy - ExtentMinY) * scale;
        return (sx, sy);
    }

    private (double wx, double wy) ScreenToWorld(double sx, double sy)
    {
        double margin = 40.0;
        double w = Math.Max(ActualWidth - 2 * margin, 10.0);
        double h = Math.Max(ActualHeight - 2 * margin, 10.0);

        double rangeX = ExtentMaxX - ExtentMinX;
        double rangeY = ExtentMaxY - ExtentMinY;

        double scaleX = w / rangeX;
        double scaleY = h / rangeY;
        double scale = Math.Min(scaleX, scaleY);

        double wx = (sx - margin) / scale + ExtentMinX;
        double wy = (ActualHeight - margin - sy) / scale + ExtentMinY;
        return (wx, wy);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // Draw background
        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (ActualWidth < 100 || ActualHeight < 100) return;

        // Reset label collision tracking for this frame
        _placedLabels.Clear();

        // Reserve legend space so other elements avoid it
        ReserveLegendBounds();

        // Draw grid lines and meter labels
        DrawGrid(dc);

        // Pre-reserve AGV bounding footprints to prevent node labels from crowding AGVs
        ReserveAgvBounds();

        // Draw planned route nodes and edges
        DrawPlannedRoute(dc);

        // Draw live AGVs on top of route
        DrawAgvs(dc);

        // Draw legend on top of grid lines
        DrawLegend(dc);
    }

    /// <summary>
    /// Draws formatted text with a semi-transparent rounded rectangle chip behind it.
    /// </summary>
    private Rect DrawTextWithChip(
        DrawingContext dc,
        FormattedText ft,
        Point origin,
        Brush? bgBrush = null,
        Pen? borderPen = null,
        double padX = 4.0,
        double padY = 2.0,
        double cornerRadius = 3.0)
    {
        var chipRect = new Rect(origin.X - padX, origin.Y - padY, ft.Width + padX * 2, ft.Height + padY * 2);
        dc.DrawRoundedRectangle(bgBrush ?? _chipBgBrush, borderPen ?? _chipBorderPen, chipRect, cornerRadius, cornerRadius);
        dc.DrawText(ft, origin);
        return chipRect;
    }

    private Rect DrawTextWithChipCentered(
        DrawingContext dc,
        FormattedText ft,
        double centerX,
        double centerY,
        Brush? bgBrush = null,
        Pen? borderPen = null,
        double padX = 4.0,
        double padY = 2.0,
        double cornerRadius = 3.0)
    {
        Point origin = new(centerX - ft.Width / 2.0, centerY - ft.Height / 2.0);
        return DrawTextWithChip(dc, ft, origin, bgBrush, borderPen, padX, padY, cornerRadius);
    }

    private static Rect GetLegendRect()
    {
        return new Rect(14, 10, 272, 24);
    }

    private void ReserveLegendBounds()
    {
        if (ActualWidth < 320 || ActualHeight < 150) return;
        _placedLabels.Add(GetLegendRect());
    }

    private void DrawLegend(DrawingContext dc)
    {
        if (ActualWidth < 320 || ActualHeight < 150) return;

        Rect legendRect = GetLegendRect();
        dc.DrawRoundedRectangle(_legendBgBrush, _legendBorderPen, legendRect, 4, 4);

        double x = legendRect.X;
        double y = legendRect.Y;
        double h = legendRect.Height;
        double midY = y + h / 2.0;

        var ftTitle = new FormattedText("图例", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _cyanBrush, 1.0);
        var ftReleased = new FormattedText("已释放", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);
        var ftHorizon = new FormattedText("远景", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);
        var ftNode = new FormattedText("节点", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);
        var ftHeading = new FormattedText("AGV朝向", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);

        double curX = x + 10;

        // Title
        dc.DrawText(ftTitle, new Point(curX, midY - ftTitle.Height / 2.0));
        curX += ftTitle.Width + 8;

        // Separator line
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1.0),
            new Point(curX - 4, y + 5), new Point(curX - 4, y + h - 5));

        // 1. Released edge
        dc.DrawLine(_releasedEdgePen, new Point(curX, midY), new Point(curX + 14, midY));
        curX += 14 + 5;
        dc.DrawText(ftReleased, new Point(curX, midY - ftReleased.Height / 2.0));
        curX += ftReleased.Width + 10;

        // 2. Horizon edge
        dc.DrawLine(_horizonEdgePen, new Point(curX, midY), new Point(curX + 14, midY));
        curX += 14 + 5;
        dc.DrawText(ftHorizon, new Point(curX, midY - ftHorizon.Height / 2.0));
        curX += ftHorizon.Width + 10;

        // 3. Node dot
        dc.DrawEllipse(_releasedNodeBrush, _nodeBorderPen, new Point(curX + 5, midY), 4, 4);
        curX += 10 + 5;
        dc.DrawText(ftNode, new Point(curX, midY - ftNode.Height / 2.0));
        curX += ftNode.Width + 10;

        // 4. AGV heading arrow
        var arrowPen = new Pen(Brushes.White, 1.8);
        dc.DrawLine(arrowPen, new Point(curX, midY), new Point(curX + 12, midY));
        dc.DrawLine(arrowPen, new Point(curX + 12, midY), new Point(curX + 8, midY - 3));
        dc.DrawLine(arrowPen, new Point(curX + 12, midY), new Point(curX + 8, midY + 3));
        curX += 14 + 5;
        dc.DrawText(ftHeading, new Point(curX, midY - ftHeading.Height / 2.0));
    }

    private void DrawGrid(DrawingContext dc)
    {
        int minX = (int)Math.Floor(ExtentMinX);
        int maxX = (int)Math.Ceiling(ExtentMaxX);
        int minY = (int)Math.Floor(ExtentMinY);
        int maxY = (int)Math.Ceiling(ExtentMaxY);

        // Grid lines every 1m, labels every 5m
        for (int x = minX; x <= maxX; x++)
        {
            var (sx1, sy1) = WorldToScreen(x, ExtentMinY);
            var (sx2, sy2) = WorldToScreen(x, ExtentMaxY);

            bool isMajor = x % 5 == 0;
            var pen = x == 0 ? _axisPen : (isMajor ? _majorGridPen : _gridPen);
            dc.DrawLine(pen, new Point(sx1, sy1), new Point(sx2, sy2));

            if (isMajor && sx1 >= 15 && sx1 <= ActualWidth - 15)
            {
                var ft = new FormattedText($"{x}m", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);
                var rect = DrawTextWithChipCentered(dc, ft, sx1, ActualHeight - 22, _gridChipBrush, _chipBorderPen, padX: 4, padY: 1.5, cornerRadius: 3);
                _placedLabels.Add(rect);
            }
        }

        for (int y = minY; y <= maxY; y++)
        {
            var (sx1, sy1) = WorldToScreen(ExtentMinX, y);
            var (sx2, sy2) = WorldToScreen(ExtentMaxX, y);

            bool isMajor = y % 5 == 0;
            var pen = y == 0 ? _axisPen : (isMajor ? _majorGridPen : _gridPen);
            dc.DrawLine(pen, new Point(sx1, sy1), new Point(sx2, sy2));

            if (isMajor && sy1 >= 15 && sy1 <= ActualHeight - 15)
            {
                var ft = new FormattedText($"{y}m", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);
                var rect = DrawTextWithChipCentered(dc, ft, 20, sy1, _gridChipBrush, _chipBorderPen, padX: 4, padY: 1.5, cornerRadius: 3);
                _placedLabels.Add(rect);
            }
        }
    }

    private void ReserveAgvBounds()
    {
        var agvs = AgvsSource;
        if (agvs == null) return;

        foreach (var agv in agvs)
        {
            var pos = agv.PositionSnapshot;
            if (pos == null) continue;

            var (sx, sy) = WorldToScreen(pos.X, pos.Y);

            // Serial label footprint above AGV
            string label = $"{agv.SerialNumber} [{agv.OperatingModeDisplay}]";
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 11, Brushes.White, 1.0);
            double labelW = ft.Width + 12.0;
            double labelH = ft.Height + 6.0;
            _placedLabels.Add(new Rect(sx - labelW / 2.0, sy - 34 - labelH / 2.0, labelW, labelH));

            // AGV body circle & halo footprint
            _placedLabels.Add(new Rect(sx - 24, sy - 24, 48, 48));

            // Battery chip footprint below AGV
            _placedLabels.Add(new Rect(sx - 26, sy + 21, 52, 25));
        }
    }

    private void DrawPlannedRoute(DrawingContext dc)
    {
        var waypoints = PlannedWaypointsSource;
        if (waypoints == null || waypoints.Count == 0) return;

        // Draw edges
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var wp1 = waypoints[i];
            var wp2 = waypoints[i + 1];

            var (sx1, sy1) = WorldToScreen(wp1.X, wp1.Y);
            var (sx2, sy2) = WorldToScreen(wp2.X, wp2.Y);

            bool isReleased = wp1.IsReleased && wp2.IsReleased;
            var pen = isReleased ? _releasedEdgePen : _horizonEdgePen;

            dc.DrawLine(pen, new Point(sx1, sy1), new Point(sx2, sy2));
        }

        // Draw node circles first
        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            var (sx, sy) = WorldToScreen(wp.X, wp.Y);

            var brush = wp.IsReleased ? _releasedNodeBrush : _horizonNodeBrush;
            dc.DrawEllipse(brush, _nodeBorderPen, new Point(sx, sy), 7, 7);
        }

        // Draw node labels with decluttering and collision avoidance
        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            var (sx, sy) = WorldToScreen(wp.X, wp.Y);

            // Declutter: show only node ID in a chip
            string nodeId = string.IsNullOrWhiteSpace(wp.NodeId) ? $"N{i + 1}" : wp.NodeId;
            var borderPen = wp.IsReleased ? _releasedChipBorderPen : _horizonChipBorderPen;
            var textBrush = wp.IsReleased ? _releasedTextBrush : _horizonTextBrush;
            var ft = new FormattedText(nodeId, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, textBrush, 1.0);

            double padX = 4.0;
            double padY = 2.0;

            // Candidate positions: 1. Right, 2. Top, 3. Bottom, 4. Left
            Point[] candidates =
            [
                new Point(sx + 10, sy - ft.Height / 2.0),
                new Point(sx - ft.Width / 2.0, sy - 12 - ft.Height),
                new Point(sx - ft.Width / 2.0, sy + 12),
                new Point(sx - 10 - ft.Width, sy - ft.Height / 2.0)
            ];

            foreach (var origin in candidates)
            {
                Rect chipRect = new(origin.X - padX, origin.Y - padY, ft.Width + padX * 2, ft.Height + padY * 2);

                // Boundary check
                if (chipRect.Left < 4 || chipRect.Right > ActualWidth - 4 || chipRect.Top < 4 || chipRect.Bottom > ActualHeight - 4)
                    continue;

                // Check collision against placed labels with 2px margin
                bool collides = false;
                Rect testRect = chipRect;
                testRect.Inflate(2, 2);

                for (int p = 0; p < _placedLabels.Count; p++)
                {
                    if (testRect.IntersectsWith(_placedLabels[p]))
                    {
                        collides = true;
                        break;
                    }
                }

                if (!collides)
                {
                    DrawTextWithChip(dc, ft, origin, _chipBgBrush, borderPen, padX, padY, cornerRadius: 3.0);
                    _placedLabels.Add(chipRect);
                    break;
                }
            }
        }
    }

    private void DrawAgvs(DrawingContext dc)
    {
        var agvs = AgvsSource;
        if (agvs == null) return;

        foreach (var agv in agvs)
        {
            var pos = agv.PositionSnapshot;
            if (pos == null) continue;

            var (sx, sy) = WorldToScreen(pos.X, pos.Y);
            bool isSelected = SelectedAgv != null && string.Equals(SelectedAgv.SerialNumber, agv.SerialNumber, StringComparison.OrdinalIgnoreCase);

            // Halo for selected AGV
            if (isSelected)
            {
                var haloPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 56, 189, 248)), 3.0);
                dc.DrawEllipse(null, haloPen, new Point(sx, sy), 22, 22);
            }

            // AGV Body color based on status
            Brush bodyBrush = Brushes.Gray;
            if (agv.ConnectionState == MasterConnectionState.Online)
            {
                if (agv.HasFatalError)
                    bodyBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                else if (agv.HasWarning)
                    bodyBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                else if (agv.Driving)
                    bodyBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
                else if (agv.Paused)
                    bodyBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
                else
                    bodyBrush = new SolidColorBrush(Color.FromRgb(20, 184, 166)); // Teal (idle)
            }
            else if (agv.ConnectionState == MasterConnectionState.ConnectionBroken || agv.IsStale)
            {
                bodyBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)); // Dark amber
            }

            // Draw AGV body circle (above route nodes)
            dc.DrawEllipse(bodyBrush, new Pen(Brushes.White, 2.0), new Point(sx, sy), 14, 14);

            // Draw heading indicator: theta is in radians. Screen Y is flipped, so angle is -theta
            double theta = pos.Theta;
            double hx = sx + 20 * Math.Cos(theta);
            double hy = sy - 20 * Math.Sin(theta);
            var headingPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 255, 255)), 2.5);
            dc.DrawLine(headingPen, new Point(sx, sy), new Point(hx, hy));

            // Arrow head
            double arrowLen = 6;
            double angle1 = theta + Math.PI * 0.85;
            double angle2 = theta - Math.PI * 0.85;
            dc.DrawLine(headingPen, new Point(hx, hy), new Point(hx + arrowLen * Math.Cos(angle1), hy - arrowLen * Math.Sin(angle1)));
            dc.DrawLine(headingPen, new Point(hx, hy), new Point(hx + arrowLen * Math.Cos(angle2), hy - arrowLen * Math.Sin(angle2)));

            // Label: Serial Number + Mode with chip
            string label = $"{agv.SerialNumber} [{agv.OperatingModeDisplay}]";
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 11, Brushes.White, 1.0);
            DrawTextWithChipCentered(dc, ft, sx, sy - 34, _chipBgBrush, isSelected ? _selectedAgvChipPen : _agvChipPen, padX: 6, padY: 2.5, cornerRadius: 3);

            // Battery chip widget below AGV
            double batChipW = 50;
            double batChipH = 24;
            double batChipX = sx - batChipW / 2.0;
            double batChipY = sy + 21;
            Rect batChipRect = new(batChipX, batChipY, batChipW, batChipH);
            dc.DrawRoundedRectangle(_chipBgBrush, _batteryTrackPen, batChipRect, 3, 3);

            // Progress bar track & fill
            double barW = 38;
            double barH = 4;
            double barX = sx - barW / 2.0;
            double barY = batChipY + 4;
            dc.DrawRoundedRectangle(_batteryTrackBrush, null, new Rect(barX, barY, barW, barH), 2, 2);

            double chargePct = Math.Clamp(agv.BatteryCharge / 100.0, 0.0, 1.0);
            Brush batteryFill = chargePct > 0.5 ? Brushes.LimeGreen : (chargePct > 0.2 ? Brushes.Yellow : Brushes.Red);
            if (chargePct > 0)
            {
                dc.DrawRoundedRectangle(batteryFill, null, new Rect(barX, barY, barW * chargePct, barH), 2, 2);
            }

            // Battery text
            string batText = $"{Math.Round(agv.BatteryCharge)}%{(agv.IsCharging ? " ⚡" : "")}";
            var batFt = new FormattedText(batText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 9, _textBrush, 1.0);
            dc.DrawText(batFt, new Point(sx - batFt.Width / 2.0, barY + barH + 1));
        }
    }
}
