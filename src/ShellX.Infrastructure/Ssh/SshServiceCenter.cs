using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using ShellX.Core.Services;

namespace ShellX.Infrastructure.Ssh;

/// <summary>
/// 用 systemctl 读取并操作系统服务。命令全部以单行字符串下发，避免源文件换行符（CRLF）
/// 被远端 shell 解释成语法错误——监控脚本踩过同样的坑。
/// </summary>
internal sealed class SshServiceCenter(SshClient ssh, ILogger log) : IServiceCenter, IPortUsageReader
{
    private const string Marker = "__SX_UNITS__";
    private const string PortsMarker = "__SX_PORTS__";
    // ss 在前、标记行、netstat 兜底（ss 缺失的最小系统上仍能出结果）。所有命令保持单行下发。
    private const string PortsCommand =
        "ss -lntup; printf '\n" + PortsMarker + "\n'; netstat -lntup";
    private const string ListCommand =
        "systemctl list-units --type=service --all --no-pager --no-legend --plain --full; printf '\\n" + Marker + "\\n'; " +
        "systemctl list-unit-files --type=service --no-pager --no-legend";
    private static readonly Regex SafeUnit = new("^[A-Za-z0-9@_.:\\-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<RemoteService>> ListAsync(CancellationToken cancellationToken = default)
    {
        var command = ssh.CreateCommand(ListCommand);
        command.CommandTimeout = TimeSpan.FromSeconds(30);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var services = Parse(command.Result ?? "");
        if (services.Count == 0)
        {
            var error = (command.Error ?? "").Trim();
            if (error.Length > 0) throw new InvalidOperationException(error);
            if (command.ExitStatus != 0) throw new InvalidOperationException($"systemctl 读取服务失败（exit={command.ExitStatus}）。");
        }
        return services;
    }

    public async Task<string> StatusAsync(string unit, CancellationToken cancellationToken = default)
    {
        if (!IsSafeUnit(unit)) throw new ArgumentException("服务名包含非法字符。", nameof(unit));
        var command = ssh.CreateCommand($"SYSTEMD_COLORS=0 systemctl status {unit} --no-pager -l -n 25");
        command.CommandTimeout = TimeSpan.FromSeconds(25);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var output = Combine(command);
        if (output.Length == 0)
            output = command.ExitStatus == 0 ? "（systemctl status 没有输出）" : $"读取服务状态失败（exit={command.ExitStatus}）。";
        return output;
    }

    public async Task<ServiceActionResult> ExecuteAsync(string unit, ServiceAction action, CancellationToken cancellationToken = default)
    {
        // 单位名会被拼进命令行，先做白名单校验，杜绝注入。
        if (!IsSafeUnit(unit)) return new(false, $"服务名包含非法字符，已拒绝执行：{unit}", -1);
        var verb = action switch
        {
            ServiceAction.Start => "start",
            ServiceAction.Stop => "stop",
            ServiceAction.Restart => "restart",
            ServiceAction.Enable => "enable",
            ServiceAction.Disable => "disable",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "未知的服务操作。")
        };
        var command = ssh.CreateCommand($"systemctl {verb} {unit}");
        command.CommandTimeout = TimeSpan.FromSeconds(90);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var output = Combine(command);
        var success = command.ExitStatus == 0;
        if (success && output.Length == 0) output = $"{verb} {unit} 已执行。";
        if (success) log.LogInformation("服务操作完成 {Unit} {Action}", unit, verb);
        else log.LogWarning("服务操作失败 {Unit} {Action} exit={Exit} {Output}", unit, verb, command.ExitStatus, output);
        return new(success, output, command.ExitStatus ?? -1);
    }

    private static bool IsSafeUnit(string unit) => !string.IsNullOrWhiteSpace(unit) && unit.Length <= 256 && SafeUnit.IsMatch(unit);

    private static string Combine(SshCommand command)
    {
        var stdout = (command.Result ?? "").Trim();
        var stderr = (command.Error ?? "").Trim();
        if (stdout.Length == 0) return stderr;
        return stderr.Length == 0 ? stdout : stdout + "\n" + stderr;
    }

