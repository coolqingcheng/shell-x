using System.Net;
using System.Text;
using ShellX.Core.Settings;
using ShellX.Desktop.Styles;

namespace ShellX.Desktop.Controls;

/// <summary>为无前景色的普通终端文本添加颜色；保留 ANSI 控制字符串及全屏程序输出。</summary>
/// <remarks>
/// 语义着色自己扫描文本，不再用正则：发布版本是 Native AOT，正则只能解释执行，
/// 一屏日志就能吃掉一个核，产生的垃圾还会把堆撑大。扫描规则与原来的正则一一对应，
/// 由 TerminalHighlighterTests 里的对照用例保证行为一致。
/// </remarks>
public sealed class TerminalHighlighter
{
    private enum ParseState { Text, Escape, Csi, String, StringEscape }
    private enum Kind { None, Error, Warning, Success, Url, Ip, Path, Host }
    private ParseState _state;
    private readonly StringBuilder _sequence = new();
    private bool _foregroundSet;
    private bool _alternateScreen;
    private bool _savedForeground;
    /// <summary>当前主题，语义着色按主题取色。</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool Enabled { get; set; } = true;

    // 解析状态是共享的：重连时新旧读线程可能短暂并存，串行化避免状态被踩。
    private readonly object _gate = new();

    // 输出缓冲区长期复用：终端是持续的大流量，每次新建缓冲区只会让堆越来越大。
    private char[] _output = new char[8 * 1024];
    private int _length;
    private byte[] _bytes = new byte[64 * 1024];

    public string Transform(string input)
    {
        lock (_gate)
        {
            TransformCore(input.AsSpan());
            return new string(_output, 0, _length);
        }
    }

    /// <summary>直接产出 UTF-8 字节：终端流不必再经过中间字符串。</summary>
    /// <remarks>
    /// 返回的缓冲区由高亮器持有，下次变换时会被覆盖。调用方必须在下一次变换之前把内容取走，
    /// 中途不复制是可以的——终端流正是靠这一点省掉一次整块 memcpy。
    /// </remarks>
    internal (byte[] Buffer, int Length) TransformToUtf8(ReadOnlySpan<char> input)
    {
        lock (_gate)
        {
            TransformCore(input);
            var needed = Encoding.UTF8.GetMaxByteCount(_length);
            if (_bytes.Length < needed) _bytes = new byte[needed];
            var count = Encoding.UTF8.GetBytes(_output.AsSpan(0, _length), _bytes);
            return (_bytes, count);
        }
    }

