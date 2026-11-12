using System.Collections.ObjectModel;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>
/// 树控件的扁平行列表：把展开的树压成一维的行列表，配合虚拟滚动只渲染视口内的行。
/// 展开时把子节点插到父节点后面并播放高度动画，收起时先归零再移除。
/// </summary>
public sealed class FileTreeList
{
    /// <summary>树的根节点。</summary>
    public ObservableCollection<FileTreeNodeViewModel> Roots { get; } = [];
    /// <summary>展开后的可见行，虚拟列表直接绑定这个集合。</summary>
    public ObservableCollection<FileTreeNodeViewModel> Flat { get; } = [];

    /// <summary>清空整棵树。</summary>
    public void Clear()
    {
        Roots.Clear();
        Flat.Clear();
    }

    /// <summary>展开节点：把可见子节点插到该节点后面，并播放出现动画。</summary>
    public void InsertDescendants(FileTreeNodeViewModel node)
    {
        RemoveDescendants(node);
        var children = node.Children.Where(c => c.IsVisible).ToList();
        if (children.Count == 0) return;
        var index = Flat.IndexOf(node);
        if (index < 0) return;
        // 行数不多时逐行动画，行数太多时直接显示，避免长时间卡顿；刷新内容时不重播动画。
        var animate = !node.IsRefreshing && children.Count <= 24;
        foreach (var child in children)
        {
            child.IsCollapsing = false;
            child.IsRevealing = animate;
        }
        for (var i = 0; i < children.Count; i++) Flat.Insert(index + 1 + i, children[i]);
        if (animate) _ = ClearRevealFlagsAsync(children);
    }

    /// <summary>收起节点：子节点先缩到 0 高度，动画结束后再从列表里移除。</summary>
    public void CollapseDescendants(FileTreeNodeViewModel node)
    {
        var descendants = DescendantsOf(node).ToList();
        if (descendants.Count == 0) return;
        foreach (var item in descendants) item.IsCollapsing = true;
        _ = RemoveAfterCollapseAsync(node);
    }

    /// <summary>移除节点的所有后代行。</summary>
    public void RemoveDescendants(FileTreeNodeViewModel node)
    {
        var set = DescendantsOf(node).ToHashSet();
        if (set.Count == 0) return;
        for (var i = Flat.Count - 1; i >= 0; i--)
            if (set.Contains(Flat[i])) Flat.RemoveAt(i);
    }

    /// <summary>按当前展开状态和搜索过滤重新生成整张行列表。</summary>
    public void Rebuild()
    {
        Flat.Clear();
        foreach (var root in Roots)
        {
            if (!root.IsVisible) continue;
            Flat.Add(root);
            if (root.IsExpanded) AddVisibleChildren(root);
        }
    }

    /// <summary>收集某个节点的所有后代。</summary>
    public static IEnumerable<FileTreeNodeViewModel> DescendantsOf(FileTreeNodeViewModel node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var item in DescendantsOf(child)) yield return item;
        }
    }

    private void AddVisibleChildren(FileTreeNodeViewModel node)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsVisible) continue;
            Flat.Add(child);
            if (child.IsExpanded) AddVisibleChildren(child);
        }
    }

    private static async Task ClearRevealFlagsAsync(IReadOnlyList<FileTreeNodeViewModel> nodes)
    {
        await Task.Delay(320);
        foreach (var node in nodes) node.IsRevealing = false;
    }

    private async Task RemoveAfterCollapseAsync(FileTreeNodeViewModel node)
    {
        await Task.Delay(260);
        // 动画期间如果又展开了，就保留这些行。
        if (!node.IsExpanded)
        {
            RemoveDescendants(node);
            foreach (var item in DescendantsOf(node)) item.IsCollapsing = false;
        }
        else
        {
            foreach (var item in DescendantsOf(node)) item.IsCollapsing = false;
        }
    }
}
