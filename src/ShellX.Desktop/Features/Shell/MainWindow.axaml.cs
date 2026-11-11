using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Connections;
using ShellX.Core.Settings;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Shell;

public partial class MainWindow : Window
{
    private bool _shutdown;
    private bool _closing;
    private MainWindowViewModel? _layoutVm;
    private McpLogWindow? _mcpLogWindow;
    private readonly TrayIconService? _tray;
    public MainWindow() : this(null) { }
    public MainWindow(TrayIconService? tray)
    {
        InitializeComponent();
        
ShellX.Desktop.Services.WindowTitles.ApplyVersion(this);
        _tray = tray;
        DataContextChanged += (_, _) => AttachLayout();
        Opened += (_, _) => AttachLayout();
        Closing += async (_, e) =>
        {
            if (_shutdown) return; e.Cancel = true;
            // 开启“关闭到托盘”时，点击关闭按钮只隐藏窗口，SSH 连接保持不断开。
            if (_tray is { CloseToTrayEnabled: true } && !e.IsProgrammatic) { Hide(); return; }
            if (_closing || DataContext is not MainWindowViewModel vm) return;
            _closing = true;
            _mcpLogWindow?.Close();
            try { if (await vm.ShutdownAsync()) { _shutdown = true; Close(); } }
            finally { _closing = false; }
        };
    }
    private void AttachLayout()
    {
        if (ReferenceEquals(_layoutVm, DataContext)) return;
        if (_layoutVm != null) _layoutVm.LayoutChanged -= ApplyLayout;
        _layoutVm = DataContext as MainWindowViewModel;
        if (_layoutVm == null) return;
        _layoutVm.LayoutChanged += ApplyLayout;
        ApplyLayout(_layoutVm.LayoutSettings);
    }
    /// <summary>面板收起后保留的窄把手宽度。</summary>
    private const double CollapsedPaneWidth = 26;

    private void ApplyLayout(AppSettings settings)
    {
        var collapsed = settings.MonitorPaneCollapsed;
        // 收起时压缩成一条窄把手并隐藏分隔条；展开后恢复此前的宽度。
        RootLayout.ColumnDefinitions[0].Width = new GridLength(
            collapsed ? CollapsedPaneWidth : Math.Clamp(settings.MonitorPaneWidth, 160, 420), GridUnitType.Pixel);
        RootLayout.ColumnDefinitions[1].Width = new GridLength(collapsed ? 0 : 4, GridUnitType.Pixel);
        MonitorSplitter.IsVisible = !collapsed;
    }
    private async void MonitorSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (_layoutVm != null) await _layoutVm.SaveMonitorPaneWidthAsync(RootLayout.ColumnDefinitions[0].Width.Value);
    }
    private void ShowMcpLog(object? sender, RoutedEventArgs e)
    {
        if (_mcpLogWindow is { IsVisible: true }) { _mcpLogWindow.Activate(); return; }
        _mcpLogWindow = new McpLogWindow();
        _mcpLogWindow.Closed += (_, _) => _mcpLogWindow = null;
        _mcpLogWindow.Show(this);
    }
    private async void EditConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.EditConnectionCommand.ExecuteAsync(profile); }
    private async void DeleteConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.DeleteConnectionCommand.ExecuteAsync(profile); }
}
