using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using ShellX.Core.Services;

namespace ShellX.Infrastructure.Ssh;

/// <summary>
/// 用 systemctl 读取并操作系统服务。命令全部以单行字符串下发，避免源文件换行符（CRLF）
/// 被远端 shell 解释成语法错误——监控脚本踩过同样的坑。
/// </summary>
internal sealed class SshServiceCenter(SshClient ssh, ILogger log) : IServiceCenter
{
    private const string Marker = "__SX_UNITS__";
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
}