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
        ["folder"] = "M3,6.5 H9.5 L11.8,8.8 H21 V19 H3 Z",
        ["tunnel"] = "M3,7 H16 M13,4 L16,7 L13,10 M21,17 H8 M11,14 L8,17 L11,20",
        ["power"] = "M12,2.5 V11 M6.3,6.3 A8,8 0 1 0 17.7,6.3",
        ["maximize"] = "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15",
        ["duplicate"] = "M8,8 H20 V20 H8 Z M16,16 V4 H4 V16",
        ["close"] = "M6,6 L18,18 M18,6 L6,18",
        ["settings"] = "M7.4,12 A4.6,4.6 0 1 0 16.6,12 A4.6,4.6 0 1 0 7.4,12 Z M10.2,12 A1.8,1.8 0 1 0 13.8,12 A1.8,1.8 0 1 0 10.2,12 Z M16.6,12 H18.9 M15.3,15.3 L16.9,16.9 M12,16.6 V18.9 M8.7,15.3 L7.1,16.9 M7.4,12 H5.1 M8.7,8.7 L7.1,7.1 M12,7.4 V5.1 M15.3,8.7 L16.9,7.1",
        ["plus"] = "M12,5 V19 M5,12 H19",
        ["download"] = "M20,12 H5 M11,6 L5,12 L11,18",
        ["upload"] = "M4,12 H19 M13,6 L19,12 L13,18",
        ["disk"] = "M3,7 H21 V17 H3 Z M3,11 H21 M6,14 H9",
        ["layout"] = "M4,4 H20 V20 H4 Z M4,12 H20 M12,4 V20",
        ["connections"] = "M4,5 H20 V11 H4 Z M4,13 H20 V19 H4 Z M7,8 H9 M7,16 H9",
        ["down"] = "M5,12 L12,19 L19,12 M12,19 V5",
        ["service"] = "M12,4 L21,8.5 L12,13 L3,8.5 Z M3,12 L12,16.5 L21,12 M3,15.5 L12,20 L21,15.5",
        ["ports"] = "M9,3 V8 M15,3 V8 M6,8 H18 V12 A6,6 0 0 1 6,12 Z M12,18 V22",
        ["command"] = "M6,7 L11,12 L6,17 M13,17 H19",
        ["chevron-down"] = "M5,9 L12,16 L19,9",
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