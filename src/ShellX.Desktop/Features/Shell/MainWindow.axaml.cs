using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShellX.Core.Connections;
using ShellX.Core.Settings;
using ShellX.Desktop.Services;
using ShellX.Desktop.Styles;

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
        // 右侧远程文件面板同样保留一条窄把手，收起后还能点开。
        var fileCollapsed = settings.FilePaneCollapsed;
        RootLayout.ColumnDefinitions[4].Width = new GridLength(
            fileCollapsed ? CollapsedPaneWidth : Math.Clamp(settings.FilePaneWidth, 240, 520), GridUnitType.Pixel);
        RootLayout.ColumnDefinitions[3].Width = new GridLength(fileCollapsed ? 0 : 4, GridUnitType.Pixel);
        FileSplitter.IsVisible = !fileCollapsed;
    }
    private async void FileSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (_layoutVm != null) await _layoutVm.SaveFilePaneWidthAsync(RootLayout.ColumnDefinitions[4].Width.Value);
    }
    private async void MonitorSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (_layoutVm != null) await _layoutVm.SaveMonitorPaneWidthAsync(RootLayout.ColumnDefinitions[0].Width.Value);
    }
    private void ShowMcpLog(object? sender, RoutedEventArgs e)
    {
        if (_mcpLogWindow is { IsVisible: true }) { _mcpLogWindow.Activate(); return; }
        var window = new McpLogWindow();
        _mcpLogWindow = window;
        window.Closed += (_, _) => { (DataContext as MainWindowViewModel)?.Dialogs.UntrackOwnerWindow(window); if (ReferenceEquals(_mcpLogWindow, window)) _mcpLogWindow = null; };
        if (DataContext is MainWindowViewModel vm) vm.Dialogs.TrackOwnerWindow(window);
        window.Show(this);
    }
    /// <summary>右上角主题按钮：列出全部主题，选中后立即生效并保存。</summary>
    private void ShowThemeMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();
        foreach (var theme in ThemeCatalog.All)
        {
            var item = new MenuItem
            {
                Header = BuildThemeEntry(ThemeCatalog.Of(theme)),
                Tag = theme,
                IsChecked = theme == ThemeService.Current
            };
            item.Click += async (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm && item.Tag is AppTheme selected)
                    await vm.ApplyThemeAsync(selected);
            };
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        menu.Open(button);
    }

    /// <summary>主题菜单项：左边一个取色块，右边是主题名。</summary>
    private static Control BuildThemeEntry(ThemePalette palette)
    {
        var swatch = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.Parse(palette.Ui[ThemeCatalog.AccentForeground])),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var text = new TextBlock
        {
            Text = palette.DisplayName,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        panel.Children.Add(swatch);
        panel.Children.Add(text);
        return panel;
    }

    private async void EditConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.EditConnectionCommand.ExecuteAsync(profile); }
    private async void DeleteConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.DeleteConnectionCommand.ExecuteAsync(profile); }
}
