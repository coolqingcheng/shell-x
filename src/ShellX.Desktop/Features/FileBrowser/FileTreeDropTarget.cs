using Avalonia;
using Avalonia.VisualTree;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>
/// 找出拖放落在哪一行。
/// 右侧面板整块都是放置区，落点不决定目标目录，用不到它；
/// 旧的两栏文件树还是树形，落在目录行上才算放进那个目录。
/// </summary>
internal static class FileTreeDropTarget
{
    /// <summary>指针下方的树节点；落在空白处返回 null。</summary>
    public static FileTreeNodeViewModel? Find(Visual root, Point position)
    {
        // 虚拟列表里只有视口内的行是实体化的，取它们就够了。
        foreach (var view in root.GetVisualDescendants().OfType<FileTreeNodeView>())
        {
            var local = root.TranslatePoint(position, view);
            if (local is not { } point) continue;
            if (new Rect(view.Bounds.Size).Contains(point)) return view.DataContext as FileTreeNodeViewModel;
        }
        return null;
    }
}