    private void TransformCore(ReadOnlySpan<char> input)
    {
        _length = 0;
        var palette = ThemeCatalog.Of(Theme).Highlight;
        var plainStart = -1;                   // 当前纯文本段的起点，-1 表示没有待输出的纯文本
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            switch (_state)
            {
                case ParseState.Text:
                    if (c != '\x1b') { if (plainStart < 0) plainStart = i; i++; break; }
                    if (plainStart >= 0) { AppendPlain(input, plainStart, i, palette); plainStart = -1; }
                    _sequence.Append(c); _state = ParseState.Escape; i++;
                    break;
                case ParseState.Escape:
                    _sequence.Append(c); i++;
                    if (c == '[') _state = ParseState.Csi;
                    else if (c is ']' or 'P' or '_' or '^') { AppendSequence(); _state = ParseState.String; }
                    else { AppendSequence(); _state = ParseState.Text; }
                    break;
                case ParseState.Csi:
                    _sequence.Append(c); i++;
                    if (c is >= '@' and <= '~')
                    {
                        var sequence = _sequence.ToString(); Track(sequence); AppendSequence(); _state = ParseState.Text;
                    }
                    else if (_sequence.Length > 256) { AppendSequence(); _state = ParseState.Text; }
                    break;
                case ParseState.String:
                    Append(c); i++;
                    if (c == '\a') _state = ParseState.Text;
                    else if (c == '\x1b') _state = ParseState.StringEscape;
                    break;
                case ParseState.StringEscape:
                    Append(c); i++;
                    _state = c == '\\' ? ParseState.Text : c == '\x1b' ? ParseState.StringEscape : ParseState.String;
                    break;
            }
        }
        if (plainStart >= 0) AppendPlain(input, plainStart, input.Length, palette);
    }

    /// <summary>纯文本段：整段扫描出要着色的词，其余原样输出。</summary>
    private void AppendPlain(ReadOnlySpan<char> text, int start, int end, HighlightPalette palette)
    {
        if (end <= start) return;
        if (!Enabled || _foregroundSet || _alternateScreen) { Append(text[start..end]); return; }
        var copied = start;
        var i = start;
        var hostRunEnd = -1;      // 当前主机名这一段的终点
        var hostEnd = -1;         // 主机名整体的终点，-1 表示这一段后面没有 @
        while (i < end)
        {
            var c = text[i];
            var matchStart = -1;
            var matchEnd = i;
            var kind = Kind.None;
            var isWord = IsWordChar(c);
            // 顺序和原来的正则一致：关键字 → 中文 → 地址 → IP → 路径 → 主机名。
            var wordStart = isWord && (i == start || !IsWordChar(text[i - 1]));
            if (wordStart)
            {
                var wordEnd = i;
                while (wordEnd < end && IsWordChar(text[wordEnd])) wordEnd++;
                if (TryKeyword(text, i, wordEnd, start, out kind)) { matchStart = i; matchEnd = wordEnd; }
            }
            if (matchStart < 0 && TryChinese(text, i, end, out matchEnd, out kind)) matchStart = i;
            if (matchStart < 0 && isWord && TryUrl(text, i, end, out matchEnd)) { matchStart = i; kind = Kind.Url; }
            if (matchStart < 0 && wordStart && TryIp(text, i, end, start, out matchEnd)) { matchStart = i; kind = Kind.Ip; }
            if (matchStart < 0 && (c is '/' or '.' or '~') && TryPath(text, i, end, start, out matchEnd)) { matchStart = i; kind = Kind.Path; }
            if (matchStart < 0 && IsHostChar(c))
            {
                // 主机名没有词边界，可以从这一段的任意位置开始；整段的结果缓存下来，避免重复扫描。
                if (i >= hostRunEnd)
                {
                    hostRunEnd = i;
                    while (hostRunEnd < end && IsHostChar(text[hostRunEnd])) hostRunEnd++;
                    hostEnd = -1;
                    if (hostRunEnd < end && text[hostRunEnd] == '@' && hostRunEnd + 1 < end && IsHostChar(text[hostRunEnd + 1]))
                    {
                        hostEnd = hostRunEnd + 1;
                        while (hostEnd < end && IsHostChar(text[hostEnd])) hostEnd++;
                    }
                }
                if (hostEnd > 0 && i < hostRunEnd) { matchStart = i; matchEnd = hostEnd; kind = Kind.Host; }
            }
            if (matchStart < 0) { i++; continue; }
            Append(text[copied..matchStart]);
            AppendColored(text[matchStart..matchEnd], kind, palette);
            copied = matchEnd; i = matchEnd;
        }
        if (copied < end) Append(text[copied..end]);
    }

    // ---- 各类词的识别，规则对应原来的正则 ----

    private static readonly string[] ErrorWords = ["error", "failed", "failure", "fatal", "denied", "exception"];
    private static readonly string[] WarningWords = ["warning", "warn"];
    private static readonly string[] SuccessWords = ["success", "successful", "successfully", "passed", "connected", "done"];

    /// <summary>整词关键字：\b(error|failed|...)\b，大小写不敏感。</summary>
    private static bool TryKeyword(ReadOnlySpan<char> text, int start, int end, int segmentStart, out Kind kind)
    {
        kind = Kind.None;
        var length = end - start;
        if (length == 0 || length > 12) return false;
        for (var i = start; i < end; i++) if (!char.IsAsciiLetter(text[i])) return false;
        if (start > segmentStart && IsWordChar(text[start - 1])) return false;   // \b 要求前面不是单词字符
        foreach (var word in ErrorWords) if (EqualsIgnoreCase(text[start..end], word)) { kind = Kind.Error; return true; }
        foreach (var word in WarningWords) if (EqualsIgnoreCase(text[start..end], word)) { kind = Kind.Warning; return true; }
        foreach (var word in SuccessWords) if (EqualsIgnoreCase(text[start..end], word)) { kind = Kind.Success; return true; }
        return false;
    }

    /// <summary>中文关键字：原正则里没有词边界，出现在任何位置都算。</summary>
    private static bool TryChinese(ReadOnlySpan<char> text, int start, int end, out int matchEnd, out Kind kind)
    {
        matchEnd = start; kind = Kind.None;
        if (start + 2 > end) return false;
        var c = text[start];
        if (c == '错' && text[start + 1] == '误') { kind = Kind.Error; matchEnd = start + 2; return true; }
        if (c == '失' && text[start + 1] == '败') { kind = Kind.Error; matchEnd = start + 2; return true; }
        if (c == '拒' && text[start + 1] == '绝') { kind = Kind.Error; matchEnd = start + 2; return true; }
        if (c == '警' && text[start + 1] == '告') { kind = Kind.Warning; matchEnd = start + 2; return true; }
        if (c == '成' && text[start + 1] == '功') { kind = Kind.Success; matchEnd = start + 2; return true; }
        if (c == '已' && start + 3 <= end && text[start + 1] == '完' && text[start + 2] == '成')
        { kind = Kind.Success; matchEnd = start + 3; return true; }
        return false;
    }

    /// <summary>https?://[^\s&lt;&gt;]+</summary>
    private static bool TryUrl(ReadOnlySpan<char> text, int start, int end, out int urlEnd)
    {
        urlEnd = start;
        if (start + 7 > end) return false;
        if (text[start] is not ('h' or 'H')) return false;
        if (!EqualsIgnoreCase(text[start..(start + 4)], "http")) return false;
        var p = start + 4;
        if (p < end && (text[p] == 's' || text[p] == 'S')) p++;
        if (p + 3 > end || text[p] != ':' || text[p + 1] != '/' || text[p + 2] != '/') return false;
        var q = p + 3;
        var body = q;
        while (q < end && !char.IsWhiteSpace(text[q]) && text[q] != '<' && text[q] != '>') q++;
        if (q == body) return false;                       // 后面至少要有一个字符
        urlEnd = q; return true;
    }

    /// <summary>\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b</summary>
    private static bool TryIp(ReadOnlySpan<char> text, int start, int end, int segmentStart, out int ipEnd)
    {
        ipEnd = start;
        if (start > segmentStart && IsWordChar(text[start - 1])) return false;    // \b 要求前面不是单词字符
        var p = start;
        for (var group = 0; group < 3; group++)
        {
            var digits = 0;
            while (p < end && text[p] is >= '0' and <= '9' && digits < 3) { p++; digits++; }
            if (digits == 0 || p >= end || text[p] != '.') return false;
            p++;
        }
        var last = 0;
        var q = p;
        while (q < end && text[q] is >= '0' and <= '9' && last < 3) { q++; last++; }
        if (last == 0) return false;
        // 端口是可选的：端口不合法时退回不带端口的形式。
        var withPort = -1;
        if (q < end && text[q] == ':')
        {
            var portDigits = 0; var s = q + 1;
            while (s < end && text[s] is >= '0' and <= '9' && portDigits < 5) { s++; portDigits++; }
            if (portDigits > 0) withPort = s;
        }
        if (withPort > 0 && !IsWordCharAt(text, withPort, end)) { ipEnd = withPort; return true; }
        if (IsWordCharAt(text, q, end)) return false;
        ipEnd = q; return true;
    }

    /// <summary>(?&lt;![\w/])(?:~?/|\.{1,2}/)[\w.\-/~]+</summary>
    private static bool TryPath(ReadOnlySpan<char> text, int start, int end, int segmentStart, out int pathEnd)
    {
        pathEnd = start;
        if (start > segmentStart)
        {
            var prev = text[start - 1];
            if (IsWordChar(prev) || prev == '/') return false;                    // 前面不能是单词字符或斜杠
        }
        var p = start;
        if (p < end && text[p] == '~') p++;                                     // ~?/
        else if (p < end && text[p] == '.')
        {
            var dots = 0;
            while (p < end && text[p] == '.' && dots < 2) { p++; dots++; }
            if (dots == 0) return false;
        }
        if (p >= end || text[p] != '/') return false;
        p++;
        var body = p;
        while (p < end && IsPathChar(text[p])) p++;
        if (p == body) return false;                                            // 路径至少要有一个字符
        pathEnd = p; return true;
    }

    private void AppendColored(ReadOnlySpan<char> text, Kind kind, HighlightPalette palette)
    {
        var rgb = kind switch
        {
            Kind.Error => palette.Error,
            Kind.Warning => palette.Warning,
            Kind.Success or Kind.Host => palette.Success,
            Kind.Ip => palette.Ip,
            _ => palette.Link
        };
        if (kind == Kind.Ip && !IsRealIp(text)) { Append(text); return; }       // 不是合法 IP 就不着色
        Append("\x1b[38;2;"); Append(rgb); Append('m'); Append(text); Append("\x1b[39m");
    }

    /// <summary>IP 文本是否合法：原正则只是形状匹配，合法性由这里把关。</summary>
    private static bool IsRealIp(ReadOnlySpan<char> text)
    {
        var end = text.IndexOf(':');
        if (end < 0) end = text.Length;
        if (end > 15) return false;
        // 标准的点分十进制直接判断，不创建 IPAddress 对象：终端里 IP 到处都是，
        // 每段创建一个对象会持续产生垃圾。判断不了的写法再交给系统解析，行为保持一致。
        if (IsCanonicalIpv4(text[..end])) return true;
        Span<char> buffer = stackalloc char[16];
        text[..end].CopyTo(buffer);
        return IPAddress.TryParse(buffer[..end], out _);
    }

    /// <summary>标准写法 a.b.c.d：四段、十进制、不允许前导零、每段不超过 255。</summary>
    private static bool IsCanonicalIpv4(ReadOnlySpan<char> text)
    {
        var parts = 0; var digits = 0; var value = 0; var leadingZero = false;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '.')
            {
                if (digits == 0) return false;
                if (leadingZero && digits > 1) return false;
                if (value > 255) return false;
                parts++;
                if (i == text.Length) return parts == 4;
                digits = 0; value = 0; leadingZero = false;
                continue;
            }
            var c = text[i];
            if (c < '0' || c > '9') return false;
            if (digits == 0) leadingZero = c == '0';
            digits++;
            if (digits > 3) return false;
            value = value * 10 + (c - '0');
        }
        return false;
    }

    // ---- 缓冲区 ----

    private void Append(ReadOnlySpan<char> text)
    {
        if (text.Length == 0) return;
        EnsureCapacity(_length + text.Length);
        text.CopyTo(_output.AsSpan(_length));
        _length += text.Length;
    }
    private void Append(string text) => Append(text.AsSpan());
    private void Append(char c) { EnsureCapacity(_length + 1); _output[_length++] = c; }
    private void AppendSequence() { Append(_sequence.ToString()); _sequence.Clear(); }
    private void EnsureCapacity(int needed)
    {
        if (_output.Length >= needed) return;
        var size = _output.Length;
        while (size < needed) size *= 2;
        Array.Resize(ref _output, size);
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

    // ---- 字符判定 ----

    /// <summary>正则里的 \w：字母、数字、下划线（CJK 也算单词字符）。</summary>
    private static bool IsWordChar(char c)
        => c < 128 ? char.IsAsciiLetterOrDigit(c) || c == '_' : char.IsLetterOrDigit(c);
    private static bool IsHostChar(char c) => IsWordChar(c) || c == '.' || c == '-';
    private static bool IsPathChar(char c) => IsWordChar(c) || c == '.' || c == '-' || c == '/' || c == '~';
    private static bool IsWordCharAt(ReadOnlySpan<char> text, int index, int end) => index < end && IsWordChar(text[index]);
    private static bool EqualsIgnoreCase(ReadOnlySpan<char> span, string value)
    {
        if (span.Length != value.Length) return false;
        for (var i = 0; i < span.Length; i++) if (char.ToLowerInvariant(span[i]) != value[i]) return false;
        return true;
    }
}

