using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ShellX.Core.Settings;
using ShellX.Desktop.Styles;

namespace ShellX.Desktop.Controls;

/// <summary>为无前景色的普通终端文本添加颜色；保留 ANSI 控制字符串及全屏程序输出。</summary>
public sealed partial class TerminalHighlighter
{
    private enum ParseState { Text, Escape, Csi, String, StringEscape }
    private ParseState _state;
    private readonly StringBuilder _sequence = new();
    private bool _foregroundSet;
    private bool _alternateScreen;
    private bool _savedForeground;
    /// <summary>当前主题，语义着色按主题取色。</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool Enabled { get; set; } = true;

    [GeneratedRegex(@"(?<error>\b(?:error|failed|failure|fatal|denied|exception)\b|错误|失败|拒绝)|(?<warning>\b(?:warning|warn)\b|警告)|(?<success>\b(?:success(?:ful(?:ly)?)?|passed|connected|done)\b|成功|已完成)|(?<url>https?://[^\s<>]+)|(?<ip>\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b)|(?<path>(?<![\w/])(?:~?/|\.{1,2}/)[\w.\-/~]+)|(?<host>[\w.\-]+@[\w.\-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();

    // 解析状态是共享的：重连时新旧读线程可能短暂并存，串行化避免状态被踩。
    private readonly object _gate = new();

    public string Transform(string input)
    {
        lock (_gate) return TransformCore(input);
    }

    private string TransformCore(string input)
    {
        var output = new StringBuilder(input.Length + 64); var text = new StringBuilder();
        void Flush()
        {
            if (text.Length == 0) return;
            var value = text.ToString(); text.Clear();
            output.Append(Enabled && !_foregroundSet && !_alternateScreen ? Tokens().Replace(value, Color) : value);
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

    private string Color(Match match)
    {
        var palette = ThemeCatalog.Of(Theme).Highlight;
        string rgb;
        if (match.Groups["error"].Success) rgb = palette.Error;
        else if (match.Groups["warning"].Success) rgb = palette.Warning;
        else if (match.Groups["success"].Success || match.Groups["host"].Success) rgb = palette.Success;
        else if (match.Groups["ip"].Success)
        {
            if (!IPAddress.TryParse(match.Value.Split(':')[0], out _)) return match.Value;
            rgb = palette.Ip;
        }
        else rgb = palette.Link;
        // 只恢复前景色，不重置服务器的加粗、下划线或背景色。
        return $"\x1b[38;2;{rgb}m{match.Value}\x1b[39m";
    }

    /// <summary>清空解析状态：重新连接时用，避免上一次连接末尾残留半个控制序列。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _state = ParseState.Text;
            _sequence.Clear();
            _foregroundSet = false;
            _alternateScreen = false;
            _savedForeground = false;
        }
    }
}

/// <summary>把原始字节流按 UTF-8 解码后交给高亮器，输出仍是带 ANSI 的字节流。</summary>
internal sealed class HighlightedTerminalStream(Stream input, TerminalHighlighter highlighter) : Stream
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly byte[] _inputBuffer = new byte[32 * 1024];
    private readonly char[] _characters = new char[32 * 1024 + 2];
    private byte[] _pending = [];
    private int _offset;
    private bool _eof;
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty) return 0;
        while (true)
        {
            // 高亮器可能把这一块整个吞掉（半个控制序列还没写完），这种情况不是结束，要继续读。
            if (_offset < _pending.Length)
            {
                var available = Math.Min(buffer.Length, _pending.Length - _offset);
                _pending.AsSpan(_offset, available).CopyTo(buffer.Span);
                _offset += available;
                return available;
            }
            if (_eof) return 0;
            cancellationToken.ThrowIfCancellationRequested();
            var length = await input.ReadAsync(_inputBuffer, cancellationToken).ConfigureAwait(false);
            if (length == 0) _eof = true;
            // 解码器跨块保存半个多字节字符，高亮器跨块保存半个控制序列，两者都不会把半个序列吐出去。
            var decoded = _decoder.GetChars(_inputBuffer, 0, length, _characters, 0, _eof);
            _pending = Encoding.UTF8.GetBytes(highlighter.Transform(new string(_characters, 0, decoded)));
            _offset = 0;
        }
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
