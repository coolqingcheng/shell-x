using System.Net;
using System.Net.Sockets;
using System.Text;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Infrastructure.Tests;

public sealed class TunnelRelayTests
{
    [Fact]
    public async Task RelayForwardsOnlyOnDemandAndCountsBothDirections()
    {
        await using var echo = new Echo();
        var relay = new TunnelRelay("127.0.0.1", 0, async token =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(IPAddress.Loopback, echo.Port, token); return socket; }
            catch { socket.Dispose(); throw; }
        });
        try
        {
            var port = relay.Start();
            Assert.True(relay.IsListening);
            Assert.Null(relay.Fault);
            // 空闲时不向目标发起任何连接，后台刷新不得制造连接负载。
            Assert.Equal(0, echo.ConnectionCount);

            var payload = Encoding.UTF8.GetBytes("relay 中文 payload");
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await client.GetStream().WriteAsync(payload);
            var buffer = new byte[payload.Length];
            await client.GetStream().ReadExactlyAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(payload, buffer);
            Assert.Equal(1, echo.ConnectionCount);
            await UntilAsync(() => relay.BytesSent >= payload.Length && relay.BytesReceived >= payload.Length);
            Assert.True(relay.BytesSent >= payload.Length, relay.BytesSent.ToString());
            Assert.True(relay.BytesReceived >= payload.Length, relay.BytesReceived.ToString());
            await Task.Delay(1100); // 采样周期内没有新连接，不能凭空多出目标连接。
            Assert.Equal(1, echo.ConnectionCount);
            var bound = port;
            relay.Dispose();
            Assert.False(relay.IsListening);
            using var rebound = new TcpListener(IPAddress.Loopback, bound);
            rebound.Start(); // 监听端口已释放
        }
        finally { relay.Dispose(); }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++) await Task.Delay(20);
    }

    private sealed class Echo : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancel = new();
        private readonly Task _accept;
        private int _count;
        public Echo()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _accept = AcceptAsync();
        }
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int ConnectionCount => Volatile.Read(ref _count);
        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cancel.Token);
                    Interlocked.Increment(ref _count);
                    _ = Task.Run(() => EchoAsync(client));
                }
            }
            catch (OperationCanceledException) { }
        }
        private async Task EchoAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var buffer = new byte[4096];
                    int count;
                    while ((count = await stream.ReadAsync(buffer, _cancel.Token)) > 0)
                        await stream.WriteAsync(buffer.AsMemory(0, count), _cancel.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        }
        public async ValueTask DisposeAsync()
        {
            _cancel.Cancel(); await _accept; _listener.Stop(); _cancel.Dispose();
        }
    }
}