using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShellX.Core.Files;
using ShellX.Infrastructure.Files;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>
/// 主窗口右侧的远程文件面板：按层级浏览远程目录，一次只显示当前这一层。
/// 点目录行就进入下一层，顶部面包屑可以随时跳回任意层级；
/// 行内下载会先选本地保存目录，把本地文件拖进来就上传到当前目录。
/// </summary>
public sealed partial class RemoteFilesPaneViewModel : ObservableObject, IFileTreeSink, IDisposable
{
    private readonly IDialogService dialogs;
    private readonly ILogger log;
    private readonly ILocalFileSystem localFiles;
    private readonly List<FileTreeNodeViewModel> selected = [];
    /// <summary>访问过的目录，配合后退、前进使用。</summary>
    private readonly List<string> history = [];
    private IRemoteFileSystem? files;
    private CancellationToken token;
    private FileTreeNodeViewModel? primary;
    /// <summary>历史里当前所在的位置。</summary>
    private int historyIndex = -1;
    private long totalBytes;
    private long doneBytes;

    public RemoteFilesPaneViewModel(IDialogService dialogs, ILogger<RemoteFilesPaneViewModel> log, ILocalFileSystem localFiles)
    {
        this.dialogs = dialogs; this.log = log; this.localFiles = localFiles;
    }

    /// <summary>当前目录里的条目，一次只显示这一层。</summary>
    public ObservableCollection<FileTreeNodeViewModel> Entries { get; } = [];
    /// <summary>当前选中的行。</summary>
    public FileTreeNodeViewModel? Selected => primary;
    /// <summary>选中项的路径，右键菜单和状态栏要用。</summary>
    public string SelectedPath => primary?.Path ?? "";

    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _summary = "连接后浏览远程文件";
    [ObservableProperty] private string _target = "";
    /// <summary>当前所在目录，面包屑显示它。</summary>
    [ObservableProperty] private string _currentPath = "/";
    /// <summary>能否回到上一级。</summary>
    [ObservableProperty] private bool _canGoUp;
    /// <summary>能否后退到上一个访问过的目录。</summary>
    [ObservableProperty] private bool _canGoBack;
    /// <summary>能否前进到下一个访问过的目录。</summary>
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private string _transferText = "";
    [ObservableProperty] private double _transferPercent;
    [ObservableProperty] private bool _hasTransfer;
    [ObservableProperty] private bool _isBusy;

