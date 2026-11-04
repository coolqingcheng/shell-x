namespace ShellX.Core;

/// <summary>当前程序版本号，取自 Directory.Build.props 的 Version。</summary>
public static class AppVersion
{
    public static string Value { get; } = Detect();

    private static string Detect()
    {
        var version = typeof(AppVersion).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}