using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ShellX.Core.Settings;

namespace ShellX.Desktop.Styles;

/// <summary>把各主题的界面配色注册成 Avalonia 主题字典。自定义变体继承 Dark/Light，只覆盖不同的键。</summary>
public sealed class ShellXThemes : Avalonia.Styling.Styles
{
    public ShellXThemes()
    {
        var dictionary = new ResourceDictionary();
        foreach (var theme in ThemeCatalog.All)
            dictionary.ThemeDictionaries[ThemeCatalog.Variant(theme)] = Build(theme);
        Resources = dictionary;
    }

    private static ResourceDictionary Build(AppTheme theme)
    {
        var result = new ResourceDictionary();
        foreach (var (key, hex) in ThemeCatalog.Of(theme).Ui)
            result[key] = new ImmutableSolidColorBrush(Color.Parse(hex));
        return result;
    }
}
