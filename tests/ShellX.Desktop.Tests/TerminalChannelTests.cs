using System.Text;
using System.Threading.Channels;
using ShellX.Core.Sessions;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Tests;

public sealed class TerminalChannelTests
{
    private const string Prompt = "\u001b[0;27;0;32;1m[defu@izuf6b0ooqp0ht8h7ujteyz ~]$ ";

    [Fact]
    public async Task ChannelKeepsEscapeSequenceWholeWhenItArrivesInPieces()
    {
        // 服务器把提示符拆成两段发过来时，着色层要等序列写完再整体吐出，
        // 否则终端里会出现 ";0;27;0;32;1" 这种裸参数。
        var source = new FakeChannel();
        var channel = new SessionTerminalChannel();
        channel.Attach(source);
        await source.Push(Encoding.UTF8.GetBytes("\u001b[0;27;0;32;1"));
        await source.Push(Encoding.UTF8.GetBytes("m[defu@izuf6b0ooqp0ht8h7ujteyz ~]$ "));

        var text = await ReadAsync(channel.Stream, Encoding.UTF8.GetByteCount(Prompt));
        Assert.Equal(Prompt, text);
        Assert.DoesNotContain(";0;27;0;32;1", text);
    }

    [Fact]
    public async Task ChannelKeepsBytesWhenReaderIsCancelled()
    {
        // 终端控件被销毁时读线程会被取消，取消不能把通道里的数据吞掉。
        var source = new FakeChannel();
        var channel = new SessionTerminalChannel();
        channel.Attach(source);

        using var cancellation = new CancellationTokenSource();
#pragma warning disable xUnit1051 // 故意用自己的令牌来测取消
        var buffer = new byte[16];
        var read = channel.Stream.ReadAsync(buffer.AsMemory(), cancellation.Token).AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
#pragma warning restore xUnit1051

        await source.Push(Encoding.UTF8.GetBytes("hello"));
        await source.Push(Encoding.UTF8.GetBytes("world"));
        var text = await ReadAsync(channel.Stream, 10);
        Assert.Equal("helloworld", text);
    }

    [Fact]
    public async Task ChannelKeepsSequenceWholeAcrossReaderReplacement()
    {
        // 终端控件被销毁重建时读线程会换人，但着色状态留在通道里，
        // 半个控制序列不会被拆到两个读者手里变成裸参数。
        var source = new FakeChannel();
        var channel = new SessionTerminalChannel();
        channel.Attach(source);
        await source.Push(Encoding.UTF8.GetBytes("\u001b[0;27;0;32;1"));
        await Task.Delay(100, TestContext.Current.CancellationToken);

#pragma warning disable xUnit1051 // 故意用自己的令牌来测取消
        using var cancellation = new CancellationTokenSource();
        var buffer = new byte[16];
        var read = channel.Stream.ReadAsync(buffer.AsMemory(), cancellation.Token).AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
#pragma warning restore xUnit1051

        await source.Push(Encoding.UTF8.GetBytes("m[defu@host ~]$ "));
        var text = await ReadAsync(channel.Stream, Encoding.UTF8.GetByteCount("\u001b[0;27;0;32;1m[defu@host ~]$ "));
        Assert.Equal("\u001b[0;27;0;32;1m[defu@host ~]$ ", text);
    }

    [Fact]
    public void RemoteConnectionDeclaresCancellableReads()
    {
        // 不可取消的读线程会在控件销毁后继续挂着，和新控件抢同一个通道，把字节流撕成两半。
        var connection = new RemotePtyConnection(new SessionTerminalChannel());
        Assert.True(connection.SupportsCancellableRead);
    }

    private static async Task<string> ReadAsync(Stream stream, int expected)
    {
        var builder = new StringBuilder();
        var buffer = new byte[4096];
        while (builder.Length < expected)
        {
            var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
            if (read == 0) break;
            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
        return builder.ToString();
    }

    private sealed class FakeChannel : ITerminalChannel
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        public Stream Stream { get; }
        public FakeChannel() => Stream = new ChunkStream(_chunks);
        public Task Push(byte[] chunk) => _chunks.Writer.WriteAsync(chunk).AsTask();
        public void Resize(int columns, int rows) { }

        private sealed class ChunkStream(Channel<byte[]> chunks) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var chunk = await chunks.Reader.ReadAsync(cancellationToken);
                var take = Math.Min(count, chunk.Length);
                chunk.AsMemory(0, take).CopyTo(buffer.AsMemory(offset));
                return take;
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}