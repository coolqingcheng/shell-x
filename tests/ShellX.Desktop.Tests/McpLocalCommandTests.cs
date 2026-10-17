using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Tests;

public sealed class McpLocalCommandTests
{
    [Fact]
    public async Task McpCommandRunsLocallyAndWritesAuditRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-mcp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new JsonConnectionRepository(paths);
            var profile = new ConnectionProfile { Name = "本机命令", Host = "localhost", McpEnabled = true };
            await repository.SaveAsync([profile], TestContext.Current.CancellationToken);
            var audit = new McpAuditLog(paths);
            var executor = new LocalCommandExecutor(repository, audit);

            var result = await executor.ExecuteAsync(new(profile.Id, "echo shellx-mcp"), "request-1", TestContext.Current.CancellationToken);

            Assert.True(result.Success, result.Error);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("shellx-mcp", result.Stdout);
            var auditPath = Assert.Single(Directory.GetFiles(Path.Combine(root, "logs"), "mcp-audit-*.jsonl"));
            var entry = await File.ReadAllTextAsync(auditPath, TestContext.Current.CancellationToken);
            Assert.Contains("request-1", entry);
            Assert.Contains("本机命令", entry);
            Assert.DoesNotContain("\\u672c", entry);
            Assert.Contains("echo shellx-mcp", entry);
            Assert.Contains("\"Status\":\"succeeded\"", entry);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PowerShellEncodedCommandIsRecordedWithReadableCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-mcp-encoded-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new JsonConnectionRepository(paths);
            var profile = new ConnectionProfile { Name = "编码命令", Host = "localhost", McpEnabled = true };
            await repository.SaveAsync([profile], TestContext.Current.CancellationToken);
            var audit = new McpAuditLog(paths);
            var executor = new LocalCommandExecutor(repository, audit);
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("echo decoded-command"));
            var command = $"powershell.exe -NoProfile -EncodedCommand {encoded}";

            var result = await executor.ExecuteAsync(new(profile.Id, command), "encoded-1", TestContext.Current.CancellationToken);

            Assert.True(result.Success, result.Error);
            Assert.Contains("decoded-command", result.Stdout);
            var auditPath = Assert.Single(Directory.GetFiles(Path.Combine(root, "logs"), "mcp-audit-*.jsonl"));
            var entry = await File.ReadAllTextAsync(auditPath, TestContext.Current.CancellationToken);
            Assert.Contains("echo decoded-command", entry);
            Assert.Contains("\"DecodedCommand\":\"echo decoded-command\"", entry);
            var row = Assert.Single(new ShellX.Desktop.Services.McpLogFileReader(Path.Combine(root, "logs")).ReadNewRows());
            Assert.Contains("echo decoded-command", row.Text);
            Assert.DoesNotContain("-EncodedCommand", row.Text);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task McpCommandIsDeniedWhenConnectionDisallowsMcp()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-mcp-denied-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new JsonConnectionRepository(paths);
            var profile = new ConnectionProfile { Name = "禁止 MCP", Host = "localhost" };
            await repository.SaveAsync([profile], TestContext.Current.CancellationToken);
            var executor = new LocalCommandExecutor(repository, new McpAuditLog(paths));

            var result = await executor.ExecuteAsync(new(profile.Id, "echo denied"), "request-2", TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("denied", result.Status);
            Assert.Contains("未允许", result.Error);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}