using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ShellX.Core.Connections;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Services;

public sealed record McpConnectionInfo(Guid Id, string Name, string Address);

public sealed record LocalCommandRequest(Guid ConnectionId, string Command, string? WorkingDirectory = null, int TimeoutSeconds = 30);

public sealed record LocalCommandResult(bool Success, string Status, int? ExitCode, long DurationMilliseconds,
    string Stdout, string Stderr, int StdoutLength, int StderrLength, string? Error = null);

public sealed record McpAuditEntry(DateTimeOffset Timestamp, string RequestId, Guid ConnectionId, string ConnectionName,
    string Command, string WorkingDirectory, int TimeoutSeconds, string Status, int? ExitCode, long DurationMilliseconds,
    string Stdout, string Stderr, int StdoutLength, int StderrLength, string? Error);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(McpAuditEntry))]
[JsonSerializable(typeof(McpConnectionInfo[]))]
[JsonSerializable(typeof(LocalCommandResult))]
internal sealed partial class McpJsonContext : JsonSerializerContext;

public sealed class McpAuditLog(AppPaths paths)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task WriteAsync(McpAuditEntry entry, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(paths.Root, "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"mcp-audit-{entry.Timestamp:yyyyMMdd}.jsonl");
        var line = JsonSerializer.Serialize(entry, McpJsonContext.Default.McpAuditEntry) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}

public sealed class LocalCommandExecutor(IConnectionRepository connections, McpAuditLog audit)
{
    private const int MaxCommandLength = 32_768;
    private const int MaxOutputCharacters = 262_144;
    public async Task<LocalCommandResult> ExecuteAsync(LocalCommandRequest request, string requestId, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : request.WorkingDirectory;
        var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 300);
        ConnectionProfile? profile = null;
        try
        {
            profile = (await connections.LoadAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == request.ConnectionId);
            if (profile == null) return await CompleteAsync("denied", null, "", "", 0, 0, "找不到对应连接。", started, watch, request, requestId, "unknown");
            if (!profile.McpEnabled) return await CompleteAsync("denied", null, "", "", 0, 0, "该连接未允许 MCP 执行本机命令。", started, watch, request, requestId, profile.Name);
            if (string.IsNullOrWhiteSpace(request.Command)) return await CompleteAsync("invalid", null, "", "", 0, 0, "命令不能为空。", started, watch, request, requestId, profile.Name);
            if (request.Command.Length > MaxCommandLength) return await CompleteAsync("invalid", null, "", "", 0, 0, $"命令长度不能超过 {MaxCommandLength} 个字符。", started, watch, request, requestId, profile.Name);
            if (request.Command.Contains('\0')) return await CompleteAsync("invalid", null, "", "", 0, 0, "命令包含非法字符。", started, watch, request, requestId, profile.Name);
            workingDirectory = Path.GetFullPath(workingDirectory);
            if (!Directory.Exists(workingDirectory)) return await CompleteAsync("invalid", null, "", "", 0, 0, "工作目录不存在。", started, watch, request, requestId, profile.Name);
            return await RunAsync(request, requestId, profile.Name, workingDirectory, timeoutSeconds, started, watch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CompleteAsync("cancelled", null, "", "", 0, 0, "命令已取消。", started, watch, request, requestId, profile?.Name ?? "unknown", CancellationToken.None);
        }
        catch (Exception ex)
        {
            return await CompleteAsync("error", null, "", "", 0, 0, ex.Message, started, watch, request, requestId, profile?.Name ?? "unknown", CancellationToken.None);
        }
    }

    private async Task<LocalCommandResult> RunAsync(LocalCommandRequest request, string requestId, string connectionName,
        string workingDirectory, int timeoutSeconds, DateTimeOffset started, Stopwatch watch)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(request.Command);
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(request.Command);
        }

        using var process = new Process { StartInfo = startInfo };
        string stdout = ""; string stderr = ""; int? exitCode = null; string status; string? error = null;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动本地命令进程。");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); status = "succeeded"; exitCode = process.ExitCode; }
            catch (OperationCanceledException)
            {
                status = "timed_out"; error = $"命令执行超过 {timeoutSeconds} 秒。";
                try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
            }
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status = "error"; error = ex.Message;
        }
        watch.Stop();
        var result = ToResult(status, exitCode, watch.ElapsedMilliseconds, stdout, stderr, error);
        await audit.WriteAsync(new(started, requestId, request.ConnectionId, connectionName, request.Command, workingDirectory,
            timeoutSeconds, status, result.ExitCode, result.DurationMilliseconds, result.Stdout, result.Stderr, result.StdoutLength, result.StderrLength, error), CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private async Task<LocalCommandResult> CompleteAsync(string status, int? exitCode, string stdout, string stderr,
        int stdoutLength, int stderrLength, string? error, DateTimeOffset started, Stopwatch watch, LocalCommandRequest request,
        string requestId, string connectionName, CancellationToken cancellationToken = default)
    {
        watch.Stop();
        var result = new LocalCommandResult(status == "succeeded", status, exitCode, watch.ElapsedMilliseconds,
            Truncate(stdout), Truncate(stderr), stdoutLength, stderrLength, error);
        await audit.WriteAsync(new(started, requestId, request.ConnectionId, connectionName, request.Command,
            request.WorkingDirectory ?? "", request.TimeoutSeconds, status, exitCode, result.DurationMilliseconds,
            result.Stdout, result.Stderr, result.StdoutLength, result.StderrLength, error), cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static LocalCommandResult ToResult(string status, int? exitCode, long duration, string stdout, string stderr, string? error)
    {
        var outLength = stdout.Length; var errorLength = stderr.Length;
        return new(status == "succeeded", status, exitCode, duration, Truncate(stdout), Truncate(stderr), outLength, errorLength, error);
    }
    private static string Truncate(string value) => value.Length <= MaxOutputCharacters ? value : value[..MaxOutputCharacters];
}

public sealed class McpHttpServer(AppPaths paths, IConnectionRepository connections, LocalCommandExecutor executor, ILogger<McpHttpServer> log) : IDisposable
{
    private const string ProtocolVersion = "2025-06-18";
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _tokenPath = paths.File("mcp-token.txt");
    private Task? _loop;
    private bool _disposed;
    public string Endpoint => $"http://127.0.0.1:{Port}/mcp";
    public int Port { get; } = int.TryParse(Environment.GetEnvironmentVariable("SHELLX_MCP_PORT"), out var port) ? Math.Clamp(port, 1, 65535) : 8765;
    public string TokenFilePath => _tokenPath;
    private string Token { get; } = LoadOrCreateToken(paths.File("mcp-token.txt"));

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener.IsListening) return;
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(_lifetime.Token));
        log.LogInformation("MCP HTTP 已启动：{Endpoint}", Endpoint);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        try { _listener.Stop(); } catch (Exception) { }
        try { _loop?.GetAwaiter().GetResult(); } catch (Exception) { }
        _lifetime.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(context, token), token);
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
    {
        var request = context.Request; var response = context.Response;
        try
        {
            if (request.Url?.AbsolutePath == "/healthz")
            {
                await WriteJsonAsync(response, 200, "{\"status\":\"ok\"}", token).ConfigureAwait(false);
                return;
            }
            if (request.Url?.AbsolutePath != "/mcp") { response.StatusCode = 404; return; }
            if (!Authorized(request)) { response.StatusCode = 401; response.AddHeader("WWW-Authenticate", "Bearer"); return; }
            if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 405; response.AddHeader("Allow", "POST"); return;
            }
            var body = await ReadBodyAsync(request, token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            {
                await WriteRpcErrorAsync(response, null, -32600, "Invalid Request", token).ConfigureAwait(false); return;
            }
            var method = methodElement.GetString() ?? "";
            if (method.StartsWith("notifications/", StringComparison.Ordinal)) { response.StatusCode = 202; return; }
            var hasId = root.TryGetProperty("id", out var id);
            if (!hasId)
            {
                if (method.StartsWith("notifications/", StringComparison.Ordinal)) { response.StatusCode = 202; return; }
                await WriteRpcErrorAsync(response, null, -32600, "Request id is required", token).ConfigureAwait(false); return;
            }
            var requestId = id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : id.GetRawText();
            switch (method)
            {
                case "initialize":
                    await WriteRpcResultAsync(response, id, writer =>
                    {
                        writer.WriteStartObject();
                        writer.WriteString("protocolVersion", ProtocolVersion);
                        writer.WriteStartObject("capabilities"); writer.WriteStartObject("tools"); writer.WriteBoolean("listChanged", false); writer.WriteEndObject(); writer.WriteEndObject();
                        writer.WriteStartObject("serverInfo"); writer.WriteString("name", "ShellX"); writer.WriteString("version", "1.0.0"); writer.WriteEndObject();
                        writer.WriteEndObject();
                    }, token).ConfigureAwait(false);
                    return;
                case "ping":
                    await WriteRpcResultAsync(response, id, writer => writer.WriteStartObject(), token).ConfigureAwait(false);
                    return;
                case "tools/list":
                    await WriteRpcResultAsync(response, id, WriteTools, token).ConfigureAwait(false);
                    return;
                case "tools/call":
                    await HandleToolCallAsync(response, id, requestId, root, token).ConfigureAwait(false);
                    return;
                default:
                    await WriteRpcErrorAsync(response, id, -32601, $"Method not found: {method}", token).ConfigureAwait(false);
                    return;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "MCP HTTP 请求处理失败");
            await WriteRpcErrorAsync(response, null, -32603, ex.Message, token).ConfigureAwait(false);
        }
    }

    private async Task HandleToolCallAsync(HttpListenerResponse response, JsonElement id, string requestId, JsonElement root, CancellationToken token)
    {
        if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            await WriteRpcErrorAsync(response, id, -32602, "Missing tool name", token).ConfigureAwait(false); return;
        }
        var name = nameElement.GetString() ?? "";
        if (name == "list_mcp_connections")
        {
            var items = (await connections.LoadAsync(token).ConfigureAwait(false)).Where(x => x.McpEnabled)
                .Select(x => new McpConnectionInfo(x.Id, x.Name, x.Address)).ToArray();
            var text = JsonSerializer.Serialize(items, McpJsonContext.Default.McpConnectionInfoArray);
            await WriteToolResultAsync(response, id, text, false, token).ConfigureAwait(false);
            return;
        }
        if (name != "execute_local_command")
        {
            await WriteRpcErrorAsync(response, id, -32602, $"Unknown tool: {name}", token).ConfigureAwait(false); return;
        }
        var args = parameters.TryGetProperty("arguments", out var argumentElement) && argumentElement.ValueKind == JsonValueKind.Object
            ? argumentElement : default;
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("connectionId", out var connectionIdElement) ||
            !Guid.TryParse(connectionIdElement.GetString(), out var connectionId) ||
            !args.TryGetProperty("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.String)
        {
            await WriteRpcErrorAsync(response, id, -32602, "connectionId and command are required", token).ConfigureAwait(false); return;
        }
        var workingDirectory = args.TryGetProperty("workingDirectory", out var directoryElement) && directoryElement.ValueKind == JsonValueKind.String
            ? directoryElement.GetString() : null;
        var timeout = args.TryGetProperty("timeoutSeconds", out var timeoutElement) && timeoutElement.TryGetInt32(out var timeoutValue) ? timeoutValue : 30;
        var result = await executor.ExecuteAsync(new(connectionId, commandElement.GetString() ?? "", workingDirectory, timeout), requestId, token).ConfigureAwait(false);
        await WriteToolResultAsync(response, id, JsonSerializer.Serialize(result, McpJsonContext.Default.LocalCommandResult), !result.Success, token).ConfigureAwait(false);
    }

    private bool Authorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var supplied = Encoding.UTF8.GetBytes(header["Bearer ".Length..]);
        var expected = Encoding.UTF8.GetBytes(Token);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request, CancellationToken token)
    {
        if (request.ContentLength64 > 1_048_576) throw new InvalidOperationException("请求内容过大。");
        await using var input = request.InputStream;
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, token).ConfigureAwait(false);
        if (buffer.Length > 1_048_576) throw new InvalidOperationException("请求内容过大。");
        return buffer.ToArray();
    }

    private static void WriteTools(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("tools");
        writer.WriteStartObject(); writer.WriteString("name", "list_mcp_connections"); writer.WriteString("description", "列出允许 MCP 的连接。");
        writer.WriteStartObject("inputSchema"); writer.WriteString("type", "object"); writer.WriteStartObject("properties"); writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndObject();
        writer.WriteStartObject(); writer.WriteString("name", "execute_local_command"); writer.WriteString("description", "在运行 ShellX 的本机执行命令。");
        writer.WriteStartObject("inputSchema"); writer.WriteString("type", "object");
        writer.WriteStartObject("properties");
        writer.WriteStartObject("connectionId"); writer.WriteString("type", "string"); writer.WriteString("description", "允许 MCP 的连接 ID"); writer.WriteEndObject();
        writer.WriteStartObject("command"); writer.WriteString("type", "string"); writer.WriteString("description", "本机命令"); writer.WriteEndObject();
        writer.WriteStartObject("workingDirectory"); writer.WriteString("type", "string"); writer.WriteEndObject();
        writer.WriteStartObject("timeoutSeconds"); writer.WriteString("type", "integer"); writer.WriteNumber("minimum", 1); writer.WriteNumber("maximum", 300); writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartArray("required"); writer.WriteStringValue("connectionId"); writer.WriteStringValue("command"); writer.WriteEndArray();
        writer.WriteEndObject(); writer.WriteEndObject();
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static Task WriteToolResultAsync(HttpListenerResponse response, JsonElement id, string text, bool isError, CancellationToken token)
        => WriteRpcResultAsync(response, id, writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("content"); writer.WriteStartObject(); writer.WriteString("type", "text"); writer.WriteString("text", text); writer.WriteEndObject(); writer.WriteEndArray();
            writer.WriteBoolean("isError", isError);
            writer.WriteEndObject();
        }, token);

    private static async Task WriteRpcResultAsync(HttpListenerResponse response, JsonElement id, Action<Utf8JsonWriter> result, CancellationToken token)
    {
        await using var body = new MemoryStream();
        using (var writer = new Utf8JsonWriter(body))
        {
            writer.WriteStartObject(); writer.WriteString("jsonrpc", "2.0"); writer.WritePropertyName("id"); id.WriteTo(writer); writer.WritePropertyName("result"); result(writer); writer.WriteEndObject();
        }
        await WriteBytesAsync(response, 200, body.ToArray(), "application/json; charset=utf-8", token).ConfigureAwait(false);
    }

    private static Task WriteRpcErrorAsync(HttpListenerResponse response, JsonElement? id, int code, string message, CancellationToken token)
    {
        using var body = new MemoryStream();
        using (var writer = new Utf8JsonWriter(body))
        {
            writer.WriteStartObject(); writer.WriteString("jsonrpc", "2.0");
            if (id.HasValue) { writer.WritePropertyName("id"); id.Value.WriteTo(writer); } else writer.WriteNull("id");
            writer.WriteStartObject("error"); writer.WriteNumber("code", code); writer.WriteString("message", message); writer.WriteEndObject(); writer.WriteEndObject();
        }
        return WriteBytesAsync(response, 200, body.ToArray(), "application/json; charset=utf-8", token);
    }

    private static Task WriteJsonAsync(HttpListenerResponse response, int status, string json, CancellationToken token)
        => WriteBytesAsync(response, status, Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", token);

    private static async Task WriteBytesAsync(HttpListenerResponse response, int status, byte[] bytes, string contentType, CancellationToken token)
    {
        response.StatusCode = status; response.ContentType = contentType; response.ContentLength64 = bytes.Length;
        response.AddHeader("MCP-Protocol-Version", ProtocolVersion);
        await response.OutputStream.WriteAsync(bytes, token).ConfigureAwait(false);
        response.Close();
    }

    private static string LoadOrCreateToken(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32) return existing;
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(path, token);
        return token;
    }
}