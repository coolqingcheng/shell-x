using ShellX.Core.Settings;

namespace ShellX.Desktop.Services;

/// <summary>终端通道的着色设置。着色在通道内完成，控件只负责把设置同步过来。</summary>
public interface ITerminalHighlighting
{
    /// <summary>是否给无前景色的普通文本补颜色。</summary>
    bool HighlightEnabled { get; set; }

    /// <summary>当前主题，语义着色按主题取色。</summary>
    AppTheme Theme { get; set; }
}
