using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using ShellX.Core.Sessions;
using ShellX.Desktop.Controls;

namespace ShellX.Desktop.Tests;

/// <summary>终端挂载远端后必须把本地视口尺寸推给远端，否则远端按 100 列排版，粘贴长命令会整行错位。</summary>
public sealed class TerminalSizeSyncTests
{
    [AvaloniaFact]
    public async Task AttachAfterLayoutPushesViewportSize()
    {
        var channel = new ProbeChannel();
        var host = new TerminalHost();
        var window = new Window { Content = host, Width = 1000, Height = 600 };
        window.Show();
        await Task.Delay(500);
        var view = host.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault();
        var cols = view!.Terminal.Cols;
        var rows = view.Terminal.Rows;
        Assert.True(cols > 0 && rows > 0);

        // 连接在布局之后才建立：终端库此时不会再推尺寸，必须由控件补一次同步。
        host.Channel = channel;
        await Task.Delay(400);

        Assert.Contains((cols, rows), channel.Resizes);
    }

    [AvaloniaFact]
    public async Task AttachBeforeLayoutStillPushesViewportSize()
    {
        var channel = new ProbeChannel();
        var host = new TerminalHost { Channel = channel };
        var window = new Window { Content = host, Width = 1000, Height = 600 };
        window.Show();
        await Task.Delay(600);
        var view = host.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault();
        var cols = view!.Terminal.Cols;
        var rows = view.Terminal.Rows;

        // 布局先于连接时，终端库在尺寸变化时会自己推一次；无论如何远端最终必须拿到视口尺寸。
        Assert.Contains((cols, rows), channel.Resizes);
    }

    private sealed class ProbeChannel : ITerminalChannel
    {
        public List<(int Columns, int Rows)> Resizes { get; } = new();
        public ProbeChannel() => Stream = new Duplex(this);
        public Stream Stream { get; }
        public void Resize(int columns, int rows) => Resizes.Add((columns, rows));
        private sealed class Duplex(ProbeChannel owner) : Stream
        {
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(0);
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) { }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
