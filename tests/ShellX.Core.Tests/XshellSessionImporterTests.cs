using System.Text;
using ShellX.Core.Connections;

namespace ShellX.Core.Tests;

public sealed class XshellSessionImporterTests
{
    [Fact]
    public void ParsesSshSessionFieldsAndKeepsSecretsOut()
    {
        var profile = XshellSessionImporter.Parse(Session(
            host: "iot99.xiaolab.cn", port: "30436", username: "deploy", initial: "/var/www"),
            "生产服务器");
        Assert.Equal("生产服务器", profile.Name);
        Assert.Equal(XshellSessionImporter.DefaultGroup, profile.Group);
        Assert.Equal("iot99.xiaolab.cn", profile.Host);
        Assert.Equal(30436, profile.Port);
        Assert.Equal("deploy", profile.Username);
        Assert.Equal("/var/www", profile.InitialDirectory);
        Assert.Equal(AuthenticationKind.Password, profile.Authentication);
        Assert.False(profile.RememberSecret);
        profile.Validate();
    }

    [Fact]
    public void ResolvesUserKeyFromSourceDirectoryAndFallsBackToPassword()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-xsh-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var keyPath = Path.Combine(root, "id_ed25519");
            File.WriteAllText(keyPath, "test-key");
            var withKey = XshellSessionImporter.Parse(Session(userKey: "id_ed25519"), "key-session", root);
            Assert.Equal(AuthenticationKind.PrivateKey, withKey.Authentication);
            Assert.Equal(keyPath, withKey.PrivateKeyPath);

            var missing = XshellSessionImporter.Parse(Session(userKey: "missing-key"), "missing-session", root);
            Assert.Equal(AuthenticationKind.Password, missing.Authentication);
            Assert.Equal("", missing.PrivateKeyPath);
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
            var profile = XshellSessionImporter.ParseFile(path);
            Assert.Equal("183.66.27.19", profile.Host);
            Assert.Equal("183.66.27.19", profile.Name);

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
        var profile = XshellSessionImporter.Parse(content, "生产 / 华东");
        Assert.Equal("示例主机.invalid", profile.Host);
        Assert.Equal("运维", profile.Username);
        Assert.Equal("生产 / 华东", profile.Name);
    }

    private static string Session(string protocol = "SSH", string host = "server.test", string port = "22",
        string username = "root", string userKey = "", string initial = "")
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
            $"UserKey={userKey}",
            $"UserName={username}",
            "[OTHER]",
            "Host=ignored.invalid",
            "Port=9999");
}