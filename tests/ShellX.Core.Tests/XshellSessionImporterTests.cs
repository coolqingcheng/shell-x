using System.Text;
using ShellX.Core.Connections;
using ShellX.Core.Tunnels;

namespace ShellX.Core.Tests;

public sealed class XshellSessionImporterTests
{
    [Fact]
    public void ParsesSshSessionFieldsAndReportsSessionWithoutSavedPassword()
    {
        var result = XshellSessionImporter.Parse(Session(
            host: "iot99.xiaolab.cn", port: "30436", username: "deploy", initial: "/var/www"),
            "生产服务器");
        var profile = result.Profile;
        Assert.Equal("生产服务器", profile.Name);
        Assert.Equal(XshellSessionImporter.DefaultGroup, profile.Group);
        Assert.Equal("iot99.xiaolab.cn", profile.Host);
        Assert.Equal(30436, profile.Port);
        Assert.Equal("deploy", profile.Username);
        Assert.Equal("/var/www", profile.InitialDirectory);
        Assert.Equal(AuthenticationKind.Password, profile.Authentication);
        Assert.False(profile.RememberSecret);
        Assert.Equal("", result.Secret.Password);
        Assert.Contains("没有保存登录密码", result.Warning);
        profile.Validate();
    }

    [Fact]
    public void ImportsSavedPasswordAndReportsWrongOrMissingCredentialData()
    {
        var key = XshellPasswordCipher.BuildCurrentUserKey();
        if (key.Length == 0) return;   // Xshell 密码保存是 Windows 本机账户特性
        var encoded = XshellPasswordCipher.Encrypt("direct-xshell-password", key);
        var result = XshellSessionImporter.Parse(Session(password: encoded), "password-session");
        Assert.Equal("direct-xshell-password", result.Secret.Password);
        Assert.True(result.Profile.RememberSecret);
        Assert.Null(result.Warning);

        var masterEncoded = XshellPasswordCipher.Encrypt("master-password-session", "xshell-master-key");
        Assert.Throws<XshellCredentialDecryptException>(() =>
            XshellSessionImporter.Parse(Session(password: masterEncoded), "needs-master-key"));
        var masterResult = XshellSessionImporter.Parse(Session(password: masterEncoded), "needs-master-key", null, "xshell-master-key");
        Assert.Equal("master-password-session", masterResult.Secret.Password);

        Assert.Throws<XshellCredentialDecryptException>(() =>
            XshellSessionImporter.Parse(Session(password: Convert.ToBase64String(new byte[64])), "bad-password"));
        Assert.Throws<XshellCredentialDecryptException>(() =>
            XshellSessionImporter.Parse(Session(password: "not-base64"), "bad-base64"));
    }

    [Fact]
    public void ResolvesExistingPrivateKeyAndDecryptsItsPassphraseButMissingKeyIsAnError()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-xsh-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var keyPath = Path.Combine(root, "id_ed25519");
            File.WriteAllText(keyPath, "test-key");
            var key = XshellPasswordCipher.BuildCurrentUserKey();
            var encodedPassphrase = key.Length == 0 ? "" : XshellPasswordCipher.Encrypt("key-passphrase", key);
            var withKey = XshellSessionImporter.Parse(Session(userKey: "id_ed25519", passphrase: encodedPassphrase), "key-session", root);
            Assert.Equal(AuthenticationKind.PrivateKey, withKey.Profile.Authentication);
            Assert.Equal(keyPath, withKey.Profile.PrivateKeyPath);
            Assert.Equal(encodedPassphrase.Length == 0 ? "" : "key-passphrase", withKey.Secret.Passphrase);
            Assert.Equal(encodedPassphrase.Length > 0, withKey.Profile.RememberSecret);

            var missing = Assert.Throws<FileNotFoundException>(() =>
                XshellSessionImporter.Parse(Session(userKey: "missing-key"), "missing-session", root));
            Assert.Contains("私钥", missing.Message);
            Assert.Contains("未找到", missing.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReadsUtf16XshellFileAndRejectsUnsupportedProtocolOrBadPort()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-xsh-file-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "183.66.27.19.xsh");
            File.WriteAllText(path, Session(host: "183.66.27.19"), Encoding.Unicode);   // FF FE BOM
            var parsed = XshellSessionImporter.ParseFile(path);
            Assert.Equal("183.66.27.19", parsed.Profile.Host);
            Assert.Equal("183.66.27.19", parsed.Profile.Name);

