using Avalonia.Controls;
using Avalonia.Interactivity;
using ShellX.Core.Connections;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Connections;

public partial class ConnectionManagerWindow : Window
{
    private readonly MainWindowViewModel? _model;
    private readonly Action<ConnectionProfile>? _picked;
    private bool _completed;
    private readonly TrayIconService? _tray;
    public ConnectionManagerWindow() : this(null, null, null) { }
    public ConnectionManagerWindow(MainWindowViewModel? model, Action<ConnectionProfile>? picked, TrayIconService? tray = null)
    {
        InitializeComponent();
        
ShellX.Desktop.Services.WindowTitles.ApplyVersion(this);
        _model = model;
        _picked = picked;
        _tray = tray;
        if (model != null) DataContext = model;
        if (_model != null) _model.ConnectionPicked += Pick;
        Closed += (_, _) => { if (_model != null) _model.ConnectionPicked -= Pick; };
        Opened += (_, _) => _completed = false;
        Closing += (_, e) =>
        {
            // 开启“关闭到托盘”时，点击关闭按钮只隐藏窗口，程序继续在托盘运行。
            if (_tray is not { CloseToTrayEnabled: true } || e.IsProgrammatic) return;
            e.Cancel = true; Hide();
        };
    }
    private void Pick(ConnectionProfile profile)
    {
        if (_completed || _picked == null) return;
        _completed = true;
        _picked(profile);
        // 托盘模式下隐藏而不是销毁窗口，便于从托盘再次唤起连接管理器。
        if (_tray is { CloseToTrayEnabled: true }) Hide(); else Close();
    }
    private void CloseWindow(object? sender, RoutedEventArgs e) { if (_tray is { CloseToTrayEnabled: true }) Hide(); else Close(); }
}