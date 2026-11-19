using System.Buffers;
using System.Text;
using System.Threading.Channels;
using ShellX.Core.Settings;
using ShellX.Core.Sessions;
using ShellX.Desktop.Controls;

namespace ShellX.Desktop.Services;

/// <summary>标签拥有的终端通道：合并远程输出与本地连接日志，重连时保留终端缓冲区。</summary>
public sealed class SessionTerminalChannel : ITerminalChannel, ITerminalHighlighting, IAsyncDisposable
{
    // 不能用 SingleReader：终端控件被销毁重建时，旧读线程可能还没退干净，两个读者同时等同一个
    // 通道时单读者优化会让其中一个拿到不完整的数据，ANSI 控制序列会被拆到两边。
    private readonly Channel<TerminalChunk> _output = Channel.CreateBounded<TerminalChunk>(new BoundedChannelOptions(128)
        { SingleReader = false, FullMode = BoundedChannelFullMode.Wait });

    // 着色放在通道里做：控制序列不会被拆到两个消费者手里，控件重建时着色状态也不会丢。
    private readonly TerminalHighlighter _highlighter = new();
    private readonly object _pendingGate = new();
    // 还没被读走的字节：记成"哪块缓冲区的哪一段"，切片会分配，所以用下标表示。
    private byte[]? _pendingArray;
    private int _pendingOffset;
    private int _pendingCount;
    private ITerminalChannel? _source;
    private CancellationTokenSource? _readerCancellation;
    private Task _reader = Task.CompletedTask;
    private int _columns = 100, _rows = 30;
    public Stream Stream { get; }
    public event Action<ITerminalChannel, string>? Closed;

    public bool HighlightEnabled { get => _highlighter.Enabled; set => _highlighter.Enabled = value; }
    public AppTheme Theme { get => _highlighter.Theme; set => _highlighter.Theme = value; }

    public SessionTerminalChannel() => Stream = new DuplexStream(this);

