using ShellX.Desktop.Controls;

namespace ShellX.Desktop.Tests;
public sealed class TerminalHighlighterTests
{
    [Theory]
    [InlineData("ERROR permission denied", "244;135;113")]
    [InlineData("连接成功 192.168.1.2:22", "78;201;176")]
    [InlineData("配置 /etc/nginx/nginx.conf", "79;193;255")]
    [InlineData("root@server:~#", "78;201;176")]
    public void PlainOutputReceivesSemanticColors(string value, string color)
    {
        var result = new TerminalHighlighter().Transform(value);
        Assert.Contains("\x1b[38;2;" + color + "m", result);
    }
    [Fact]
    public void ExistingForegroundColorsAndControlStringsArePreserved()
    {
        var highlighter = new TerminalHighlighter();
        const string colored = "\x1b[31mERROR /etc/config\x1b[0m";
        Assert.Equal(colored, highlighter.Transform(colored));
        const string trueColor = "\x1b[38;2;0;255;0mconnected 192.168.1.2\x1b[39m";
        Assert.Equal(trueColor, highlighter.Transform(trueColor));
        const string osc = "\x1b]8;;https://example.com/error\x1b\\";
        Assert.Equal(osc, highlighter.Transform(osc));
    }
    [Fact]
    public void SplitEscapeSequencesAndAlternateScreenRemainIntact()
    {
        var highlighter = new TerminalHighlighter();
        Assert.Equal("", highlighter.Transform("\x1b[?10"));
        Assert.Equal("\x1b[?1049h", highlighter.Transform("49h"));
        const string fullScreen = "\x1b[2J\x1b[1;1HERROR /etc/config 192.168.1.2";
        Assert.Equal(fullScreen, highlighter.Transform(fullScreen));
        Assert.Equal("\x1b[?1049l", highlighter.Transform("\x1b[?1049l"));
        Assert.Contains("\x1b[38;2;", highlighter.Transform("ERROR /etc/config"));
    }
    [Fact]
    public void HighlightCanBeDisabledWithoutRemovingAnsiSupport()
    {
        var highlighter = new TerminalHighlighter { Enabled = false };
        const string plain = "ERROR /etc/config 192.168.1.2\r\n\x1b[32mcolored\x1b[0m";
        Assert.Equal(plain, highlighter.Transform(plain));
    }
}
