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
    public void StreamKeepsAnsiSequencesIntactWhenReadsSplitMidSequence()
    {
        // 真实提示符：ESC[0;27;0;32;1m 是 bash PS1 的颜色序列，被拆散就会漏出 "0;27;0;32;1" 这种裸参数。
        const string prompt = "\x1b[0;27;0;32;1m[defu@izuf6b0ooqp0ht8h7ujteyz ~]$ ";
        foreach (var chunk in new[] { 1, 2, 3, 5, 8 })
        {
            var source = new ChunkedStream(System.Text.Encoding.UTF8.GetBytes(prompt), chunk);
            var stream = new HighlightedTerminalStream(source, new TerminalHighlighter());
            using var output = new MemoryStream();
            var buffer = new byte[64];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
            var text = System.Text.Encoding.UTF8.GetString(output.ToArray());
            Assert.Contains("\x1b[0;27;0;32;1m", text);              // 颜色序列完整保留
            Assert.DoesNotContain("0;27;0;32;1m[defu", text.Replace("\x1b[0;27;0;32;1m", ""));
        }
    }

    private sealed class ChunkedStream(byte[] data, int chunk) : Stream
    {
        private int _position;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= data.Length) return 0;
            var take = Math.Min(Math.Min(count, chunk), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, take); _position += take; return take;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    [Fact]
    public void HighlightCanBeDisabledWithoutRemovingAnsiSupport()
    {
        var highlighter = new TerminalHighlighter { Enabled = false };
        const string plain = "ERROR /etc/config 192.168.1.2\r\n\x1b[32mcolored\x1b[0m";
        Assert.Equal(plain, highlighter.Transform(plain));
    }
}
