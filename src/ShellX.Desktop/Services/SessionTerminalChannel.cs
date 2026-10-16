using System.Text;
using System.Threading.Channels;
using ShellX.Core.Sessions;

namespace ShellX.Desktop.Services;

/// <summary>标签拥有的终端通道：合并远程输出与本地连接日志，重连时保留终端缓冲区。</summary>
public sealed class SessionTerminalChannel : ITerminalChannel, IAsyncDisposable
{
    private readonly Channel<byte[]> _output = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private ITerminalChannel? _source;
    private CancellationTokenSource? _readerCancellation;
    private Task _reader = Task.CompletedTask;
    private int _columns = 100, _rows = 30;
    public Stream Stream { get; }
    public event Action<ITerminalChannel, string>? Closed;

    public SessionTerminalChannel() => Stream = new DuplexStream(this);

    public void Attach(ITerminalChannel source)
    {
        _source = source;
        source.Resize(_columns, _rows);
        var cancellation = _readerCancellation = new CancellationTokenSource();
        _reader = Task.Run(async () =>
        {
            var buffer = new byte[8192];
            var reason = "远程终端已关闭。";
            try
            {
                while (await source.Stream.ReadAsync(buffer, cancellation.Token) is var count && count > 0)
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
    {
        // A server error must never inject terminal control sequences into a local log line.
        var clean = new string(message.Where(c => !char.IsControl(c)).Take(600).ToArray());
        var color = error ? "31" : "36";
        return _output.Writer.WriteAsync(Encoding.UTF8.GetBytes($"\u001b[?47l\u001b[0m\r\n\u001b[{color}m[ShellX {DateTime.Now:HH:mm:ss}] {clean}\u001b[0m\r\n"), cancellationToken);
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

    private sealed class DuplexStream(SessionTerminalChannel owner) : Stream
    {
        private ReadOnlyMemory<byte> _pending;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty) return 0;
            if (_pending.IsEmpty)
            {
                if (!await owner._output.Reader.WaitToReadAsync(cancellationToken)) return 0;
                _pending = await owner._output.Reader.ReadAsync(cancellationToken);
            }
            var count = Math.Min(buffer.Length, _pending.Length);
            _pending[..count].CopyTo(buffer); _pending = _pending[count..]; return count;
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
