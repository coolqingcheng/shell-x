using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using ShellX.Core.Settings;
using XTerm.Options;

namespace ShellX.Desktop.Styles;

/// <summary>自定义主题变体。继承 Dark/Light，某个键没单独定义时自动回退到对应变体。</summary>
public static class ThemeVariants
{
    public static readonly ThemeVariant OneDark = new("OneDark", ThemeVariant.Dark);
    public static readonly ThemeVariant Dracula = new("Dracula", ThemeVariant.Dark);
    public static readonly ThemeVariant SolarizedLight = new("SolarizedLight", ThemeVariant.Light);
}

/// <summary>终端 ANSI 调色板，值是 #RRGGBB。</summary>
public sealed record TerminalPalette(
    string Foreground, string Background, string Cursor, string Selection,
    string Black, string Red, string Green, string Yellow, string Blue, string Magenta, string Cyan, string White,
    string BrightBlack, string BrightRed, string BrightGreen, string BrightYellow,
    string BrightBlue, string BrightMagenta, string BrightCyan, string BrightWhite);

/// <summary>文本编辑器语法高亮配色。</summary>
public sealed record SyntaxPalette(string Comment, string String, string Property, string Keyword, string Number, string Variable, string Delimiter);

/// <summary>终端语义着色配色，值是 24 位 RGB 的 "R;G;B"。</summary>
public sealed record HighlightPalette(string Error, string Warning, string Success, string Ip, string Link);

/// <summary>一套主题：界面、终端、编辑器三处配色放在一起，免得各自漂移。</summary>
public sealed record ThemePalette(
    string DisplayName,
    bool IsDark,
    IReadOnlyDictionary<string, string> Ui,
    TerminalPalette Terminal,
    SyntaxPalette Syntax,
    HighlightPalette Highlight);

/// <summary>所有可选主题的配色表。新增主题时在这里加一份，并同步 <see cref="ThemeVariants"/>。</summary>
public static class ThemeCatalog
{
    // 界面配色键
    public const string AppBackground = "AppBackground";
    public const string PanelBackground = "PanelBackground";
    public const string BorderColor = "BorderColor";
    public const string ForegroundBrush = "ForegroundBrush";
    public const string EditorForeground = "EditorForeground";
    public const string RaisedBackground = "RaisedBackground";
    public const string InputBackground = "InputBackground";
    public const string HeaderBackground = "HeaderBackground";
    public const string HoverBackground = "HoverBackground";
    public const string SelectionBackground = "SelectionBackground";
    public const string MutedForeground = "MutedForeground";
    public const string SecondaryForeground = "SecondaryForeground";
    public const string IconForeground = "IconForeground";
    public const string PlaceholderForeground = "PlaceholderForeground";
    public const string FaintForeground = "FaintForeground";
    public const string AccentForeground = "AccentForeground";
    public const string LinkForeground = "LinkForeground";
    public const string FolderForeground = "FolderForeground";
    public const string CommandForeground = "CommandForeground";
    public const string SuccessForeground = "SuccessForeground";
    public const string ErrorForeground = "ErrorForeground";
    public const string WarningForeground = "WarningForeground";
    public const string WarningBackground = "WarningBackground";
    public const string SuccessBackground = "SuccessBackground";
    public const string TreeBackground = "TreeBackground";
    public const string ChartBackground = "ChartBackground";
    public const string ChartGrid = "ChartGrid";
    public const string PurpleForeground = "PurpleForeground";
    public const string OrangeForeground = "OrangeForeground";
    public const string CommentForeground = "CommentForeground";

    // 编辑器语法角色
    public const string RoleComment = "Comment";
    public const string RoleString = "String";
    public const string RoleProperty = "Property";
    public const string RoleKeyword = "Keyword";
    public const string RoleNumber = "Number";
    public const string RoleVariable = "Variable";
    public const string RoleDelimiter = "Delimiter";

    public static IReadOnlyList<AppTheme> All { get; } =
        [AppTheme.Dark, AppTheme.Light, AppTheme.OneDark, AppTheme.Dracula, AppTheme.SolarizedLight];

