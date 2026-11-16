using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Desktop.Features.Shell;
using ShellX.Core.Services;
using ShellX.Desktop.Features.Ports;
using ShellX.Desktop.Features.Services;
using ShellX.Desktop.Features.Tunnels;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Workspace;
public partial class WorkspaceView : UserControl
{
    private WorkspaceTabViewModel? _vm;
    private ServiceWindow? _serviceWindow;
    private PortUsageWindow? _portsWindow;
    private TunnelWindow? _tunnelWindow;

    public WorkspaceView()
    {
        InitializeComponent();
        Terminal.AddHandler(PointerWheelChangedEvent, TerminalWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => { if (_vm != null) _vm.Disposed -= VmDisposed; _vm = null; };
    }

    private void Subscribe()
    {
        if (_vm != null) _vm.Disposed -= VmDisposed;
        _vm = DataContext as WorkspaceTabViewModel;
        if (_vm != null) _vm.Disposed += VmDisposed;
    }

    // 标签被关闭时会话已经释放，同步关掉该连接的服务窗口与端口窗口。
    private void VmDisposed(object? sender, EventArgs e)
    {
        _serviceWindow?.Close();
        _portsWindow?.Close();
        _tunnelWindow?.Close();
    }

    private async void CommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        if (e.Key == Key.Enter) { e.Handled = true; await _vm.Commands.SendCommand.ExecuteAsync(null); }
        else if (e.Key == Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = true; _vm.Commands.PreviousHistoryCommand.Execute(null); }
        else if (e.Key == Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = true; _vm.Commands.NextHistoryCommand.Execute(null); }
        else if (e.Key == Key.Escape) { e.Handled = true; Terminal.FocusTerminal(); }
    }
    private void TerminalWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm == null || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0) return;
        if (e.Delta.Y > 0) _vm.IncreaseFontCommand.Execute(null);
        else _vm.DecreaseFontCommand.Execute(null);
        e.Handled = true;
    }
    /// <summary>端口转发面板的“服务”入口：非模态窗口，同一连接重复打开时激活已有窗口。</summary>
    private void OpenServices(object? sender, RoutedEventArgs e)
    {
        if (_vm is not { Session: { } session }) return;
        if (_serviceWindow is { IsVisible: true }) { _serviceWindow.Activate(); return; }
        var target = $"{_vm.Name} · {_vm.Profile.Host}:{_vm.Profile.Port}";
        var viewModel = new ServiceWindowViewModel(session.Services, target,
            message => _vm.Dialogs.ConfirmAsync("服务操作", message, true));
        var window = new ServiceWindow(viewModel, session);
        _serviceWindow = window;
        window.Closed += (_, _) => { _vm.Dialogs.UntrackOwnerWindow(window); if (ReferenceEquals(_serviceWindow, window)) _serviceWindow = null; };
        _vm.Dialogs.TrackOwnerWindow(window);
        if (TopLevel.GetTopLevel(this) is Window owner) _serviceWindow.Show(owner);
        else _serviceWindow.Show();
    }
    private void OpenPorts(object? sender, RoutedEventArgs e)
    {
        if (_vm is not { Session: { } session }) return;
        if (_portsWindow is { IsVisible: true }) { _portsWindow.Activate(); return; }
        var target = $"{_vm.Name} · {_vm.Profile.Host}:{_vm.Profile.Port}";
        var viewModel = new PortUsageWindowViewModel(session.Services as IPortUsageReader, target);
        var window = new PortUsageWindow(viewModel, session);
        _portsWindow = window;
        window.Closed += (_, _) => { _vm.Dialogs.UntrackOwnerWindow(window); if (ReferenceEquals(_portsWindow, window)) _portsWindow = null; };
        _vm.Dialogs.TrackOwnerWindow(window);
        if (TopLevel.GetTopLevel(this) is Window owner) _portsWindow.Show(owner);
        else _portsWindow.Show();
    }
    /// <summary>端口转发的独立窗口：非模态，同一连接重复打开时激活已有窗口。</summary>
    private void OpenTunnels(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (_tunnelWindow is { IsVisible: true }) { _tunnelWindow.Activate(); return; }
        var target = $"{_vm.Name} · {_vm.Profile.Host}:{_vm.Profile.Port}";
        var viewModel = new TunnelWindowViewModel(_vm, target);
        var window = new TunnelWindow(viewModel);
        _tunnelWindow = window;
        window.Closed += (_, _) => { _vm.Dialogs.UntrackOwnerWindow(window); if (ReferenceEquals(_tunnelWindow, window)) _tunnelWindow = null; };
        _vm.Dialogs.TrackOwnerWindow(window);
        if (TopLevel.GetTopLevel(this) is Window owner) _tunnelWindow.Show(owner);
        else _tunnelWindow.Show();
    }
    /// <summary>工具栏上的文件夹图标：显示 / 隐藏右侧的远程文件面板。</summary>
    private void ToggleFilePane(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel manager)
            manager.ToggleFilePaneCommand.Execute(null);
    }
    private async void CopyTerminal(object? sender, RoutedEventArgs e) => await Terminal.CopyAsync();
    private async void PasteTerminal(object? sender, RoutedEventArgs e) => await Terminal.PasteAsync();
    private void ClearTerminal(object? sender, RoutedEventArgs e) => Terminal.ClearScreen();
    private void UseHistory(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: string command }) return;
        _vm.Commands.FillCommand.Execute(command); HistoryButton.Flyout?.Hide(); CommandInput.Focus();
    }
}
