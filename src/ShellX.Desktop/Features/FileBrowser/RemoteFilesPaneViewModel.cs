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
    private IRemoteFileSystem? files;
    private CancellationToken token;
    private FileTreeNodeViewModel? primary;
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
            for (var i = 0; i < entries.Length; i++)
            {
                var node = new RemoteTreeNodeViewModel(entries[i], remoteFiles, token, this) { Level = 0 };
                // 进入新目录时逐行淡入，刷新时直接显示，避免闪一下。
                node.IsRevealing = animate && entries.Length <= 24;
                node.RevealOrder = i;
                Entries.Add(node);
            }
            CurrentPath = directory.Path ?? path;
            CanGoUp = CurrentPath.TrimEnd('/').Length > 0;
            Summary = $"{Entries.Count} 项";
            selected.Clear(); primary = null;
            OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedPath));
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
