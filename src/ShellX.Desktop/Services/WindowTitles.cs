using Avalonia.Controls;
using ShellX.Core;

namespace ShellX.Desktop.Services;

/// <summary>窗口标题统一附加当前版本号，便于确认实际运行的构建。</summary>
public static class WindowTitles
{
    public static void ApplyVersion(Window window)
    {
        var baseTitle = window.Title;
        if (string.IsNullOrWhiteSpace(baseTitle)) return;
        var version = $"v{AppVersion.Value}";
        if (baseTitle.Contains(version, StringComparison.OrdinalIgnoreCase)) return;
        window.Title = $"{baseTitle} {version}";
    }
}
