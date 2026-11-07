using System.Text;
using ShellX.Core.Connections;

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