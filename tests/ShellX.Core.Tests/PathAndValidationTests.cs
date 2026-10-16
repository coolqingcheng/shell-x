using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Tunnels;

namespace ShellX.Core.Tests;
public class PathAndValidationTests
{
    [Theory]
    [InlineData("/", "hello.txt", "/hello.txt")]
    [InlineData("/home/user/", "中文文件.txt", "/home/user/中文文件.txt")]
    [InlineData("/tmp", "a\\b", "/tmp/a\\b")]
    public void RemotePathsUseUnixSeparators(string directory, string name, string expected)
        => Assert.Equal(expected, RemotePath.Combine(directory, name));
    [Theory]
    [InlineData("..")][InlineData(".")][InlineData("")][InlineData("/etc/passwd")][InlineData("a/b")]
    public void FileNamesCannotEscapeDirectory(string name) => Assert.Throws<ArgumentException>(() => RemotePath.Combine("/tmp", name));
    [Theory]
    [InlineData("/", "/")][InlineData("/root", "/")][InlineData("/a/b/", "/a")]
    public void ParentHandlesRoot(string path, string expected) => Assert.Equal(expected, RemotePath.Parent(path));
    [Fact]
    public void AuthenticationAndPortAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new ConnectionProfile { Host = "localhost", Port = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new ConnectionProfile { Host = "localhost", Authentication = AuthenticationKind.PrivateKey }.Validate());
        Assert.Throws<ArgumentException>(() => new TunnelDefinition { BindPort = 65536 }.Validate());
    }
}
