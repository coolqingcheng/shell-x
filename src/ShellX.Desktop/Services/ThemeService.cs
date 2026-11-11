using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using ShellX.Core.Settings;
using ShellX.Desktop.Styles;

namespace ShellX.Desktop.Services;

public static class ThemeService
{
    /// <summary>当前主题。终端与编辑器要按主题取色，不能只看是不是浅色。</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Bind(AvaloniaObject target, AvaloniaProperty property, string key)
        => target.Bind(property, new DynamicResourceExtension(key));

    public static IBrush Brush(Control control, string key)
        => control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush : Brushes.Transparent;

    public static void Apply(AppTheme theme)
    {
        // 先记住主题再切换变体：界面、终端、编辑器都是收到变体变化后才按新主题取色。
        Current = theme;
        if (Application.Current is { } application)
            application.RequestedThemeVariant = ThemeCatalog.Variant(theme);
    }
}
