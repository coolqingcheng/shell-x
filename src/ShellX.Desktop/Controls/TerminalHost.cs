using Avalonia;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Iciclecreek.Terminal;
using Porta.Pty;
using Avalonia.Styling;
using ShellX.Desktop.Services;
using ShellX.Core.Sessions;
using ShellX.Desktop.Styles;
using XTerm.Options;

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
        AllowWindowOps = false, Process = "",
        // 终端的 ANSI 配色也要跟主题走：整体赋值，模拟器构造时会用这份调色板。
        Options = new TerminalOptions { Theme = ThemeCatalog.TerminalTheme(ThemeService.Current) }
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
        // Esc 由我们自己发，详见 OnTerminalKeyDown 的说明。
        AddHandler(InputElement.KeyDownEvent, OnTerminalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyUpEvent, OnTerminalKeyUp, RoutingStrategies.Tunnel);
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

    /// <summary>
    /// Esc 走标准 VT 序列，不走终端库的 Win32 输入记录。
    /// </summary>
    /// <remarks>
    /// 终端库在 Windows 上会把 Esc 编码成 Win32 INPUT_RECORD（ESC [ 27;0;27;1;…;1 _），
    /// 因为本机 ConPTY 没有单独表示 Esc 的 VT 序列，只能用输入记录传。
    /// 但这里的 PTY 是 SSH 过去的远端 shell，不是 ConPTY，它读不懂这种记录：
    /// 远端把收到的字节原样回显，命令行里就会多出一串裸参数（;0;27;1;32;1_）。
    /// 所以这里在隧道阶段拦下按键，标记为已处理，库不会再发那条记录，改由我们发标准的 0x1B。
    /// </remarks>
    private void OnTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _ = SendEscapeAsync();
    }

    /// <summary>松开按键时库还会补一条 Win32 记录（Kd=0），必须一起拦掉，否则远端照样回显裸参数。</summary>
    private void OnTerminalKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
    }

    private async Task SendEscapeAsync()
    {
        try { await _terminal.SendInputAsync("\u001b").ConfigureAwait(false); }
        catch (Exception) { /* 会话已经断开时忽略：Esc 没有别的地方可去。 */ }
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
            highlighting.Theme = ThemeService.Current;
        }
        ApplyTerminalTheme();
    }

    /// <summary>终端的 ANSI 配色跟随主题。必须整体赋值新对象，模拟器才会重新播种调色板。</summary>
    private void ApplyTerminalTheme()
    {
        var theme = ThemeService.Current;
        if (_terminal.Options is { } options) options.Theme = ThemeCatalog.TerminalTheme(theme);
        var palette = ThemeCatalog.Of(theme).Terminal;
        var selection = Color.Parse(palette.Selection);
        _terminal.SelectionBrush = new ImmutableSolidColorBrush(new Color(0x80, selection.R, selection.G, selection.B));
        _terminal.CursorColor = Color.Parse(palette.Cursor);
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
