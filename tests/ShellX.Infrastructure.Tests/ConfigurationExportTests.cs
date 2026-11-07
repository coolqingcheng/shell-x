using System.Text.Json;
using ShellX.Core.Connections;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Infrastructure.Tests;

public sealed class ConfigurationExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shellx-export-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task ExportContainsAllFoldersRulesAndPreferencesWithoutCredentialOrLockData()
    {
        var paths = new AppPaths(_root); var path = paths.File("backup.json");
        var rule = new TunnelDefinition { Name = "数据库" };
        var profile = new ConnectionProfile { Name = "生产", Host = "example.test", Group = "线上/华东", AccentColor = "#BF6969", RememberSecret = true,
            PrivateKeyPath = "C:/keys/production.key", Tunnels = [rule], QuickCommands = [new() { Name = "目录", Text = "pwd" }] };
        var store = new JsonConnectionRepository(paths); await store.SaveCatalogAsync(new([profile], ["空目录/子目录"]));
        var original = await File.ReadAllTextAsync(paths.File("connections.json"));
        await File.WriteAllTextAsync(path, "old backup");
        await new JsonConfigurationExporter(paths).ExportAsync(path, await store.LoadCatalogAsync(), new AppSettings { Theme = AppTheme.Light, TerminalFontSize = 17,
            EditorFontSize = 19, AutoReconnectEnabled = false, ReconnectIntervalSeconds = 23, LockPasswordHash = "must-not-be-exported", AutoLockEnabled = true });
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path)); var root = document.RootElement;
        Assert.Equal("ShellX.Configuration", root.GetProperty("Format").GetString()); Assert.Equal(2, root.GetProperty("Version").GetInt32());
        Assert.True(root.GetProperty("ExportedAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
        var connection = Assert.Single(root.GetProperty("Connections").EnumerateArray());
        Assert.Equal(profile.Id, connection.GetProperty("Id").GetGuid()); Assert.Equal("#BF6969", connection.GetProperty("AccentColor").GetString());
        Assert.False(connection.GetProperty("RememberSecret").GetBoolean());
        Assert.Equal(rule.Id, Assert.Single(connection.GetProperty("Tunnels").EnumerateArray()).GetProperty("Id").GetGuid());
        Assert.Equal("pwd", Assert.Single(connection.GetProperty("QuickCommands").EnumerateArray()).GetProperty("Text").GetString());
        Assert.Contains("空目录/子目录", root.GetProperty("Folders").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(23, root.GetProperty("Settings").GetProperty("ReconnectIntervalSeconds").GetInt32());
        Assert.Equal(19, root.GetProperty("Settings").GetProperty("EditorFontSize").GetDouble());
        var json = root.GetRawText();
        foreach (var forbidden in new[] { "must-not-be-exported", "Password", "Passphrase", "AutoLock", "credentials", "PRIVATE KEY" }) Assert.DoesNotContain(forbidden, json);
        Assert.Equal(original, await File.ReadAllTextAsync(paths.File("connections.json")));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }
    [Fact]
    public async Task EncryptedExportRoundTripsPasswordsPassphrasesSettingsAndRejectsWrongBackupPassword()
    {
        var paths = new AppPaths(_root);
        var backup = paths.File("encrypted-backup.json");
        var profile = new ConnectionProfile { Name = "恢复测试", Host = "restore.test", Group = "备份", Port = 2222, Username = "deploy" };
        var secret = new ConnectionSecret("S3cret-Login-Password!", "private-key-passphrase");
        var exporter = new JsonConfigurationExporter(paths);
        var importer = new JsonConfigurationImporter();

        await exporter.ExportAsync(backup, new([profile], ["备份"]), new AppSettings
        {
            Theme = AppTheme.Light, TerminalFontSize = 18, EditorFontSize = 20,
            LockPasswordHash = "lock-secret-must-not-export", McpToken = "mcp-secret-must-not-export"
        }, new Dictionary<Guid, ConnectionSecret> { [profile.Id] = secret }, "backup-password-123");

        var json = await File.ReadAllTextAsync(backup);
        Assert.DoesNotContain(secret.Password, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secret.Passphrase, json, StringComparison.Ordinal);
        Assert.DoesNotContain("lock-secret-must-not-export", json, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp-secret-must-not-export", json, StringComparison.Ordinal);
        Assert.Contains("ShellX.Credentials", json, StringComparison.Ordinal);
        Assert.True(await importer.HasCredentialsAsync(backup));
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(backup, "wrong-password"));

        var restored = await importer.ImportAsync(backup, "backup-password-123");
        var restoredProfile = Assert.Single(restored.Catalog.Connections);
        Assert.Equal(profile.Id, restoredProfile.Id);
        Assert.Equal("restore.test", restoredProfile.Host);
        Assert.Equal(2222, restoredProfile.Port);
        Assert.True(restoredProfile.RememberSecret);
        Assert.Equal(secret, restored.Secrets[profile.Id]);
        Assert.Equal(AppTheme.Light, restored.Settings.Theme);
        Assert.Equal(18, restored.Settings.TerminalFontSize);
        Assert.Equal(20, restored.Settings.EditorFontSize);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }
    [Fact]
    public async Task CancelledExportPreservesExistingBackupAndActiveFilesCannotBeOverwritten()
    {
        var paths = new AppPaths(_root); var exporter = new JsonConfigurationExporter(paths);
        var output = paths.File("backup.json"); await File.WriteAllTextAsync(output, "previous backup");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(output, new([], []), new(), cancelled.Token));
        Assert.Equal("previous backup", await File.ReadAllTextAsync(output)); Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        foreach (var file in new[] { "connections.json", "settings.json", "known-hosts.json", "credentials/test.bin" })
        {
            var target = paths.File(file); Directory.CreateDirectory(Path.GetDirectoryName(target)!); await File.WriteAllTextAsync(target, "keep");
            await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(target, new([], []), new()));
            Assert.Equal("keep", await File.ReadAllTextAsync(target));
        }
        await Assert.ThrowsAnyAsync<IOException>(() => exporter.ExportAsync(_root, new([], []), new()));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
