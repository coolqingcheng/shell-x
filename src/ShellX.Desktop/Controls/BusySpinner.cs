using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ShellX.Desktop.Controls;

/// <summary>行内加载指示：旋转的圆弧，用于删除等耗时操作；只有可见时才计时。</summary>
public sealed class BusySpinner : Control
{
    /// <summary>圆弧颜色，默认使用中性灰。</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<BusySpinner, IBrush?>(nameof(Stroke));

    private readonly DispatcherTimer _timer;
    private double _angle;

    static BusySpinner() => AffectsRender<BusySpinner>(StrokeProperty);

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public BusySpinner()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _timer.Tick += (_, _) => { _angle = (_angle + 24) % 360; InvalidateVisual(); };
        Width = 16;
        Height = 16;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;
        if (IsVisible) _timer.Start(); else _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 3) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = size / 2 - 1.5;
        if (radius <= 0) return;
        var stroke = Stroke ?? Brushes.Gray;
        var thickness = Math.Max(1.5, size / 8);
        context.DrawEllipse(null, new Pen(Fade(stroke, 0.25), thickness), center, radius, radius);
        using (context.PushTransform(Matrix.CreateRotation(_angle, center)))
            context.DrawGeometry(null, new Pen(stroke, thickness), Arc(center, radius));
    }

    /// <summary>按比例降低不透明度，用于绘制底圈。</summary>
    private static IBrush Fade(IBrush brush, double factor)
        => brush is ISolidColorBrush solid
            ? new SolidColorBrush(solid.Color) { Opacity = solid.Opacity * factor }
            : brush;

    /// <summary>四分之三圆弧，配合旋转形成加载动画。</summary>
    private static Geometry Arc(Point center, double radius)
    {
        var start = Polar(center, radius, 0);
        var end = Polar(center, radius, 270);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false);
            context.ArcTo(end, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true);
            context.EndFigure(false);
        }
        return geometry;
    }

    private static Point Polar(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
