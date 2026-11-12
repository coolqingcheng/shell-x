using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace ShellX.Desktop.Controls;

public enum ConnectionAccentAppearance { Indicator, Tab, Frame, Row }

// 颜色跟随控件的实际主题计算，不重建终端；背景和边框比顶部标识线更轻。
public sealed class ConnectionAccentBorder : Border
{
    public static readonly StyledProperty<string> AccentColorProperty = AvaloniaProperty.Register<ConnectionAccentBorder, string>(nameof(AccentColor), "");
    public static readonly StyledProperty<ConnectionAccentAppearance> AppearanceProperty = AvaloniaProperty.Register<ConnectionAccentBorder, ConnectionAccentAppearance>(nameof(Appearance));
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<ConnectionAccentBorder, bool>(nameof(IsActive));
    public string AccentColor { get => GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public ConnectionAccentAppearance Appearance { get => GetValue(AppearanceProperty); set => SetValue(AppearanceProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public ConnectionAccentBorder()
    {
        ActualThemeVariantChanged += (_, _) => ApplyColor();
        ApplyColor();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AccentColorProperty || change.Property == AppearanceProperty || change.Property == IsActiveProperty || change.Property == Border.ChildProperty) ApplyColor();
    }
    /// <summary>模板或主题切换后重新计算配色，让前景色与子控件同步。</summary>
    public void Refresh() => ApplyColor();

    private void ApplyColor()
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var surface = Color.Parse(light ? "#FFFFFF" : "#1E1E1E");
        var header = Color.Parse(light ? "#EAEAEA" : "#2D2D30");
        var marked = !string.IsNullOrEmpty(AccentColor) && AccentColor.Length == 7 && AccentColor[0] == '#' && Color.TryParse(AccentColor, out _);
        var accent = marked ? Muted(Color.Parse(AccentColor), light) : Colors.Transparent;
        var activeFallback = Color.Parse(light ? "#CCE8FF" : "#094771");
        switch (Appearance)
        {
            case ConnectionAccentAppearance.Indicator:
                Background = new SolidColorBrush(marked ? accent : IsActive ? activeFallback : Colors.Transparent); break;
            case ConnectionAccentAppearance.Tab:
                if (IsActive)
                {
                    var active = marked ? AccentSurface(surface, accent) : activeFallback;
                    Background = new SolidColorBrush(active);
                    ApplyForeground(ReadableForeground(active));
                }
                else
                {
                    Background = new SolidColorBrush(marked ? Blend(header, accent, light ? .08 : .10) : header);
                    ApplyForeground(Color.Parse(light ? "#333333" : "#CCCCCC"));
                }
                break;
            case ConnectionAccentAppearance.Frame:
                BorderBrush = new SolidColorBrush(marked ? Blend(surface, accent, light ? .38 : .52) : Colors.Transparent); break;
            case ConnectionAccentAppearance.Row:
                Background = new SolidColorBrush(marked ? Color.FromArgb(light ? (byte)46 : (byte)66, accent.R, accent.G, accent.B) : Colors.Transparent); break;
        }
    }
    private void ApplyForeground(Color color)
    {
        var brush = new SolidColorBrush(color);
        TextElement.SetForeground(this, brush);
        if (Child is not Control root) return;
        SetForegroundRecursive(root, brush);
        foreach (var control in root.GetVisualDescendants().OfType<Control>()) SetForegroundRecursive(control, brush);
    }
    private static void SetForegroundRecursive(Control control, IBrush brush)
    {
        if (control is TextBlock text) text.Foreground = brush;
        else TextElement.SetForeground(control, brush);
    }

    private static Color AccentSurface(Color surface, Color accent)
    {
        for (var weight = 1d; weight >= 0; weight -= .05)
        {
            var candidate = Blend(surface, accent, weight);
            if (Contrast(ReadableForeground(candidate), candidate) >= 4.5) return candidate;
        }
        return Blend(surface, accent, .4);
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(Color color) => .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static Color ReadableForeground(Color background)
    {
        var white = Colors.White;
        var dark = Color.Parse("#1E1E1E");
        return Contrast(white, background) >= Contrast(dark, background) ? white : dark;
    }
    private static Color Muted(Color color, bool light)
    {
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B)) / 255d;
        var minimum = Math.Min(color.R, Math.Min(color.G, color.B)) / 255d;
        var saturation = maximum == 0 ? 0 : (maximum - minimum) / maximum;
        var capped = Math.Min(saturation, .55);
        var value = Math.Clamp(maximum, light ? .32 : .42, light ? .68 : .78);
        byte Channel(byte component) => (byte)Math.Round(255 * value * (saturation == 0 ? 1 : 1 - (1 - component / 255d / maximum) * capped / saturation));
        return Color.FromRgb(Channel(color.R), Channel(color.G), Channel(color.B));
    }
    private static Color Blend(Color background, Color accent, double weight)
    {
        byte Channel(byte back, byte front) => (byte)Math.Round(back * (1 - weight) + front * weight);
        return Color.FromRgb(Channel(background.R, accent.R), Channel(background.G, accent.G), Channel(background.B, accent.B));
    }
}
