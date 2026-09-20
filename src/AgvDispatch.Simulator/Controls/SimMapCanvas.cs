using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AgvDispatch.Simulator.Models;

namespace AgvDispatch.Simulator.Controls;

/// <summary>
/// Map canvas rendering the AGV simulator's live view:
/// - Grid with meter labels and chips (-1..21m or auto-expanded extent)
/// - Current order path: nodes and edges (released solid green vs horizon dashed amber)
/// - AGV live position, heading arrow, status color, and battery indicator
/// - Traversed historical trail (capped polyline)
/// - Compact legend
/// Uses a 100ms DispatcherTimer for smooth 10Hz redraw.
/// </summary>
public class SimMapCanvas : FrameworkElement
{
    public static readonly DependencyProperty WaypointsSourceProperty =
        DependencyProperty.Register(nameof(WaypointsSource), typeof(ObservableCollection<SimWaypointItem>), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty TrailSourceProperty =
        DependencyProperty.Register(nameof(TrailSource), typeof(ObservableCollection<Point>), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty AgvXProperty =
        DependencyProperty.Register(nameof(AgvX), typeof(double), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty AgvYProperty =
        DependencyProperty.Register(nameof(AgvY), typeof(double), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty AgvThetaProperty =
        DependencyProperty.Register(nameof(AgvTheta), typeof(double), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty AgvBatteryChargeProperty =
        DependencyProperty.Register(nameof(AgvBatteryCharge), typeof(double), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(100.0));

    public static readonly DependencyProperty IsDrivingProperty =
        DependencyProperty.Register(nameof(IsDriving), typeof(bool), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsPausedProperty =
        DependencyProperty.Register(nameof(IsPaused), typeof(bool), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HasFatalErrorProperty =
        DependencyProperty.Register(nameof(HasFatalError), typeof(bool), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty AgvSerialNumberProperty =
        DependencyProperty.Register(nameof(AgvSerialNumber), typeof(string), typeof(SimMapCanvas),
            new FrameworkPropertyMetadata("AGV0001"));

    public ObservableCollection<SimWaypointItem>? WaypointsSource
    {
        get => (ObservableCollection<SimWaypointItem>?)GetValue(WaypointsSourceProperty);
        set => SetValue(WaypointsSourceProperty, value);
    }

    public ObservableCollection<Point>? TrailSource
    {
        get => (ObservableCollection<Point>?)GetValue(TrailSourceProperty);
        set => SetValue(TrailSourceProperty, value);
    }

    public double AgvX
    {
        get => (double)GetValue(AgvXProperty);
        set => SetValue(AgvXProperty, value);
    }

    public double AgvY
    {
        get => (double)GetValue(AgvYProperty);
        set => SetValue(AgvYProperty, value);
    }

    public double AgvTheta
    {
        get => (double)GetValue(AgvThetaProperty);
        set => SetValue(AgvThetaProperty, value);
    }

    public double AgvBatteryCharge
    {
        get => (double)GetValue(AgvBatteryChargeProperty);
        set => SetValue(AgvBatteryChargeProperty, value);
    }

    public bool IsDriving
    {
        get => (bool)GetValue(IsDrivingProperty);
        set => SetValue(IsDrivingProperty, value);
    }

    public bool IsPaused
    {
        get => (bool)GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    public bool HasFatalError
    {
        get => (bool)GetValue(HasFatalErrorProperty);
        set => SetValue(HasFatalErrorProperty, value);
    }

    public string AgvSerialNumber
    {
        get => (string)GetValue(AgvSerialNumberProperty);
        set => SetValue(AgvSerialNumberProperty, value);
    }

    public double ExtentMinX { get; set; } = -1.0;
    public double ExtentMaxX { get; set; } = 21.0;
    public double ExtentMinY { get; set; } = -1.0;
    public double ExtentMaxY { get; set; } = 21.0;

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

    private readonly Pen _trailPen = new(new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)), 1.8);

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
    private readonly Pen _agvHaloPen = new(new SolidColorBrush(Color.FromArgb(140, 56, 189, 248)), 2.5);
    private readonly Pen _agvBorderPen = new(Brushes.White, 2.0);
    private readonly Pen _agvChipPen = new(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 0.8);
    private readonly Brush _batteryTrackBrush = new SolidColorBrush(Color.FromArgb(220, 30, 36, 46));
    private readonly Pen _batteryTrackPen = new(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 0.5);
    private readonly Brush _cyanBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));

    // Collision avoidance tracking for current frame
    private readonly List<Rect> _placedLabels = new(64);

    public SimMapCanvas()
    {
        ClipToBounds = true;

        _bgBrush.Freeze();
        _gridPen.Freeze();
        _majorGridPen.Freeze();
        _axisPen.Freeze();
        _releasedEdgePen.Freeze();
        _horizonEdgePen.Freeze();
        _trailPen.Freeze();
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
        _agvHaloPen.Freeze();
        _agvBorderPen.Freeze();
        _agvChipPen.Freeze();
        _batteryTrackBrush.Freeze();
        _batteryTrackPen.Freeze();
        _cyanBrush.Freeze();

        // 100ms DispatcherTimer for smooth redraw without UI thrashing
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _renderTimer.Tick += (s, e) => InvalidateVisual();
        _renderTimer.Start();
    }

    private (double minX, double maxX, double minY, double maxY) GetEffectiveExtents()
    {
        double minX = ExtentMinX;
        double maxX = ExtentMaxX;
        double minY = ExtentMinY;
        double maxY = ExtentMaxY;

        // Auto-expand bounds if AGV is outside default extent
        if (AgvX - 2 < minX) minX = Math.Floor(AgvX - 2);
        if (AgvX + 2 > maxX) maxX = Math.Ceiling(AgvX + 2);
        if (AgvY - 2 < minY) minY = Math.Floor(AgvY - 2);
        if (AgvY + 2 > maxY) maxY = Math.Ceiling(AgvY + 2);

        // Auto-expand bounds if any waypoint is outside
        var waypoints = WaypointsSource;
        if (waypoints != null)
        {
            foreach (var wp in waypoints)
            {
                if (wp.X - 2 < minX) minX = Math.Floor(wp.X - 2);
                if (wp.X + 2 > maxX) maxX = Math.Ceiling(wp.X + 2);
                if (wp.Y - 2 < minY) minY = Math.Floor(wp.Y - 2);
                if (wp.Y + 2 > maxY) maxY = Math.Ceiling(wp.Y + 2);
            }
        }

        return (minX, maxX, minY, maxY);
    }

    private (double sx, double sy) WorldToScreen(double wx, double wy)
    {
        var (extMinX, extMaxX, extMinY, extMaxY) = GetEffectiveExtents();

        double margin = 40.0;
        double w = Math.Max(ActualWidth - 2 * margin, 10.0);
        double h = Math.Max(ActualHeight - 2 * margin, 10.0);

        double rangeX = Math.Max(extMaxX - extMinX, 1.0);
        double rangeY = Math.Max(extMaxY - extMinY, 1.0);

        double scaleX = w / rangeX;
        double scaleY = h / rangeY;
        double scale = Math.Min(scaleX, scaleY);

        double sx = margin + (wx - extMinX) * scale;
        double sy = (ActualHeight - margin) - (wy - extMinY) * scale;
        return (sx, sy);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // Draw dark background
        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (ActualWidth < 100 || ActualHeight < 100) return;

        // Reset collision labels for this frame
        _placedLabels.Clear();

        // Reserve legend space so node labels avoid it
        ReserveLegendBounds();

        // Draw grid lines and meter chips
        DrawGrid(dc);

        // Reserve AGV space
        ReserveAgvBounds();

        // Draw historical traversed trail
        DrawTrail(dc);

        // Draw current order path (nodes and edges)
        DrawOrderPath(dc);

        // Draw live AGV marker with heading & battery
        DrawAgv(dc);

        // Draw top legend
        DrawLegend(dc);
    }

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
        return new Rect(14, 10, 360, 24);
    }

    private void ReserveLegendBounds()
    {
        if (ActualWidth < 380 || ActualHeight < 150) return;
        _placedLabels.Add(GetLegendRect());
    }

    private void DrawLegend(DrawingContext dc)
    {
        if (ActualWidth < 380 || ActualHeight < 150) return;

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
        var ftAgv = new FormattedText("AGV", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);
        var ftTrail = new FormattedText("历史轨迹", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);

        double curX = x + 8;

        // Title
        dc.DrawText(ftTitle, new Point(curX, midY - ftTitle.Height / 2.0));
        curX += ftTitle.Width + 6;

        // Separator line
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1.0),
            new Point(curX - 2, y + 5), new Point(curX - 2, y + h - 5));
        curX += 4;

        // 1. Released edge (green)
        dc.DrawLine(_releasedEdgePen, new Point(curX, midY), new Point(curX + 12, midY));
        curX += 12 + 4;
        dc.DrawText(ftReleased, new Point(curX, midY - ftReleased.Height / 2.0));
        curX += ftReleased.Width + 8;

        // 2. Horizon edge (amber dashed)
        dc.DrawLine(_horizonEdgePen, new Point(curX, midY), new Point(curX + 12, midY));
        curX += 12 + 4;
        dc.DrawText(ftHorizon, new Point(curX, midY - ftHorizon.Height / 2.0));
        curX += ftHorizon.Width + 8;

        // 3. Node dot
        dc.DrawEllipse(_releasedNodeBrush, _nodeBorderPen, new Point(curX + 4, midY), 4, 4);
        curX += 8 + 4;
        dc.DrawText(ftNode, new Point(curX, midY - ftNode.Height / 2.0));
        curX += ftNode.Width + 8;

        // 4. AGV heading arrow
        var arrowPen = new Pen(Brushes.White, 1.6);
        dc.DrawLine(arrowPen, new Point(curX, midY), new Point(curX + 10, midY));
        dc.DrawLine(arrowPen, new Point(curX + 10, midY), new Point(curX + 7, midY - 3));
        dc.DrawLine(arrowPen, new Point(curX + 10, midY), new Point(curX + 7, midY + 3));
        curX += 12 + 4;
        dc.DrawText(ftAgv, new Point(curX, midY - ftAgv.Height / 2.0));
        curX += ftAgv.Width + 8;

        // 5. Historical trail
        dc.DrawLine(_trailPen, new Point(curX, midY), new Point(curX + 12, midY));
        curX += 12 + 4;
        dc.DrawText(ftTrail, new Point(curX, midY - ftTrail.Height / 2.0));
    }

    private void DrawGrid(DrawingContext dc)
    {
        var (extMinX, extMaxX, extMinY, extMaxY) = GetEffectiveExtents();

        int minX = (int)Math.Floor(extMinX);
        int maxX = (int)Math.Ceiling(extMaxX);
        int minY = (int)Math.Floor(extMinY);
        int maxY = (int)Math.Ceiling(extMaxY);

        // Grid lines every 1m, labels every 5m
        for (int x = minX; x <= maxX; x++)
        {
            var (sx1, sy1) = WorldToScreen(x, extMinY);
            var (sx2, sy2) = WorldToScreen(x, extMaxY);

            bool isMajor = x % 5 == 0;
            var pen = x == 0 ? _axisPen : (isMajor ? _majorGridPen : _gridPen);
            dc.DrawLine(pen, new Point(sx1, sy1), new Point(sx2, sy2));

            if (isMajor && sx1 >= 15 && sx1 <= ActualWidth - 15)
            {
                var ft = new FormattedText($"{x}m", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.0);
                var rect = DrawTextWithChipCentered(dc, ft, sx1, ActualHeight - 20, _gridChipBrush, _chipBorderPen, padX: 4, padY: 1.5, cornerRadius: 3);
                _placedLabels.Add(rect);
            }
        }

        for (int y = minY; y <= maxY; y++)
        {
            var (sx1, sy1) = WorldToScreen(extMinX, y);
            var (sx2, sy2) = WorldToScreen(extMaxX, y);

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
        var (sx, sy) = WorldToScreen(AgvX, AgvY);

        // AGV body & halo
        _placedLabels.Add(new Rect(sx - 24, sy - 24, 48, 48));

        // AGV top label
        string label = $"{AgvSerialNumber} (X:{AgvX:F2}, Y:{AgvY:F2})";
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, Brushes.White, 1.0);
        _placedLabels.Add(new Rect(sx - ft.Width / 2.0 - 6, sy - 32 - ft.Height / 2.0 - 2, ft.Width + 12, ft.Height + 4));

        // AGV bottom battery chip
        _placedLabels.Add(new Rect(sx - 26, sy + 18, 52, 24));
    }

    private void DrawTrail(DrawingContext dc)
    {
        var trail = TrailSource;
        if (trail == null || trail.Count < 2) return;

        StreamGeometry geometry = new();
        using (var ctx = geometry.Open())
        {
            var p0 = WorldToScreen(trail[0].X, trail[0].Y);
            ctx.BeginFigure(new Point(p0.sx, p0.sy), false, false);
            for (int i = 1; i < trail.Count; i++)
            {
                var (sx, sy) = WorldToScreen(trail[i].X, trail[i].Y);
                ctx.LineTo(new Point(sx, sy), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, _trailPen, geometry);
    }

    private void DrawOrderPath(DrawingContext dc)
    {
        var waypoints = WaypointsSource;
        if (waypoints == null || waypoints.Count == 0) return;

        // 1. Draw edges between consecutive waypoints
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

        // 2. Draw node circles
        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            var (sx, sy) = WorldToScreen(wp.X, wp.Y);

            var brush = wp.IsReleased ? _releasedNodeBrush : _horizonNodeBrush;
            dc.DrawEllipse(brush, _nodeBorderPen, new Point(sx, sy), 7, 7);
        }

        // 3. Draw node ID labels with decluttering collision avoidance
        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            var (sx, sy) = WorldToScreen(wp.X, wp.Y);

            string nodeId = string.IsNullOrWhiteSpace(wp.NodeId) ? $"N{i + 1}" : wp.NodeId;
            var borderPen = wp.IsReleased ? _releasedChipBorderPen : _horizonChipBorderPen;
            var textBrush = wp.IsReleased ? _releasedTextBrush : _horizonTextBrush;
            var ft = new FormattedText(nodeId, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, textBrush, 1.0);

            double padX = 4.0;
            double padY = 2.0;

            // Candidate positions: Right, Top, Bottom, Left
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

                if (chipRect.Left < 4 || chipRect.Right > ActualWidth - 4 || chipRect.Top < 4 || chipRect.Bottom > ActualHeight - 4)
                    continue;

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

    private void DrawAgv(DrawingContext dc)
    {
        var (sx, sy) = WorldToScreen(AgvX, AgvY);

        // Outer halo
        dc.DrawEllipse(null, _agvHaloPen, new Point(sx, sy), 20, 20);

        // Body brush based on status
        Brush bodyBrush = new SolidColorBrush(Color.FromRgb(20, 184, 166)); // Teal (idle)
        if (HasFatalError)
        {
            bodyBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
        }
        else if (IsPaused)
        {
            bodyBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
        }
        else if (IsDriving)
        {
            bodyBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
        }

        // AGV body circle
        dc.DrawEllipse(bodyBrush, _agvBorderPen, new Point(sx, sy), 14, 14);

        // Heading indicator: theta is in radians. Screen Y is inverted, so use -theta
        double theta = AgvTheta;
        double hx = sx + 22 * Math.Cos(theta);
        double hy = sy - 22 * Math.Sin(theta);
        var headingPen = new Pen(Brushes.White, 2.5);
        dc.DrawLine(headingPen, new Point(sx, sy), new Point(hx, hy));

        // Arrow head
        double arrowLen = 6.0;
        double angle1 = theta + Math.PI * 0.85;
        double angle2 = theta - Math.PI * 0.85;
        dc.DrawLine(headingPen, new Point(hx, hy), new Point(hx + arrowLen * Math.Cos(angle1), hy - arrowLen * Math.Sin(angle1)));
        dc.DrawLine(headingPen, new Point(hx, hy), new Point(hx + arrowLen * Math.Cos(angle2), hy - arrowLen * Math.Sin(angle2)));

        // Label above AGV: Serial Number + Coordinates
        string label = $"{AgvSerialNumber} (X:{AgvX:F2}, Y:{AgvY:F2})";
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, Brushes.White, 1.0);
        DrawTextWithChipCentered(dc, ft, sx, sy - 32, _chipBgBrush, _agvChipPen, padX: 5, padY: 2.0, cornerRadius: 3);

        // Battery widget below AGV
        double batChipW = 50;
        double batChipH = 22;
        double batChipX = sx - batChipW / 2.0;
        double batChipY = sy + 18;
        Rect batChipRect = new(batChipX, batChipY, batChipW, batChipH);
        dc.DrawRoundedRectangle(_chipBgBrush, _batteryTrackPen, batChipRect, 3, 3);

        // Battery progress bar track & fill
        double barW = 38;
        double barH = 4;
        double barX = sx - barW / 2.0;
        double barY = batChipY + 3;
        dc.DrawRoundedRectangle(_batteryTrackBrush, null, new Rect(barX, barY, barW, barH), 2, 2);

        double chargePct = Math.Clamp(AgvBatteryCharge / 100.0, 0.0, 1.0);
        Brush batteryFill = chargePct > 0.5 ? Brushes.LimeGreen : (chargePct > 0.2 ? Brushes.Yellow : Brushes.Red);
        if (chargePct > 0)
        {
            dc.DrawRoundedRectangle(batteryFill, null, new Rect(barX, barY, barW * chargePct, barH), 2, 2);
        }

        // Battery text
        string batText = $"{Math.Round(AgvBatteryCharge)}%";
        var batFt = new FormattedText(batText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 9, _textBrush, 1.0);
        dc.DrawText(batFt, new Point(sx - batFt.Width / 2.0, barY + barH + 1));
    }
}
