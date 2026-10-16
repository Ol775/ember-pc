using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Ember.App;

/// <summary>The icon of a sidebar page (a path on a 24 by 24 grid), used by the redesigned navigation item.</summary>
public static class Nav
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(Nav));
    public static Geometry? GetIcon(DependencyObject d) => (Geometry?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, Geometry? value) => d.SetValue(IconProperty, value);
}

/// <summary>Capital letters for the small card headings of the redesign.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string)?.ToUpper(culture);
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A temperature gauge: a ring that fills with the value and shows it in the middle.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(RingGauge),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public RingGauge()
    {
        Loaded += (_, _) => ThemeManager.Changed += Redraw;
        Unloaded += (_, _) => ThemeManager.Changed -= Redraw;
    }

    void Redraw() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 20) return;
        const double thickness = 10;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - thickness / 2 - 1;
        var track = Application.Current.TryFindResource("PanelBorderBrush") as Brush ?? Brushes.Gray;
        var text = Application.Current.TryFindResource("TextBrush") as Brush ?? Brushes.White;
        dc.DrawEllipse(null, new Pen(track, thickness), centre, radius, radius);

        var fraction = Math.Clamp(double.IsNaN(Fraction) ? 0 : Fraction, 0, 1);
        if (fraction > 0.005)
        {
            // Starts at the top and runs clockwise; a full ring is drawn just short of a closed circle, which an arc cannot draw.
            var angle = Math.Min(fraction, 0.9995) * 2 * Math.PI - Math.PI / 2;
            var start = new Point(centre.X, centre.Y - radius);
            var end = new Point(centre.X + radius * Math.Cos(angle), centre.Y + radius * Math.Sin(angle));
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(Stroke, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        }

        var formatted = new FormattedText(Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size * 0.24, text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(centre.X - formatted.Width / 2, centre.Y - formatted.Height / 2));
    }
}
