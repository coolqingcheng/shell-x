using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Services;
using ShellX.Desktop.Features.Security;
using ShellX.Infrastructure;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Composition;

internal static class ServiceRegistration
{
    public static ServiceProvider Create()
    {
        var paths = new AppPaths();
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
            .WriteTo.File(paths.File("logs/shellx-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14, fileSizeLimitBytes: 5 * 1024 * 1024, rollOnFileSizeLimit: true).CreateLogger();
        return new ServiceCollection().AddInfrastructure().AddSingleton(paths)
            .AddLogging(builder => builder.ClearProviders().AddSerilog())
            .AddSingleton<AppLockService>().AddSingleton<AppLockCoordinator>()
            .AddSingleton<DialogService>().AddSingleton<IDialogService>(s => s.GetRequiredService<DialogService>())
            .AddSingleton<McpAuditLog>().AddSingleton<LocalCommandExecutor>().AddSingleton<McpHttpServer>()
            .AddSingleton<MainWindowViewModel>().BuildServiceProvider();
    }
}
