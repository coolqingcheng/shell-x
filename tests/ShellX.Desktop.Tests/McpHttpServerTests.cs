using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Tests;

public sealed class McpHttpServerTests
{
    [Fact]
    public async Task StreamableHttpEndpointExposesLocalCommandTool()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-mcp-http-" + Guid.NewGuid().ToString("N"));
        var oldPort = Environment.GetEnvironmentVariable("SHELLX_MCP_PORT");
        try
        {
            var paths = new AppPaths(root);
            var repository = new JsonConnectionRepository(paths);
            var profile = new ConnectionProfile { Name = "HTTP MCP", Host = "localhost", McpEnabled = true };
            await repository.SaveAsync([profile], TestContext.Current.CancellationToken);
            Environment.SetEnvironmentVariable("SHELLX_MCP_PORT", FreePort().ToString());
            using var server = new McpHttpServer(paths, repository, new LocalCommandExecutor(repository, new McpAuditLog(paths)), NullLogger<McpHttpServer>.Instance);
            server.Start();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", await File.ReadAllTextAsync(server.TokenFilePath, TestContext.Current.CancellationToken));

            using var initialize = await PostAsync(client, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""");
            Assert.Equal("ShellX", initialize.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

            using var tools = await PostAsync(client, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
            var names = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(x => x.GetProperty("name").GetString());
            Assert.Contains("execute_local_command", names);

            var call = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"execute_local_command\",\"arguments\":{\"connectionId\":\"" + profile.Id + "\",\"command\":\"echo http-mcp\"}}}";
            using var result = await PostAsync(client, call);
            var payload = result.RootElement.GetProperty("result");
            Assert.False(payload.GetProperty("isError").GetBoolean());
            Assert.Contains("http-mcp", payload.GetProperty("content")[0].GetProperty("text").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHELLX_MCP_PORT", oldPort);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<JsonDocument> PostAsync(HttpClient client, string json)
    {
        var response = await client.PostAsync("http://127.0.0.1:" + Environment.GetEnvironmentVariable("SHELLX_MCP_PORT") + "/mcp",
            new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}