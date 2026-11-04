using ShellX.Core.Tunnels;

namespace ShellX.Core.Tests;

public class TunnelSpeedTests
{
    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(-5, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(2048, "2 KB/s")]
    [InlineData(1572864, "1.5 MB/s")]
    [InlineData(3221225472d, "3 GB/s")]
    public void FormatsBytesPerSecond(double value, string expected)
        => Assert.Equal(expected, TunnelSpeed.Format(value));
}