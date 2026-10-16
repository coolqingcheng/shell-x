using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Sessions;
using ShellX.Core.Tunnels;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Infrastructure.Tests;

public sealed class FixtureFactAttribute : FactAttribute
{
    public FixtureFactAttribute()
    { if (!File.Exists(Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE"))) Skip = "请先启动本地 SSH 测试服务并设置 SHELLX_TEST_FIXTURE。"; }
}

public class SshIntegrationTests
{
    private sealed record Fixture(int Port, string Root, string PrivateKey, string EncryptedPrivateKey);
    private static Fixture Config => JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE")!))!;
    private static ConnectionProfile Profile => new() { Name = "本机测试", Host = "127.0.0.1", Port = Config.Port, Username = "shellx" };
    private static SshSessionFactory Factory => new(NullLoggerFactory.Instance);
    private static Task<IRemoteSession> Connect() => Factory.ConnectAsync(Profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);

    [FixtureFact]
    public async Task PasswordAndKeysCanOpenUtf8TerminalAndSftp()
    {
        foreach (var kind in new[] { 0, 1, 2 })
        {
            var profile = kind == 0 ? Profile : Profile with { Authentication = AuthenticationKind.PrivateKey, PrivateKeyPath = kind == 1 ? Config.PrivateKey : Config.EncryptedPrivateKey };
            await using var session = await Factory.ConnectAsync(profile, new("fixture-password", kind == 2 ? "fixture-passphrase" : ""), _ => Task.FromResult(true), CancellationToken.None);
            Assert.NotNull(session.Files); Assert.Null(session.FileError);
            var banner = await ReadUntilAsync(session.Terminal.Stream, "$ "); Assert.Contains("本地测试服务器", banner); Assert.Contains("\x1b[32m", banner);
            session.Terminal.Resize(132, 40);
            await session.Terminal.Stream.WriteAsync(Encoding.UTF8.GetBytes("中文交互\r")); await session.Terminal.Stream.FlushAsync();
            Assert.Contains("中文交互", await ReadUntilAsync(session.Terminal.Stream, "$ "));
            Assert.Contains((await session.Files!.ListAsync(".")).Entries, x => x.Name == "hello.txt");
        }
    }

    [FixtureFact]
    public async Task UntrustedHostIsRejectedBeforeAuthentication()
    {
        var challenged = false;
        await Assert.ThrowsAnyAsync<Exception>(() => Factory.ConnectAsync(Profile, new("fixture-password"), challenge =>
        { challenged = true; Assert.StartsWith("SHA256:", challenge.Fingerprint); return Task.FromResult(false); }, CancellationToken.None));
        Assert.True(challenged);
    }

    [FixtureFact]
    public async Task MonitoringUsesExecChannelWithoutWritingIntoTerminal()
    {
        await using var session = await Connect();
        Assert.NotNull(session.Monitor);
        var first = await session.Monitor!.ReadAsync(CancellationToken.None);
        var next = await session.Monitor.ReadAsync(CancellationToken.None);
        Assert.Equal("fixture-linux", first.Hostname); Assert.NotEmpty(first.Disks); Assert.NotEmpty(first.Processes);
        Assert.True(next.Cpu.Total > first.Cpu.Total);
        Assert.Contains("本地测试服务器", await ReadUntilAsync(session.Terminal.Stream, "$ "));
        await session.Terminal.Stream.WriteAsync(Encoding.UTF8.GetBytes("still-interactive\r")); await session.Terminal.Stream.FlushAsync();
        Assert.Contains("still-interactive", await ReadUntilAsync(session.Terminal.Stream, "$ "));
    }

    [FixtureFact]
    public async Task FileOperationsPreservePermissionsAndDetectExternalChanges()
    {
        await using var session = await Connect(); var files = session.Files!;
        var name = "workflow-" + Guid.NewGuid().ToString("N"); var directory = "/" + name;
        await files.CreateAsync(directory, true);
        try
        {
            var path = directory + "/中文.txt"; await files.CreateAsync(path, false);
            var document = await files.ReadTextAsync(path); document = await files.SaveTextAsync(document, "hello 中文\n");
            Assert.Equal("hello 中文\n", (await files.ReadTextAsync(path)).Content);
            await files.SetPermissionsAsync(path, Convert.ToInt32("4750", 8));
            Assert.Equal("4750", Assert.Single((await files.ListAsync(directory)).Entries).PermissionText);
            document = await files.SaveTextAsync(document, "updated\n");
            Assert.Equal("4750", Assert.Single((await files.ListAsync(directory)).Entries).PermissionText);
            await File.AppendAllTextAsync(Path.Combine(Config.Root, name, "中文.txt"), "other writer");
            await Assert.ThrowsAsync<IOException>(() => files.SaveTextAsync(document, "must not overwrite"));
            await files.RenameAsync(path, directory + "/renamed.txt");
            Assert.True(await files.ExistsAsync(directory + "/renamed.txt"));
        }
        finally { await files.DeleteAsync(directory); }
        Assert.False(await files.ExistsAsync(directory));
        await files.ListAsync("/folder"); Assert.Equal("/", (await files.ListAsync("~")).Path);
    }

    [FixtureFact]
    public async Task RemoteEditorRejectsLargeLongLineAndBinaryFilesBeforeDisplayAndPreservesExistingDataOnSave()
    {
        await using var session = await Connect(); var files = session.Files!;
        var name = "editor-" + Guid.NewGuid().ToString("N");
        var local = Path.Combine(Config.Root, name); var remote = "/" + name;
        try
        {
            using (var large = File.Create(local)) large.SetLength(TextEditorLimits.MaxBytes + 1L);
            await Assert.ThrowsAsync<IOException>(() => files.ReadTextAsync(remote));
            foreach (var content in new[] { new string('a', TextEditorLimits.MaxLineLength + 1), new string('\n', TextEditorLimits.MaxLines), new string('x', TextEditorLimits.MaxCharacters + 1), "binary\0data" })
            {
                await File.WriteAllTextAsync(local, content);
                await Assert.ThrowsAsync<IOException>(() => files.ReadTextAsync(remote));
            }
            await File.WriteAllTextAsync(local, "preserve\n");
            var original = await files.ReadTextAsync(remote);
            await Assert.ThrowsAsync<IOException>(() => files.SaveTextAsync(original, new string('x', TextEditorLimits.MaxCharacters + 1)));
            Assert.Equal("preserve\n", await File.ReadAllTextAsync(local));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.ReadTextAsync(remote, cancel.Token));
        }
        finally { File.Delete(local); }
    }

    [FixtureFact]
    public async Task RemoteEditingPreservesLegacyAndUnicodeEncodingsAndSupportsConversion()
    {
        await using var session = await Connect(); var files = session.Files!; var codec = new ShellX.Infrastructure.Files.TextCodec();
        var name = "encodings-" + Guid.NewGuid().ToString("N"); var local = Path.Combine(Config.Root, name); var remote = "/" + name;
        try
        {
            foreach (var (codePage, bom) in new[] { (936, false), (1200, true), (1201, true), (950, false), (932, false) })
            {
                var standard = TextEncodingTests.StandardEncoding(codePage, bom); var text = TextEncodingTests.Sample(codePage);
                await File.WriteAllBytesAsync(local, standard.GetPreamble().Concat(standard.GetBytes(text)).ToArray());
                var original = await files.ReadTextAsync(remote);
                var decoded = codec.Decode(original.SourceBytes, TextEncodingInfo.Find(codePage, bom));
                original = original with { Content = decoded.Content, Encoding = decoded.Encoding };
                var updated = await files.SaveTextAsync(original, text + "# edited\r\n");
                Assert.Equal(standard.GetPreamble().Concat(standard.GetBytes(text + "# edited\r\n")).ToArray(), await File.ReadAllBytesAsync(local));
                var converted = await files.SaveTextAsync(updated with { SaveEncoding = TextEncodingInfo.Utf8 }, updated.Content);
                Assert.Equal(updated.Content, new UTF8Encoding(false, true).GetString(await File.ReadAllBytesAsync(local)));
                Assert.Equal(65001, converted.Encoding.CodePage); Assert.False(converted.Encoding.HasBom);
            }
        }
        finally { File.Delete(local); }
    }

    [FixtureFact]
    public async Task UploadDownloadRoundTripAndCancelledTask()
    {
        await using var session = await Connect(); var files = session.Files!;
        var local = Path.Combine(Path.GetTempPath(), "shellx-transfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(local);
        var remote = "/transfer-" + Guid.NewGuid().ToString("N"); var bytes = new byte[512 * 1024]; Random.Shared.NextBytes(bytes);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(local, "source"), bytes);
            var progress = new Progress<long>();
            await files.UploadAsync(Path.Combine(local, "source"), remote, progress);
            await files.DownloadAsync(remote, Path.Combine(local, "download"), progress);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(local, "download")));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.UploadAsync(Path.Combine(local, "source"), remote + "-cancel", progress, cancel.Token));
            Assert.False(await files.ExistsAsync(remote + "-cancel"));
        }
        finally { if (await files.ExistsAsync(remote)) await files.DeleteAsync(remote); Directory.Delete(local, true); }
    }

    [FixtureFact]
    public async Task RenamingAndDeletingSymlinksLeavesTargetIntact()
    {
        await using var session = await Connect(); var files = session.Files!;
        var name = "/link-test-" + Guid.NewGuid().ToString("N"); await files.CreateAsync(name, true);
        var target = name + "/target"; await files.CreateAsync(target, true); await files.CreateAsync(target + "/keep.txt", false);
        using var setup = new Renci.SshNet.SftpClient("127.0.0.1", Config.Port, "shellx", "fixture-password"); setup.Connect();
        setup.SymbolicLink(target, name + "/link");
        try
        {
            var link = (await files.ListAsync(name)).Entries.Single(x => x.Name == "link");
            Assert.True(link.IsSymbolicLink); Assert.True(link.IsDirectory);
            await files.RenameAsync(link.FullPath, name + "/renamed-link");
            Assert.True(await files.ExistsAsync(target + "/keep.txt"));
            Assert.True((await files.ListAsync(name)).Entries.Single(x => x.Name == "renamed-link").IsSymbolicLink);
            await files.DeleteAsync(name + "/renamed-link");
            Assert.True(await files.ExistsAsync(target + "/keep.txt"));
            setup.SymbolicLink(target, name + "/second-link");
            await files.DeleteAsync(name);
            Assert.False(await files.ExistsAsync(name));
        }
        finally { if (await files.ExistsAsync(name)) await files.DeleteAsync(name); }
    }

    [FixtureFact]
    public async Task ReconnectingReplacesFailedSharedTransportForExistingTabs()
    {
        var factory = Factory; var profile = Profile;
        await using var first = await factory.ConnectAsync(profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        await using var second = await factory.ConnectAsync(profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        await using var echo = new EchoServer();
        var rule = new TunnelDefinition { BindPort = FreePort(), TargetPort = echo.Port };
        await first.Tunnels.AutoStartAsync(rule);
        await first.Terminal.Stream.WriteAsync(Encoding.ASCII.GetBytes("__SX_DISCONNECT_TRANSPORT__\r"));
        await first.Terminal.Stream.FlushAsync();
        await UntilAsync(() => second.Tunnels.GetStatus(rule.Id).State == TunnelState.Failed);
        await using var reconnected = await factory.ConnectAsync(profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        await reconnected.Tunnels.AutoStartAsync(rule);
        Assert.Equal(TunnelState.Connected, second.Tunnels.GetStatus(rule.Id).State);
        Assert.Equal(reconnected.Tunnels.GetStatus(rule.Id), second.Tunnels.GetStatus(rule.Id));
        await second.Tunnels.StopAsync(rule.Id);
        Assert.Equal(TunnelState.Stopped, reconnected.Tunnels.GetStatus(rule.Id).State);
    }

    [FixtureFact]
    public async Task TabsShareTunnelStateAndClosingOwnerKeepsForwardingUntilLastTab()
    {
        var factory = Factory; var profile = Profile;
        await using var first = await factory.ConnectAsync(profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        await using var second = await factory.ConnectAsync(profile with { Id = Guid.NewGuid() }, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        await using var echo = new EchoServer();
        var rule = new TunnelDefinition { BindPort = FreePort(), TargetPort = echo.Port };
        var changes = new System.Collections.Concurrent.ConcurrentQueue<TunnelStatus>();
        second.Tunnels.StatusChanged += (_, status) => changes.Enqueue(status);
        await Task.WhenAll(first.Tunnels.AutoStartAsync(rule), second.Tunnels.AutoStartAsync(rule));
        Assert.Equal(TunnelState.Connected, first.Tunnels.GetStatus(rule.Id).State);
        Assert.Equal(first.Tunnels.GetStatus(rule.Id), second.Tunnels.GetStatus(rule.Id));
        Assert.Contains(changes, s => s.State == TunnelState.Connected);
        await second.Tunnels.StopAsync(rule.Id);
        Assert.Equal(TunnelState.Stopped, first.Tunnels.GetStatus(rule.Id).State);
        await using var third = await factory.ConnectAsync(profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        await third.Tunnels.AutoStartAsync(rule);
        Assert.Equal(TunnelState.Stopped, third.Tunnels.GetStatus(rule.Id).State);
        await third.Tunnels.StartAsync(rule);
        await first.DisposeAsync(); // 首个 SSH 传输连接仍由余下标签的隧道持有。
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, rule.BindPort);
            var payload = Encoding.UTF8.GetBytes("owner closed, tunnel stays");
            await client.GetStream().WriteAsync(payload);
            var response = new byte[payload.Length];
            await client.GetStream().ReadExactlyAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(payload, response);
        }
        await second.DisposeAsync();
        Assert.True(third.Tunnels.IsRunning(rule.Id));
        await third.DisposeAsync();
        using var released = new TcpListener(IPAddress.Loopback, rule.BindPort); released.Start();
    }

    [FixtureFact]
    public async Task BothTunnelDirectionsForwardBytesAndReleaseListeners()
    {
        foreach (var direction in new[] { TunnelDirection.Local, TunnelDirection.Remote })
        {
            await using var session = await Connect();
            await using var echo = new EchoServer();
            var bindPort = FreePort();
            var rule = new TunnelDefinition { Direction = direction, BindPort = bindPort, TargetPort = echo.Port };
            await session.Tunnels.StartAsync(rule); Assert.True(session.Tunnels.IsRunning(rule.Id));
            Assert.Equal(TunnelState.Connected, session.Tunnels.GetStatus(rule.Id).State);
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, bindPort);
            var payload = Encoding.UTF8.GetBytes("tunnel 中文"); await client.GetStream().WriteAsync(payload);
            var response = new byte[payload.Length];
            await client.GetStream().ReadExactlyAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(payload, response);
            await session.Tunnels.StopAsync(rule.Id); Assert.False(session.Tunnels.IsRunning(rule.Id));
            await Task.Delay(100); // 已停止的规则不应被稍晚到达的连接回调改回异常或连通。
            Assert.Equal(TunnelState.Stopped, session.Tunnels.GetStatus(rule.Id).State);
        }
    }

    [FixtureFact]
    public async Task TunnelHealthTracksTargetFailureRecoveryAndRealForwardErrors()
    {
        foreach (var direction in new[] { TunnelDirection.Local, TunnelDirection.Remote })
        {
            await using var session = await Connect();
            using var target = new TcpListener(IPAddress.Loopback, 0); target.Start();
            var targetPort = ((IPEndPoint)target.LocalEndpoint).Port;
            var rule = new TunnelDefinition { Name = "连通性测试", Direction = direction, BindPort = FreePort(), TargetPort = targetPort };
            var events = new System.Collections.Concurrent.ConcurrentQueue<TunnelStatus>();
            session.Tunnels.StatusChanged += (_, status) => events.Enqueue(status);
            await session.Tunnels.StartAsync(rule);
            Assert.Equal(TunnelState.Connected, session.Tunnels.GetStatus(rule.Id).State);
            target.Stop(); await session.Tunnels.CheckAsync(rule.Id);
            var failed = session.Tunnels.GetStatus(rule.Id);
            Assert.Equal(TunnelState.Failed, failed.State); Assert.True(failed.IsRunning);
            Assert.Contains(targetPort.ToString(), failed.Reason); Assert.NotNull(failed.CheckedAt);
            using var recovered = new TcpListener(IPAddress.Loopback, targetPort); recovered.Start();
            try { await UntilAsync(() => session.Tunnels.GetStatus(rule.Id).State == TunnelState.Connected, 20); }
            catch (TimeoutException ex)
            { throw new InvalidOperationException(direction + " 恢复失败：\n" + string.Join("\n", events.Select(e => $"{e.State}: {e.Reason}")), ex); }
            var recoveredStatus = session.Tunnels.GetStatus(rule.Id);
            Assert.True(recoveredStatus.State == TunnelState.Connected, $"{direction}: {recoveredStatus.State} / {recoveredStatus.Reason}");
            recovered.Stop();
            using var incoming = new TcpClient(); await incoming.ConnectAsync(IPAddress.Loopback, rule.BindPort);
            await UntilAsync(() => session.Tunnels.GetStatus(rule.Id).State == TunnelState.Failed);
            Assert.NotEmpty(session.Tunnels.GetStatus(rule.Id).Reason);
            await session.Tunnels.StopAsync(rule.Id); await session.Tunnels.CheckAsync(rule.Id);
            Assert.Equal(TunnelState.Stopped, session.Tunnels.GetStatus(rule.Id).State);
            Assert.All(events, state => Assert.Equal(rule.Id, state.TunnelId));
        }
    }

    [FixtureFact]
    public async Task TunnelConflictsIdentifyTheRuleAndExternalPortWithoutBreakingOtherTunnels()
    {
        await using var session = await Connect(); await using var echo = new EchoServer();
        var first = new TunnelDefinition { Name = "正常数据库隧道", BindPort = FreePort(), TargetPort = echo.Port };
        await session.Tunnels.StartAsync(first);
        var conflict = first with { Id = Guid.NewGuid(), Name = "重复监听" };
        await Assert.ThrowsAnyAsync<Exception>(() => session.Tunnels.StartAsync(conflict));
        Assert.Contains("正常数据库隧道", session.Tunnels.GetStatus(conflict.Id).Reason);
        Assert.Equal(TunnelState.Failed, session.Tunnels.GetStatus(conflict.Id).State);
        Assert.False(session.Tunnels.IsRunning(conflict.Id));
        Assert.Equal(TunnelState.Connected, session.Tunnels.GetStatus(first.Id).State);
        using var occupied = new TcpListener(IPAddress.Loopback, 0); occupied.Start();
        var occupiedPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
        var local = new TunnelDefinition { BindPort = occupiedPort, TargetPort = echo.Port };
        await Assert.ThrowsAnyAsync<Exception>(() => session.Tunnels.StartAsync(local));
        Assert.Contains("端口冲突", session.Tunnels.GetStatus(local.Id).Reason);
        var remote = local with { Id = Guid.NewGuid(), Direction = TunnelDirection.Remote };
        await Assert.ThrowsAnyAsync<Exception>(() => session.Tunnels.StartAsync(remote));
        Assert.Contains("服务端拒绝监听", session.Tunnels.GetStatus(remote.Id).Reason);
        Assert.Contains("未返回细分原因", session.Tunnels.GetStatus(remote.Id).Reason);
        var prohibited = first with { Id = Guid.NewGuid(), BindPort = FreePort(), TargetHost = "192.0.2.1" };
        await session.Tunnels.StartAsync(prohibited); // 测试服务拒绝此目标，不向外部地址发起连接。
        Assert.Equal(TunnelState.Failed, session.Tunnels.GetStatus(prohibited.Id).State);
        Assert.Contains("未通过连通检测", session.Tunnels.GetStatus(prohibited.Id).Reason);
        Assert.NotEmpty(session.Tunnels.GetStatus(prohibited.Id).Reason);
        Assert.Equal(TunnelState.Connected, session.Tunnels.GetStatus(first.Id).State);
        Assert.NotEmpty((await session.Files!.ListAsync("/")).Entries);
    }

    [FixtureFact]
    public async Task EditingRunningAndStoppedRulesKeepsSharedStatusAndReleasesOldListeners()
    {
        foreach (var direction in new[] { TunnelDirection.Local, TunnelDirection.Remote })
        {
            var factory = Factory;
            await using var first = await factory.ConnectAsync(Profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
            await using var second = await factory.ConnectAsync(Profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
            await using var echo = new EchoServer();
            var rule = new TunnelDefinition { Direction = direction, BindPort = FreePort(), TargetPort = echo.Port };
            await first.Tunnels.AutoStartAsync(rule);
            var changed = rule with { Name = "编辑后的规则", BindPort = FreePort() };
            await second.Tunnels.UpdateAsync(changed);
            Assert.Equal(TunnelState.Connected, first.Tunnels.GetStatus(rule.Id).State);
            Assert.Equal(first.Tunnels.GetStatus(rule.Id), second.Tunnels.GetStatus(rule.Id));
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, changed.BindPort);
                var payload = Encoding.UTF8.GetBytes("edited tunnel 中文"); await client.GetStream().WriteAsync(payload);
                var response = new byte[payload.Length]; await client.GetStream().ReadExactlyAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(payload, response);
            }
            // The local listener is owned by this process; rebinding proves its old port was released.
            if (direction == TunnelDirection.Local) { using var released = new TcpListener(IPAddress.Loopback, rule.BindPort); released.Start(); }
            await first.Tunnels.StopAsync(rule.Id);
            changed = changed with { BindPort = FreePort() };
            await second.Tunnels.UpdateAsync(changed);
            Assert.False(first.Tunnels.IsRunning(rule.Id));
            await second.Tunnels.AutoStartAsync(changed);
            Assert.Equal(TunnelState.Stopped, second.Tunnels.GetStatus(rule.Id).State);
            await first.Tunnels.StartAsync(changed);
            using var conflict = new TcpListener(IPAddress.Loopback, 0); conflict.Start();
            var conflicting = changed with { BindPort = ((IPEndPoint)conflict.LocalEndpoint).Port };
            await Assert.ThrowsAnyAsync<Exception>(() => first.Tunnels.UpdateAsync(conflicting));
            Assert.Equal(TunnelState.Failed, second.Tunnels.GetStatus(rule.Id).State);
            Assert.NotEmpty(second.Tunnels.GetStatus(rule.Id).Reason);
            Assert.False(second.Tunnels.IsRunning(rule.Id));
        }
    }

    [FixtureFact]
    public async Task ConnectionTestingUsesCurrentCredentialsVerifiesIdentityAndReleasesItsSession()
    {
        var tester = new SshConnectionTester(NullLoggerFactory.Instance);
        foreach (var kind in new[] { 0, 1, 2 })
        {
            var profile = kind == 0 ? Profile : Profile with { Authentication = AuthenticationKind.PrivateKey, PrivateKeyPath = kind == 1 ? Config.PrivateKey : Config.EncryptedPrivateKey };
            var verified = false;
            var result = await tester.TestAsync(profile, new("fixture-password", kind == 2 ? "fixture-passphrase" : ""), challenge =>
            { verified = true; Assert.StartsWith("SHA256:", challenge.Fingerprint); return Task.FromResult(true); }, CancellationToken.None);
            Assert.True(verified); Assert.True(result.SftpAvailable); Assert.Null(result.FileError);
        }
        await Assert.ThrowsAnyAsync<Exception>(() => tester.TestAsync(Profile, new("incorrect-password"), _ => Task.FromResult(true), CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => tester.TestAsync(Profile, new("fixture-password"), _ => Task.FromResult(false), CancellationToken.None));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tester.TestAsync(Profile, new(), _ => Task.FromResult(true), cancelled.Token));
    }

    private sealed class EchoServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancel = new();
        private readonly List<Task> _clients = [];
        private readonly Task _accept;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public EchoServer() { _listener.Start(); _accept = AcceptAsync(); }
        private async Task AcceptAsync()
        {
            try { while (true) { var client = await _listener.AcceptTcpClientAsync(_cancel.Token); _clients.Add(EchoAsync(client)); } }
            catch (OperationCanceledException) { }
        }
        private async Task EchoAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream(); var buffer = new byte[4096]; int count;
                    while ((count = await stream.ReadAsync(buffer, _cancel.Token)) > 0) await stream.WriteAsync(buffer.AsMemory(0, count), _cancel.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        }
        public async ValueTask DisposeAsync() { _cancel.Cancel(); await _accept; _listener.Stop(); await Task.WhenAll(_clients); _cancel.Dispose(); }
    }
    private static async Task UntilAsync(Func<bool> condition, int seconds = 8)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition()) { if (DateTime.UtcNow >= limit) throw new TimeoutException("未收到隧道状态更新。"); await Task.Delay(20); }
    }
    private static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    private static Task<string> ReadUntilAsync(Stream stream, string marker) => Task.Run(() =>
    {
        using var memory = new MemoryStream(); var buffer = new byte[4096];
        while (memory.Length < 65536)
        {
            var read = stream.Read(buffer, 0, buffer.Length); if (read == 0) break; memory.Write(buffer, 0, read);
            var text = Encoding.UTF8.GetString(memory.ToArray()); if (text.Contains(marker)) return text;
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }).WaitAsync(TimeSpan.FromSeconds(10));
}
