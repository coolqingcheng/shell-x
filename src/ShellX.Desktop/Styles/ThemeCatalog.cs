using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using ShellX.Core.Settings;
using XTerm.Options;

namespace ShellX.Desktop.Styles;

/// <summary>自定义主题变体。继承 Dark/Light，某个键没单独定义时自动回退到对应变体。</summary>
public static class ThemeVariants
{
    /// <summary>按主题生成变体：暗色主题继承 Dark，亮色主题继承 Light。</summary>
    public static ThemeVariant Create(string name, bool isDark)
        => new(name, isDark ? ThemeVariant.Dark : ThemeVariant.Light);
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

    /// <summary>全部可选主题，顺序即设置页下拉框与快速切换菜单的顺序。</summary>
    public static IReadOnlyList<AppTheme> All { get; } =
    [
        AppTheme.Dark, AppTheme.Light, AppTheme.OneDark, AppTheme.Dracula, AppTheme.SolarizedLight,
        AppTheme.Monokai, AppTheme.Nord, AppTheme.TokyoNight, AppTheme.CatppuccinMocha, AppTheme.GruvboxDark,
        AppTheme.OneLight, AppTheme.GitHubDark, AppTheme.GitHubLight, AppTheme.VitesseDark
    ];

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

    // VS Code 经典 Monokai 暗色主题。
    private static readonly ThemePalette Monokai = new(
        "Monokai", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#272822", [PanelBackground] = "#1E1F1C", [BorderColor] = "#3E3D32", [ForegroundBrush] = "#F8F8F2",
            [EditorForeground] = "#F8F8F2", [RaisedBackground] = "#3E3D32", [InputBackground] = "#1E1F1C", [HeaderBackground] = "#1E1F1C",
            [HoverBackground] = "#3A3A32", [SelectionBackground] = "#49483E", [MutedForeground] = "#75715E", [SecondaryForeground] = "#A6A28C",
            [IconForeground] = "#CFCFC2", [PlaceholderForeground] = "#75715E", [FaintForeground] = "#5A594F", [AccentForeground] = "#F92672",
            [LinkForeground] = "#66D9EF", [FolderForeground] = "#E6DB74", [CommandForeground] = "#E6DB74", [SuccessForeground] = "#A6E22E",
            [ErrorForeground] = "#F92672", [WarningForeground] = "#E6DB74", [WarningBackground] = "#3B3A32", [SuccessBackground] = "#2C3B22",
            [TreeBackground] = "#1E1F1C", [ChartBackground] = "#1E1F1C", [ChartGrid] = "#3E3D32", [PurpleForeground] = "#AE81FF",
            [OrangeForeground] = "#FD971F", [CommentForeground] = "#75715E",
        },
        new TerminalPalette(
            "#F8F8F2", "#272822", "#F8F8F0", "#49483E",
            "#333333", "#F92672", "#A6E22E", "#E6DB74",
            "#66D9EF", "#AE81FF", "#A1EFE4", "#F8F8F2",
            "#75715E", "#F92672", "#A6E22E", "#E6DB74", "#66D9EF", "#AE81FF", "#A1EFE4", "#F9F8F5"),
        new SyntaxPalette("#75715E", "#E6DB74", "#F92672", "#F92672", "#AE81FF", "#FD971F", "#F8F8F2"),
        new HighlightPalette("249;38;114", "230;219;116", "166;226;46", "102;217;239", "249;38;114"));

    // 冷色调 Nord 暗色主题。
    private static readonly ThemePalette Nord = new(
        "Nord", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#2E3440", [PanelBackground] = "#292E39", [BorderColor] = "#3B4252", [ForegroundBrush] = "#D8DEE9",
            [EditorForeground] = "#D8DEE9", [RaisedBackground] = "#3B4252", [InputBackground] = "#292E39", [HeaderBackground] = "#292E39",
            [HoverBackground] = "#3B4252", [SelectionBackground] = "#434C5E", [MutedForeground] = "#616E88", [SecondaryForeground] = "#81A1C1",
            [IconForeground] = "#D8DEE9", [PlaceholderForeground] = "#616E88", [FaintForeground] = "#4C566A", [AccentForeground] = "#88C0D0",
            [LinkForeground] = "#88C0D0", [FolderForeground] = "#88C0D0", [CommandForeground] = "#81A1C1", [SuccessForeground] = "#A3BE8C",
            [ErrorForeground] = "#BF616A", [WarningForeground] = "#EBCB8B", [WarningBackground] = "#43402F", [SuccessBackground] = "#30392F",
            [TreeBackground] = "#292E39", [ChartBackground] = "#292E39", [ChartGrid] = "#3B4252", [PurpleForeground] = "#B48EAD",
            [OrangeForeground] = "#D08770", [CommentForeground] = "#616E88",
        },
        new TerminalPalette(
            "#D8DEE9", "#2E3440", "#D8DEE9", "#434C5E",
            "#3B4252", "#BF616A", "#A3BE8C", "#EBCB8B",
            "#81A1C1", "#B48EAD", "#88C0D0", "#E5E9F0",
            "#4C566A", "#BF616A", "#A3BE8C", "#EBCB8B", "#81A1C1", "#B48EAD", "#8FBCBB", "#ECEFF4"),
        new SyntaxPalette("#616E88", "#A3BE8C", "#8FBCBB", "#81A1C1", "#B48EAD", "#D8DEE9", "#ECEFF4"),
        new HighlightPalette("191;97;106", "235;203;139", "163;190;140", "136;192;208", "136;192;208"));

    // 蓝紫色调 Tokyo Night 暗色主题。
    private static readonly ThemePalette TokyoNight = new(
        "Tokyo Night", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#1A1B26", [PanelBackground] = "#16161E", [BorderColor] = "#2A2C41", [ForegroundBrush] = "#C0CAF5",
            [EditorForeground] = "#C0CAF5", [RaisedBackground] = "#292E42", [InputBackground] = "#16161E", [HeaderBackground] = "#16161E",
            [HoverBackground] = "#292E42", [SelectionBackground] = "#283457", [MutedForeground] = "#565F89", [SecondaryForeground] = "#7982A9",
            [IconForeground] = "#A9B1D6", [PlaceholderForeground] = "#565F89", [FaintForeground] = "#414868", [AccentForeground] = "#7AA2F7",
            [LinkForeground] = "#7DCFFF", [FolderForeground] = "#7AA2F7", [CommandForeground] = "#BB9AF7", [SuccessForeground] = "#9ECE6A",
            [ErrorForeground] = "#F7768E", [WarningForeground] = "#E0AF68", [WarningBackground] = "#3D2B1E", [SuccessBackground] = "#1F3324",
            [TreeBackground] = "#16161E", [ChartBackground] = "#16161E", [ChartGrid] = "#292E42", [PurpleForeground] = "#BB9AF7",
            [OrangeForeground] = "#FF9E64", [CommentForeground] = "#565F89",
        },
        new TerminalPalette(
            "#C0CAF5", "#1A1B26", "#C0CAF5", "#33467C",
            "#15161E", "#F7768E", "#9ECE6A", "#E0AF68",
            "#7AA2F7", "#BB9AF7", "#7DCFFF", "#A9B1D6",
            "#414868", "#F7768E", "#9ECE6A", "#E0AF68", "#7AA2F7", "#BB9AF7", "#7DCFFF", "#C0CAF5"),
        new SyntaxPalette("#565F89", "#9ECE6A", "#7DCFFF", "#BB9AF7", "#FF9E64", "#C0CAF5", "#89DDFF"),
        new HighlightPalette("247;118;142", "224;175;104", "158;206;106", "125;207;255", "122;162;247"));

    // 柔和粉紫调 Catppuccin Mocha 暗色主题。
    private static readonly ThemePalette CatppuccinMocha = new(
        "Catppuccin Mocha", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#1E1E2E", [PanelBackground] = "#181825", [BorderColor] = "#313244", [ForegroundBrush] = "#CDD6F4",
            [EditorForeground] = "#CDD6F4", [RaisedBackground] = "#45475A", [InputBackground] = "#181825", [HeaderBackground] = "#181825",
            [HoverBackground] = "#313244", [SelectionBackground] = "#45475A", [MutedForeground] = "#6C7086", [SecondaryForeground] = "#A6ADC8",
            [IconForeground] = "#A6ADC8", [PlaceholderForeground] = "#6C7086", [FaintForeground] = "#45475A", [AccentForeground] = "#89B4FA",
            [LinkForeground] = "#89DCEB", [FolderForeground] = "#89B4FA", [CommandForeground] = "#CBA6F7", [SuccessForeground] = "#A6E3A1",
            [ErrorForeground] = "#F38BA8", [WarningForeground] = "#F9E2AF", [WarningBackground] = "#45333C", [SuccessBackground] = "#263A2F",
            [TreeBackground] = "#181825", [ChartBackground] = "#181825", [ChartGrid] = "#313244", [PurpleForeground] = "#CBA6F7",
            [OrangeForeground] = "#FAB387", [CommentForeground] = "#6C7086",
        },
        new TerminalPalette(
            "#CDD6F4", "#1E1E2E", "#F5E0DC", "#585B70",
            "#45475A", "#F38BA8", "#A6E3A1", "#F9E2AF",
            "#89B4FA", "#F5C2E7", "#94E2D5", "#BAC2DE",
            "#585B70", "#F38BA8", "#A6E3A1", "#F9E2AF", "#89B4FA", "#F5C2E7", "#94E2D5", "#A6ADC8"),
        new SyntaxPalette("#6C7086", "#A6E3A1", "#89DCEB", "#CBA6F7", "#FAB387", "#F5C2E7", "#89B4FA"),
        new HighlightPalette("243;139;168", "249;226;175", "166;227;161", "137;220;235", "137;180;250"));

    // 复古暖褐调 Gruvbox Dark 主题。
    private static readonly ThemePalette GruvboxDark = new(
        "Gruvbox Dark", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#282828", [PanelBackground] = "#1D2021", [BorderColor] = "#3C3836", [ForegroundBrush] = "#EBDBB2",
            [EditorForeground] = "#EBDBB2", [RaisedBackground] = "#504945", [InputBackground] = "#1D2021", [HeaderBackground] = "#1D2021",
            [HoverBackground] = "#3C3836", [SelectionBackground] = "#504945", [MutedForeground] = "#928374", [SecondaryForeground] = "#BDAE93",
            [IconForeground] = "#A89984", [PlaceholderForeground] = "#928374", [FaintForeground] = "#7C6F64", [AccentForeground] = "#FE8019",
            [LinkForeground] = "#83A598", [FolderForeground] = "#FABD2F", [CommandForeground] = "#FABD2F", [SuccessForeground] = "#B8BB26",
            [ErrorForeground] = "#FB4934", [WarningForeground] = "#FABD2F", [WarningBackground] = "#3B2B1F", [SuccessBackground] = "#2C3116",
            [TreeBackground] = "#1D2021", [ChartBackground] = "#1D2021", [ChartGrid] = "#3C3836", [PurpleForeground] = "#D3869B",
            [OrangeForeground] = "#FE8019", [CommentForeground] = "#928374",
        },
        new TerminalPalette(
            "#EBDBB2", "#282828", "#FBF1C7", "#504945",
            "#282828", "#CC241D", "#98971A", "#D79921",
            "#458588", "#B16286", "#689D6A", "#A89984",
            "#928374", "#FB4934", "#B8BB26", "#FABD2F", "#83A598", "#D3869B", "#8EC07C", "#EBDBB2"),
        new SyntaxPalette("#928374", "#B8BB26", "#FE8019", "#FB4934", "#D3869B", "#83A598", "#EBDBB2"),
        new HighlightPalette("251;73;52", "250;189;47", "184;187;38", "131;165;152", "254;128;25"));

    // 清爽白底 One Light 亮色主题。
    private static readonly ThemePalette OneLight = new(
        "One Light", false,
        new Dictionary<string, string>
        {
            [AppBackground] = "#FAFAFA", [PanelBackground] = "#EAEAEA", [BorderColor] = "#D0D0D0", [ForegroundBrush] = "#383A42",
            [EditorForeground] = "#383A42", [RaisedBackground] = "#DCDCDC", [InputBackground] = "#FFFFFF", [HeaderBackground] = "#EAEAEA",
            [HoverBackground] = "#E3E3E3", [SelectionBackground] = "#D0DCF0", [MutedForeground] = "#9D9D9F", [SecondaryForeground] = "#696969",
            [IconForeground] = "#5F5F5F", [PlaceholderForeground] = "#A0A0A0", [FaintForeground] = "#B5B5B5", [AccentForeground] = "#526FFF",
            [LinkForeground] = "#4078F2", [FolderForeground] = "#C18401", [CommandForeground] = "#986801", [SuccessForeground] = "#50A14F",
            [ErrorForeground] = "#CA1243", [WarningForeground] = "#986801", [WarningBackground] = "#FBEBC8", [SuccessBackground] = "#E6F4DE",
            [TreeBackground] = "#F3F3F3", [ChartBackground] = "#FAFAFA", [ChartGrid] = "#E0E0E0", [PurpleForeground] = "#A626A4",
            [OrangeForeground] = "#986801", [CommentForeground] = "#A0A0A0",
        },
        new TerminalPalette(
            "#383A42", "#FAFAFA", "#383A42", "#C9D0D9",
            "#383A42", "#CA1243", "#50A14F", "#986801",
            "#0184BC", "#A626A4", "#0997B3", "#383A42",
            "#696969", "#CA1243", "#50A14F", "#986801", "#0184BC", "#A626A4", "#0997B3", "#696969"),
        new SyntaxPalette("#A0A0A0", "#50A14F", "#986801", "#CA1243", "#986801", "#4078F2", "#383A42"),
        new HighlightPalette("202;18;67", "152;104;1", "80;161;79", "64;120;242", "82;111;255"));

    // GitHub 官方暗色主题。
    private static readonly ThemePalette GitHubDark = new(
        "GitHub Dark", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#0D1117", [PanelBackground] = "#161B22", [BorderColor] = "#30363D", [ForegroundBrush] = "#C9D1D9",
            [EditorForeground] = "#C9D1D9", [RaisedBackground] = "#21262D", [InputBackground] = "#0D1117", [HeaderBackground] = "#161B22",
            [HoverBackground] = "#21262D", [SelectionBackground] = "#264F78", [MutedForeground] = "#8B949E", [SecondaryForeground] = "#B1BAC4",
            [IconForeground] = "#8B949E", [PlaceholderForeground] = "#6E7681", [FaintForeground] = "#484F58", [AccentForeground] = "#58A6FF",
            [LinkForeground] = "#58A6FF", [FolderForeground] = "#D29922", [CommandForeground] = "#D2A8FF", [SuccessForeground] = "#3FB950",
            [ErrorForeground] = "#F85149", [WarningForeground] = "#D29922", [WarningBackground] = "#2D2416", [SuccessBackground] = "#12261E",
            [TreeBackground] = "#0D1117", [ChartBackground] = "#161B22", [ChartGrid] = "#30363D", [PurpleForeground] = "#D2A8FF",
            [OrangeForeground] = "#FFA657", [CommentForeground] = "#8B949E",
        },
        new TerminalPalette(
            "#C9D1D9", "#0D1117", "#C9D1D9", "#264F78",
            "#484F58", "#FF7B72", "#3FB950", "#D29922",
            "#58A6FF", "#BC8CFF", "#39C5CF", "#B1BAC4",
            "#6E7681", "#FFA198", "#56D364", "#E3B341", "#79C0FF", "#D2A8FF", "#56D4DD", "#F0F6FC"),
        new SyntaxPalette("#8B949E", "#A5D6FF", "#79C0FF", "#FF7B72", "#79C0FF", "#FFA657", "#C9D1D9"),
        new HighlightPalette("248;81;73", "210;153;34", "63;185;80", "88;166;255", "88;166;255"));

    // GitHub 官方亮色主题。
    private static readonly ThemePalette GitHubLight = new(
        "GitHub Light", false,
        new Dictionary<string, string>
        {
            [AppBackground] = "#FFFFFF", [PanelBackground] = "#F6F8FA", [BorderColor] = "#D0D7DE", [ForegroundBrush] = "#24292F",
            [EditorForeground] = "#24292F", [RaisedBackground] = "#EAEEF2", [InputBackground] = "#FFFFFF", [HeaderBackground] = "#F6F8FA",
            [HoverBackground] = "#EFF2F5", [SelectionBackground] = "#B6D7FF", [MutedForeground] = "#57606A", [SecondaryForeground] = "#6E7781",
            [IconForeground] = "#57606A", [PlaceholderForeground] = "#8C959F", [FaintForeground] = "#AFB8C1", [AccentForeground] = "#0969DA",
            [LinkForeground] = "#0969DA", [FolderForeground] = "#BC4C00", [CommandForeground] = "#953800", [SuccessForeground] = "#1A7F37",
            [ErrorForeground] = "#CF222E", [WarningForeground] = "#9A6700", [WarningBackground] = "#FFF8C5", [SuccessBackground] = "#DAFBE1",
            [TreeBackground] = "#F6F8FA", [ChartBackground] = "#FFFFFF", [ChartGrid] = "#EAEEF2", [PurpleForeground] = "#8250DF",
            [OrangeForeground] = "#953800", [CommentForeground] = "#6E7781",
        },
        new TerminalPalette(
            "#24292F", "#FFFFFF", "#24292F", "#B6D7FF",
            "#24292F", "#CF222E", "#116329", "#4D2D00",
            "#0550AE", "#8250DF", "#1B7C83", "#6E7781",
            "#57606A", "#A40E26", "#1A7F37", "#633C01", "#218BFF", "#A475F9", "#3192AA", "#8C959F"),
        new SyntaxPalette("#6E7781", "#0A3069", "#0550AE", "#CF222E", "#0550AE", "#953800", "#24292F"),
        new HighlightPalette("207;34;46", "154;103;0", "26;127;55", "9;105;218", "9;105;218"));

    // 低饱和暖灰 Vitesse Dark 主题。
    private static readonly ThemePalette VitesseDark = new(
        "Vitesse Dark", true,
        new Dictionary<string, string>
        {
            [AppBackground] = "#121212", [PanelBackground] = "#181818", [BorderColor] = "#2E2E2E", [ForegroundBrush] = "#DBD7CA",
            [EditorForeground] = "#DBD7CA", [RaisedBackground] = "#2E2E2E", [InputBackground] = "#1A1A1A", [HeaderBackground] = "#181818",
            [HoverBackground] = "#262626", [SelectionBackground] = "#2E3F4E", [MutedForeground] = "#7E6E64", [SecondaryForeground] = "#91827B",
            [IconForeground] = "#A3A3A3", [PlaceholderForeground] = "#7E6E64", [FaintForeground] = "#56534E", [AccentForeground] = "#4D9375",
            [LinkForeground] = "#4D9375", [FolderForeground] = "#E6C07B", [CommandForeground] = "#E6C07B", [SuccessForeground] = "#8FBC8F",
            [ErrorForeground] = "#D16D6D", [WarningForeground] = "#E6C07B", [WarningBackground] = "#3A2F1F", [SuccessBackground] = "#24301F",
            [TreeBackground] = "#181818", [ChartBackground] = "#181818", [ChartGrid] = "#2E2E2E", [PurpleForeground] = "#A6ACCD",
            [OrangeForeground] = "#D19A66", [CommentForeground] = "#7E6E64",
        },
        new TerminalPalette(
            "#DBD7CA", "#121212", "#DBD7CA", "#2E3F4E",
            "#121212", "#BD465B", "#8FBC8F", "#E6C07B",
            "#6A9FB5", "#A37BAB", "#5FB3B3", "#DBD7CA",
            "#4E4E4E", "#D16D6D", "#A6C97F", "#F0D399", "#85B6D0", "#C39AC0", "#7FD1D1", "#EDEAE2"),
        new SyntaxPalette("#7E6E64", "#A1B187", "#E6C07B", "#CB7676", "#B3AE60", "#DBD7CA", "#A3A3A3"),
        new HighlightPalette("209;109;109", "230;192;123", "143;188;143", "77;147;117", "77;147;117"));


    /// <summary>主题与配色的对应表。</summary>
    private static readonly Dictionary<AppTheme, ThemePalette> Palettes = new()
    {
        [AppTheme.Dark] = Dark,
        [AppTheme.Light] = Light,
        [AppTheme.OneDark] = OneDark,
        [AppTheme.Dracula] = Dracula,
        [AppTheme.SolarizedLight] = SolarizedLight,
        [AppTheme.Monokai] = Monokai,
        [AppTheme.Nord] = Nord,
        [AppTheme.TokyoNight] = TokyoNight,
        [AppTheme.CatppuccinMocha] = CatppuccinMocha,
        [AppTheme.GruvboxDark] = GruvboxDark,
        [AppTheme.OneLight] = OneLight,
        [AppTheme.GitHubDark] = GitHubDark,
        [AppTheme.GitHubLight] = GitHubLight,
        [AppTheme.VitesseDark] = VitesseDark
    };

    /// <summary>自定义主题的变体缓存，同一主题始终使用同一个实例。</summary>
    private static readonly Dictionary<AppTheme, ThemeVariant> CustomVariants = BuildCustomVariants();

    private static Dictionary<AppTheme, ThemeVariant> BuildCustomVariants()
    {
        var result = new Dictionary<AppTheme, ThemeVariant>();
        foreach (var theme in All)
        {
            if (theme is AppTheme.Dark or AppTheme.Light) continue;
            result[theme] = ThemeVariants.Create(theme.ToString(), Of(theme).IsDark);
        }
        return result;
    }

    public static ThemePalette Of(AppTheme theme) => Palettes.TryGetValue(theme, out var palette) ? palette : Dark;

    /// <summary>主题对应的 Avalonia 变体；自定义变体继承 Dark/Light。</summary>
    public static ThemeVariant Variant(AppTheme theme) => theme switch
    {
        AppTheme.Dark => ThemeVariant.Dark,
        AppTheme.Light => ThemeVariant.Light,
        _ => CustomVariants.TryGetValue(theme, out var variant) ? variant : ThemeVariant.Dark
    };

    private static readonly Dictionary<(AppTheme Theme, string Role), IBrush> SyntaxBrushes = BuildSyntaxBrushes();

    public static string DisplayName(AppTheme theme) => Of(theme).DisplayName;

    /// <summary>设置页下拉框顺序与枚举的对应关系。</summary>
    public static AppTheme FromIndex(int index) => index >= 0 && index < All.Count ? All[index] : AppTheme.Dark;

    public static int ToIndex(AppTheme theme)
    {
        for (var i = 0; i < All.Count; i++) if (All[i] == theme) return i;
        return 0;
    }

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
