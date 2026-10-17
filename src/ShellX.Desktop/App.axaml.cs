using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ShellX.Desktop.Composition;
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
            var window = new MainWindow { DataContext = vm };
            var lockState = _services.GetRequiredService<ShellX.Desktop.Features.Security.AppLockService>();
            _services.GetRequiredService<AppLockCoordinator>().Protect(window);
            _services.GetRequiredService<DialogService>().Owner = window;
            desktop.MainWindow = window;
            var startup = true;
            var startupManagerOpen = false;
            async Task OpenStartupManagerAsync()
            {
                if (!startup || startupManagerOpen || lockState.IsLocked) return;
                startupManagerOpen = true;
                try
                {
                    await vm.ToggleConnectionsCommand.ExecuteAsync(null);
                    if (startup && !vm.HasTabs && !lockState.IsLocked) window.Close();
                }
                finally { startupManagerOpen = false; }
            }
            lockState.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ShellX.Desktop.Features.Security.AppLockService.IsLocked) && !lockState.IsLocked)
                    _ = OpenStartupManagerAsync();
            };
            vm.ConnectionPicked += _ =>
            {
                startup = false;
                window.Opacity = 1;
                window.ShowInTaskbar = true;
                window.WindowState = WindowState.Normal;
                window.Show();
            };
            window.Opacity = 0;
            window.ShowInTaskbar = false;
            window.WindowState = WindowState.Normal;
            window.Opened += async (_, _) =>
            {
                await vm.InitializeAsync();
                await OpenStartupManagerAsync();
            };
            desktop.Exit += (_, _) => { _services.Dispose(); Log.CloseAndFlush(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