    public bool HasError => !string.IsNullOrEmpty(Error);
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>切换连接或重连：重新挂上远程文件系统并回到根目录。</summary>
    public void Attach(IRemoteFileSystem? remote, CancellationToken sessionToken, string target = "")
    {
        files = remote; token = sessionToken; IsAvailable = remote != null;
        Target = target; Error = ""; IsBusy = false;
        Summary = remote == null ? "尚未连接 SFTP" : "SFTP 已就绪";
        Entries.Clear(); selected.Clear(); primary = null;
        history.Clear(); historyIndex = -1;
        CanGoBack = false; CanGoForward = false;
        CurrentPath = "/"; CanGoUp = false;
        TransferText = ""; HasTransfer = false; TransferPercent = 0;
        OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedPath));
        if (remote == null) return;
        _ = LoadAsync("/", animate: true);
    }

    /// <summary>读取一个目录的内容：目录在前，文件按名称排序。</summary>
    private async Task LoadAsync(string path, bool animate)
    {
        var remoteFiles = files; if (remoteFiles == null) return;
        IsBusy = true;
        try
        {
            var directory = await remoteFiles.ListAsync(path, token);
            var entries = directory.Entries
                .Where(e => !e.Name.StartsWith('.'))
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Entries.Clear();
            var revealed = new List<FileTreeNodeViewModel>();
            for (var i = 0; i < entries.Length; i++)
            {
                var node = new RemoteTreeNodeViewModel(entries[i], remoteFiles, token, this) { Level = 0 };
                // 进入新目录时逐行淡入，刷新时直接显示，避免闪一下。
                node.IsRevealing = animate && entries.Length <= 24;
                node.RevealOrder = i;
                if (node.IsRevealing) revealed.Add(node);
                Entries.Add(node);
            }
            // 动画播完必须清掉标记：行被虚拟列表复用时会重播一次动画，看起来一直在跳。
            if (revealed.Count > 0) _ = ClearRevealFlagsAsync(revealed);
            CurrentPath = directory.Path ?? path;
            CanGoUp = CurrentPath.TrimEnd('/').Length > 0;
            PushHistory(CurrentPath);
            Summary = $"{Entries.Count} 项";
            selected.Clear(); primary = null;
            OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedPath));
            NotifySelectionCommands();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = "目录读取失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>进入指定目录：面包屑跳转和上级按钮都走这里。</summary>
    public async Task NavigateAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var target = path.Trim();
        if (!target.StartsWith('/')) target = "/" + target;
        if (target != CurrentPath) await LoadAsync(target, animate: true);
    }

    /// <summary>记录访问过的目录：后退、前进都靠它。</summary>
    private void PushHistory(string path)
    {
        if (historyIndex >= 0 && historyIndex < history.Count && history[historyIndex] == path) return;
        if (historyIndex >= 0 && historyIndex < history.Count - 1)
            history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
        history.Add(path);
        historyIndex = history.Count - 1;
        UpdateHistoryState();
    }

    private void UpdateHistoryState()
    {
        CanGoBack = historyIndex > 0;
        CanGoForward = historyIndex >= 0 && historyIndex < history.Count - 1;
    }

    /// <summary>后退：回到上一个访问过的目录。</summary>
    [RelayCommand]
    public async Task BackAsync()
    {
        if (!CanGoBack) return;
        historyIndex--;
        await LoadAsync(history[historyIndex], animate: true);
        UpdateHistoryState();
    }

    /// <summary>前进：回到下一个访问过的目录。</summary>
    [RelayCommand]
    public async Task ForwardAsync()
    {
        if (!CanGoForward) return;
        historyIndex++;
        await LoadAsync(history[historyIndex], animate: true);
        UpdateHistoryState();
    }

    /// <summary>回到上一级目录。</summary>
    [RelayCommand]
    public async Task UpAsync()
    {
        if (!CanGoUp) return;
        await NavigateAsync(RemotePath.Parent(CurrentPath));
    }

    /// <summary>重新读取当前目录。</summary>
    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync(CurrentPath, animate: false);

    // ---------------- 文件树宿主 ----------------
    public bool ShowHiddenFiles => false;
    public string SearchText => "";
    /// <summary>远程目录接收本地文件拖入：上传到该目录。</summary>
    public bool AcceptsLocalDrop => IsAvailable;
    /// <summary>远程目录不接受远程条目拖入。</summary>
    public bool AcceptsRemoteDrop => false;
    public bool CanDownload => IsAvailable;
    public bool CanUpload => IsAvailable;
    /// <summary>这个面板的行内带下载按钮。</summary>
    public bool ShowRowDownload => true;
    /// <summary>面板窄，用紧凑行：省掉时间列，避免下载按钮被裁掉。</summary>
    public bool CompactRows => true;
    /// <summary>这里按层级浏览，不是一棵树，不显示展开箭头。</summary>
    public bool ShowRowChevron => false;
    /// <summary>这里按层级浏览，行不缩进。</summary>
    public bool ShowRowIndent => false;
    /// <summary>按层级跳转不做位移动画，只淡入，避免看着一直在跳。</summary>
    public bool SlideReveal => false;

    /// <summary>点击目录行就进入下一层；按住 Ctrl 点击只选中。</summary>
    public void Select(FileTreeNodeViewModel node, bool toggle = false, bool fromContextMenu = false)
    {
        if (toggle)
        {
            if (selected.Remove(node)) node.IsSelected = false;
            else { selected.Add(node); node.IsSelected = true; }
        }
        else
        {
            foreach (var item in selected) item.IsSelected = false;
            selected.Clear(); selected.Add(node); node.IsSelected = true;
        }
        primary = node;
        OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedPath));
        NotifySelectionCommands();
        if (!toggle && !fromContextMenu && node.IsDirectory) _ = NavigateAsync(node.Path);
    }

    public void SelectAll(IReadOnlyList<FileTreeNodeViewModel> nodes)
    {
        foreach (var item in selected) item.IsSelected = false;
        selected.Clear();
        foreach (var node in nodes) { selected.Add(node); node.IsSelected = true; }
        primary = nodes.Count > 0 ? nodes[^1] : null;
        OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedPath));
    }

    /// <summary>这里没有树，展开收起不做任何事。</summary>
    public void NotifyExpanded(FileTreeNodeViewModel node, bool expanded) { }

    /// <summary>双击：目录进入下一层，文件直接下载。</summary>
    public async Task ActivateAsync(FileTreeNodeViewModel node)
    {
        if (node.IsDirectory) { await NavigateAsync(node.Path); return; }
        await DownloadItemAsync(node);
    }

    /// <summary>选中的行对应的远程条目。</summary>
    private RemoteEntry? SelectedEntry => (primary as RemoteTreeNodeViewModel)?.Entry;
    /// <summary>只有普通文件能编辑文本。</summary>
    public bool CanEditSelected => SelectedEntry is { IsDirectory: false, IsSymbolicLink: false };
    /// <summary>有选中项才能下载、改权限、重命名、删除。</summary>
    public bool CanOperateSelected => SelectedEntry != null;

    /// <summary>选中项变了，右键菜单里命令的可用状态跟着更新。</summary>
    private void NotifySelectionCommands()
    {
        DownloadSelectedCommand.NotifyCanExecuteChanged();
        EditTextCommand.NotifyCanExecuteChanged();
        PermissionsCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>下载选中的文件或目录：先选本地保存目录。</summary>
    [RelayCommand(CanExecute = nameof(CanOperateSelected))]
    private async Task DownloadSelectedAsync()
    {
        if (primary == null) return;
        await DownloadItemAsync(primary);
    }

    /// <summary>编辑文本：打开编辑器标签，保存后刷新当前目录。</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task EditTextAsync()
    {
        var entry = SelectedEntry; var remoteFiles = files;
        if (entry == null || remoteFiles == null || entry.IsDirectory || entry.IsSymbolicLink) return;
        await RunAsync(async () =>
        {
            TextEditorLimits.ValidateSize(entry.Size);
            var document = await remoteFiles.ReadTextAsync(entry.FullPath, token);
            token.ThrowIfCancellationRequested();
            await dialogs.EditFileAsync(document, async (original, content) =>
            {
                var updated = await remoteFiles.SaveTextAsync(original, content, token);
                await RefreshAsync();
                return updated;
            });
        });
    }

    /// <summary>属性与权限：改完刷新当前目录。</summary>
    [RelayCommand(CanExecute = nameof(CanOperateSelected))]
    private async Task PermissionsAsync()
    {
        var entry = SelectedEntry; var remoteFiles = files; if (entry == null || remoteFiles == null) return;
        if (entry.IsSymbolicLink) { await dialogs.ShowErrorAsync("为避免修改符号链接指向的目标，请进入目标实际目录后修改权限。"); return; }
        if (await dialogs.PermissionsAsync(entry) is not { } permissions) return;
        await RunAsync(async () => { await remoteFiles.SetPermissionsAsync(entry.FullPath, permissions, token); await RefreshAsync(); });
    }

    /// <summary>重命名。</summary>
    [RelayCommand(CanExecute = nameof(CanOperateSelected))]
    private async Task RenameAsync()
    {
        var entry = SelectedEntry; var remoteFiles = files; if (entry == null || remoteFiles == null) return;
        var name = await dialogs.PromptAsync("重命名", "新名称", entry.Name);
        if (name == null || name == entry.Name) return;
        await RunAsync(async () =>
        {
            await remoteFiles.RenameAsync(entry.FullPath, RemotePath.Combine(RemotePath.Parent(entry.FullPath), name), token);
            await RefreshAsync();
        });
    }

    /// <summary>删除：先确认，删完刷新。</summary>
    [RelayCommand(CanExecute = nameof(CanOperateSelected))]
    private async Task DeleteAsync()
    {
        var entry = SelectedEntry; var remoteFiles = files; if (entry == null || remoteFiles == null) return;
        var tip = entry.IsDirectory ? "目录会递归删除。" : entry.IsSymbolicLink ? "符号链接只删除链接本身。" : string.Empty;
        if (!await dialogs.ConfirmAsync("删除远程项目", $"永久删除？{tip}\n\n{entry.FullPath}\n\n此操作不会进入回收站。", true)) return;
        await RunAsync(async () => { await remoteFiles.DeleteAsync(entry.FullPath, token); await RefreshAsync(); });
    }

    /// <summary>新建文件。</summary>
    [RelayCommand] private Task NewFileAsync() => CreateAsync(false);
    /// <summary>新建文件夹。</summary>
    [RelayCommand] private Task NewFolderAsync() => CreateAsync(true);
    private async Task CreateAsync(bool directory)
    {
        var remoteFiles = files; if (remoteFiles == null) return;
        var name = await dialogs.PromptAsync(directory ? "新建文件夹" : "新建文件", "名称", directory ? "新建文件夹" : "untitled.txt");
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync(async () =>
        {
            await remoteFiles.CreateAsync(RemotePath.Combine(CurrentPath, name), directory, token);
            await RefreshAsync();
        });
    }

    /// <summary>这个面板不向外拖拽，只接收本地文件。</summary>
    public FileDragDrop.Payload? CreateDragPayload(FileTreeNodeViewModel node) => null;

    /// <summary>本地文件拖到目录行：上传到该目录。</summary>
    public async Task DropLocalAsync(FileTreeNodeViewModel node, IReadOnlyList<string> paths)
    {
        var target = node is RemoteTreeNodeViewModel remote && remote.IsDirectory ? remote.Path : CurrentPath;
        await UploadPathsAsync(paths, target);
    }

    public Task DropRemoteAsync(FileTreeNodeViewModel node, RemoteFileSelection selection) => Task.CompletedTask;

    public void ReportError(string message) => Error = message;

    /// <summary>接收本地文件：本应用文件管理器拖过来的，或系统文件管理器拖进来的。</summary>
    public bool CanAcceptDrop(IDataTransfer data)
    {
        if (!IsAvailable) return false;
        if (FileDragDrop.ReadLocalPaths(data) is { Length: > 0 }) return true;
        if (FileDragDrop.ReadFilePaths(data) is { Length: > 0 }) return true;
        return false;
    }

    /// <summary>下载节点：先选本地保存目录，再把文件或文件夹写进去。</summary>
    public async Task DownloadItemAsync(FileTreeNodeViewModel node)
    {
        if (node is not RemoteTreeNodeViewModel remote) return;
        var remoteFiles = files; if (remoteFiles == null) return;
        if (remote.Entry.IsSymbolicLink) { await dialogs.ShowErrorAsync("暂不下载符号链接，请选择实际文件。"); return; }
        var folder = await dialogs.PickDownloadFolderAsync();
        if (string.IsNullOrEmpty(folder)) return;
        await RunAsync(async () =>
        {
            if (remote.Entry.IsDirectory)
            {
                totalBytes = 0; doneBytes = 0;
                TransferText = "↓ 下载 " + remote.Entry.Name; HasTransfer = true;
                await new DirectoryDownloader(remoteFiles).DownloadAsync(remote.Entry, folder,
                    target => dialogs.ConfirmAsync("合并本地文件夹", $"本地文件夹已存在：\n{target}\n\n是否合并并覆盖其中的同名文件？其他文件会保留，符号链接会跳过。", true),
                    size => totalBytes = size, new Progress<long>(Report), token);
            }
            else
            {
                var local = DirectoryDownloader.ChildPath(folder, remote.Entry.Name);
                DirectoryDownloader.EnsureFile(local);
                if (File.Exists(local))
                {
                    var answer = await dialogs.ConfirmAsync("覆盖本地文件", $"本地文件已存在：\n{local}\n\n是否覆盖？", true);
                    if (!answer) return;
                }
                totalBytes = remote.Entry.Size; doneBytes = 0;
                TransferText = "↓ 下载 " + remote.Entry.Name; HasTransfer = true;
                await remoteFiles.DownloadAsync(remote.Entry.FullPath, local, new Progress<long>(Report), token);
            }
        });
    }

    /// <summary>上传本地路径到远程目录。</summary>
    public async Task UploadPathsAsync(IReadOnlyList<string> paths, string? targetDirectory = null)
    {
        var remoteFiles = files; if (remoteFiles == null || paths.Count == 0) return;
        targetDirectory ??= CurrentPath;
        foreach (var local in paths)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(local);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { await dialogs.ShowErrorAsync("暂不支持上传符号链接，请选择实际文件或目录。"); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var plan = await localFiles.PlanDirectoryUploadAsync(local, token);
                    totalBytes = plan.TotalBytes; doneBytes = 0;
                    TransferText = "↑ 上传 " + plan.Name; HasTransfer = true;
                    await new DirectoryUploader(remoteFiles).UploadAsync(plan, targetDirectory,
                        target => dialogs.ConfirmAsync("合并远程文件夹", $"远程文件夹已存在：\n{target}\n\n是否合并并覆盖其中的同名文件？远端其他文件会保留。", true),
                        new Progress<long>(Report), token);
                }
                else
                {
                    var info = new FileInfo(local);
                    var target = RemotePath.Combine(targetDirectory, info.Name);
                    if (await remoteFiles.ExistsAsync(target, token))
                    {
                        var answer = await dialogs.ConfirmAsync("覆盖文件", $"远程文件已存在：\n{target}\n\n是否覆盖？", true);
                        if (!answer) continue;
                    }
                    totalBytes = info.Length; doneBytes = 0;
                    TransferText = "↑ 上传 " + info.Name; HasTransfer = true;
                    await remoteFiles.UploadAsync(local, target, new Progress<long>(Report), token);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { await dialogs.ShowErrorAsync(ex.Message); }
        }
        await RefreshAsync();
    }

    /// <summary>把本地路径拖到面板空白处：上传到当前目录。</summary>
    public async Task UploadDroppedAsync(IReadOnlyList<string> paths) => await UploadPathsAsync(paths, CurrentPath);

    /// <summary>出现动画播完后清掉标记，避免行被复用时反复重播。</summary>
    private static async Task ClearRevealFlagsAsync(IReadOnlyList<FileTreeNodeViewModel> nodes)
    {
        // 错峰最长 224 毫秒，动画 200 毫秒，等动画结束再收标记。
        await Task.Delay(520);
        foreach (var node in nodes) node.IsRevealing = false;
    }

    private void Report(long value)
    {
        doneBytes = value;
        TransferPercent = totalBytes > 0 ? Math.Clamp(value * 100d / totalBytes, 0, 100) : 0;
        OnPropertyChanged(nameof(TransferPercent));
    }

    private async Task RunAsync(Func<Task> action)
    {
        Error = "";
        try { await action(); }
        catch (OperationCanceledException) { Error = "操作已取消"; }
        catch (Exception ex) { log.LogWarning(ex, "远程文件操作失败"); Error = ex.Message; await dialogs.ShowErrorAsync(ex.Message); }
        finally { IsBusy = false; }
    }

    public void Dispose()
    {
        Entries.Clear(); selected.Clear(); primary = null; files = null;
    }
}
