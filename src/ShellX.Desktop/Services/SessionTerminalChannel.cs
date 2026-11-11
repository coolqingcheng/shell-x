using System.Text;
using System.Threading.Channels;
using ShellX.Core.Sessions;
using ShellX.Desktop.Controls;

namespace ShellX.Desktop.Services;

/// <summary>标签拥有的终端通道：合并远程输出与本地连接日志，重连时保留终端缓冲区。</summary>
public sealed class SessionTerminalChannel : ITerminalChannel, ITerminalHighlighting, IAsyncDisposable
{
    // 不能用 SingleReader：终端控件被销毁重建时，旧读线程可能还没退干净，两个读者同时等同一个
    // 通道时单读者优化会让其中一个拿到不完整的数据，ANSI 控制序列会被拆到两边。
    private readonly Channel<byte[]> _output = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128)
        { SingleReader = false, FullMode = BoundedChannelFullMode.Wait });

    // 着色放在通道里做：控制序列不会被拆到两个消费者手里，控件重建时着色状态也不会丢。
    private readonly TerminalHighlighter _highlighter = new();
    private readonly object _pendingGate = new();
    private ReadOnlyMemory<byte>? _pending;
    private ITerminalChannel? _source;
    private CancellationTokenSource? _readerCancellation;
    private Task _reader = Task.CompletedTask;
    private int _columns = 100, _rows = 30;
    public Stream Stream { get; }
    public event Action<ITerminalChannel, string>? Closed;

    public bool HighlightEnabled { get => _highlighter.Enabled; set => _highlighter.Enabled = value; }
    public bool LightTheme { get => _highlighter.LightTheme; set => _highlighter.LightTheme = value; }

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
            var buffer = new byte[32 * 1024];
            var reason = "远程终端已关闭。";
            try
            {
                var stream = new HighlightedTerminalStream(source.Stream, _highlighter);
                while (await stream.ReadAsync(buffer, cancellation.Token) is var count && count > 0)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await _output.Writer.WriteAsync(buffer.AsSpan(0, count).ToArray(), cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { reason = ex.Message; }
            finally
            {
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

    public ValueTask LogAsync(string message, bool error = false, CancellationToken cancellationToken = default)
        => _output.Writer.WriteAsync(Format(message, error), cancellationToken);

    /// <summary>写一条本地日志但不等待消费者；终端没有读取方或已关闭时直接丢弃，不拖住调用方。</summary>
    public bool TryLog(string message, bool error = false)
    {
        try { return _output.Writer.TryWrite(Format(message, error)); }
        catch (ChannelClosedException) { return false; }
    }

    // A server error must never inject terminal control sequences into a local log line.
    private static byte[] Format(string message, bool error)
    {
        var clean = new string(message.Where(c => !char.IsControl(c)).Take(600).ToArray());
        var color = error ? "31" : "36";
        return Encoding.UTF8.GetBytes($"\u001b[?47l\u001b[0m\r\n\u001b[{color}m[ShellX {DateTime.Now:HH:mm:ss}] {clean}\u001b[0m\r\n");
    }

    public void Resize(int columns, int rows)
    {
        _columns = columns; _rows = rows;
        try { _source?.Resize(columns, rows); } catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await DetachAsync(); _output.Writer.TryComplete();
    }

    /// <summary>把通道里已经取出但还没被读走的字节交给下一个读者，取消的读取不会吞掉数据。</summary>
    private bool TryReadPending(Memory<byte> buffer, out int count)
    {
        lock (_pendingGate)
        {
            count = 0;
            if (_pending is not { Length: > 0 } pending) return false;
            var take = Math.Min(buffer.Length, pending.Length);
            pending[..take].CopyTo(buffer);
            _pending = take == pending.Length ? null : pending[take..];
            count = take;
            return true;
        }
    }

    private void SetPending(byte[] chunk)
    {
        lock (_pendingGate)
        {
            if (_pending is { Length: > 0 } existing)
            {
                var merged = new byte[existing.Length + chunk.Length];
                existing.Span.CopyTo(merged.AsSpan(0, existing.Length));
                chunk.CopyTo(merged.AsSpan(existing.Length));
                _pending = merged;
                return;
            }
            _pending = chunk;
        }
    }

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
