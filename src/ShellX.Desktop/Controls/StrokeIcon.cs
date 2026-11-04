using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ShellX.Desktop.Controls;

/// <summary>线条风格图标：几何按 24×24 视框绘制，按控件尺寸等比缩放，描边宽度保持不变。</summary>
public class StrokeIcon : Control
{
    public static readonly StyledProperty<string> SymbolProperty =
        AvaloniaProperty.Register<StrokeIcon, string>(nameof(Symbol), "");
    public string Symbol { get => GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<StrokeIcon, IBrush?>(nameof(Foreground));
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<StrokeIcon, double>(nameof(StrokeThickness), 1.5d);
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    // 通用线条图标：导航和更多操作按钮共用。
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["back"] = "M15,5 L8,12 L15,19",
        ["forward"] = "M9,5 L16,12 L9,19",
        ["up"] = "M5,12 L12,5 L19,12 M12,5 V19",
        ["home"] = "M3.5,11.5 L12,4 L20.5,11.5 M6.5,10 V19.5 H17.5 V10",
        ["refresh"] = "M20,12 A8,8 0 1,1 16.2,5.7 M15.4,2.7 L16.4,5.8 L13.2,4.7",
        ["flow"] = "M8,18 V7 M4.5,10.5 L8,7 L11.5,10.5 M16,6 V17 M12.5,13.5 L16,17 L19.5,13.5",
        ["more"] = "M6,12 m-1.5,0 a1.5,1.5 0 1,0 3,0 a1.5,1.5 0 1,0 -3,0 M12,12 m-1.5,0 a1.5,1.5 0 1,0 3,0 a1.5,1.5 0 1,0 -3,0 M18,12 m-1.5,0 a1.5,1.5 0 1,0 3,0 a1.5,1.5 0 1,0 -3,0"
    };
    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    static StrokeIcon() { AffectsRender<StrokeIcon>(SymbolProperty, ForegroundProperty, StrokeThicknessProperty); }

    public static string SymbolPath(string? symbol)
        => !string.IsNullOrWhiteSpace(symbol) && Paths.TryGetValue(symbol!, out var path) ? path : "";

    protected virtual string? ResolvePath(string? symbol) => SymbolPath(symbol);

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush) return;
        var path = ResolvePath(Symbol);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Cache.TryGetValue(path, out var geometry))
        { geometry = Geometry.Parse(path); Cache[path] = geometry; }
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var scale = size / 24d;
        var offset = new Vector((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateTranslation(offset) * Matrix.CreateScale(scale, scale)))
        {
            var pen = new Pen(brush, StrokeThickness / scale)
            { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            context.DrawGeometry(null, pen, geometry);
        }
    }

}