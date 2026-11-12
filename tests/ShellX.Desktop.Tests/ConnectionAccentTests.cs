using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using ShellX.Core.Connections;
using ShellX.Core.Settings;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.Connections;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Tests;

public sealed class ConnectionAccentTests
{
    [AvaloniaFact]
    public async Task VividCustomRedBecomesBrightHighlightBarWithMutedReadableTabBackgroundAndSubtleFourSidedFrameInBothThemes()
    {
        var indicator = new ConnectionAccentBorder { AccentColor = "#FF0000", Height = 3, IsActive = true };
        var label = new TextBlock { Text = "敏感连接", Margin = new Thickness(10) };
        var tab = new ConnectionAccentBorder { AccentColor = "#FF0000", Appearance = ConnectionAccentAppearance.Tab, IsActive = true, Child = label };
        var frame = new ConnectionAccentBorder { AccentColor = "#FF0000", Appearance = ConnectionAccentAppearance.Frame, BorderThickness = new Thickness(2), Child = new TextBlock { Text = "terminal", Margin = new Thickness(15) } };
        var panel = new StackPanel(); panel.Children.Add(indicator); panel.Children.Add(tab); panel.Children.Add(frame);
        var window = new Window { Content = panel, Width = 400, Height = 200 }; window.Show();
        try
        {
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme); await Task.Delay(40);
                var top = Solid(indicator.Background); var background = Solid(tab.Background); var edge = Solid(frame.BorderBrush);
                Assert.True(top.R > top.G && top.R > top.B); Assert.InRange(top.R, 200, 255);
                Assert.True(top.G > 0); Assert.True(edge.R > edge.G); Assert.True(background.R > background.G);
                indicator.IsActive = false; Assert.Equal(0, Solid(indicator.Background).A); indicator.IsActive = true;   // 未激活的标签没有高亮条
                Assert.True(Contrast(Solid(label.Foreground), background) >= 4.5);
                var active = background; tab.IsActive = false; Assert.NotEqual(active, Solid(tab.Background)); tab.IsActive = true;
                frame.AccentColor = ""; Assert.Equal(0, Solid(frame.BorderBrush).A);
                frame.AccentColor = "invalid"; Assert.Equal(0, Solid(frame.BorderBrush).A);
                frame.AccentColor = "#FF0000";
            }
            var editor = new ConnectionEditorViewModel(new ConnectionProfile { Host = "host" }, null);
            editor.SelectedAccent = ConnectionAccentChoice.Find("#BF6969"); Assert.Equal("#BF6969", editor.Build().Profile.AccentColor);
            editor.AccentColor = "#6b96b8"; Assert.Equal("#6B96B8", editor.Build().Profile.AccentColor);
            editor.AccentColor = "bad"; Assert.Throws<ArgumentException>(() => editor.Build());
            editor.SelectedAccent = ConnectionAccentChoice.Find(""); Assert.Equal("", editor.Build().Profile.AccentColor);
        }
        finally { ThemeService.Apply(AppTheme.Dark); window.Close(); }
    }
    [AvaloniaFact]
    public async Task PlainActiveTabUsesClearSelectionColorInBothThemes()
    {
        var active = new ConnectionAccentBorder { Appearance = ConnectionAccentAppearance.Tab, IsActive = true };
        var inactive = new ConnectionAccentBorder { Appearance = ConnectionAccentAppearance.Tab, IsActive = false };
        var window = new Window { Content = new StackPanel { Children = { active, inactive } }, Width = 240, Height = 120 }; window.Show();
        try
        {
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme); await Task.Delay(40);
                Assert.NotEqual(Solid(inactive.Background), Solid(active.Background));
                Assert.Equal(Color.Parse(theme == AppTheme.Light ? "#CCE8FF" : "#094771"), Solid(active.Background));
            }
        }
        finally { ThemeService.Apply(AppTheme.Dark); window.Close(); }
    }

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static double Contrast(Color foreground, Color background)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        var a = Luminance(foreground); var b = Luminance(background); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
