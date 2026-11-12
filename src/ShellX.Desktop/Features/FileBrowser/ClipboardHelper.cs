using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>剪贴板小工具：视图层没有剪贴板，用所属窗口的剪贴板即可。</summary>
internal static class ClipboardHelper
{
    /// <summary>把文本复制到剪贴板，失败时静默忽略。</summary>
    public static async Task CopyAsync(Control owner, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }
        catch (Exception) { }
    }
}
