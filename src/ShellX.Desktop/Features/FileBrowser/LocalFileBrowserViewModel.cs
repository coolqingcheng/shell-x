using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Files;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class LocalFileBrowserViewModel(ILocalFileSystem files, IDialogService dialogs,
    Func<IReadOnlyList<string>, Task> upload) : ObservableObject, IDisposable, IFileTreeSink
{
    /// <summary>远程面板：本地树拖拽上传、远程树拖拽下载都要用到。</summary>
    public FileBrowserViewModel? Browser { get; set; }
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<LocalEntry> _all = [];
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private int _navigationGeneration;
    public ObservableCollection<LocalEntry> Entries { get; } = [];
    /// <summary>树控件的扁平行列表，展开动画和虚拟滚动都靠它。</summary>
    private readonly FileTreeList tree = new();
    /// <summary>本地目录树的根节点（磁盘）。</summary>
    public ObservableCollection<FileTreeNodeViewModel> TreeRoots => tree.Roots;
    /// <summary>展开后的可见行，供虚拟化列表显示。</summary>
    public ObservableCollection<FileTreeNodeViewModel> FlatNodes => tree.Flat;
    public ObservableCollection<string> RecentPaths { get; } = [];
    public IReadOnlyList<string> Roots { get; } = files.Roots;
    public List<LocalEntry> SelectedEntries { get; } = [];
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(EditTextCommand))] private LocalEntry? _selectedEntry;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(EditTextCommand))] private bool _isEditingText;
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _summary = "本地文件";
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _isBusy;
    /// <summary>删除进行中：期间禁止重复触发删除。</summary>
    [ObservableProperty] private bool _isDeleting;
    [ObservableProperty] private bool _isRemoteAvailable;
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// 初始化本地目录树：每个磁盘一个根节点。
    /// </summary>
    private void InitializeTree()
    {
        tree.Clear();
        foreach (var root in files.Roots)
        {
            var entry = new LocalEntry(root.TrimEnd('\\'), root, true, false, false, 0, DateTime.UtcNow);
            var node = new LocalTreeNodeViewModel(entry, files, _lifetime.Token, this);
            node.Level = 0;
            TreeRoots.Add(node);
            FlatNodes.Add(node);
        }
    }

    /// <summary>在已经展开的目录树里高亮当前目录，不会额外读取磁盘。</summary>
    private void MarkCurrent(string path)
    {
        if (TreeRoots.Count == 0 || string.IsNullOrEmpty(path)) return;
        foreach (var root in TreeRoots) root.ClearCurrent();
        foreach (var root in TreeRoots)
        {
            if (root.MarkCurrent(path)) return;
        }
    }
    public bool CanEditText => !IsEditingText && SelectedEntries.Count <= 1 && SelectedEntry is { IsDirectory: false, IsSymbolicLink: false };
    public void NotifySelectionChanged() => EditTextCommand.NotifyCanExecuteChanged();
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnSearchChanged(string value) { Filter(); UpdateTreeVisibility(); }
    partial void OnShowHiddenChanged(bool value) { Filter(); _ = RefreshTreeAsync(); }

    public Task InitializeAsync()
    {
        if (TreeRoots.Count == 0) InitializeTree();
        return Path.Length > 0 || IsBusy ? Task.CompletedTask : NavigateAsync(files.HomeDirectory);
    }
    public async Task NavigateAsync(string path, bool addHistory = true)
    {
        var generation = ++_navigationGeneration; IsBusy = true; Error = "";
        try
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请输入本地目录路径。");
            if (!System.IO.Path.IsPathFullyQualified(path)) path = System.IO.Path.GetFullPath(path, Path.Length > 0 ? Path : files.HomeDirectory);
            var result = await files.ListAsync(path, _lifetime.Token);
            if (generation != _navigationGeneration || _lifetime.IsCancellationRequested) return;
            Path = result.Path; _all = result.Entries; Filter();
            MarkCurrent(result.Path);
            RecentPaths.Remove(Path); RecentPaths.Insert(0, Path);
            while (RecentPaths.Count > 30) RecentPaths.RemoveAt(RecentPaths.Count - 1);
            if (addHistory && (_historyIndex < 0 || _history[_historyIndex] != Path))
            {
                if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(Path); _historyIndex = _history.Count - 1;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _navigationGeneration) { Error = "本地目录读取失败：" + ex.Message; } }
        finally { if (generation == _navigationGeneration) IsBusy = false; }
    }

    private void Filter()
    {
        SelectedEntries.Clear(); SelectedEntry = null; Entries.Clear();
        foreach (var entry in _all.Where(e => (ShowHidden || !e.IsHidden) && e.Name.Contains(Search, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)) Entries.Add(entry);
        Summary = $"{Entries.Count} 个项目";
    }
    [RelayCommand] private Task RefreshAsync() => Path.Length == 0 ? InitializeAsync() : NavigateAsync(Path, false);
    [RelayCommand] private Task HomeAsync() => NavigateAsync(files.HomeDirectory);
    [RelayCommand] private Task UpAsync() => Path.Length == 0 ? InitializeAsync() : NavigateAsync(Directory.GetParent(Path)?.FullName ?? Path);
    [RelayCommand] private async Task BackAsync() { if (_historyIndex > 0) await NavigateAsync(_history[--_historyIndex], false); }
    [RelayCommand] private async Task ForwardAsync() { if (_historyIndex + 1 < _history.Count) await NavigateAsync(_history[++_historyIndex], false); }
    [RelayCommand] private Task OpenAsync() => OpenEntryAsync(SelectedEntry);
    public Task OpenEntryAsync(LocalEntry? entry) => entry == null ? Task.CompletedTask : entry.IsDirectory ? NavigateAsync(entry.FullPath) : RunAsync(() => files.OpenAsync(entry.FullPath));
    [RelayCommand(CanExecute = nameof(CanEditText))]
    private async Task EditTextAsync()
    {
        if (!CanEditText || SelectedEntry is not { } entry) return;
        IsEditingText = true;
        try
        {
            await RunAsync(async () =>
            {
                TextEditorLimits.ValidateSize(entry.Size);
                var document = await files.ReadTextAsync(entry.FullPath, _lifetime.Token);
                _lifetime.Token.ThrowIfCancellationRequested();
                await dialogs.EditFileAsync(document, async (original, content) =>
                {
                    var updated = await files.SaveTextAsync(original, content, _lifetime.Token);
                    await RefreshAsync(); return updated;
                });
            });
        }
        finally { IsEditingText = false; }
    }
    [RelayCommand] private Task ShowInExplorerAsync() => Path.Length == 0 ? Task.CompletedTask : RunAsync(() => files.OpenAsync(Path));
    [RelayCommand] private Task NewFileAsync() => CreateAsync(false);
    [RelayCommand] private Task NewFolderAsync() => CreateAsync(true);
    private async Task CreateAsync(bool folder)
    {
        if (Path.Length == 0) return;
        // 选中了目录节点就建在那里，否则建在当前路径。
        var directory = SelectedEntry is { IsDirectory: true } selected ? selected.FullPath : Path;
        var name = await dialogs.PromptAsync(folder ? "新建本地文件夹" : "新建本地文件", "名称", folder ? "新建文件夹" : "untitled.txt");
        if (name != null) await RunAsync(async () => { await files.CreateAsync(directory, name, folder); await RefreshAsync(); await RefreshTreeAsync(); });
    }
    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedEntry is not { } entry) return;
        var name = await dialogs.PromptAsync("重命名本地项目", "新名称", entry.Name);
        if (name != null && name != entry.Name) await RunAsync(async () => { await files.RenameAsync(entry.FullPath, name); await RefreshAsync(); });
    }
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var entries = SelectedEntries.Count > 0 ? SelectedEntries.ToArray() : SelectedEntry == null ? [] : new[] { SelectedEntry };
        if (entries.Length == 0 || IsDeleting) return;
        if (!await dialogs.ConfirmAsync("删除本地项目", $"永久删除以下 {entries.Length} 个项目？目录会递归删除，符号链接只删除链接本身。\n\n" + string.Join("\n", entries.Take(8).Select(e => e.FullPath)) + "\n\n此操作不会进入回收站。", true)) return;
        IsDeleting = true;
        try
        {
            // 删除过程中条目保持可见并显示加载指示，只有这一项删完才从列表里消失。
            await RunAsync(async () =>
            {
                foreach (var entry in entries)
                {
                    entry.IsDeleting = true;
                    try { await files.DeleteAsync(entry.FullPath, _lifetime.Token); }
                    finally { entry.IsDeleting = false; }
                    RemoveEntry(entry.FullPath);
                }
                await RefreshAsync();
            });
        }
        finally { IsDeleting = false; }
    }

    /// <summary>删除完成后把条目从列表里移除。</summary>
    private void RemoveEntry(string fullPath)
    {
        var item = Entries.FirstOrDefault(e => e.FullPath == fullPath);
        if (item != null) Entries.Remove(item);
        _all = _all.Where(e => e.FullPath != fullPath).ToArray();
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (!IsRemoteAvailable) return;
        var selected = SelectedEntries.Count > 0 ? SelectedEntries.ToArray() : SelectedEntry == null ? [] : new[] { SelectedEntry };
        if (selected.Length == 0) { await dialogs.ShowErrorAsync("请先在左侧选择要上传的本地文件或文件夹。"); return; }
        await upload(selected.Select(e => e.FullPath).ToArray());
    }
    private async Task RunAsync(Func<Task> action)
    {
        Error = "";
        try { await action(); }
        catch (OperationCanceledException) { Error = "操作已取消"; }
        catch (Exception ex) { Error = ex.Message; await dialogs.ShowErrorAsync(ex.Message); }
    }

    // ---------------- 文件树宿主 ----------------
    /// <summary>当前选中的树节点集合，支持 Ctrl 多选。</summary>
    private readonly List<FileTreeNodeViewModel> _selectedTreeNodes = [];
    /// <summary>最近点选的树节点，作为面板的主要选择。</summary>
    private FileTreeNodeViewModel? _primaryTreeNode;
    /// <summary>当前选中的树节点路径，右键“复制路径”用它。</summary>
    public string? SelectedPath => _primaryTreeNode?.Path;

    public bool ShowHiddenFiles => ShowHidden;
    public string SearchText => Search;
    /// <summary>本地树不接受本地文件拖入。</summary>
    public bool AcceptsLocalDrop => false;
    /// <summary>本地树接受远程条目拖入，表示下载到该目录。</summary>
    public bool AcceptsRemoteDrop => IsRemoteAvailable;
    public bool CanDownload => false;
    public bool CanUpload => IsRemoteAvailable;

    /// <summary>选中树节点：同步面板的当前条目，右键菜单据此工作。</summary>
    public void Select(FileTreeNodeViewModel node, bool toggle = false)
    {
        if (toggle)
        {
            if (_selectedTreeNodes.Remove(node)) node.IsSelected = false;
            else { _selectedTreeNodes.Add(node); node.IsSelected = true; }
        }
        else
        {
            foreach (var item in _selectedTreeNodes) item.IsSelected = false;
            _selectedTreeNodes.Clear();
            _selectedTreeNodes.Add(node);
            node.IsSelected = true;
        }
        _primaryTreeNode = node;
        SyncTreeSelection();
        OnPropertyChanged(nameof(SelectedPath));
    }

    /// <summary>Ctrl+A：选中当前可见的所有行。</summary>
    public void SelectAll(IReadOnlyList<FileTreeNodeViewModel> nodes)
    {
        foreach (var item in _selectedTreeNodes) item.IsSelected = false;
        _selectedTreeNodes.Clear();
        foreach (var item in nodes) { _selectedTreeNodes.Add(item); item.IsSelected = true; }
        _primaryTreeNode = nodes.Count > 0 ? nodes[^1] : null;
        SyncTreeSelection();
        OnPropertyChanged(nameof(SelectedPath));
    }

    /// <summary>把树上的选择同步到面板的当前条目。</summary>
    private void SyncTreeSelection()
    {
        SelectedEntries.Clear();
        SelectedEntry = null;
        foreach (var item in _selectedTreeNodes)
            if (item is LocalTreeNodeViewModel local) SelectedEntries.Add(local.Entry);
        if (_primaryTreeNode is LocalTreeNodeViewModel primary) SelectedEntry = primary.Entry;
        else if (SelectedEntries.Count > 0) SelectedEntry = SelectedEntries[^1];
        NotifySelectionChanged();
    }

    /// <summary>展开或收起节点：把子节点插进或移出扁平行列表。</summary>
    public void NotifyExpanded(FileTreeNodeViewModel node, bool expanded)
    {
        if (expanded) tree.InsertDescendants(node);
        else tree.CollapseDescendants(node);
    }

    /// <summary>双击节点：目录进入该目录，文件直接打开。</summary>
    public async Task ActivateAsync(FileTreeNodeViewModel node)
    {
        if (node is not LocalTreeNodeViewModel local) return;
        if (local.Entry.IsDirectory)
        {
            if (!local.IsExpanded) await local.ToggleCommand.ExecuteAsync(null);
            await NavigateAsync(local.Entry.FullPath);
        }
        else await OpenEntryAsync(local.Entry);
    }

    /// <summary>本地树节点可以拖到远程树上传。</summary>
    public FileDragDrop.Payload? CreateDragPayload(FileTreeNodeViewModel node)
    {
        if (node is not LocalTreeNodeViewModel local || Browser == null) return null;
        return FileDragDrop.LocalPayload(Browser, [local.Entry.FullPath]);
    }

    /// <summary>本地树不接受本地文件拖入。</summary>
    public Task DropLocalAsync(FileTreeNodeViewModel node, IReadOnlyList<string> paths) => Task.CompletedTask;

    /// <summary>远程条目拖到本地目录节点：下载到该目录。</summary>
    public async Task DropRemoteAsync(FileTreeNodeViewModel node, RemoteFileSelection selection)
    {
        if (node is not LocalTreeNodeViewModel local || Browser == null) return;
        await Browser.DownloadEntriesAsync(selection, local.Entry.FullPath);
    }

    /// <summary>读取目录失败时上报。</summary>
    public void ReportError(string message) => Error = message;

    /// <summary>本地树只接收远程条目：远程树节点拖过来就是下载到该目录。</summary>
    public bool CanAcceptDrop(IDataTransfer data) => Browser != null && FileDragDrop.CanDrop(Browser, false, data);

    /// <summary>搜索过滤后重建可见行。</summary>
    private void UpdateTreeVisibility()
    {
        foreach (var root in TreeRoots) root.UpdateVisibility();
        tree.Rebuild();
    }

    /// <summary>重新读取已经展开的目录。</summary>
    public async Task RefreshTreeAsync()
    {
        foreach (var root in TreeRoots) await root.ReloadLoadedAsync();
        UpdateTreeVisibility();
    }

    public void Dispose() { ++_navigationGeneration; _lifetime.Cancel(); _lifetime.Dispose(); }
}
