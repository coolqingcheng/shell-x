using System.Text.Json;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.Settings;
using ShellX.Infrastructure.Security;

namespace ShellX.Desktop.Tests;

public sealed class SettingsMcpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task McpAddressAndTokenProduceCopyableJsonAndPersist(bool enabled)
    {
        var vm = new SettingsViewModel(new AppSettings
        {
            McpEnabled = enabled,
            McpAddress = "http://127.0.0.1:9000",
            McpToken = "settings-mcp-token-1234567890"
        }, new LockPasswordHasher(), ["Consolas"]);
        AppSettings? saved = null; vm.Saved += value => saved = value;
        Assert.Equal(enabled, vm.McpEnabled);

        using var json = JsonDocument.Parse(vm.McpJson);
        var server = json.RootElement.GetProperty("mcpServers").GetProperty("shellx");
        Assert.Equal("http", server.GetProperty("type").GetString());
        Assert.Equal("http://127.0.0.1:9000/mcp", server.GetProperty("url").GetString());
        Assert.Equal("Bearer settings-mcp-token-1234567890", server.GetProperty("headers").GetProperty("Authorization").GetString());

        vm.McpAddress = "http://127.0.0.1:9100/";
        vm.McpToken = "changed-mcp-token-1234567890";
        vm.McpEnabled = !enabled;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("", vm.Error);
        Assert.NotNull(saved);
        Assert.Equal(!enabled, saved.McpEnabled);
        Assert.Equal("http://127.0.0.1:9100", saved.McpAddress);
        Assert.Equal("changed-mcp-token-1234567890", saved.McpToken);
    }
}
