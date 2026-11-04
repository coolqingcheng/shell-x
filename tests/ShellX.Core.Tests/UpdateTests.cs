using ShellX.Core;
using ShellX.Core.Updates;

namespace ShellX.Core.Tests;

public sealed class UpdateTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3-beta.1", "1.2.3")]
    public void VersionTagsAreNormalized(string tag, string expected)
    {
        Assert.True(UpdateVersion.TryParse(tag, out var version));
        Assert.Equal(expected, version.ToString());
    }

    [Fact]
    public void NewerReleaseIsDetected()
    {
        Assert.True(UpdateVersion.IsNewer("v1.0.1", "1.0.0"));
        Assert.False(UpdateVersion.IsNewer("v1.0.0", "1.0.0"));
        Assert.False(UpdateVersion.IsNewer("v0.9.9", "1.0.0"));
        Assert.False(UpdateVersion.IsNewer(null, "1.0.0"));
        Assert.False(UpdateVersion.IsNewer("v1.0.1", "not-a-version"));
    }

    [Fact]
    public void InstallerAssetIsPreferredOverOtherFiles()
    {
        var assets = new[]
        {
            new UpdateAsset("ShellX-1.0.0.zip", "https://example.invalid/zip", 10),
            new UpdateAsset("ShellX-Setup-1.0.0.exe", "https://example.invalid/setup", 20),
            new UpdateAsset("notes.txt", "https://example.invalid/notes", 1)
        };
        var installer = UpdateAssetSelector.SelectInstaller(assets);
        Assert.NotNull(installer);
        Assert.Equal("ShellX-Setup-1.0.0.exe", installer!.Name);
        Assert.Null(UpdateAssetSelector.SelectInstaller([]));
    }

    [Fact]
    public void ApplicationVersionIsNumeric()
    {
        Assert.True(UpdateVersion.TryParse(AppVersion.Value, out var version));
        Assert.True(version.Build >= 0);
    }
}