using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Styling;
using AvaloniaEdit.Rendering;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using ShellX.Desktop.Services;
using ShellX.Desktop.Styles;

namespace ShellX.Desktop.Features.FileEditor;

public sealed record EditorSyntax(string Name, IHighlightingDefinition? Highlighting);

public static class EditorSyntaxes
{
    private const string Quoted = @"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'";
    private static readonly HighlightingColor Comment = Color("Comment", ThemeCatalog.RoleComment);
    private static readonly HighlightingColor String = Color("String", ThemeCatalog.RoleString);
    private static readonly HighlightingColor Key = Color("Property", ThemeCatalog.RoleProperty);
    private static readonly HighlightingColor Keyword = Color("Keyword", ThemeCatalog.RoleKeyword);
    private static readonly HighlightingColor Number = Color("Number", ThemeCatalog.RoleNumber);
    private static readonly HighlightingColor Variable = Color("Variable", ThemeCatalog.RoleVariable);
    private static readonly HighlightingColor Delimiter = Color("Delimiter", ThemeCatalog.RoleDelimiter);

    public static IReadOnlyList<EditorSyntax> All { get; } = Create();
    public static EditorSyntax Detect(string path, string content)
    {
        var lower = path.Replace('\\', '/').ToLowerInvariant(); var extension = Path.GetExtension(lower);
        var text = content.AsSpan().TrimStart();
        var name = extension switch
        {
            ".json" or ".jsonc" or ".json5" => "JSON",
            ".xml" or ".xaml" or ".axaml" or ".csproj" or ".props" or ".targets" => "XML",
            ".yaml" or ".yml" => "YAML", ".ini" or ".env" or ".properties" => "INI",
            ".sh" or ".bash" or ".zsh" => "Shell", ".sql" => "SQL", _ => ""
        };
        if (name.Length == 0 && text.StartsWith("<")) name = "XML";
        if (name.Length == 0 && text.StartsWith("{")) name = "JSON";
        if (name.Length == 0 && (lower.Contains("/nginx/") || lower.EndsWith("nginx.conf") ||
            Regex.IsMatch(content, @"(?m)^[ \t]*(?:http|events|server|upstream|location|proxy_pass|worker_processes)\b", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100)))) name = "Nginx";
        if (name.Length == 0 && text.StartsWith("#!")) name = "Shell";
        return All.First(s => s.Name == (name.Length == 0 ? "纯文本" : name));
    }

    private static EditorSyntax[] Create()
    {
        var json = new Definition("JSON");
        Span(json.MainRuleSet, @"/\*", @"\*/", Comment);
        Rule(json.MainRuleSet, @"//.*$", Comment);
        Rule(json.MainRuleSet, @"""(?:\\.|[^""\\])*""(?=\s*:)", Key);
        Rule(json.MainRuleSet, @"""(?:\\.|[^""\\])*""", String);
        Numbers(json.MainRuleSet);
        Rule(json.MainRuleSet, @"\b(?:true|false|null)\b", Keyword);

        var xml = new Definition("XML"); var tag = new HighlightingRuleSet();
        Span(xml.MainRuleSet, "<!--", "-->", Comment);
        Span(xml.MainRuleSet, @"<!\[CDATA\[", @"\]\]>", String);
        Span(xml.MainRuleSet, @"<\?", @"\?>", Delimiter, tag);
        Span(xml.MainRuleSet, @"</?[\w:.-]+", "/?>", Delimiter, tag, Keyword);
        Span(tag, "\"", "\"", String); Span(tag, "'", "'", String);
        Rule(tag, @"(?<![\w:.-])[\w:.-]+(?=\s*=)", Key);
        Rule(xml.MainRuleSet, @"&(?:#\d+|#x[\da-fA-F]+|[\w:.-]+);", Variable);

        var nginx = new Definition("Nginx"); StringsAndComments(nginx.MainRuleSet);
        Rule(nginx.MainRuleSet, @"\$(?:\{[\w]+\}|[\w]+)", Variable);
        Rule(nginx.MainRuleSet, @"(?:^\s*|(?<=[{;])\s*)[A-Za-z_][\w-]*", Key);
        Numbers(nginx.MainRuleSet);
        Rule(nginx.MainRuleSet, @"\b(?:on|off|last|break|permanent|redirect)\b", Keyword);

        var ini = new Definition("INI"); StringsAndComments(ini.MainRuleSet, "[;#].*$");
        Rule(ini.MainRuleSet, @"^\s*\[[^\]]+\]", Variable);
        Rule(ini.MainRuleSet, @"^\s*[\w.-]+(?=\s*[=:])", Key); Numbers(ini.MainRuleSet);

        var yaml = new Definition("YAML"); StringsAndComments(yaml.MainRuleSet);
        Rule(yaml.MainRuleSet, @"(?<![\w.-])[\w.-]+(?=\s*:)", Key);
        Rule(yaml.MainRuleSet, @"\b(?:true|false|null|yes|no)\b", Keyword); Numbers(yaml.MainRuleSet);

        var shell = new Definition("Shell"); StringsAndComments(shell.MainRuleSet);
        Rule(shell.MainRuleSet, @"\$(?:\{[^}]+\}|[\w?#!@*]+)", Variable);
        Rule(shell.MainRuleSet, @"\b(?:if|then|else|elif|fi|for|while|do|done|case|esac|function|in|export|return|exit)\b", Keyword);
        Numbers(shell.MainRuleSet);

        var sql = new Definition("SQL"); Span(sql.MainRuleSet, @"/\*", @"\*/", Comment);
        StringsAndComments(sql.MainRuleSet, "--.*$");
        Rule(sql.MainRuleSet, @"(?i:\b(?:select|from|where|insert|into|update|delete|create|alter|drop|table|join|left|right|inner|outer|on|as|and|or|not|null|values|set|group|order|by|limit|having|distinct|primary|key|index|begin|commit|rollback)\b)", Keyword);
        Numbers(sql.MainRuleSet);
        return [new("纯文本", null), new("JSON", json), new("XML", xml), new("Nginx", nginx), new("INI", ini), new("YAML", yaml), new("Shell", shell), new("SQL", sql)];
    }
    private static void StringsAndComments(HighlightingRuleSet rules, string comment = "#.*$")
    { Rule(rules, Quoted, String); Rule(rules, comment, Comment); }
    private static void Numbers(HighlightingRuleSet rules) => Rule(rules, @"(?<![\w.])-?\b\d+(?:\.\d+)*(?:[eE][+-]?\d+)?[kKmMgGsShHdD]?\b", Number);
    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static void Rule(HighlightingRuleSet rules, string pattern, HighlightingColor color) => rules.Rules.Add(new() { Regex = Pattern(pattern), Color = color });
    private static void Span(HighlightingRuleSet rules, string start, string end, HighlightingColor color, HighlightingRuleSet? nested = null, HighlightingColor? startColor = null) =>
        rules.Spans.Add(new() { StartExpression = Pattern(start), EndExpression = Pattern(end), SpanColor = color, StartColor = startColor ?? color, EndColor = color,
            SpanColorIncludesStart = startColor == null, SpanColorIncludesEnd = true, RuleSet = nested ?? new() });
    private static HighlightingColor Color(string name, string role) => new() { Name = name, Foreground = new ThemedHighlightingBrush(role) };
    // 语法色按当前主题取：主题切换后画刷要跟着换，所以不缓存具体颜色。
    private sealed class ThemedHighlightingBrush(string role) : HighlightingBrush
    {
        public override IBrush GetBrush(ITextRunConstructionContext context)
            => ThemeCatalog.SyntaxBrush(ThemeService.Current, role);
    }
    private sealed class Definition(string name) : IHighlightingDefinition
    {
        public string Name => name;
        public HighlightingRuleSet MainRuleSet { get; } = new();
        public HighlightingRuleSet GetNamedRuleSet(string ruleName) => ruleName == Name ? MainRuleSet : null!;
        public HighlightingColor GetNamedColor(string colorName) => NamedHighlightingColors.FirstOrDefault(c => c.Name == colorName)!;
        public IEnumerable<HighlightingColor> NamedHighlightingColors => [Comment, String, Key, Keyword, Number, Variable, Delimiter];
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
    }
}
