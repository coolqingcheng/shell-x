using Avalonia;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Iciclecreek.Terminal;
using Porta.Pty;
using Avalonia.Styling;
using ShellX.Desktop.Services;
using ShellX.Core.Sessions;

namespace ShellX.Desktop.Controls;

// 仅适配控件与终端通道，不创建 SSH 连接。标签隐藏时保留此控件及其缓冲区。
public sealed class TerminalHost : Border
{
    public static readonly StyledProperty<ITerminalChannel?> ChannelProperty = AvaloniaProperty.Register<TerminalHost, ITerminalChannel?>(nameof(Channel));
    public static readonly StyledProperty<double> TerminalFontSizeProperty = AvaloniaProperty.Register<TerminalHost, double>(nameof(TerminalFontSize), 14);
    public static readonly StyledProperty<string> TerminalFontFamilyProperty = AvaloniaProperty.Register<TerminalHost, string>(nameof(TerminalFontFamily), "Cascadia Mono,Consolas,monospace");
    public static readonly StyledProperty<bool> HighlightTextProperty = AvaloniaProperty.Register<TerminalHost, bool>(nameof(HighlightText), true);

    private readonly TerminalControl _terminal = new()
    {
        FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 14,
        BufferSize = 5000,
        AllowWindowOps = false, Process = ""
    };
    private ITerminalChannel? _attached;
    public ITerminalChannel? Channel { get => GetValue(ChannelProperty); set => SetValue(ChannelProperty, value); }
    public double TerminalFontSize { get => GetValue(TerminalFontSizeProperty); set => SetValue(TerminalFontSizeProperty, value); }
    public string TerminalFontFamily { get => GetValue(TerminalFontFamilyProperty); set => SetValue(TerminalFontFamilyProperty, value); }
    public bool HighlightText { get => GetValue(HighlightTextProperty); set => SetValue(HighlightTextProperty, value); }
    public Task CopyAsync() => _terminal.CopyAsync();
    public Task PasteAsync() => _terminal.PasteAsync();
    public void ClearScreen() => _terminal.ClearScreen();
    public void FocusTerminal() => _terminal.Focus();
    // The terminal also resolves its compiled theme through this loader in its static constructor.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, "CompiledAvaloniaXaml.!XamlLoader", "Iciclecreek.Avalonia.Terminal")]
    public TerminalHost()
    {
        ThemeService.Bind(_terminal, TerminalControl.BackgroundProperty, "AppBackground");
        ThemeService.Bind(_terminal, TerminalControl.ForegroundProperty, "EditorForeground");
        ActualThemeVariantChanged += (_, _) => ApplyHighlightSettings();
        Padding = new Thickness(12, 6, 4, 4); Child = _terminal;
        var contextMenu = BuildContextMenu();
        ContextMenu = contextMenu;
        _terminal.ContextMenu = contextMenu;
        AddHandler(InputElement.PointerPressedEvent, OnTerminalPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(InputElement.PointerReleasedEvent, OnTerminalPointerReleased, RoutingStrategies.Tunnel, true);
        AddHandler(InputElement.ContextRequestedEvent, OnTerminalContextRequested, RoutingStrategies.Bubble, true);
        Loaded += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => { _terminal.DetachConnection(); _terminal.Dispose(); _attached = null; };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChannelProperty && IsLoaded) Attach();
        if (change.Property == TerminalFontSizeProperty) _terminal.FontSize = TerminalFontSize;
        if (change.Property == TerminalFontFamilyProperty) _terminal.FontFamily = new FontFamily(TerminalFontFamily);
        if (change.Property == HighlightTextProperty) ApplyHighlightSettings();
    }
    private void OnTerminalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        e.Handled = true;
    }

    private void OnTerminalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || ContextMenu is not { IsOpen: false } menu) return;
        menu.Open(this);
        e.Handled = true;
    }

    private void OnTerminalContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ContextMenu is not { IsOpen: false } menu) return;
        menu.Open(this);
        e.Handled = true;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "复制选中内容" };
        copy.Click += async (_, _) => await CopyAsync();
        var paste = new MenuItem { Header = "粘贴剪贴板内容" };
        paste.Click += async (_, _) => await PasteAsync();
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        return menu;
    }

    private void Attach()
    {
        ApplyHighlightSettings();
        if (ReferenceEquals(_attached, Channel)) return;
        _terminal.DetachConnection(); _attached = Channel;
        if (Channel != null)
        {
            _terminal.AttachConnection(new RemotePtyConnection(Channel)); _terminal.Focus();
        }
    }

    /// <summary>着色由通道负责，控件只把开关和主题同步过去。</summary>
    private void ApplyHighlightSettings()
    {
        if (Channel is ITerminalHighlighting highlighting)
        {
            highlighting.HighlightEnabled = HighlightText;
            highlighting.LightTheme = ActualThemeVariant == ThemeVariant.Light;
        }
    }
}

internal sealed class RemotePtyConnection(ITerminalChannel terminal) : IPtyConnection
{
    public event EventHandler<PtyExitedEventArgs>? ProcessExited { add { } remove { } }
    public Stream ReaderStream => terminal.Stream;
    // 声明可取消读取：控件销毁时库才能叫醒阻塞中的读线程并退出，
    // 否则旧读线程会一直挂着，和新控件的读线程抢同一个通道，把字节流撕成两半。
    public bool SupportsCancellableRead => true;
    public Stream WriterStream => terminal.Stream;
    public int Pid => -1;
    public int ExitCode => 0;
    public bool WaitForExit(int milliseconds) => false;
    public void Kill() { }
    public void Resize(int cols, int rows) { try { terminal.Resize(cols, rows); } catch (ObjectDisposedException) { } }
    public void Dispose() { /* 会话由 Workspace 持有，控件不重复释放。 */ }
}
