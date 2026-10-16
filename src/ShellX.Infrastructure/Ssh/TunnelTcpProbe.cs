using System.Net;
using System.Net.Sockets;
using System.Text;
using Renci.SshNet;

namespace ShellX.Infrastructure.Ssh;

// SOCKS CONNECT 的成功响应在 SSH direct-tcpip 打开成功后返回，避免把本地 TCP 握手当作目标连通。
internal static class TunnelTcpProbe
{
    internal sealed class ProbeException(string message, bool restricted = false) : IOException(message)
    { public bool Restricted { get; } = restricted; }

    private sealed class ProbePort() : ForwardedPortDynamic("127.0.0.1", 0)
    { public void CloseProbe() => StopPort(TimeSpan.Zero); }

    public static async Task ThroughSshAsync(SshClient ssh, string host, int port, CancellationToken token)
    {
        using var proxy = new ProbePort();
        Exception? failure = null;
        proxy.Exception += (_, e) => Interlocked.Exchange(ref failure, e.Exception);
        try
        {
            await Task.Run(() => { ssh.AddForwardedPort(proxy); proxy.Start(); }, token).ConfigureAwait(false);
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, (int)proxy.BoundPort, token).ConfigureAwait(false);
            var stream = tcp.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, token).ConfigureAwait(false);
            var greeting = new byte[2];
            await stream.ReadExactlyAsync(greeting, token).ConfigureAwait(false);
            if (greeting[0] != 5 || greeting[1] != 0) throw new ProbeException("检测通道协商失败。");
            byte type; byte[] address;
            if (IPAddress.TryParse(host.Trim('[', ']'), out var ip))
            { type = ip.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)4; address = ip.GetAddressBytes(); }
            else
            {
                type = 3; var name = Encoding.ASCII.GetBytes(new System.Globalization.IdnMapping().GetAscii(host));
                if (name.Length > 255) throw new ProbeException("目标主机名过长。");
                address = [(byte)name.Length, .. name];
            }
            byte[] request = [5, 1, 0, type, .. address, (byte)(port >> 8), (byte)port];
            await stream.WriteAsync(request, token).ConfigureAwait(false);
            var response = new byte[10];
            try { await stream.ReadExactlyAsync(response, token).ConfigureAwait(false); }
            catch (EndOfStreamException) when (failure != null) { throw Failure(failure); }
            if (response[0] != 5 || response[1] != 0) throw Failure(failure);
        }
        finally
        {
            // 只关闭本次检测创建的临时回环端口，不触碰用户隧道中的连接。
            await Task.Run(() =>
            {
                try { proxy.CloseProbe(); } catch (Exception) { }
                try { ssh.RemoveForwardedPort(proxy); } catch (Exception) { }
            }).ConfigureAwait(false);
        }
    }

    private static ProbeException Failure(Exception? error)
    {
        var detail = error?.Message ?? "SSH 服务端未确认目标连接。";
        var restricted = detail.Contains("administratively", StringComparison.OrdinalIgnoreCase) || detail.Contains("prohibited", StringComparison.OrdinalIgnoreCase);
        return new(restricted ? "SSH 服务端策略拒绝检测连接：" + detail : "经 SSH 连接目标失败：" + detail, restricted);
    }

    public static async Task LocalAsync(string host, int port, CancellationToken token)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, token).ConfigureAwait(false);
    }
}