    /// <summary>解析一次执行的两段输出：list-units 在标记行之前，list-unit-files 在之后。</summary>
    internal static IReadOnlyList<RemoteService> Parse(string output)
    {
        var text = (output ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var marker = text.IndexOf(Marker, StringComparison.Ordinal);
        var unitsText = marker < 0 ? text : text[..marker];
        var filesText = marker < 0 ? "" : text[(marker + Marker.Length)..];

        var enabled = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in filesText.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].Contains('.')) continue;   // 跳过页脚等非条目行
            enabled[parts[0]] = parts[1];
        }

        var services = new List<RemoteService>();
        foreach (var line in unitsText.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || !parts[0].Contains('.')) continue;
            var description = parts.Length > 4 ? string.Join(' ', parts[4..]) : "";
            if (description == "-") description = "";
            services.Add(new(parts[0], parts[1], parts[2], parts[3], description, enabled.GetValueOrDefault(parts[0], "")));
        }
        services.Sort((left, right) => string.CompareOrdinal(left.Unit, right.Unit));
        return services;
    }

    public async Task<IReadOnlyList<ListeningPort>> PortsAsync(CancellationToken cancellationToken = default)
    {
        var command = ssh.CreateCommand(PortsCommand);
        command.CommandTimeout = TimeSpan.FromSeconds(30);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var ports = ParsePorts(command.Result ?? "");
        if (ports.Count == 0)
        {
            var error = (command.Error ?? "").Trim();
            if (error.Length > 0) throw new InvalidOperationException(error);
            if (command.ExitStatus != 0) throw new InvalidOperationException($"读取监听端口失败（exit={command.ExitStatus}）。");
        }
        return ports;
    }

    /// <summary>优先用 ss 的输出，ss 没有结果时回退到 netstat。</summary>
    internal static IReadOnlyList<ListeningPort> ParsePorts(string output)
    {
        var text = (output ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var marker = text.IndexOf(PortsMarker, StringComparison.Ordinal);
        var ssText = marker < 0 ? text : text[..marker];
        var netstatText = marker < 0 ? "" : text[(marker + PortsMarker.Length)..];
        var ports = ParseSs(ssText);
        return ports.Count > 0 ? ports : ParseNetstat(netstatText);
    }

    internal static IReadOnlyList<ListeningPort> ParseSs(string text)
    {
        var result = new List<ListeningPort>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            // LISTEN = TCP 监听，UNCONN = UDP 监听；其余（表头、已建立连接）跳过。
            var protocol = parts[0] switch { "LISTEN" => "TCP", "UNCONN" => "UDP", _ => "" };
            if (protocol.Length == 0) continue;
            if (!TryParseEndpoint(parts[3], out var address, out var port)) continue;
            var tail = parts.Length > 5 ? string.Join(' ', parts[5..]) : "";
            var pid = Regex.Match(tail, @"pid=(\d+)").Groups[1].Value;
            var name = Regex.Match(tail, @"""([^""]+)""").Groups[1].Value;
            result.Add(new(protocol, parts[0], address, port, name, pid, line.Trim()));
        }
        SortPorts(result);
        return result;
    }

    internal static IReadOnlyList<ListeningPort> ParseNetstat(string text)
    {
        var result = new List<ListeningPort>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("tcp", StringComparison.Ordinal) && !line.StartsWith("udp", StringComparison.Ordinal)) continue;  // 表头与说明行
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            var protocol = parts[0].Equals("tcp", StringComparison.OrdinalIgnoreCase) ? "TCP" : "UDP";
            if (!TryParseEndpoint(parts[3], out var address, out var port)) continue;
            string state;
            string pidPart;
            if (parts.Length >= 7) { state = parts[5]; pidPart = parts[6]; }
            else if (parts.Length == 6 && parts[5].Contains('/')) { state = protocol == "UDP" ? "UNCONN" : "LISTEN"; pidPart = parts[5]; }
            else { state = protocol == "UDP" ? "UNCONN" : "LISTEN"; pidPart = ""; }
            if (state is "-" or "") state = protocol == "UDP" ? "UNCONN" : "LISTEN";
            var (pid, name) = ParsePidProgram(pidPart);
            result.Add(new(protocol, state, address, port, name, pid, line));
        }
        SortPorts(result);
        return result;
    }

    private static (string Pid, string Name) ParsePidProgram(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || text == "-") return ("", "");
        var index = text.IndexOf('/');
        return index <= 0 ? ("", text) : (text[..index], text[(index + 1)..]);
    }

    private static bool TryParseEndpoint(string value, out string address, out int port)
    {
        address = ""; port = 0;
        var text = value.Trim();
        var index = text.LastIndexOf(':');
        if (index <= 0 || index == text.Length - 1) return false;
        if (!int.TryParse(text[(index + 1)..], out port) || port < 1 || port > 65535) return false;
        address = text[..index];
        return true;
    }

    private static void SortPorts(List<ListeningPort> ports)
        => ports.Sort((left, right) => left.Port != right.Port ? left.Port.CompareTo(right.Port) : string.CompareOrdinal(left.Protocol, right.Protocol));
}
