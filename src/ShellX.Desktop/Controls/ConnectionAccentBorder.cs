using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ShellX.Desktop.Controls;

public enum ConnectionAccentAppearance { Indicator, Tab, Frame }

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
        if (change.Property == AccentColorProperty || change.Property == AppearanceProperty || change.Property == IsActiveProperty) ApplyColor();
    }
    private void ApplyColor()
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var surface = Color.Parse(light ? "#FFFFFF" : "#1E1E1E");
        var header = Color.Parse(light ? "#EAEAEA" : "#2D2D30");
        var marked = !string.IsNullOrEmpty(AccentColor) && AccentColor.Length == 7 && AccentColor[0] == '#' && Color.TryParse(AccentColor, out _);
        var accent = marked ? Muted(Color.Parse(AccentColor), light) : Colors.Transparent;
        switch (Appearance)
        {
            case ConnectionAccentAppearance.Indicator:
                Background = new SolidColorBrush(accent); break;
            case ConnectionAccentAppearance.Tab:
                var activeFallback = Color.Parse(light ? "#CCE8FF" : "#094771");
                var baseline = IsActive ? marked ? Blend(surface, accent, light ? .24 : .26) : activeFallback : header;
                Background = new SolidColorBrush(marked && !IsActive ? Blend(baseline, accent, light ? .08 : .10) : baseline); break;
            case ConnectionAccentAppearance.Frame:
                BorderBrush = new SolidColorBrush(marked ? Blend(surface, accent, light ? .38 : .52) : Colors.Transparent); break;
        }
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
