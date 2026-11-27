using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PAD.Desktop.Controls;

public sealed record BarItem(int Key, string Label, double Value, string Tooltip);

/// <summary>Столбчатый график по месяцам: подсказка с точной суммой при наведении, клик выбирает месяц.</summary>
public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(IReadOnlyList<BarItem>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedKeyProperty = DependencyProperty.Register(
        nameof(SelectedKey), typeof(int?), typeof(BarChart),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IReadOnlyList<BarItem>? Bars
    {
        get => (IReadOnlyList<BarItem>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public int? SelectedKey
    {
        get => (int?)GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    const double L = 8, R = 8, T = 14, B = 28;
    static readonly Brush FbAccent = Frozen(Color.FromRgb(0x19, 0xC3, 0xA6));
    static readonly Brush FbEmpty = Frozen(Color.FromRgb(0x2A, 0x2F, 0x37));
    static readonly Brush FbMuted = Frozen(Color.FromRgb(0x8A, 0x92, 0x9E));
    static readonly Brush FbText = Frozen(Color.FromRgb(0xE8, 0xEB, 0xEF));
    int _hover = -1;

    public BarChart()
    {
        Cursor = Cursors.Hand;
        Loaded += (_, _) => PAD.Desktop.Services.ThemeService.ThemeChanged += OnTheme;
        Unloaded += (_, _) => PAD.Desktop.Services.ThemeService.ThemeChanged -= OnTheme;
    }

    void OnTheme() => InvalidateVisual();

    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var bars = Bars;
        if (bars is null || bars.Count == 0 || ActualWidth < 60) return;
        var Accent = PAD.Desktop.Services.ThemeService.Brush("AccentBrush", FbAccent);
        var Empty = PAD.Desktop.Services.ThemeService.Brush("LineBrush", FbEmpty);
        var Muted = PAD.Desktop.Services.ThemeService.Brush("TextMutedBrush", FbMuted);
        var Strong = PAD.Desktop.Services.ThemeService.Brush("TextBrush", FbText);

        double w = ActualWidth - L - R, h = ActualHeight - T - B;
        var slot = w / bars.Count;
        var bw = Math.Min(slot * 0.6, 56);
        var max = bars.Max(b => b.Value);
        if (max <= 0) max = 1;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var hasSel = SelectedKey.HasValue && bars.Any(b => b.Key == SelectedKey);

        dc.DrawLine(new Pen(Empty, 1), new Point(L, T + h), new Point(L + w, T + h));

        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var x = L + slot * i + (slot - bw) / 2;
            var bh = b.Value > 0 ? Math.Max(h * b.Value / max, 3) : 2;
            var selected = hasSel && b.Key == SelectedKey;

            var opacity = hasSel ? (selected ? 1.0 : 0.35) : (i == _hover ? 1.0 : 0.85);
            dc.PushOpacity(opacity);
            dc.DrawRoundedRectangle(b.Value > 0 ? Accent : Empty, null, new Rect(x, T + h - bh, bw, bh), 4, 4);
            dc.Pop();

            if (slot >= 26 || i % 2 == 0)
            {
                var ft = new FormattedText(b.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Inter, Segoe UI"), 11, selected || i == _hover ? Strong : Muted, dpi);
                dc.DrawText(ft, new Point(L + slot * i + (slot - ft.Width) / 2, T + h + 7));
            }
        }
    }

    int IndexAt(Point p)
    {
        var bars = Bars;
        if (bars is null || bars.Count == 0) return -1;
        var slot = (ActualWidth - L - R) / bars.Count;
        var i = (int)Math.Floor((p.X - L) / slot);
        return i >= 0 && i < bars.Count ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = IndexAt(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        ToolTip = i >= 0 ? Bars![i].Tooltip : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var i = IndexAt(e.GetPosition(this));
        if (i < 0) { SelectedKey = null; return; }
        var key = Bars![i].Key;
        SelectedKey = SelectedKey == key ? null : key;
    }
}
