using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using ShellX.Core.Settings;

namespace ShellX.Desktop.Services;

public static class ThemeService
{
    public static void Bind(AvaloniaObject target, AvaloniaProperty property, string key)
        => target.Bind(property, new DynamicResourceExtension(key));

    public static IBrush Brush(Control control, string key)
        => control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush : Brushes.Transparent;

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is { } application)
            application.RequestedThemeVariant = theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
