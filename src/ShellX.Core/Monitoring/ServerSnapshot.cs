namespace ShellX.Core.Monitoring;

public sealed record CpuCounters(ulong Total, ulong Idle);
public sealed record NetworkCounters(string Name, ulong ReceivedBytes, ulong SentBytes);
public sealed record ServerProcess(int Pid, double CpuPercent, long MemoryBytes, string Command);
public sealed record ServerDisk(string Device, string MountPath, long TotalBytes, long AvailableBytes, double UsedPercent);
public sealed record ServerSnapshot(DateTimeOffset SampleTime, string Hostname, string OperatingSystem, string Kernel,
    string Addresses, TimeSpan Uptime, string LoadAverage, CpuCounters Cpu, long MemoryTotal, long MemoryAvailable,
    long SwapTotal, long SwapFree, IReadOnlyList<NetworkCounters> Networks,
    IReadOnlyList<ServerProcess> Processes, IReadOnlyList<ServerDisk> Disks, double ResponseMilliseconds);
public sealed record NetworkRate(string Name, double DownloadBytesPerSecond, double UploadBytesPerSecond);

public interface IServerMonitor
{
    Task<ServerSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public static class ServerMetrics
{
    public static double? CpuUsage(CpuCounters? previous, CpuCounters current)
    {
        if (previous == null || current.Total <= previous.Total || current.Idle < previous.Idle) return null;
        var total = current.Total - previous.Total;
        var idle = Math.Min(total, current.Idle - previous.Idle);
        return (total - idle) * 100d / total;
    }

    public static IReadOnlyList<NetworkRate> NetworkRates(ServerSnapshot? previous, ServerSnapshot current)
    {
        var seconds = previous == null ? 0 : (current.SampleTime - previous.SampleTime).TotalSeconds;
        var old = previous?.Networks.ToDictionary(n => n.Name);
        return current.Networks.Select(n =>
        {
            if (seconds <= 0 || old == null || !old.TryGetValue(n.Name, out var before)) return new NetworkRate(n.Name, 0, 0);
            return new NetworkRate(n.Name,
                n.ReceivedBytes >= before.ReceivedBytes ? (n.ReceivedBytes - before.ReceivedBytes) / seconds : 0,
                n.SentBytes >= before.SentBytes ? (n.SentBytes - before.SentBytes) / seconds : 0);
        }).ToArray();
    }

    public static string Size(double bytes) => bytes switch
    {
        < 1024 => $"{Math.Max(0, bytes):0} B",
        < 1048576 => $"{bytes / 1024:0.#} KiB",
        < 1073741824 => $"{bytes / 1048576:0.#} MiB",
        _ => $"{bytes / 1073741824:0.#} GiB"
    };
}
