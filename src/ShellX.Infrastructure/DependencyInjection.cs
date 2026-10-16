using Microsoft.Extensions.DependencyInjection;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services) => services
        .AddSingleton<AppPaths>()
        .AddSingleton<IConnectionRepository, JsonConnectionRepository>()
        .AddSingleton<ISettingsStore, JsonSettingsStore>()
        .AddSingleton<IConfigurationExporter, JsonConfigurationExporter>()
        .AddSingleton<ICredentialStore, WindowsCredentialStore>()
        .AddSingleton<IHostKeyStore, JsonHostKeyStore>()
        .AddSingleton<ILocalFileSystem, LocalFileSystem>()
        .AddSingleton<ITextCodec, TextCodec>()
        .AddSingleton<ILockPasswordHasher, LockPasswordHasher>()
        .AddSingleton<SharedTunnelRegistry>()
        .AddSingleton<IConnectionTester, SshConnectionTester>()
        .AddSingleton<IRemoteSessionFactory, SshSessionFactory>();
}
