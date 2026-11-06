using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Services;
using ShellX.Desktop.Services;
using ShellX.Desktop.Features.Shell;

namespace ShellX.Desktop.Features.Workspace;
public partial class WorkspaceView : UserControl
{
    private WorkspaceTabViewModel? _vm;
    private GridLength _top = new(1.3, GridUnitType.Star);
    private GridLength _bottom = new(1, GridUnitType.Star);
    private bool _maximized;
    private MainWindowViewModel? _layoutVm;
    private ServiceWindow? _serviceWindow;
    private Window? _fileWindow;
    public WorkspaceView()
    {
        InitializeComponent();
        Terminal.AddHandler(PointerWheelChangedEvent, TerminalWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => { if (_vm != null) { _vm.PropertyChanged -= VmChanged; _vm.Disposed -= VmDisposed; } _vm = null; DetachLayout(); };
    }
    private void Subscribe()
    {
        if (_vm != null) { _vm.PropertyChanged -= VmChanged; _vm.Disposed -= VmDisposed; }
        _vm = DataContext as WorkspaceTabViewModel;
        if (_vm != null) { _vm.PropertyChanged += VmChanged; _vm.Disposed += VmDisposed; UpdateLayoutMode(); AttachLayout(); }
    }
    // 标签被关闭时会话已经释放，同步关掉该连接的服务窗口与文件管理器窗口。
    private void VmDisposed(object? sender, EventArgs e)
    {
        _serviceWindow?.Close();
        _fileWindow?.Close();
    }
    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(WorkspaceTabViewModel.IsTerminalMaximized)) UpdateLayoutMode(); }
    private void AttachLayout()
    {
        var vm = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
        if (ReferenceEquals(_layoutVm, vm)) return;
        DetachLayout();
        _layoutVm = vm;
        if (_layoutVm == null) return;
        _layoutVm.LayoutChanged += ApplyLayout;
        ApplyLayout(_layoutVm.LayoutSettings);
    }
    private void DetachLayout()
    {
        if (_layoutVm != null) _layoutVm.LayoutChanged -= ApplyLayout;
        _layoutVm = null;
    }
    private void ApplyLayout(AppSettings settings)
    {
        if (_maximized)
        {
            _maximized = false;
            if (_vm != null) _vm.IsTerminalMaximized = false;
            WorkspaceLayout.RowDefinitions[1].Height = new(5);
        }
        _top = new(Math.Clamp(settings.TerminalPaneStar, 0.3, 8), GridUnitType.Star);
        _bottom = new(1, GridUnitType.Star);
        WorkspaceLayout.RowDefinitions[0].Height = _top;
        WorkspaceLayout.RowDefinitions[2].Height = _bottom;
    }
    private async void WorkspaceSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (_layoutVm == null || _maximized) return;
        var top = WorkspaceLayout.RowDefinitions[0].Height;
        var bottom = WorkspaceLayout.RowDefinitions[2].Height;
        await _layoutVm.SaveWorkspaceLayoutAsync(top.GridUnitType == GridUnitType.Star ? top.Value : 1.3,
            bottom.GridUnitType == GridUnitType.Star ? bottom.Value : 1);
    }

    private void UpdateLayoutMode()
    {
        var maximize = _vm?.IsTerminalMaximized == true;
        if (maximize == _maximized) return;
        if (maximize)
        {
            _top = WorkspaceLayout.RowDefinitions[0].Height; _bottom = WorkspaceLayout.RowDefinitions[2].Height;
            WorkspaceLayout.RowDefinitions[0].Height = new(1, GridUnitType.Star);
            WorkspaceLayout.RowDefinitions[1].Height = new(0); WorkspaceLayout.RowDefinitions[2].Height = new(0);
        }
        else
        {
            WorkspaceLayout.RowDefinitions[0].Height = _top; WorkspaceLayout.RowDefinitions[1].Height = new(5); WorkspaceLayout.RowDefinitions[2].Height = _bottom;
        }
        _maximized = maximize;
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
        _serviceWindow = new ServiceWindow(viewModel, session);
        _serviceWindow.Closed += (_, _) => _serviceWindow = null;
        if (TopLevel.GetTopLevel(this) is Window owner) _serviceWindow.Show(owner);
        else _serviceWindow.Show();
    }
    private void OpenFileManager(object? sender, RoutedEventArgs e)
    {
        if (_vm is not { Files: var files } vm) return;
        if (_fileWindow is { IsVisible: true }) { _fileWindow.Activate(); return; }
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var window = new Window
        {
            Title = $"文件管理 · {vm.Name} · {vm.Profile.Host}",
            Width = 1180, Height = 760, MinWidth = 780, MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = owner.Icon,
            Content = new FileBrowserView { DataContext = files }
        };
        WindowTitles.ApplyVersion(window);
        _fileWindow = window;
        window.Closed += (_, _) => _fileWindow = null;
        window.Show(owner);
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
