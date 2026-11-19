using System.Text;
using System.Text.RegularExpressions;
using ShellX.Core.Settings;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Styles;

namespace ShellX.Desktop.Tests;

/// <summary>
/// 对照测试：手写扫描器必须和原来的正则实现给出完全一样的着色结果。
/// 参照实现是改动前的原版逻辑（含跨调用的解析状态），用来锁住行为不漂移。
/// </summary>
public sealed partial class TerminalHighlighterParityTests
{
    [Theory]
    [InlineData("ERROR permission denied")]
    [InlineData("连接成功 192.168.1.2:22")]
    [InlineData("配置 /etc/nginx/nginx.conf")]
    [InlineData("root@server:~#")]
    [InlineData("warning: disk 90% used")]
    [InlineData("build succeeded in 12.3s")]
    [InlineData("visit https://example.com/a/b?c=d now")]
    [InlineData("http://127.0.0.1:8080/health")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    [InlineData("failed to open ./config.yaml")]
    [InlineData("../parent/path.txt")]
    [InlineData("~/notes/readme.md")]
    [InlineData("/absolute/path")]
    [InlineData("relative/path/without/leading/slash")]
    [InlineData("999.999.999.999")]
    [InlineData("10.0.0.1:22")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.3.4567")]
    [InlineData("xhttps://example.com")]
    [InlineData("re_error and errors and error_code")]
    [InlineData("err.or@host")]
    [InlineData("error@host")]
    [InlineData("done@host")]
    [InlineData("已完成 操作")]
    [InlineData("失败，拒绝访问")]
    [InlineData("警告：磁盘快满了")]
    [InlineData("错误error")]
    [InlineData("a/b/c")]
    [InlineData("/.hidden")]
    [InlineData("..")]
    [InlineData("~")]
    [InlineData("http://")]
    [InlineData("https://")]
    [InlineData("no match at all")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\tsep\tvalue")]
    [InlineData("\x1b[31mERROR /etc/config\x1b[0m")]
    [InlineData("\x1b]8;;https://example.com/error\x1b\\")]
    [InlineData("\x1b[2J\x1b[1;1HERROR /etc/config 192.168.1.2")]
    public void ScannerMatchesTheOriginalImplementation(string value)
    {
        var expected = new LegacyHighlighter().Transform(value);
        Assert.Equal(expected, new TerminalHighlighter().Transform(value));
    }

    [Fact]
    public void ScannerMatchesTheOriginalImplementationOnRandomInput()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789./~:@-_ \t错误失败拒绝警告成功已完成http";
        var random = new Random(20241119);
        for (var round = 0; round < 4000; round++)
        {
            var length = random.Next(0, 60);
            var text = new char[length];
            for (var i = 0; i < length; i++) text[i] = alphabet[random.Next(alphabet.Length)];
            var value = new string(text);
            Assert.Equal(new LegacyHighlighter().Transform(value), new TerminalHighlighter().Transform(value));
        }
    }

    [Fact]
    public void ScannerMatchesTheOriginalImplementationOnRealisticLogLines()
    {
        var lines = new[]
        {
            "2026-11-19 10:22:31 INFO  [worker-3] build ok in /home/dev/project/dist/main.js",
            "2026-11-19 10:22:32 ERROR failed to connect to 10.12.0.5:5432 after 3 retries",
            "2026-11-19 10:22:33 WARN  disk usage 91% on /dev/sda1, see https://wiki.example.com/disk",
            "2026-11-19 10:22:34 INFO  user root@10.12.0.5 logged in, session started",
            "Traceback (most recent call last):\n  File \"/app/main.py\", line 42, in <module>",
            "总计 15 个文件，共 1.2G，可用空间 30G",
            "npm WARN deprecated request@2.88.2: request has been deprecated",
            "E: Unable to locate package abc-def",
            "127.0.0.1 - - [19/Nov/2026:10:22:35 +0800] \"GET /api/v1 HTTP/1.1\" 200 1234",
            "error: the requested URL returned error: 404 Not Found",
            "  10.0.0.1      255.255.255.0  eth0",
        };
        foreach (var line in lines)
            Assert.Equal(new LegacyHighlighter().Transform(line), new TerminalHighlighter().Transform(line));
    }

    [Fact]
    public void ScannerKeepsStateAcrossChunksLikeTheOriginal()
    {
        var expected = new LegacyHighlighter();
        var actual = new TerminalHighlighter();
        var chunks = new[] { "\x1b[?10", "49", "hERROR /etc/", "config 192.168.1.2\r\n", "\x1b[0m" };
        foreach (var chunk in chunks) Assert.Equal(expected.Transform(chunk), actual.Transform(chunk));
    }

    /// <summary>改动前的实现：正则着色 + 同样的解析状态机，用作对照基准。</summary>
    private sealed partial class LegacyHighlighter
    {
        private enum ParseState { Text, Escape, Csi, String, StringEscape }
        private ParseState _state;
        private readonly StringBuilder _sequence = new();
        private bool _foregroundSet;
        private bool _alternateScreen;
        private bool _savedForeground;

        [GeneratedRegex(@"(?<error>\b(?:error|failed|failure|fatal|denied|exception)\b|错误|失败|拒绝)|(?<warning>\b(?:warning|warn)\b|警告)|(?<success>\b(?:success(?:ful(?:ly)?)?|passed|connected|done)\b|成功|已完成)|(?<url>https?://[^\s<>]+)|(?<ip>\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b)|(?<path>(?<![\w/])(?:~?/|\.{1,2}/)[\w.\-/~]+)|(?<host>[\w.\-]+@[\w.\-]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex Tokens();

        public string Transform(string input)
        {
            var output = new StringBuilder(input.Length + 64); var text = new StringBuilder();
            void Flush()
            {
                if (text.Length == 0) return;
                var value = text.ToString(); text.Clear();
                output.Append(!_foregroundSet && !_alternateScreen ? Tokens().Replace(value, Color) : value);
            }
            foreach (var c in input)
            {
                switch (_state)
                {
                    case ParseState.Text:
                        if (c == '\x1b') { Flush(); _sequence.Append(c); _state = ParseState.Escape; }
                        else text.Append(c);
                        break;
                    case ParseState.Escape:
                        _sequence.Append(c);
                        if (c == '[') _state = ParseState.Csi;
                        else if (c is ']' or 'P' or '_' or '^') { output.Append(_sequence); _sequence.Clear(); _state = ParseState.String; }
                        else { output.Append(_sequence); _sequence.Clear(); _state = ParseState.Text; }
                        break;
                    case ParseState.Csi:
                        _sequence.Append(c);
                        if (c is >= '@' and <= '~')
                        {
                            var sequence = _sequence.ToString(); Track(sequence); output.Append(sequence); _sequence.Clear(); _state = ParseState.Text;
                        }
                        else if (_sequence.Length > 256) { output.Append(_sequence); _sequence.Clear(); _state = ParseState.Text; }
                        break;
                    case ParseState.String:
                        output.Append(c);
                        if (c == '\a') _state = ParseState.Text;
                        else if (c == '\x1b') _state = ParseState.StringEscape;
                        break;
                    case ParseState.StringEscape:
                        output.Append(c); _state = c == '\\' ? ParseState.Text : c == '\x1b' ? ParseState.StringEscape : ParseState.String;
                        break;
                }
            }
            Flush(); return output.ToString();
        }

        private string Color(Match match)
        {
            var palette = ThemeCatalog.Of(AppTheme.Dark).Highlight;
            string rgb;
            if (match.Groups["error"].Success) rgb = palette.Error;
            else if (match.Groups["warning"].Success) rgb = palette.Warning;
            else if (match.Groups["success"].Success || match.Groups["host"].Success) rgb = palette.Success;
            else if (match.Groups["ip"].Success)
            {
                if (!System.Net.IPAddress.TryParse(match.Value.Split(':')[0], out _)) return match.Value;
                rgb = palette.Ip;
            }
            else rgb = palette.Link;
            return $"\x1b[38;2;{rgb}m{match.Value}\x1b[39m";
        }

        private void Track(string sequence)
        {
            var parameters = sequence[2..^1]; var final = sequence[^1];
            if (parameters.StartsWith('?') && parameters[1..].Split(';').Any(mode => mode is "1049" or "1047" or "47") && final is 'h' or 'l')
            {
                if (final == 'h' && !_alternateScreen) _savedForeground = _foregroundSet;
                if (final == 'l' && _alternateScreen) _foregroundSet = _savedForeground;
                _alternateScreen = final == 'h';
            }
            if (final != 'm') return;
            var fields = parameters.Split(';');
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (!int.TryParse(field.Split(':')[0], out var code)) code = 0;
                if (code is 0 or 39) _foregroundSet = false;
                else if (code is >= 30 and <= 37 or >= 90 and <= 97) _foregroundSet = true;
                else if (code is 38 or 48 or 58)
                {
                    if (code == 38) _foregroundSet = true;
                    if (!field.Contains(':') && i + 1 < fields.Length) i += fields[i + 1] == "2" ? 4 : fields[i + 1] == "5" ? 2 : 0;
                }
            }
        }
    }
}
