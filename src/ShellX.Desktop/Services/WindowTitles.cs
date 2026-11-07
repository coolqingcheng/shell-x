using Avalonia.Controls;
using ShellX.Core;

namespace ShellX.Desktop.Services;

/// <summary>窗口标题统一附加当前版本号，便于确认实际运行的构建。</summary>
public static class WindowTitles
{
    public static void ApplyVersion(Window window)
    {
        if (!string.IsNullOrWhiteSpace(window.Title)) window.Title = ApplyVersionToTitle(window.Title);
    }

    public static string ApplyVersionToTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        var version = $"v{AppVersion.Value}";
        return title.Contains(version, StringComparison.OrdinalIgnoreCase) ? title : $"{title} {version}";
    }
}
