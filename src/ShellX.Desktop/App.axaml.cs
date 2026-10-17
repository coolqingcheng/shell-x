using Avalonia;
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
            _services.GetRequiredService<AppLockCoordinator>().Protect(window);
            _services.GetRequiredService<DialogService>().Owner = window;
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                await vm.InitializeAsync();
                await vm.ToggleConnectionsCommand.ExecuteAsync(null);
            };
            desktop.Exit += (_, _) => { _services.Dispose(); Log.CloseAndFlush(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