    public void Attach(ITerminalChannel source)
    {
        _source = source;
        source.Resize(_columns, _rows);
        _highlighter.Reset();
        var cancellation = _readerCancellation = new CancellationTokenSource();
        var previous = _reader;
        _reader = Task.Run(async () =>
        {
            // 上一个读线程可能还没退干净，两个线程共用一个着色器会互相踩状态，先等它收工。
            try { await previous.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
            var reason = "远程终端已关闭。";
            try
            {
                var stream = new HighlightedTerminalStream(source.Stream, _highlighter);
                while (await stream.ReadAsync(buffer, cancellation.Token) is var count && count > 0)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    // 缓冲区从池子里取，用完由消费者还回去：终端是持续大流量，
                    // 每次都 new 一块数组只会让堆不断变大。
                    var chunk = ArrayPool<byte>.Shared.Rent(count);
                    buffer.AsSpan(0, count).CopyTo(chunk);
                    await _output.Writer.WriteAsync(new TerminalChunk(chunk, count, true), cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { reason = ex.Message; }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                if (!cancellation.IsCancellationRequested) Closed?.Invoke(source, reason);
            }
        });
    }

    // Cancel first; the owner closes the SSH stream before awaiting this task because some
    // SSH streams implement blocking reads that only wake when the session is disposed.
    public Task DetachAsync()
    {
        _source = null;
        var cancellation = _readerCancellation; _readerCancellation = null;
        cancellation?.Cancel();
        return FinishAsync();
        async Task FinishAsync() { try { await _reader; } finally { cancellation?.Dispose(); } }
    }

    public async ValueTask LogAsync(string message, bool error = false, CancellationToken cancellationToken = default)
    {
        var chunk = Format(message, error);
        // 写入失败时数据没有进通道，数组交给 GC 回收即可，绝不能还回池子造成重复归还。
        await _output.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>写一条本地日志但不等待消费者；终端没有读取方或已关闭时直接丢弃，不拖住调用方。</summary>
    public bool TryLog(string message, bool error = false)
    {
        var chunk = Format(message, error);
        bool written;
        try { written = _output.Writer.TryWrite(chunk); }
        catch (ChannelClosedException) { written = false; }
        if (!written) Release(chunk);          // 没写进去的数组可以安全还回池子
        return written;
    }

    // A server error must never inject terminal control sequences into a local log line.
    // 整行直接拼进字符缓冲区再转 UTF-8，不经过中间字符串。
    private static TerminalChunk Format(string message, bool error)
    {
        Span<char> body = stackalloc char[600];
        var bodyLength = 0;
        foreach (var c in message)
        {
            if (char.IsControl(c)) continue;
            if (bodyLength == body.Length) break;
            body[bodyLength++] = c;
        }
        var now = DateTime.Now;
        Span<char> line = stackalloc char[700];
        var length = 0;
        Append(line, ref length, "\u001b[?47l\u001b[0m\r\n\u001b[");
        Append(line, ref length, error ? "31" : "36");
        Append(line, ref length, "m[ShellX ");
        WriteTime(now, line[length..]);
        length += 8;
        Append(line, ref length, "] ");
        Append(line, ref length, body[..bodyLength]);
        Append(line, ref length, "\u001b[0m\r\n");
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(length));
        var count = Encoding.UTF8.GetBytes(line[..length], buffer);
        return new TerminalChunk(buffer, count, true);
    }

    /// <summary>往字符缓冲区尾部追加一段内容。</summary>
    private static void Append(Span<char> line, ref int length, ReadOnlySpan<char> text)
    {
        text.CopyTo(line[length..]);
        length += text.Length;
    }

    /// <summary>写 HH:mm:ss。</summary>
    private static void WriteTime(DateTime now, Span<char> target)
    {
        WriteTwoDigits(now.Hour, target);
        target[2] = ':';
        WriteTwoDigits(now.Minute, target[3..]);
        target[5] = ':';
        WriteTwoDigits(now.Second, target[6..]);
    }

    private static void WriteTwoDigits(int value, Span<char> target)
    {
        target[0] = (char)('0' + value / 10);
        target[1] = (char)('0' + value % 10);
    }

    public void Resize(int columns, int rows)
    {
        _columns = columns; _rows = rows;
        try { _source?.Resize(columns, rows); } catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await DetachAsync(); _output.Writer.TryComplete();
        lock (_pendingGate)
        {
            if (_pendingArray is { } array) { ArrayPool<byte>.Shared.Return(array); _pendingArray = null; _pendingCount = 0; _pendingOffset = 0; }
        }
    }

    /// <summary>把通道里已经取出但还没被读走的字节交给下一个读者，取消的读取不会吞掉数据。</summary>
    private bool TryReadPending(Memory<byte> buffer, out int count)
    {
        lock (_pendingGate)
        {
            count = 0;
            if (_pendingArray is not { } array || _pendingCount <= 0) return false;
            var take = Math.Min(buffer.Length, _pendingCount);
            array.AsSpan(_pendingOffset, take).CopyTo(buffer.Span);
            _pendingOffset += take; _pendingCount -= take;
            count = take;
            if (_pendingCount == 0)
            {
                _pendingArray = null; _pendingOffset = 0;
                Release(new TerminalChunk(array, take, true));   // 读完了，缓冲区还回池子
            }
            return true;
        }
    }

    private void SetPending(TerminalChunk chunk)
    {
        lock (_pendingGate)
        {
            if (_pendingArray is { } existing && _pendingCount > 0)
            {
                // 正常读法下残留一定先被读走，走不到这里；真发生了就合并两块并把旧块还回池子。
                var needed = _pendingCount + chunk.Length;
                var merged = ArrayPool<byte>.Shared.Rent(needed);
                existing.AsSpan(_pendingOffset, _pendingCount).CopyTo(merged);
                chunk.Buffer.AsSpan(0, chunk.Length).CopyTo(merged.AsSpan(_pendingCount));
                Release(new TerminalChunk(existing, existing.Length, true));
                Release(chunk);
                _pendingArray = merged; _pendingOffset = 0; _pendingCount = needed;
                return;
            }
            _pendingArray = chunk.Buffer; _pendingOffset = 0; _pendingCount = chunk.Length;
        }
    }

    /// <summary>把池子里的缓冲区还回去；不是池子来的数组直接丢掉。</summary>
    private static void Release(TerminalChunk chunk)
    {
        if (chunk.Pooled && chunk.Buffer.Length > 0) ArrayPool<byte>.Shared.Return(chunk.Buffer);
    }

    /// <summary>通道里的一段字节；来自池子的缓冲区读完后要还回去。</summary>
    private readonly record struct TerminalChunk(byte[] Buffer, int Length, bool Pooled);

    private sealed class DuplexStream(SessionTerminalChannel owner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty) return 0;
            while (true)
            {
                // 取消时不能取走数据：取走了就等于吞掉，交给下一个读者才算没丢。
                cancellationToken.ThrowIfCancellationRequested();
                if (owner.TryReadPending(buffer, out var count)) return count;
                if (!await owner._output.Reader.WaitToReadAsync(cancellationToken)) return 0;
                var chunk = await owner._output.Reader.ReadAsync(cancellationToken);
                owner.SetPending(chunk);
            }
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Write(byte[] buffer, int offset, int count) => owner._source?.Stream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => owner._source?.Stream.WriteAsync(buffer, cancellationToken) ?? ValueTask.CompletedTask;
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => owner._source?.Stream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => owner._source?.Stream.FlushAsync(cancellationToken) ?? Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
