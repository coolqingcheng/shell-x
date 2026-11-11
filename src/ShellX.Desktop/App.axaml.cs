using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ShellX.Desktop.Composition;
using ShellX.Desktop.Features.Connections;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Services;
using ShellX.Desktop.Features.Security;

namespace ShellX.Desktop;

public sealed partial class App : Application
{
    private ServiceProvider? _services;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = ServiceRegistration.Create();
            var vm = _services.GetRequiredService<MainWindowViewModel>();
            var dialogs = _services.GetRequiredService<DialogService>();
            var lockCoordinator = _services.GetRequiredService<AppLockCoordinator>();
            var mcpServer = _services.GetRequiredService<McpHttpServer>();
            var updates = _services.GetRequiredService<UpdateManager>();
            var lockState = _services.GetRequiredService<ShellX.Desktop.Features.Security.AppLockService>();
            var tray = new TrayIconService(lockState);
            ConnectionManagerWindow? manager = null;
            var started = false;
            MainWindow? workspace = null;

            ConnectionManagerWindow CreateManager(bool takeDialogOwner)
            {
                var window = new ConnectionManagerWindow(vm, profile =>
                {
                    var main = new MainWindow(tray) { DataContext = vm };
                    lockCoordinator.Protect(main);
                    dialogs.Owner = main;
                    desktop.MainWindow = main;
                    workspace = main;
                    main.Show();
                    _ = vm.ConnectCommand.ExecuteAsync(profile);
                }, tray);
                lockCoordinator.Protect(window);
                if (takeDialogOwner) dialogs.Owner = window;
                window.Closed += (_, _) => { if (ReferenceEquals(manager, window)) manager = null; };
                window.Opened += async (_, _) =>
                {
                    if (started) return;
                    started = true;
                    try { await mcpServer.StartAsync(); }
                    catch (Exception ex) { Serilog.Log.Error(ex, "MCP HTTP 启动失败"); }
                    await vm.InitializeAsync();
                    await CheckUpdateOnStartupAsync(updates, vm.LayoutSettings, dialogs);
                };
                manager = window;
                return window;
            }

            void Restore()
            {
                if (workspace is { } main)
                {
                    if (!main.IsVisible) main.Show();
                    main.Activate();
                    return;
                }
                ShowManager();
            }

            void ShowManager()
            {
                var window = manager ?? CreateManager(false);
                if (!window.IsVisible) window.Show();
                window.Activate();
            }

            async void ExitApplication()
            {
                try { await vm.ShutdownAsync(); }
                catch (Exception ex) { Serilog.Log.Error(ex, "退出前清理失败"); }
                desktop.Shutdown();
            }

            manager = CreateManager(true);
            desktop.MainWindow = manager;
            lockState.SettingsChanged += () =>
            {
                try { mcpServer.ApplySettings(lockState.Settings); mcpServer.Restart(); }
                catch (Exception ex) { Serilog.Log.Error(ex, "MCP HTTP 配置更新失败"); }
            };
            tray.Initialize(Restore, ShowManager, ExitApplication);
            // 重复启动时把已在运行的窗口带到前台，不再开第二个实例。
            Program.ActivateRequested = Restore;
            Program.ListenSingleInstance();
            desktop.Exit += (_, _) => { updates.ApplyOnExit(); mcpServer.Dispose(); tray.Dispose(); _services.Dispose(); Log.CloseAndFlush(); };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CheckUpdateOnStartupAsync(UpdateManager updates, Core.Settings.AppSettings settings, IDialogService dialogs)
    {
        if (!settings.CheckUpdateOnStartup) return;
        try
        {
            var release = await updates.CheckAsync(settings.UpdateRepositoryOwner, settings.UpdateRepositoryName);
            if (release is null || !updates.UpdateAvailable) return;
            if (await updates.DownloadAsync(release) is null) return;
            var apply = await dialogs.ConfirmAsync("发现新版本", updates.BuildUpdatePrompt(release));
            if (apply) updates.ApplyAndRestart(); else updates.ClearPending();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "启动时检查更新失败"); }
    }
}
