using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShellX.Core.Files;
using ShellX.Core.Logs;
using ShellX.Core.Sessions;
using ShellX.Infrastructure.Files;
using ShellX.Desktop.Features.Transfers;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class FileBrowserViewModel : ObservableObject, IDisposable, IFileTreeSink
{
    private readonly IDialogService dialogs;
    private readonly ILogger<FileBrowserViewModel> log;
    private readonly ILocalFileSystem _localFiles;
    public LocalFileBrowserViewModel Local { get; }
    /// <summary>对话框服务：文件管理器里的弹窗需要登记窗口层级。</summary>
    public IDialogService Dialogs => dialogs;
    public FileBrowserViewModel(IDialogService dialogs, ILogger<FileBrowserViewModel> log, ILocalFileSystem localFiles)
    {
        this.dialogs = dialogs; this.log = log; _localFiles = localFiles;
        Local = new(localFiles, dialogs, paths => UploadPathsAsync(paths));
        Local.Browser = this;
    }
    private IRemoteFileSystem? _files;
    private CancellationToken _sessionToken;
    internal int SessionVersion { get; private set; }
    private IReadOnlyList<RemoteEntry> _all = [];
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private int _navigationGeneration;
    private readonly List<Task> _transfers = [];
    public ObservableCollection<RemoteEntry> Entries { get; } = [];
    /// <summary>树控件的扁平行列表，展开动画和虚拟滚动都靠它。</summary>
    private readonly FileTreeList tree = new();
    /// <summary>远程文件树的根节点。</summary>
    public ObservableCollection<FileTreeNodeViewModel> TreeRoots => tree.Roots;
    /// <summary>展开后的可见行，供虚拟化列表显示。</summary>
    public ObservableCollection<FileTreeNodeViewModel> FlatNodes => tree.Flat;
    public ObservableCollection<string> RecentPaths { get; } = [];
    public ObservableCollection<TransferViewModel> Transfers { get; } = [];
    public List<RemoteEntry> SelectedEntries { get; } = [];
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(EditTextCommand)), NotifyCanExecuteChangedFor(nameof(ViewLogCommand))] private RemoteEntry? _selectedEntry;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(EditTextCommand))] private bool _isEditingText;
    [ObservableProperty] private string _path = ".";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _summary = "连接后浏览远程文件";
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _isBusy;
    /// <summary>删除进行中：期间禁止重复触发删除。</summary>
    [ObservableProperty] private bool _isDeleting;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _showTransfers;
    [ObservableProperty] private double _transferPanelHeight = 180;
    public bool HasActiveTransfers => Transfers.Any(t => t.IsActive);
    public bool ShowTransferTray => !ShowTransfers;
    public string TransferSummary
    {
        get
        {
            if (Transfers.Count == 0) return "暂无传输任务";
            var active = Transfers.Count(t => t.IsActive);
            if (active == 0) return $"{Transfers.Count} 个任务 · 已结束";
            var percent = Transfers.Where(t => t.IsActive).Average(t => t.Percent);
            return $"{Transfers.Count} 个任务 · {active} 个进行中 · {percent:0}%";
        }
    }
    public bool HasError => !string.IsNullOrEmpty(Error);
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsAvailableChanged(bool value) { Local.IsRemoteAvailable = value; EditTextCommand.NotifyCanExecuteChanged(); ViewLogCommand.NotifyCanExecuteChanged(); }
    public bool CanEditText => IsAvailable && !IsEditingText && SelectedEntries.Count <= 1 && SelectedEntry is { IsDirectory: false, IsSymbolicLink: false };

    /// <summary>当前远程会话，日志窗口断开连接时需要它。</summary>
    public IRemoteSession? Session { get; private set; }

    /// <summary>远程日志读取器：只有服务端会话提供时才可用。</summary>
    public IRemoteLogReader? LogReader { get; private set; }

    /// <summary>日志窗口标题里显示的连接信息。</summary>
    public string LogTarget { get; private set; } = "";

    /// <summary>用户请求查看某个远程日志文件，由界面负责开窗口。</summary>
    public event Action<string>? LogRequested;

    public bool CanViewLog => LogReader != null && SelectedEntry is { IsDirectory: false, IsSymbolicLink: false };
    public void NotifySelectionChanged() { EditTextCommand.NotifyCanExecuteChanged(); ViewLogCommand.NotifyCanExecuteChanged(); }

    /// <summary>在独立的日志窗口里查看选中的远程文件。</summary>
    [RelayCommand(CanExecute = nameof(CanViewLog))]
    private void ViewLog() { if (SelectedEntry is { } entry) LogRequested?.Invoke(entry.FullPath); }
    public void Attach(IRemoteFileSystem? files, CancellationToken token, IRemoteSession? session = null, string target = "")
    {
        ++_navigationGeneration; ++SessionVersion; _files = files; _sessionToken = token; IsAvailable = files != null;
        Session = session; LogReader = session?.Logs; LogTarget = target; ViewLogCommand.NotifyCanExecuteChanged();
        _all = []; Entries.Clear(); _history.Clear(); _historyIndex = -1; SelectedEntries.Clear(); SelectedEntry = null;
        Error = ""; IsBusy = false; Summary = files == null ? "尚未连接 SFTP" : "SFTP 已就绪";
        tree.Clear(); RecentPaths.Clear();
        if (files != null)
        {
            var capturedToken = token;
            var root = new RemoteEntry("/", "/", true, false, 0, DateTime.UtcNow, 493, 0, 0);
            var node = new RemoteTreeNodeViewModel(root, files, capturedToken, this);
            node.Level = 0;
            TreeRoots.Add(node);
            FlatNodes.Add(node);
        }
    }
    partial void OnSearchChanged(string value) { Filter(); UpdateTreeVisibility(); }
    partial void OnShowHiddenChanged(bool value) { Filter(); _ = RefreshTreeAsync(); }
    private void Filter()
    {
        Entries.Clear();
        foreach (var entry in _all.Where(x => (ShowHidden || !x.Name.StartsWith('.')) && x.Name.Contains(Search, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) Entries.Add(entry);
        Summary = $"{Entries.Count} 个项目";
    }
    public async Task NavigateAsync(string path, bool addHistory = true)
    {
        var files = _files; if (files == null) return;
        if (Path.StartsWith('/') && !path.StartsWith('/') && path != "~") path = Path.TrimEnd('/') + "/" + path;
        var generation = ++_navigationGeneration; IsBusy = true; Error = "";
        try
        {
            var result = await files.ListAsync(path, _sessionToken);
            if (generation != _navigationGeneration || !ReferenceEquals(files, _files)) return;
            Path = result.Path; _all = result.Entries; Filter(); ClearTreeSelection();
            await SyncTreeAsync(result.Path);
            RecentPaths.Remove(Path); RecentPaths.Insert(0, Path);
            while (RecentPaths.Count > 30) RecentPaths.RemoveAt(RecentPaths.Count - 1);
            if (addHistory && (_historyIndex < 0 || _history[_historyIndex] != Path))
            { if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1); _history.Add(Path); _historyIndex = _history.Count - 1; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _navigationGeneration) { Error = "目录读取失败：" + ex.Message; } }
        finally { if (generation == _navigationGeneration) IsBusy = false; }
    }
    [RelayCommand] private Task RefreshAsync() => NavigateAsync(Path, false);
    [RelayCommand] private Task UpAsync() => NavigateAsync(RemotePath.Parent(Path));
    [RelayCommand] private Task HomeAsync() => NavigateAsync("~");
    [RelayCommand] private async Task BackAsync() { if (_historyIndex > 0) await NavigateAsync(_history[--_historyIndex], false); }
    [RelayCommand] private async Task ForwardAsync() { if (_historyIndex + 1 < _history.Count) await NavigateAsync(_history[++_historyIndex], false); }
    [RelayCommand] private void ToggleHidden() => ShowHidden = !ShowHidden;
    partial void OnShowTransfersChanged(bool value) { OnPropertyChanged(nameof(ShowTransferTray)); OnPropertyChanged(nameof(TransferSummary)); }
    public void SetTransferPanelHeight(double value) => TransferPanelHeight = Math.Clamp(value, 96, 420);
    private void TrackTransfer(TransferViewModel transfer)
    {
        transfer.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TransferSummary));
        Transfers.Add(transfer);
        OnPropertyChanged(nameof(TransferSummary));
        OnPropertyChanged(nameof(HasActiveTransfers));
    }
    [RelayCommand] private void ToggleTransfers() => ShowTransfers = !ShowTransfers;
    /// <summary>目录树跟随当前路径：展开祖先目录并高亮当前目录。</summary>
    private async Task SyncTreeAsync(string path)
    {
        var root = TreeRoots.FirstOrDefault();
        if (root == null) return;
        root.ClearCurrent();
        await root.ExpandToAsync(path);
    }
    [RelayCommand] private Task OpenAsync() => OpenEntryAsync(SelectedEntry);
    public async Task OpenEntryAsync(RemoteEntry? entry)
    {
        if (entry == null || _files == null) return;
        if (entry.IsDirectory) { await NavigateAsync(entry.FullPath); return; }
        await EditEntryAsync(entry);
    }
    [RelayCommand(CanExecute = nameof(CanEditText))] private Task EditTextAsync() => EditEntryAsync(SelectedEntry);
    private async Task EditEntryAsync(RemoteEntry? entry)
    {
        if (entry == null || _files == null || entry.IsDirectory || IsEditingText) return;
        if (entry.IsSymbolicLink) { await dialogs.ShowErrorAsync("请打开实际文本文件后编辑，不能通过链接修改目标。"); return; }
        var files = _files; var token = _sessionToken;
        IsEditingText = true;
        try { await RunAsync(async () =>
        {
            TextEditorLimits.ValidateSize(entry.Size);
            var document = await files.ReadTextAsync(entry.FullPath, token);
            token.ThrowIfCancellationRequested();
            await dialogs.EditFileAsync(document, async (original, content) =>
            {
                var updated = await files.SaveTextAsync(original, content, token);
                if (ReferenceEquals(files, _files)) await RefreshAsync();
                return updated;
            });
        }); }
        finally { IsEditingText = false; }
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_files == null) return;
        Error = "";
        try { await action(); }
        catch (OperationCanceledException) { Error = "操作已取消"; }
        catch (Exception ex) { log.LogWarning(ex, "远程文件操作失败"); Error = ex.Message; await dialogs.ShowErrorAsync(ex.Message); }
    }
    private async Task CreateAsync(bool directory)
    {
        if (_files == null) return;
        var name = await dialogs.PromptAsync(directory ? "新建文件夹" : "新建文件", "名称", directory ? "新建文件夹" : "untitled.txt");
        if (name == null) return;
        // 选中了目录节点就建在那里，否则建在当前路径。
        var target = SelectedEntry is { IsDirectory: true } selected ? selected.FullPath : Path;
        await RunAsync(async () => { await _files.CreateAsync(RemotePath.Combine(target, name), directory, _sessionToken); await RefreshAsync(); await RefreshTreeAsync(); });
    }
    [RelayCommand] private Task NewFileAsync() => CreateAsync(false);
    [RelayCommand] private Task NewFolderAsync() => CreateAsync(true);
    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedEntry is not { } entry) return;
        var name = await dialogs.PromptAsync("重命名", "新名称", entry.Name); if (name == null || name == entry.Name) return;
        await RunAsync(async () => { await _files!.RenameAsync(entry.FullPath, RemotePath.Combine(RemotePath.Parent(entry.FullPath), name), _sessionToken); await RefreshAsync(); });
    }
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var entries = SelectedEntries.Count > 0 ? SelectedEntries.ToArray() : SelectedEntry == null ? [] : new[] { SelectedEntry };
        if (entries.Length == 0 || _files == null || IsDeleting) return;
        if (!await dialogs.ConfirmAsync("删除远程项目", $"永久删除以下 {entries.Length} 个项目？目录会递归删除，符号链接只删除链接本身。\n\n" + string.Join("\n", entries.Take(8).Select(e => e.FullPath)) + "\n\n此操作不会进入回收站。", true)) return;
        IsDeleting = true;
        try
        {
            // 删除过程中条目保持可见并显示加载指示，只有这一项删完才从列表里消失。
            await RunAsync(async () =>
            {
                foreach (var entry in entries)
                {
                    entry.IsDeleting = true;
                    try { await _files.DeleteAsync(entry.FullPath, _sessionToken); }
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
    private async Task PermissionsAsync()
    {
        if (SelectedEntry is not { } entry || _files == null) return;
        if (entry.IsSymbolicLink) { await dialogs.ShowErrorAsync("为避免修改符号链接指向的目标，请进入目标实际目录后修改权限。"); return; }
        if (await dialogs.PermissionsAsync(entry) is not { } permissions) return;
        await RunAsync(async () => { await _files.SetPermissionsAsync(entry.FullPath, permissions, _sessionToken); await RefreshAsync(); });
    }
    [RelayCommand]
    private Task UploadAsync() => Local.UploadCommand.ExecuteAsync(null);
    public Task ReportTransferErrorAsync(string message) => dialogs.ShowErrorAsync(message);
    [RelayCommand] private async Task ChooseUploadAsync() => await UploadPathsAsync(await dialogs.PickUploadAsync());
    public async Task UploadPathsAsync(IReadOnlyList<string> paths, string? targetDirectory = null)
    {
        var files = _files; targetDirectory ??= Path; var sessionToken = _sessionToken;
        if (files == null) return;
        // 勾选“保持选择”后，本次批量上传里的后续文件直接覆盖，不再逐个询问。
        var overwriteConfirmed = false;
        foreach (var local in paths)
        {
            try
            {
                sessionToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(local);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { await dialogs.ShowErrorAsync("暂不支持上传符号链接，请选择实际文件或目录。"); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var folder = new TransferViewModel(new DirectoryInfo(local).Name, "↑ 上传文件夹", 0) { Status = "扫描目录中", Details = "正在统计文件和子目录…" };
                    TrackTransfer(folder); ShowTransfers = true;
                    _transfers.Add(TransferAsync(folder, async ct =>
                    {
                        var plan = await _localFiles.PlanDirectoryUploadAsync(local, ct);
                        folder.SetTotal(plan.TotalBytes);
                        var uploaded = await new DirectoryUploader(files).UploadAsync(plan, targetDirectory,
                            target => dialogs.ConfirmAsync("合并远程文件夹", $"远程文件夹已存在：\n{target}\n\n是否合并并覆盖其中的同名文件？远端其他文件会保留。", true),
                            new Progress<long>(folder.Report), ct);
                        if (!uploaded) folder.Skip("已保留远端现有文件夹");
                        else if (plan.SkippedLinks > 0) folder.Details = $"已上传 {plan.Files.Count} 个文件，跳过 {plan.SkippedLinks} 个链接";
                    }, sessionToken, true));
                    continue;
                }
                var name = System.IO.Path.GetFileName(local); var target = RemotePath.Combine(targetDirectory, name);
                if (!overwriteConfirmed && await files.ExistsAsync(target, sessionToken))
                {
                    var answer = await dialogs.ConfirmWithRememberAsync("覆盖文件", $"远程文件已存在：\n{target}\n\n是否覆盖？", true);
                    if (!answer.Confirmed) continue;
                    overwriteConfirmed = answer.Remember;
                }
                var transfer = new TransferViewModel(name, "↑ 上传", new FileInfo(local).Length); TrackTransfer(transfer); ShowTransfers = true;
                var task = TransferAsync(transfer, ct => files.UploadAsync(local, target, new Progress<long>(transfer.Report), ct), sessionToken, true);
                _transfers.Add(task);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { await dialogs.ShowErrorAsync(ex.Message); }
        }
    }
    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (_files == null) return;
        await Local.InitializeAsync();
        if (string.IsNullOrEmpty(Local.Path)) { await dialogs.ShowErrorAsync("请先在左侧打开本地保存目录。"); return; }
        await DownloadEntriesAsync(Local.Path);
    }
    [RelayCommand] private Task DownloadAsAsync() => DownloadEntriesAsync(null);
    public RemoteFileSelection? CaptureDownloadSelection()
        => CaptureDownloadSelection(SelectedEntries.Count > 0 ? SelectedEntries.ToArray() : SelectedEntry == null ? [] : [SelectedEntry]);

    /// <summary>按指定条目生成拖拽用的远程选择。</summary>
    public RemoteFileSelection? CaptureDownloadSelection(IReadOnlyList<RemoteEntry> entries)
        => _files == null || !IsAvailable || entries.Count == 0 ? null : new(this, _files, _sessionToken, SessionVersion, entries);

    public bool IsCurrent(RemoteFileSelection selection)
        => ReferenceEquals(selection.Owner, this) && selection.SessionVersion == SessionVersion && ReferenceEquals(selection.Files, _files) && !selection.Token.IsCancellationRequested && IsAvailable;

    private Task DownloadEntriesAsync(string? directory)
        => CaptureDownloadSelection() is { } selection ? DownloadEntriesAsync(selection, directory) : Task.CompletedTask;

    public async Task DownloadEntriesAsync(RemoteFileSelection selection, string? directory)
    {
        if (!IsCurrent(selection)) return;
        var files = selection.Files; var sessionToken = selection.Token;
        // 勾选“保持选择”后，本次批量下载里的后续文件直接覆盖，不再逐个询问。
        var overwriteConfirmed = false;
        foreach (var entry in selection.Entries)
        {
            if (!IsCurrent(selection)) break;
            try
            {
                sessionToken.ThrowIfCancellationRequested();
                if (entry.IsSymbolicLink) throw new IOException("暂不下载符号链接，请选择实际文件或目录。");
                if (entry.IsDirectory)
                {
                    var parent = directory ?? await dialogs.PickDownloadFolderAsync();
                    if (parent == null) continue;
                    sessionToken.ThrowIfCancellationRequested();
                    var folder = new TransferViewModel(entry.Name, "↓ 下载文件夹", 0) { Status = "扫描目录中", Details = "正在统计文件和子目录，符号链接会跳过…" };
                    TrackTransfer(folder); ShowTransfers = true;
                    _transfers.Add(TransferAsync(folder, async ct =>
                    {
                        var downloaded = await new DirectoryDownloader(files).DownloadAsync(entry, parent,
                            target => dialogs.ConfirmAsync("合并本地文件夹", $"本地文件夹已存在：\n{target}\n\n是否合并并覆盖其中的同名文件？其他文件会保留，符号链接会跳过。", true),
                            folder.SetTotal, new Progress<long>(folder.Report), ct);
                        if (!downloaded) folder.Skip("已保留本地现有文件夹");
                    }, sessionToken, false));
                    continue;
                }
                var local = directory == null ? await dialogs.PickDownloadAsync(entry.Name) : DirectoryDownloader.ChildPath(directory, entry.Name);
                if (local == null) continue;
                DirectoryDownloader.EnsureFile(local);
                if (!overwriteConfirmed && directory != null && File.Exists(local))
                {
                    var answer = await dialogs.ConfirmWithRememberAsync("覆盖本地文件", $"本地文件已存在：\n{local}\n\n是否覆盖？", true);
                    if (!answer.Confirmed) continue;
                    overwriteConfirmed = answer.Remember;
                }
                sessionToken.ThrowIfCancellationRequested();
                var transfer = new TransferViewModel(entry.Name, "↓ 下载", entry.Size); TrackTransfer(transfer); ShowTransfers = true;
                _transfers.Add(TransferAsync(transfer, ct => files.DownloadAsync(entry.FullPath, local, new Progress<long>(transfer.Report), ct), sessionToken, false));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { await dialogs.ShowErrorAsync(ex.Message); }
        }
    }
    private async Task TransferAsync(TransferViewModel transfer, Func<CancellationToken, Task> operation, CancellationToken sessionToken, bool uploading)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(transfer.Token, sessionToken);
        try { await operation(linked.Token); transfer.Complete(); }
        catch (OperationCanceledException) { transfer.FinishCanceled(uploading); }
        catch (Exception) when (linked.IsCancellationRequested) { transfer.FinishCanceled(uploading); }
        catch (Exception ex) { transfer.Fail(ex.Message); log.LogWarning(ex, "文件传输失败"); }
        finally { OnPropertyChanged(nameof(HasActiveTransfers)); }
        if (!sessionToken.IsCancellationRequested)
        {
            if (uploading) { await RefreshAsync(); await RefreshTreeAsync(); }
            else { await Local.RefreshCommand.ExecuteAsync(null); await Local.RefreshTreeAsync(); }
        }
    }
    public void CancelTransfers() { foreach (var transfer in Transfers.Where(x => x.IsActive)) transfer.CancelCommand.Execute(null); }
    public Task DrainTransfersAsync() => Task.WhenAll(_transfers);
    [RelayCommand]
    private void ClearTransfers()
    {
        foreach (var item in Transfers.Where(t => !t.IsActive).ToArray()) { Transfers.Remove(item); item.Dispose(); }
        _transfers.RemoveAll(t => t.IsCompleted);
        OnPropertyChanged(nameof(TransferSummary));
        OnPropertyChanged(nameof(HasActiveTransfers));
    }

    // ---------------- 文件树宿主 ----------------
    /// <summary>当前选中的树节点集合，支持 Ctrl 多选。</summary>
    private readonly List<FileTreeNodeViewModel> _selectedTreeNodes = [];
    /// <summary>最近点选的树节点，作为面板的主要选择。</summary>
    private FileTreeNodeViewModel? _primaryTreeNode;

    public bool ShowHiddenFiles => ShowHidden;
    public string SearchText => Search;
    /// <summary>远程树接受本地文件拖入，表示上传到该目录。</summary>
    public bool AcceptsLocalDrop => IsAvailable;
    /// <summary>远程树不接受远程条目拖入。</summary>
    public bool AcceptsRemoteDrop => false;
    public bool CanDownload => IsAvailable;
    public bool CanUpload => false;

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
    }

    /// <summary>Ctrl+A：选中当前可见的所有行。</summary>
    public void SelectAll(IReadOnlyList<FileTreeNodeViewModel> nodes)
    {
        foreach (var item in _selectedTreeNodes) item.IsSelected = false;
        _selectedTreeNodes.Clear();
        foreach (var item in nodes) { _selectedTreeNodes.Add(item); item.IsSelected = true; }
        _primaryTreeNode = nodes.Count > 0 ? nodes[^1] : null;
        SyncTreeSelection();
    }

    /// <summary>清掉树上的高亮：目录变了，之前的选中项就不该再留着。</summary>
    private void ClearTreeSelection()
    {
        foreach (var item in _selectedTreeNodes) item.IsSelected = false;
        _selectedTreeNodes.Clear();
        _primaryTreeNode = null;
        SelectedEntries.Clear();
        SelectedEntry = null;
    }

    /// <summary>把树上的选择同步到面板的当前条目。</summary>
    private void SyncTreeSelection()
    {
        SelectedEntries.Clear();
        SelectedEntry = null;
        foreach (var item in _selectedTreeNodes)
            if (item is RemoteTreeNodeViewModel remote) SelectedEntries.Add(remote.Entry);
        if (_primaryTreeNode is RemoteTreeNodeViewModel primary) SelectedEntry = primary.Entry;
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
        if (node is not RemoteTreeNodeViewModel remote) return;
        if (remote.Entry.IsDirectory)
        {
            if (!remote.IsExpanded) await remote.ToggleCommand.ExecuteAsync(null);
            await NavigateAsync(remote.Entry.FullPath);
        }
        else await OpenEntryAsync(remote.Entry);
    }

    /// <summary>远程树节点可以拖到本地树下载。</summary>
    public FileDragDrop.Payload? CreateDragPayload(FileTreeNodeViewModel node)
    {
        if (node is not RemoteTreeNodeViewModel remote) return null;
        return CaptureDownloadSelection([remote.Entry]) is { } selection ? FileDragDrop.RemotePayload(selection) : null;
    }

    /// <summary>本地文件拖到远程目录节点：上传到该目录。</summary>
    public async Task DropLocalAsync(FileTreeNodeViewModel node, IReadOnlyList<string> paths)
    {
        if (node is not RemoteTreeNodeViewModel remote) return;
        await UploadPathsAsync(paths, remote.Entry.FullPath);
    }

    /// <summary>远程树不接受远程条目拖入。</summary>
    public Task DropRemoteAsync(FileTreeNodeViewModel node, RemoteFileSelection selection) => Task.CompletedTask;

    /// <summary>读取目录失败时上报。</summary>
    public void ReportError(string message) => Error = message;

    /// <summary>远程树只接收本地文件：外部资源管理器拖入，或本地树节点拖过来。</summary>
    public bool CanAcceptDrop(IDataTransfer data) => FileDragDrop.CanDrop(this, true, data);

    /// <summary>搜索过滤后重建可见行。</summary>
    private void UpdateTreeVisibility()
    {
        foreach (var root in TreeRoots) root.UpdateVisibility();
        tree.Rebuild();
    }

    /// <summary>重新读取已经展开的目录，隐藏文件开关或传输完成后调用。</summary>
    public async Task RefreshTreeAsync()
    {
        foreach (var root in TreeRoots) await root.ReloadLoadedAsync();
        UpdateTreeVisibility();
    }

    /// <summary>只刷新指定目录的树节点，没加载过就跳过。</summary>
    public async Task RefreshTreeNodeAsync(string path)
    {
        foreach (var root in TreeRoots)
        {
            var node = root.Find(path);
            if (node != null) { await node.ReloadAsync(); return; }
        }
    }

    public void Dispose() { CancelTransfers(); foreach (var transfer in Transfers) transfer.Dispose(); Local.Dispose(); }
}


// 拖动时固定远端会话和选择；切换目录不改变来源，重连后旧拖动失效。
public sealed record RemoteFileSelection(FileBrowserViewModel Owner, IRemoteFileSystem Files, CancellationToken Token, int SessionVersion, IReadOnlyList<RemoteEntry> Entries)
{
    public bool IsValid => Entries.Count > 0 && Owner.IsCurrent(this);
}
