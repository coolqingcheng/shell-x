using System.Net;
using System.Net.Sockets;

namespace ShellX.Infrastructure.Ssh;

/// <summary>
/// 监听端和下一跳之间的 TCP 中转：只在收到连接时才建立上游连接，空闲时不产生任何目标连接，
/// 同时累计上下行字节供隧道速度显示。端口冲突在 <see cref="Start"/> 里以 SocketException 抛出，
/// 保证和原来的监听错误文案一致。
/// </summary>
internal sealed class TunnelRelay : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<CancellationToken, Task<Socket>> _connect;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private readonly object _sync = new();
    private long _sent;
    private long _received;
    private bool _disposed;
    private string? _fault;

    public TunnelRelay(string host, int port, Func<CancellationToken, Task<Socket>> connect)
    {
        _listener = new TcpListener(Resolve(host), port);
        _connect = connect;
        _token = _lifetime.Token;
    }

    public long BytesSent => Interlocked.Read(ref _sent);
    public long BytesReceived => Interlocked.Read(ref _received);
    public bool IsListening { get; private set; }
    public string? Fault { get { lock (_sync) return _fault; } }

    /// <summary>开始监听并返回实际端口；绑定失败（端口占用、地址不可用）直接抛出。</summary>
    public int Start()
    {
        _listener.Start();
        IsListening = true;
        _ = Task.Run(AcceptAsync);
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_token).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            IsListening = false;
            lock (_sync) _fault = ex is SocketException socket ? socket.Message : ex.Message;
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        CancellationTokenSource? pump = null;
        Socket? upstream = null;
        try
        {
            if (_token.IsCancellationRequested) return;
            pump = CancellationTokenSource.CreateLinkedTokenSource(_token);
            // 上游连接失败（目标拒绝、SSH 转发被拒）只关闭这条客户端连接，不影响监听本身。
            try { upstream = await _connect(pump.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception) { return; }
            var local = client.Client;
            var outbound = PumpAsync(local, upstream, outgoing: true, pump.Token);
            var inbound = PumpAsync(upstream, local, outgoing: false, pump.Token);
            await Task.WhenAny(outbound, inbound).ConfigureAwait(false);
            try { pump.Cancel(); } catch (ObjectDisposedException) { }
            await Task.WhenAll(outbound, inbound).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            pump?.Dispose();
            if (upstream != null)
            {
                try { upstream.Shutdown(SocketShutdown.Both); } catch (SocketException) { } catch (ObjectDisposedException) { }
                try { upstream.Dispose(); } catch (ObjectDisposedException) { }
            }
            client.Dispose();
        }
    }

    private async Task PumpAsync(Socket from, Socket to, bool outgoing, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var read = await from.ReceiveAsync(buffer, SocketFlags.None, token).ConfigureAwait(false);
                if (read <= 0) break;
                var offset = 0;
                while (offset < read)
                {
                    var written = await to.SendAsync(buffer.AsMemory(offset, read - offset), SocketFlags.None, token).ConfigureAwait(false);
                    if (written <= 0) return;
                    offset += written;
                }
                if (outgoing) Interlocked.Add(ref _sent, read);
                else Interlocked.Add(ref _received, read);
            }
            try { to.Shutdown(SocketShutdown.Send); } catch (SocketException) { } catch (ObjectDisposedException) { }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private static IPAddress Resolve(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return IPAddress.Loopback;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
        if (IPAddress.TryParse(host, out var address)) return address;
        // 解析失败沿用 SocketException，由启动错误文案显示“无法解析地址”。
        var addresses = Dns.GetHostAddresses(host);
        return addresses.Length > 0 ? addresses[0] : throw new SocketException((int)SocketError.HostNotFound);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsListening = false;
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
        try { _listener.Stop(); } catch (ObjectDisposedException) { }
        _lifetime.Dispose();
    }
}