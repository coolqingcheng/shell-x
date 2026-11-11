using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>目录树节点基类：本地与远程共用，负责展开状态、动画参数和路径定位。</summary>
public abstract partial class DirectoryTreeNodeViewModel : ObservableObject
{
    private CancellationTokenSource? _hideDelay;

    protected DirectoryTreeNodeViewModel(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<DirectoryTreeNodeViewModel> Children { get; } = [];

    /// <summary>节点是否展开。</summary>
    [ObservableProperty] private bool _isExpanded;
    /// <summary>子目录是否正在加载。</summary>
    [ObservableProperty] private bool _isLoading;
    /// <summary>是否是当前所在目录。</summary>
    [ObservableProperty] private bool _isCurrent;
    /// <summary>子节点区域是否可见：折叠时等淡出动画结束再真正隐藏。</summary>
    [ObservableProperty] private bool _isChildrenVisible;

    /// <summary>展开箭头角度：展开 90 度，折叠 0 度。</summary>
    public double ChevronAngle => IsExpanded ? 90 : 0;
    /// <summary>子节点透明度，配合淡入淡出动画。</summary>
    public double ExpandOpacity => IsExpanded ? 1 : 0;
    /// <summary>子节点纵向位移，展开时从上方滑入。</summary>
    public double ExpandOffset => IsExpanded ? 0 : -6;
    public bool ShowLoadingText => IsLoading;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ChevronAngle));
        OnPropertyChanged(nameof(ExpandOpacity));
        OnPropertyChanged(nameof(ExpandOffset));
        if (value)
        {
            CancelHide();
            IsChildrenVisible = true;
        }
        else ScheduleHide();
    }

    /// <summary>折叠后延迟隐藏子节点，让淡出动画能播完。</summary>
    private void ScheduleHide()
    {
        CancelHide();
        var delay = new CancellationTokenSource();
        _hideDelay = delay;
        _ = HideChildrenAsync(delay.Token);
    }

    private async Task HideChildrenAsync(CancellationToken token)
    {
        try { await Task.Delay(170, token); }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested || IsExpanded) return;
        IsChildrenVisible = false;
    }

    private void CancelHide()
    {
        var delay = _hideDelay;
        _hideDelay = null;
        if (delay == null) return;
        delay.Cancel();
        delay.Dispose();
    }

    /// <summary>加载子目录（懒加载）。</summary>
    protected abstract Task LoadChildrenAsync();

    /// <summary>打开（导航到）该节点对应的目录。</summary>
    protected abstract Task NavigateAsync();

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (IsLoading) return;
        var expand = !IsExpanded;
        IsExpanded = expand;
        if (!expand) return;
        await LoadChildrenAsync();
    }

    [RelayCommand]
    private Task OpenAsync() => NavigateAsync();

    /// <summary>展开到指定路径：沿途展开父节点、加载子目录并高亮目标。</summary>
    public async Task<bool> ExpandToAsync(string path)
    {
        if (PathEquals(Path, path)) { IsCurrent = true; return true; }
        // 子目录还没加载时先加载一次，否则定位不到目标目录。
        if (Children.Count == 0) { IsExpanded = true; await LoadChildrenAsync(); }
        var child = Children.FirstOrDefault(c => IsAncestor(c.Path, path));
        if (child == null) return false;
        IsExpanded = true;
        await LoadChildrenAsync();
        child.IsExpanded = true;
        await child.LoadChildrenAsync();
        return await child.ExpandToAsync(path);
    }

    /// <summary>清除整棵子树的高亮状态。</summary>
    public void ClearCurrent()
    {
        IsCurrent = false;
        foreach (var child in Children) child.ClearCurrent();
    }

    /// <summary>在已经加载出来的节点里标记当前目录，不会额外读取磁盘。</summary>
    public bool MarkCurrent(string path)
    {
        if (PathEquals(Path, path)) { IsCurrent = true; return true; }
        foreach (var child in Children)
            if (child.MarkCurrent(path)) return true;
        return false;
    }

    protected virtual bool PathEquals(string left, string right)
        => string.Equals(left.TrimEnd('/', '\\'), right.TrimEnd('/', '\\'), StringComparison.Ordinal);

    /// <summary>判断 ancestor 是否为 target 的上级目录。</summary>
    protected virtual bool IsAncestor(string ancestor, string target)
    {
        var prefix = ancestor.TrimEnd('/', '\\');
        if (prefix.Length == 0) return true;
        if (!target.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (target.Length == prefix.Length) return true;
        var next = target[prefix.Length];
        return next == '/' || next == '\\';
    }
}
