using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform;
using ShellX.Desktop.Features.Security;

namespace ShellX.Desktop.Services;

/// <summary>
/// 系统托盘图标：窗口的关闭按钮可以最小化到托盘，托盘菜单可恢复窗口或退出程序。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly AppLockService _settings;
    private MemoryStream? _iconStream;
    private TrayIcon? _icon;
    private Action? _restore;
    private Action? _showManager;
    private Action? _exit;
    private bool _initialized;

    public TrayIconService(AppLockService settings) => _settings = settings;

    /// <summary>点击关闭按钮是否最小化到系统托盘；托盘图标创建失败时自动关闭该行为，避免进程变成后台僵尸。</summary>
    public bool CloseToTrayEnabled => _settings.Settings.CloseToTray && (!_initialized || _icon is not null);

    /// <summary>创建托盘图标；restore 用于恢复主窗口，showManager 打开连接管理器，exit 退出程序。</summary>
    public void Initialize(Action restore, Action showManager, Action exit)
    {
        _restore = restore; _showManager = showManager; _exit = exit;
        _settings.SettingsChanged += OnSettingsChanged;
        CreateIcon();
        _initialized = true;
    }

    /// <summary>把窗口隐藏到托盘（不关闭，保留会话）。</summary>
    public static void HideToTray(Window window) => window.Hide();

    private void OnSettingsChanged()
    {
        // 设置变化时只更新提示文案，托盘图标本身常驻。
        if (_icon is not null) _icon.ToolTipText = ToolTip;
    }

    private string ToolTip => $"ShellX v{Core.AppVersion.Value}";

    private void CreateIcon()
    {
        if (_icon is not null) return;
        try
        {
            var uri = new Uri($"avares://{typeof(TrayIconService).Assembly.GetName().Name}/Assets/ShellX.ico");
            using var source = AssetLoader.Open(uri);
            var buffer = new MemoryStream();
            source.CopyTo(buffer);
            buffer.Position = 0;
            _iconStream = buffer;
            var menu = new NativeMenu();
            var showItem = new NativeMenuItem("显示 ShellX");
            showItem.Click += (_, _) => _restore?.Invoke();
            var managerItem = new NativeMenuItem("连接管理器");
            managerItem.Click += (_, _) => _showManager?.Invoke();
            var exitItem = new NativeMenuItem("退出 ShellX");
            exitItem.Click += (_, _) => _exit?.Invoke();
            menu.Add(showItem);
            menu.Add(managerItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);
            var icon = new TrayIcon { Icon = new WindowIcon(buffer), ToolTipText = ToolTip, IsVisible = true, Menu = menu };
            icon.Clicked += (_, _) => _restore?.Invoke();
            if (Application.Current is { } app) TrayIcon.SetIcons(app, new TrayIcons { icon });
            _icon = icon;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "系统托盘图标创建失败，关闭按钮将直接退出程序。"); }
    }

    public void Dispose()
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        _icon?.Dispose();
        _icon = null;
        _iconStream?.Dispose();
        _iconStream = null;
    }
}
