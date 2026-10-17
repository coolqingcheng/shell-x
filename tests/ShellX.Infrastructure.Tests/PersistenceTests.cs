using System.Text;
using ShellX.Core.Connections;
using ShellX.Core.Settings;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;

namespace ShellX.Infrastructure.Tests;
public sealed class PersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShellX-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task ConnectionConfigurationHasNoCredentialFields()
    {
        var paths = new AppPaths(_root); var store = new JsonConnectionRepository(paths);
        var profile = new ConnectionProfile { Name = "测试服务器", Host = "localhost" };
        await store.SaveAsync([profile]);
        Assert.Equal(profile.Id, Assert.Single(await store.LoadAsync()).Id);
        var json = await File.ReadAllTextAsync(paths.File("connections.json"));
        Assert.DoesNotContain("Password", json); Assert.DoesNotContain("Passphrase", json);
        Assert.False(File.Exists(paths.File("connections.json.tmp")));
    }
    [Fact]
    public async Task TrustingHostsConcurrentlyPreservesAllEntries()
    {
        var store = new JsonHostKeyStore(new(_root));
        await Task.WhenAll(Enumerable.Range(1, 20).Select(i => store.TrustAsync(new($"host{i}", 22, "test", $"SHA256:{i}"))));
        for (var i = 1; i <= 20; i++) Assert.Equal($"SHA256:{i}", await store.GetAsync($"HOST{i}", 22));
    }
    [Fact]
    public async Task CorruptConfigurationIsNotSilentlyOverwritten()
    {
        var paths = new AppPaths(_root); var original = "{ broken-json"; await File.WriteAllTextAsync(paths.File("connections.json"), original);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new JsonConnectionRepository(paths).LoadAsync());
        Assert.Equal(original, await File.ReadAllTextAsync(paths.File("connections.json")));
    }
    [Fact]
    public async Task OlderConnectionFilesRetainDefaultsAndNestedDefinitions()
    {
        var paths = new AppPaths(_root);
        await File.WriteAllTextAsync(paths.File("connections.json"), """
            {"Version":1,"Connections":[{"Host":"localhost","Tunnels":[{"Name":"legacy"}],"QuickCommands":[{"Text":"pwd"}]}]}
            """);
        var profile = Assert.Single(await new JsonConnectionRepository(paths).LoadAsync());
        Assert.Equal(22, profile.Port); Assert.Equal("root", profile.Username); Assert.Equal(".", profile.InitialDirectory);
        Assert.Equal("", profile.AccentColor);
        Assert.NotEqual(Guid.Empty, profile.Id); Assert.NotNull(profile.QuickCommands);
        var tunnel = Assert.Single(profile.Tunnels);
        Assert.Equal(3307, tunnel.BindPort); Assert.Equal("127.0.0.1", tunnel.TargetHost);
        Assert.NotEqual(Guid.Empty, Assert.Single(profile.QuickCommands).Id);
        profile = profile with { AccentColor = "#BF6969" };
        await new JsonConnectionRepository(paths).SaveAsync([profile]);
        var restored = Assert.Single(await new JsonConnectionRepository(paths).LoadAsync());
        Assert.Equal(profile.Id, restored.Id); Assert.Equal(tunnel, Assert.Single(restored.Tunnels));
        Assert.Equal("#BF6969", restored.AccentColor);
    }
    [Fact]
    public async Task ProcessSortPersistsAndOlderSettingsKeepDefaults()
    {
        var paths = new AppPaths(_root); var store = new JsonSettingsStore(paths);
        await File.WriteAllTextAsync(paths.File("settings.json"), "{\"TerminalFontSize\":16}");
        var old = await store.LoadAsync();
        Assert.Equal(AppTheme.Dark, old.Theme);
        Assert.Equal(ProcessSortColumn.Cpu, old.ProcessSortColumn); Assert.True(old.ProcessSortDescending);
        Assert.True(old.HighlightTerminalText); Assert.Equal(1, old.Version);
        Assert.True(old.AutoReconnectEnabled); Assert.Equal(5, old.ReconnectIntervalSeconds);
        Assert.Equal(220, old.MonitorPaneWidth); Assert.Equal(1.3, old.TerminalPaneStar);
        Assert.Equal(1, old.LocalPaneStar); Assert.Equal(150, old.RemoteTreeWidth);
        await store.SaveAsync(old with { Theme = AppTheme.Light, ProcessSortColumn = ProcessSortColumn.Memory, ProcessSortDescending = false, AutoReconnectEnabled = false, ReconnectIntervalSeconds = 27, MonitorPaneWidth = 310, TerminalPaneStar = 2.4, LocalPaneStar = 0.8, RemoteTreeWidth = 185 });
        var restored = await new JsonSettingsStore(paths).LoadAsync();
        Assert.Equal(ProcessSortColumn.Memory, restored.ProcessSortColumn); Assert.False(restored.ProcessSortDescending);
        Assert.Equal(16, restored.TerminalFontSize);
        Assert.Equal(AppTheme.Light, restored.Theme);
        Assert.False(restored.AutoReconnectEnabled); Assert.Equal(27, restored.ReconnectIntervalSeconds);
        Assert.Equal(310, restored.MonitorPaneWidth); Assert.Equal(2.4, restored.TerminalPaneStar);
        Assert.Equal(0.8, restored.LocalPaneStar); Assert.Equal(185, restored.RemoteTreeWidth);
    }
    [Fact]
    public async Task ConnectionCatalogKeepsEmptyNestedFoldersAcrossLegacyConnectionSaves()
    {
        var paths = new AppPaths(_root); var store = new JsonConnectionRepository(paths);
        await File.WriteAllTextAsync(paths.File("connections.json"), """{"Version":1,"Connections":[{"Name":"legacy","Host":"localhost","Group":"生产/华东"}]}""");
        var catalog = await store.LoadCatalogAsync();
        Assert.Contains("生产", catalog.Folders); Assert.Contains("生产/华东", catalog.Folders);
        await store.SaveCatalogAsync(catalog with { Folders = [.. catalog.Folders, "空文件夹/子文件夹"] });
        await store.SaveAsync(catalog.Connections);
        var restored = await new JsonConnectionRepository(paths).LoadCatalogAsync();
        Assert.Contains("空文件夹", restored.Folders); Assert.Contains("空文件夹/子文件夹", restored.Folders);
        Assert.Equal(Assert.Single(catalog.Connections).Id, Assert.Single(restored.Connections).Id);
    }
    [Fact]
    public async Task CredentialsUseWindowsProtectionAndCanBeDeleted()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialStore(new(_root)); var id = Guid.NewGuid(); var secret = new ConnectionSecret("test-only-secret", "test-only-passphrase");
        await store.SaveAsync(id, secret); Assert.Equal(secret, await store.ReadAsync(id));
        var encrypted = await File.ReadAllBytesAsync(Directory.GetFiles(Path.Combine(_root, "credentials"))[0]);
        Assert.DoesNotContain(secret.Password, Encoding.UTF8.GetString(encrypted));
        await store.DeleteAsync(id); Assert.Null(await store.ReadAsync(id));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