/// <summary>把原始字节流按 UTF-8 解码后交给高亮器，输出仍是带 ANSI 的字节流。</summary>
internal sealed class HighlightedTerminalStream(Stream input, TerminalHighlighter highlighter) : Stream
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly byte[] _inputBuffer = new byte[32 * 1024];
    private readonly char[] _characters = new char[32 * 1024 + 2];
    // 借来的缓冲区：内容读完之后才会再次变换，所以期间不会被高亮器覆盖。
    private byte[]? _pending;
    private int _pendingLength;
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
            if (_offset < _pendingLength)
            {
                var available = Math.Min(buffer.Length, _pendingLength - _offset);
                _pending!.AsSpan(_offset, available).CopyTo(buffer.Span);
                _offset += available;
                return available;
            }
            if (_eof) return 0;
            cancellationToken.ThrowIfCancellationRequested();
            var length = await input.ReadAsync(_inputBuffer, cancellationToken).ConfigureAwait(false);
            if (length == 0) _eof = true;
            // 解码器跨块保存半个多字节字符，高亮器跨块保存半个控制序列，两者都不会把半个序列吐出去。
            var decoded = _decoder.GetChars(_inputBuffer, 0, length, _characters, 0, _eof);
            // 直接借用高亮器的字节缓冲区：这段内容被读完之后才会发生下一次变换，
            // 中间不需要再复制一份，省掉每屏一次整块 memcpy。
            var (bytes, count) = highlighter.TransformToUtf8(_characters.AsSpan(0, decoded));
            _pending = bytes;
            _pendingLength = count; _offset = 0;
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