            var telnet = Assert.Throws<NotSupportedException>(() => XshellSessionImporter.Parse(Session(protocol: "TELNET"), "telnet"));
            Assert.Contains("仅支持导入 SSH", telnet.Message);
            Assert.Throws<InvalidDataException>(() => XshellSessionImporter.Parse(Session(port: "0"), "bad-port"));
            Assert.Throws<InvalidDataException>(() => XshellSessionImporter.Parse("[OTHER]\nHost=host", "not-xshell"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SupportsUnicodeSessionNamesAndIgnoresDuplicateIniKeysInOtherSections()
    {
        var content = """
            [SessionInfo]
            Description=Xshell session file
            [CONNECTION]
            Host=示例主机.invalid
            Port=22
            Protocol=SSH
            [CONNECTION:AUTHENTICATION]
            UserName=运维
            AuthMethodList=01
            UserKey=
            """;
        var parsed = XshellSessionImporter.Parse(content, "生产 / 华东");
        Assert.Equal("示例主机.invalid", parsed.Profile.Host);
        Assert.Equal("运维", parsed.Profile.Username);
        Assert.Equal("生产 / 华东", parsed.Profile.Name);
        Assert.Contains("没有保存登录密码", parsed.Warning);
    }

    [Fact]
    public void ImportsForwardingRulesAsTunnels()
    {
        var content = """
            [CONNECTION]
            Host=iot99.xiaolab.cn
            Port=22
            Protocol=SSH
            [CONNECTION:SSH]
            FwdReqCount=3
            FwdReq_0_Description=emqx面板
            FwdReq_0_Incoming=0
            FwdReq_0_Source=localhost
            FwdReq_0_Port=18083
            FwdReq_0_Host=localhost
            FwdReq_0_HostPort=18083
            FwdReq_0_LocalOnly=1
            FwdReq_1_Incoming=0
            FwdReq_1_Source=localhost
            FwdReq_1_Port=3307
            FwdReq_1_Host=127.0.0.1
            FwdReq_1_HostPort=3306
            FwdReq_1_LocalOnly=1
            FwdReq_2_Incoming=1
            FwdReq_2_Source=0.0.0.0
            FwdReq_2_Port=8080
            FwdReq_2_Host=127.0.0.1
            FwdReq_2_HostPort=80
            FwdReq_2_LocalOnly=0
            [CONNECTION:AUTHENTICATION]
            UserName=root
            AuthMethodList=01
            Password=
            UserKey=
            """;

        var result = XshellSessionImporter.Parse(content, "转发测试");
        var tunnels = result.Profile.Tunnels;
        Assert.Equal(3, tunnels.Count);

        Assert.Equal("emqx面板", tunnels[0].Name);
        Assert.Equal(TunnelDirection.Local, tunnels[0].Direction);
        Assert.Equal("127.0.0.1", tunnels[0].BindHost);
        Assert.Equal(18083, tunnels[0].BindPort);
        Assert.Equal("localhost", tunnels[0].TargetHost);
        Assert.Equal(18083, tunnels[0].TargetPort);

        Assert.Equal("Xshell 转发 2", tunnels[1].Name);
        Assert.Equal(TunnelDirection.Local, tunnels[1].Direction);
        Assert.Equal(3307, tunnels[1].BindPort);
        Assert.Equal("127.0.0.1", tunnels[1].TargetHost);
        Assert.Equal(3306, tunnels[1].TargetPort);

        Assert.Equal(TunnelDirection.Remote, tunnels[2].Direction);
        Assert.Equal("0.0.0.0", tunnels[2].BindHost);
        Assert.Equal(8080, tunnels[2].BindPort);
        Assert.Equal("127.0.0.1", tunnels[2].TargetHost);
        Assert.Equal(80, tunnels[2].TargetPort);
        Assert.Equal(3, tunnels.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public void SkipsUnsupportedForwardingRulesAndReportsWarning()
    {
        var content = """
            [CONNECTION]
            Host=server.test
            Port=22
            Protocol=SSH
            [CONNECTION:SSH]
            FwdReq_0_Incoming=2
            FwdReq_0_Port=1080
            FwdReq_0_Host=127.0.0.1
            FwdReq_0_HostPort=1080
            FwdReq_1_Incoming=0
            FwdReq_1_Port=0
            FwdReq_1_Host=127.0.0.1
            FwdReq_1_HostPort=22
            [CONNECTION:AUTHENTICATION]
            UserName=root
            AuthMethodList=01
            Password=
            UserKey=
            """;

        var result = XshellSessionImporter.Parse(content, "跳过测试");
        Assert.Empty(result.Profile.Tunnels);
        Assert.Contains("暂不支持", result.Warning);
        Assert.Contains("监听端口", result.Warning);
    }

    private static string Session(string protocol = "SSH", string host = "server.test", string port = "22",
        string username = "root", string userKey = "", string initial = "", string password = "", string passphrase = "")
        => string.Join('\n',
            "[SessionInfo]",
            "Version=8.1",
            "Description=Xshell session file",
            "[CONNECTION]",
            $"Host={host}",
            $"Port={port}",
            $"Protocol={protocol}",
            "[CONNECTION:SSH]",
            $"InitRemoteDirectory={initial}",
            "[CONNECTION:AUTHENTICATION]",
            $"AuthMethodList={(userKey.Length == 0 ? "01" : "01,11")}",
            $"Password={password}",
            $"Passphrase={passphrase}",
            $"UserKey={userKey}",
            $"UserName={username}",
            "[OTHER]",
            "Host=ignored.invalid",
            "Port=9999");
}