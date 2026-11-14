using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>
/// 文件树节点基类：本地与远程共用，负责展开状态、层级、路径定位、搜索过滤和节点操作。
/// 面板用扁平行列表承载显示，节点自身只保存树形数据。
/// </summary>
public abstract partial class FileTreeNodeViewModel : ObservableObject
{
    /// <summary>树里每行的固定高度，展开收起动画按这个高度推进。</summary>
    public const double RowHeight = 26;

    protected FileTreeNodeViewModel(IFileTreeSink sink, string name, string path, bool isDirectory,
        string icon, string sizeText, string modifiedText)
    {
        Sink = sink;
        Name = name; Path = path; IsDirectory = isDirectory;
        Icon = icon; SizeText = sizeText; ModifiedText = modifiedText;
    }

    /// <summary>宿主：面板提供的实际操作。</summary>
    public IFileTreeSink Sink { get; }
    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    /// <summary>是否是符号链接。</summary>
    public virtual bool IsLink => false;
    /// <summary>删除中：删除完成前这一行显示加载指示。</summary>
    public virtual bool IsDeleting => false;
    /// <summary>图标类别：目录是 folder，文件按名称判断。</summary>
    public string Icon { get; }
    /// <summary>紧凑行：窄面板下不显示时间列。</summary>
    public bool IsCompact => Sink.CompactRows;
    /// <summary>是否显示展开箭头。</summary>
    public bool ShowChevron => Sink.ShowRowChevron && IsDirectory;
    /// <summary>行的左边距：树形列表按层级缩进，按层级浏览的面板不缩进。</summary>
    public Thickness RowMargin => Sink.ShowRowIndent ? Indent : new Thickness(0, 0, 0, 0);
    public string SizeText { get; }
    public string ModifiedText { get; }
    /// <summary>子节点：目录展开后加载，文件为空。</summary>
    public ObservableCollection<FileTreeNodeViewModel> Children { get; } = [];

    /// <summary>父节点。</summary>
    public FileTreeNodeViewModel? Parent { get; internal set; }

    /// <summary>子节点是否已经加载过。</summary>
    protected bool Loaded { get; set; }

    [ObservableProperty] private bool _isExpanded;
    /// <summary>子节点是否正在加载，远程目录读取慢时显示加载中。</summary>
    [ObservableProperty] private bool _isLoading;
    /// <summary>是否是当前所在目录。</summary>
    [ObservableProperty] private bool _isCurrent;
    /// <summary>是否是当前选中的节点。</summary>
    [ObservableProperty] private bool _isSelected;
    /// <summary>是否是当前的拖放目标。</summary>
    [ObservableProperty] private bool _isDropTarget;
    /// <summary>搜索过滤后是否显示。</summary>
    [ObservableProperty] private bool _isVisible = true;
    /// <summary>在树中的层级，0 是根。</summary>
    [ObservableProperty] private int _level;
    /// <summary>新展开出来的行，显示时播放高度动画。</summary>
    [ObservableProperty] private bool _isRevealing;
    /// <summary>正在收起：高度归零后由面板移除。</summary>
    [ObservableProperty] private bool _isCollapsing;
    /// <summary>本次是刷新内容而不是展开，重新插入的行不播出现动画。</summary>
    public bool IsRefreshing { get; private set; }
    /// <summary>本次展开时的行序号，用于出现动画错峰，避免所有行同时动。</summary>
    public int RevealOrder { get; internal set; }