    // VS Code Dark+：默认暗色。
    private static readonly ThemePalette Dark = new(
        "暗色", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#1E1E1E", [PanelBackground] = "#252526", [BorderColor] = "#333333",
            [ForegroundBrush] = "#CCCCCC", [EditorForeground] = "#D4D4D4", [RaisedBackground] = "#3C3C3C",
            [InputBackground] = "#3C3C3C", [HeaderBackground] = "#2D2D30", [HoverBackground] = "#37373D",
            [SelectionBackground] = "#094771", [MutedForeground] = "#858585", [SecondaryForeground] = "#9D9D9D",
            [IconForeground] = "#969696", [PlaceholderForeground] = "#666666", [FaintForeground] = "#555555",
            [AccentForeground] = "#4FC1FF", [LinkForeground] = "#75BEFF", [FolderForeground] = "#D7BA7D",
            [CommandForeground] = "#DCDCAA", [SuccessForeground] = "#73C991", [ErrorForeground] = "#F48771",
            [WarningForeground] = "#F4B894", [WarningBackground] = "#332823", [SuccessBackground] = "#20382B",
            [TreeBackground] = "#222223", [ChartBackground] = "#202022", [ChartGrid] = "#353539",
            [PurpleForeground] = "#C586C0", [OrangeForeground] = "#CE9178", [CommentForeground] = "#608B4E"
        },
        new TerminalPalette(
            "#CCCCCC", "#1E1E1E", "#FFFFFF", "#264F78",
            "#000000", "#CD3131", "#0DBC79", "#E5E510", "#2472C8", "#BC3FBC", "#11A8CD", "#E5E5E5",
            "#666666", "#F14C4C", "#23D18B", "#F5F543", "#3B8EEA", "#D670D6", "#29B8DB", "#FFFFFF"),
        new SyntaxPalette("#6A9955", "#CE9178", "#9CDCFE", "#569CD6", "#B5CEA8", "#C586C0", "#808080"),
        new HighlightPalette("244;135;113", "215;186;125", "78;201;176", "181;206;168", "79;193;255"));

    // VS Code Light+：默认亮色。
    private static readonly ThemePalette Light = new(
        "亮色", false,
        new Dictionary<string, string>
        {
            [AppBackground] = "#FFFFFF", [PanelBackground] = "#F3F3F3", [BorderColor] = "#D4D4D4",
            [ForegroundBrush] = "#333333", [EditorForeground] = "#242424", [RaisedBackground] = "#E5E5E5",
            [InputBackground] = "#FFFFFF", [HeaderBackground] = "#EAEAEA", [HoverBackground] = "#E1E1E1",
            [SelectionBackground] = "#CCE8FF", [MutedForeground] = "#666666", [SecondaryForeground] = "#595959",
            [IconForeground] = "#5F5F5F", [PlaceholderForeground] = "#767676", [FaintForeground] = "#8A8A8A",
            [AccentForeground] = "#006AB1", [LinkForeground] = "#005FB8", [FolderForeground] = "#8A6100",
            [CommandForeground] = "#795E00", [SuccessForeground] = "#16723C", [ErrorForeground] = "#BF2020",
            [WarningForeground] = "#8A3C12", [WarningBackground] = "#FFF1DE", [SuccessBackground] = "#E4F3E7",
            [TreeBackground] = "#F7F7F7", [ChartBackground] = "#FAFAFA", [ChartGrid] = "#DDDDDD",
            [PurpleForeground] = "#8228A1", [OrangeForeground] = "#A5451B", [CommentForeground] = "#477A30"
        },
        new TerminalPalette(
            "#333333", "#FFFFFF", "#000000", "#ADD6FF",
            "#000000", "#CD3131", "#00BC00", "#949800", "#0451A5", "#BC05BC", "#0598BC", "#555555",
            "#666666", "#CD3131", "#14CE14", "#B5BA00", "#0451A5", "#BC05BC", "#0598BC", "#A5A5A5"),
        new SyntaxPalette("#008000", "#A31515", "#0451A5", "#0000FF", "#098658", "#AF00DB", "#666666"),
        new HighlightPalette("191;32;32", "138;97;0", "22;114;60", "71;122;48", "0;106;177"));

    // One Dark Pro：VS Code 上装机量最高的第三方主题之一。
    private static readonly ThemePalette OneDark = new(
        "One Dark Pro", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#282C34", [PanelBackground] = "#21252B", [BorderColor] = "#3E4451",
            [ForegroundBrush] = "#ABB2BF", [EditorForeground] = "#ABB2BF", [RaisedBackground] = "#3E4451",
            [InputBackground] = "#1B1D23", [HeaderBackground] = "#21252B", [HoverBackground] = "#2C313A",
            [SelectionBackground] = "#3E4451", [MutedForeground] = "#7F848E", [SecondaryForeground] = "#9DA5B4",
            [IconForeground] = "#8B929E", [PlaceholderForeground] = "#6B717C", [FaintForeground] = "#4B5263",
            [AccentForeground] = "#61AFEF", [LinkForeground] = "#61AFEF", [FolderForeground] = "#E5C07B",
            [CommandForeground] = "#E5C07B", [SuccessForeground] = "#98C379", [ErrorForeground] = "#E06C75",
            [WarningForeground] = "#E5C07B", [WarningBackground] = "#3A2E22", [SuccessBackground] = "#23331F",
            [TreeBackground] = "#21252B", [ChartBackground] = "#21252B", [ChartGrid] = "#3E4451",
            [PurpleForeground] = "#C678DD", [OrangeForeground] = "#D19A66", [CommentForeground] = "#5C6370"
        },
        new TerminalPalette(
            "#ABB2BF", "#282C34", "#528BFF", "#3E4451",
            "#282C34", "#E06C75", "#98C379", "#E5C07B", "#61AFEF", "#C678DD", "#56B6C2", "#ABB2BF",
            "#5C6370", "#E06C75", "#98C379", "#E5C07B", "#61AFEF", "#C678DD", "#56B6C2", "#FFFFFF"),
        new SyntaxPalette("#5C6370", "#98C379", "#E06C75", "#C678DD", "#D19A66", "#E5C07B", "#8F949E"),
        new HighlightPalette("224;108;117", "229;192;123", "152;195;121", "97;175;239", "97;175;239"));

    // Dracula：高对比紫调暗色主题。
    private static readonly ThemePalette Dracula = new(
        "Dracula", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#282A36", [PanelBackground] = "#21222C", [BorderColor] = "#44475A",
            [ForegroundBrush] = "#F8F8F2", [EditorForeground] = "#F8F8F2", [RaisedBackground] = "#44475A",
            [InputBackground] = "#1E1F29", [HeaderBackground] = "#21222C", [HoverBackground] = "#343746",
            [SelectionBackground] = "#44475A", [MutedForeground] = "#6272A4", [SecondaryForeground] = "#9AA5CE",
            [IconForeground] = "#9AA5CE", [PlaceholderForeground] = "#6272A4", [FaintForeground] = "#4C5375",
            [AccentForeground] = "#BD93F9", [LinkForeground] = "#8BE9FD", [FolderForeground] = "#8BE9FD",
            [CommandForeground] = "#50FA7B", [SuccessForeground] = "#50FA7B", [ErrorForeground] = "#FF5555",
            [WarningForeground] = "#FFB86C", [WarningBackground] = "#3B2F3A", [SuccessBackground] = "#1E3A2B",
            [TreeBackground] = "#21222C", [ChartBackground] = "#21222C", [ChartGrid] = "#44475A",
            [PurpleForeground] = "#BD93F9", [OrangeForeground] = "#FFB86C", [CommentForeground] = "#6272A4"
        },
        new TerminalPalette(
            "#F8F8F2", "#282A36", "#F8F8F2", "#44475A",
            "#21222C", "#FF5555", "#50FA7B", "#F1FA8C", "#BD93F9", "#FF79C6", "#8BE9FD", "#F8F8F2",
            "#6272A4", "#FF6E6E", "#69FF94", "#FFFFA5", "#D6ACFF", "#FF92DF", "#A4FFFF", "#FFFFFF"),
        new SyntaxPalette("#6272A4", "#F1FA8C", "#50FA7B", "#FF79C6", "#BD93F9", "#8BE9FD", "#F8F8F2"),
        new HighlightPalette("255;85;85", "241;250;140", "80;250;123", "139;233;253", "189;147;249"));

    // Solarized Light：暖色底的经典亮色主题。
    private static readonly ThemePalette SolarizedLight = new(
        "Solarized Light", false,
        new Dictionary<string, string>
        {
            [AppBackground] = "#FDF6E3", [PanelBackground] = "#EEE8D5", [BorderColor] = "#D9D2C2",
            [ForegroundBrush] = "#657B83", [EditorForeground] = "#586E75", [RaisedBackground] = "#DCD6C4",
            [InputBackground] = "#FBF4E4", [HeaderBackground] = "#EEE8D5", [HoverBackground] = "#E4DCC8",
            [SelectionBackground] = "#DCD6C4", [MutedForeground] = "#93A1A1", [SecondaryForeground] = "#839496",
            [IconForeground] = "#657B83", [PlaceholderForeground] = "#93A1A1", [FaintForeground] = "#AAB2B3",
            [AccentForeground] = "#268BD2", [LinkForeground] = "#268BD2", [FolderForeground] = "#B58900",
            [CommandForeground] = "#CB4B16", [SuccessForeground] = "#859900", [ErrorForeground] = "#DC322F",
            [WarningForeground] = "#B58900", [WarningBackground] = "#FAF0D0", [SuccessBackground] = "#E9F0D8",
            [TreeBackground] = "#F5EFDF", [ChartBackground] = "#F5EFDF", [ChartGrid] = "#D9D2C2",
            [PurpleForeground] = "#6C71C4", [OrangeForeground] = "#CB4B16", [CommentForeground] = "#93A1A1"
        },
        new TerminalPalette(
            "#657B83", "#FDF6E3", "#657B83", "#D9D2C2",
            "#073642", "#DC322F", "#859900", "#B58900", "#268BD2", "#D33682", "#2AA198", "#EEE8D5",
            "#002B36", "#CB4B16", "#586E75", "#657B83", "#839496", "#6C71C4", "#93A1A1", "#FDF6E3"),
        new SyntaxPalette("#93A1A1", "#2AA198", "#268BD2", "#859900", "#D33682", "#CB4B16", "#657B83"),
        new HighlightPalette("220;50;47", "181;137;0", "133;153;0", "38;139;210", "38;139;210"));

    private static readonly Dictionary<(AppTheme Theme, string Role), IBrush> SyntaxBrushes = BuildSyntaxBrushes();

    public static ThemePalette Of(AppTheme theme) => theme switch
    {
        AppTheme.Light => Light,
        AppTheme.OneDark => OneDark,
        AppTheme.Dracula => Dracula,
        AppTheme.SolarizedLight => SolarizedLight,
        _ => Dark
    };

    /// <summary>主题对应的 Avalonia 变体；自定义变体继承 Dark/Light。</summary>
    public static ThemeVariant Variant(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.OneDark => ThemeVariants.OneDark,
        AppTheme.Dracula => ThemeVariants.Dracula,
        AppTheme.SolarizedLight => ThemeVariants.SolarizedLight,
        _ => ThemeVariant.Dark
    };

    public static string DisplayName(AppTheme theme) => Of(theme).DisplayName;

    /// <summary>设置页下拉框顺序与枚举的对应关系。</summary>
    public static AppTheme FromIndex(int index) => index switch
    {
        1 => AppTheme.Light,
        2 => AppTheme.OneDark,
        3 => AppTheme.Dracula,
        4 => AppTheme.SolarizedLight,
        _ => AppTheme.Dark
    };

    public static int ToIndex(AppTheme theme) => theme switch
    {
        AppTheme.Light => 1,
        AppTheme.OneDark => 2,
        AppTheme.Dracula => 3,
        AppTheme.SolarizedLight => 4,
        _ => 0
    };

    /// <summary>终端的 ANSI 配色。必须整体赋值新对象，模拟器才会重新播种调色板。</summary>
    public static ThemeOptions TerminalTheme(AppTheme theme)
    {
        var palette = Of(theme).Terminal;
        return new ThemeOptions
        {
            Foreground = palette.Foreground,
            Background = palette.Background,
            Cursor = palette.Cursor,
            Selection = palette.Selection,
            Black = palette.Black,
            Red = palette.Red,
            Green = palette.Green,
            Yellow = palette.Yellow,
            Blue = palette.Blue,
            Magenta = palette.Magenta,
            Cyan = palette.Cyan,
            White = palette.White,
            BrightBlack = palette.BrightBlack,
            BrightRed = palette.BrightRed,
            BrightGreen = palette.BrightGreen,
            BrightYellow = palette.BrightYellow,
            BrightBlue = palette.BrightBlue,
            BrightMagenta = palette.BrightMagenta,
            BrightCyan = palette.BrightCyan,
            BrightWhite = palette.BrightWhite
        };
    }

    /// <summary>终端选中背景。</summary>
    public static Color TerminalSelection(AppTheme theme) => Color.Parse(Of(theme).Terminal.Selection);

    /// <summary>编辑器语法色画刷；按主题缓存，省得每次绘制都新建。</summary>
    public static IBrush SyntaxBrush(AppTheme theme, string role)
        => SyntaxBrushes.TryGetValue((theme, role), out var brush) ? brush : Brushes.Black;

    private static Dictionary<(AppTheme, string), IBrush> BuildSyntaxBrushes()
    {
        var result = new Dictionary<(AppTheme, string), IBrush>();
        foreach (var theme in All)
        {
            var syntax = Of(theme).Syntax;
            Add(theme, RoleComment, syntax.Comment);
            Add(theme, RoleString, syntax.String);
            Add(theme, RoleProperty, syntax.Property);
            Add(theme, RoleKeyword, syntax.Keyword);
            Add(theme, RoleNumber, syntax.Number);
            Add(theme, RoleVariable, syntax.Variable);
            Add(theme, RoleDelimiter, syntax.Delimiter);
        }
        return result;

        void Add(AppTheme theme, string role, string hex)
            => result[(theme, role)] = new ImmutableSolidColorBrush(Color.Parse(hex));
    }
}
