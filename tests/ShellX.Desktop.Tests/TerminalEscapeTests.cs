using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using ShellX.Core.Sessions;
using ShellX.Desktop.Controls;

namespace ShellX.Desktop.Tests;

public sealed class TerminalEscapeTests
{
    [AvaloniaFact]
    public async Task EscapeGoesOutAsPlainVtByte()
    {
        // Esc 必须按标准的 0x1B 发给远端。终端库在 Windows 上会改写成 Win32 输入记录，
        // 远端 shell 不认识，只会在命令行里回显出一串裸参数（;0;27;1;32;1_）。
        var channel = new ProbeChannel();
        var host = new TerminalHost { Channel = channel };
        var window = new Window { Content = host, Width = 900, Height = 600 };
        window.Show();
        await Task.Delay(120);
        var view = host.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault();
        Assert.NotNull(view);
        window.Focus();
        view!.Focus();
        await Task.Delay(80);
        Assert.True(view.IsFocused);

        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, KeyModifiers = KeyModifiers.None });
        await Task.Delay(150);
        Assert.Equal("\u001b", channel.Written);

        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Escape, KeyModifiers = KeyModifiers.None });
        await Task.Delay(150);
        Assert.Equal("\u001b", channel.Written);   // 松开时不能再补一条 Win32 记录
    }

    [AvaloniaFact]
    public async Task LibraryStillEncodesEscapeAsWin32RecordOnWindows()
    {
        // 记录库的行为，作为上面那层拦截的理由：Windows 上没有单独表示 Esc 的 VT 序列，
        // 库会发 Win32 输入记录（Vk=27, Sc=0, Uc=27, Kd=1, Cs=控制键状态, Rc=1）。
        if (!OperatingSystem.IsWindows()) return;
        var channel = new ProbeChannel();
        var control = new TerminalControl();
        var window = new Window { Content = control, Width = 900, Height = 600 };
        window.Show();
        await Task.Delay(120);
        control.AttachConnection(new RemotePtyConnection(channel));
        var view = control.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault();
        Assert.NotNull(view);
        window.Focus();
        view!.Focus();
        await Task.Delay(80);

        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, KeyModifiers = KeyModifiers.None });
        await Task.Delay(150);
        Assert.StartsWith("\u001b[27;0;27;", channel.Written);
        Assert.EndsWith("_", channel.Written);
    }

    private sealed class ProbeChannel : ITerminalChannel
    {
        private readonly StringBuilder _written = new();
        public string Written { get { lock (_written) return _written.ToString(); } }
        public Stream Stream { get; }
        public ProbeChannel() => Stream = new Duplex(this);
        public void Resize(int columns, int rows) { }

        private sealed class Duplex(ProbeChannel owner) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(0);
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (owner._written) owner._written.Append(Encoding.UTF8.GetString(buffer, offset, count));
            }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
        }
    }
}