    /// <summary>展开箭头角度：展开 90 度，折叠 0 度。</summary>
    public double ChevronAngle => IsExpanded ? 90 : 0;
    /// <summary>层级缩进。</summary>
    public Thickness Indent => new(Level * 15, 0, 0, 0);
    /// <summary>是否支持下载。</summary>
    public bool CanDownload => Sink.CanDownload;
    /// <summary>是否支持上传。</summary>
    public bool CanUpload => Sink.CanUpload;
    /// <summary>行内是否显示下载按钮：只有远程面板会显示，文件管理器那边用工具栏下载。</summary>
    public bool CanDownloadItem => Sink.ShowRowDownload && !IsDirectory;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ChevronAngle));
        Sink.NotifyExpanded(this, value);
    }

    partial void OnLevelChanged(int value) => OnPropertyChanged(nameof(Indent));

    /// <summary>加载子节点（懒加载）。</summary>
    protected abstract Task LoadChildrenAsync();
    /// <summary>打开（双击）该节点。</summary>
    protected abstract Task ActivateAsync();

    /// <summary>展开或收起：先读完子节点，读完再展开，这样动画高度才准确。</summary>
    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (IsLoading) return;
        if (IsExpanded) { IsExpanded = false; return; }
        if (!Loaded && IsDirectory) await LoadChildrenAsync();
        if (IsExpanded) return;
        IsExpanded = true;
    }

    [RelayCommand]
    private Task OpenAsync() => ActivateAsync();

    /// <summary>行内下载：交给宿主决定保存到哪里。</summary>
    [RelayCommand]
    private Task DownloadItemAsync() => Sink.DownloadItemAsync(this);

    /// <summary>重新读取子节点：上传或下载完成后刷新。</summary>
    public async Task ReloadAsync()
    {
        Loaded = false;
        Children.Clear();
        IsRefreshing = true;
        try { await LoadChildrenAsync(); }
        finally { IsRefreshing = false; }
        Sink.NotifyExpanded(this, IsExpanded);
    }

    /// <summary>刷新已经加载出来的节点内容（传输完成后调用）。</summary>
    public async Task ReloadLoadedAsync()
    {
        if (Loaded && IsDirectory) await ReloadAsync();
        foreach (var child in Children) await child.ReloadLoadedAsync();
    }

    /// <summary>按搜索关键字更新可见性：目录始终保留，文件按名称过滤。</summary>
    public void UpdateVisibility()
    {
        var keyword = Sink.SearchText;
        IsVisible = IsDirectory || keyword.Length == 0 || Name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        foreach (var child in Children) child.UpdateVisibility();
    }

    /// <summary>展开到指定路径：沿途展开父节点、加载子目录并高亮目标。</summary>
    public async Task<bool> ExpandToAsync(string path)
    {
        if (PathEquals(Path, path)) { IsCurrent = true; return true; }
        if (Children.Count == 0) await LoadChildrenAsync();
        var child = Children.FirstOrDefault(c => c.IsDirectory && IsAncestor(c.Path, path));
        if (child == null) return false;
        IsExpanded = true;
        return await child.ExpandToAsync(path);
    }

    /// <summary>在已经加载出来的节点里查找指定路径。</summary>
    public FileTreeNodeViewModel? Find(string path)
    {
        if (PathEquals(Path, path)) return this;
        foreach (var child in Children)
        {
            var found = child.Find(path);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>判断该节点是否是指定路径的上级目录。</summary>
    public bool IsAncestorOf(string path) => IsAncestor(Path, path);

    /// <summary>清除整棵子树的高亮状态。</summary>
    public void ClearCurrent()
    {
        IsCurrent = false;
        foreach (var child in Children) child.ClearCurrent();
    }

    /// <summary>清除整棵子树的选中状态。</summary>
    public void ClearSelected()
    {
        IsSelected = false;
        foreach (var child in Children) child.ClearSelected();
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

    /// <summary>当前节点能否接收这次拖放。</summary>
    public bool CanAcceptDrop(IDataTransfer data)
    {
        if (!IsDirectory) return false;
        // 连接换了或会话失效后，之前的拖动一律不再接收。
        if (!Sink.CanAcceptDrop(data)) return false;
        if (Sink.AcceptsRemoteDrop && FileDragDrop.ReadRemoteSelection(data) is { IsValid: true }) return true;
        if (Sink.AcceptsLocalDrop && FileDragDrop.ReadLocalPaths(data) is { Length: > 0 }) return true;
        if (Sink.AcceptsLocalDrop && FileDragDrop.ReadFilePaths(data) is { Length: > 0 }) return true;
        return false;
    }

    /// <summary>处理拖放：本地路径上传到远程目录，远程条目下载到本地目录。</summary>
    public async Task HandleDropAsync(IDataTransfer data)
    {
        if (!IsDirectory) return;
        if (Sink.AcceptsRemoteDrop && FileDragDrop.ReadRemoteSelection(data) is { } remote)
        {
            await Sink.DropRemoteAsync(this, remote);
            return;
        }
        if (Sink.AcceptsLocalDrop)
        {
            if (FileDragDrop.ReadLocalPaths(data) is { Length: > 0 } paths) { await Sink.DropLocalAsync(this, paths); return; }
            if (FileDragDrop.ReadFilePaths(data) is { Length: > 0 } files) { await Sink.DropLocalAsync(this, files); }
        }
    }
}