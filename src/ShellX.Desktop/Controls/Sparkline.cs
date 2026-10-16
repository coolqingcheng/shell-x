using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Controls;

public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SamplesProperty = AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Samples));
    public static readonly StyledProperty<IReadOnlyList<double>?> SecondarySamplesProperty = AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(SecondarySamples));
    public static readonly StyledProperty<IBrush> StrokeProperty = AvaloniaProperty.Register<Sparkline, IBrush>(nameof(Stroke), Brush.Parse("#4FC1FF"));
    public IReadOnlyList<double>? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public IReadOnlyList<double>? SecondarySamples { get => GetValue(SecondarySamplesProperty); set => SetValue(SecondarySamplesProperty, value); }
    public IBrush Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    static Sparkline() => AffectsRender<Sparkline>(SamplesProperty, SecondarySamplesProperty, StrokeProperty);
    public Sparkline() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size; if (size.Width <= 0 || size.Height <= 0) return;
        context.FillRectangle(ThemeService.Brush(this, "ChartBackground"), new Rect(size));
        var grid = new Pen(ThemeService.Brush(this, "ChartGrid"), 1);
        for (var i = 1; i < 4; i++) context.DrawLine(grid, new Point(0, i * size.Height / 4), new Point(size.Width, i * size.Height / 4));
        var maximum = Math.Max(1, Math.Max(Samples?.DefaultIfEmpty(0).Max() ?? 0, SecondarySamples?.DefaultIfEmpty(0).Max() ?? 0)) * 1.15;
        DrawSeries(context, Samples, Stroke, maximum, size);
        DrawSeries(context, SecondarySamples, ThemeService.Brush(this, "OrangeForeground"), maximum, size);
    }
    private static void DrawSeries(DrawingContext context, IReadOnlyList<double>? samples, IBrush stroke, double maximum, Size size)
    {
        if (samples == null || samples.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            for (var i = 0; i < samples.Count; i++)
            {
                var point = new Point(size.Width * (60 - samples.Count + i) / 59d, size.Height - 2 - Math.Clamp(samples[i] / maximum, 0, 1) * (size.Height - 4));
                if (i == 0) path.BeginFigure(point, false); else path.LineTo(point);
            }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(stroke, 1.3), geometry);
    }
}
