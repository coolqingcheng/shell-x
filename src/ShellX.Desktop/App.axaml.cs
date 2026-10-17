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
            var manager = new ConnectionManagerWindow(vm, profile =>
            {
                var window = new MainWindow { DataContext = vm };
                lockCoordinator.Protect(window);
                dialogs.Owner = window;
                desktop.MainWindow = window;
                window.Show();
                _ = vm.ConnectCommand.ExecuteAsync(profile);
            });
            lockCoordinator.Protect(manager);
            dialogs.Owner = manager;
            desktop.MainWindow = manager;
            manager.Opened += async (_, _) => await vm.InitializeAsync();
            try { mcpServer.Start(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "MCP HTTP 启动失败"); }
            desktop.Exit += (_, _) => { mcpServer.Dispose(); _services.Dispose(); Log.CloseAndFlush(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
