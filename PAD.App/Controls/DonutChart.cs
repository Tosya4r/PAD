using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PAD.Desktop.Controls;

public sealed record ChartSlice(int Id, string Color, double Fraction);

/// <summary>Кольцевая диаграмма: клик по сегменту выделяет категорию, остальные приглушаются.</summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(
        nameof(Slices), typeof(IReadOnlyList<ChartSlice>), typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedIdProperty = DependencyProperty.Register(
        nameof(SelectedId), typeof(int?), typeof(DonutChart),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IReadOnlyList<ChartSlice>? Slices
    {
        get => (IReadOnlyList<ChartSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public int? SelectedId
    {
        get => (int?)GetValue(SelectedIdProperty);
        set => SetValue(SelectedIdProperty, value);
    }

    public DonutChart()
    {
        Cursor = Cursors.Hand;
        Loaded += (_, _) => PAD.Desktop.Services.ThemeService.ThemeChanged += OnTheme;
        Unloaded += (_, _) => PAD.Desktop.Services.ThemeService.ThemeChanged -= OnTheme;
    }

    void OnTheme() => InvalidateVisual();

    static readonly Brush EmptyBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xF0)));

    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    static Brush Parse(string hex)
    {
        try { return Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))); }
        catch { return EmptyBrush; }
    }

    (Point Center, double Outer, double Thick) Geometry()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        var outer = size / 2 - 10;
        return (new Point(ActualWidth / 2, ActualHeight / 2), outer, outer * 0.27);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Math.Min(ActualWidth, ActualHeight) < 40) return;

        var (c, outer, thick) = Geometry();
        var r = outer - thick / 2;
        var slices = Slices ?? Array.Empty<ChartSlice>();

        if (slices.Count == 0)
        {
            dc.DrawEllipse(null, new Pen(PAD.Desktop.Services.ThemeService.Brush("LineBrush", EmptyBrush), thick), c, r, r);
            return;
        }

        var hasSelection = SelectedId.HasValue && slices.Any(s => s.Id == SelectedId);
        var gap = slices.Count > 1 ? 2.5 : 0.0;
        var angle = -90.0;

        foreach (var s in slices)
        {
            var sweep = s.Fraction * 360.0;
            var selected = hasSelection && s.Id == SelectedId;
            var pen = new Pen(Parse(s.Color), selected ? thick + 12 : thick)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat,
            };

            if (hasSelection && !selected) dc.PushOpacity(0.35);

            if (slices.Count == 1 || sweep >= 359.9)
                dc.DrawEllipse(null, pen, c, r, r);
            else
            {
                var a0 = angle + gap / 2;
                var a1 = angle + sweep - gap / 2;
                if (a1 > a0) dc.DrawGeometry(null, pen, Arc(c, r, a0, a1));
            }

            if (hasSelection && !selected) dc.Pop();
            angle += sweep;
        }
    }

    static Geometry Arc(Point c, double r, double a0, double a1)
    {
        Point P(double a) => new(c.X + r * Math.Cos(a * Math.PI / 180), c.Y + r * Math.Sin(a * Math.PI / 180));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(a0), false, false);
            ctx.ArcTo(P(a1), new Size(r, r), 0, a1 - a0 > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var (c, outer, thick) = Geometry();
        var p = e.GetPosition(this);
        double dx = p.X - c.X, dy = p.Y - c.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < outer - thick - 8 || dist > outer + 10) { SelectedId = null; return; }

        var deg = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
        if (deg < 0) deg += 360;

        double acc = 0;
        foreach (var s in Slices ?? Array.Empty<ChartSlice>())
        {
            acc += s.Fraction * 360.0;
            if (deg < acc)
            {
                SelectedId = SelectedId == s.Id ? null : s.Id;
                return;
            }
        }
    }
}
