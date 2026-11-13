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
/// 主窗口右侧的远程文件面板：只管远程目录，用层级行展示（目录可展开收起，带动画）。
/// 点行内下载会先选本地保存目录；把本地文件拖进来就上传到对应目录。
/// </summary>
public sealed partial class RemoteFilesPaneViewModel : ObservableObject, IFileTreeSink, IDisposable
{
    private readonly IDialogService dialogs;
    private readonly ILogger log;
    private readonly ILocalFileSystem localFiles;
    private readonly FileTreeList tree = new();
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

    /// <summary>展开后的可见行，虚拟列表直接绑定它。</summary>
    public ObservableCollection<FileTreeNodeViewModel> FlatNodes => tree.Flat;
    /// <summary>当前选中的行。</summary>
    public FileTreeNodeViewModel? Selected => primary;
    /// <summary>选中项的路径，右键菜单和状态栏要用。</summary>
    public string SelectedPath => primary?.Path ?? "";

    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _summary = "连接后浏览远程文件";
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private string _transferText = "";
    [ObservableProperty] private double _transferPercent;
    [ObservableProperty] private bool _hasTransfer;
    [ObservableProperty] private bool _isBusy;

    public bool HasError => !string.IsNullOrEmpty(Error);
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>切换连接或重连：重新挂上远程文件系统并展开根目录。</summary>
    public void Attach(IRemoteFileSystem? remote, CancellationToken sessionToken, string target = "")
    {
        files = remote; token = sessionToken; IsAvailable = remote != null;
        Target = target; Error = ""; IsBusy = false;
        Summary = remote == null ? "尚未连接 SFTP" : "SFTP 已就绪";
        tree.Clear(); selected.Clear(); primary = null;
        TransferText = ""; HasTransfer = false; TransferPercent = 0;
        OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedPath));
        if (remote == null) return;
        var root = new RemoteEntry("/", "/", true, false, 0, DateTime.UtcNow, 493, 0, 0);
        var node = new RemoteTreeNodeViewModel(root, remote, sessionToken, this) { Level = 0 };
        tree.Roots.Add(node);
        tree.Flat.Add(node);
        // 根目录默认展开：面板一打开就能看到远程文件层级。
        _ = ExpandRootAsync(node);
    }

    private async Task ExpandRootAsync(FileTreeNodeViewModel node)
    {
        try { await node.ToggleCommand.ExecuteAsync(null); }
        catch (Exception ex) { Error = ex.Message; }
    }

    /// <summary>重新读取根目录内容。</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var root = tree.Roots.FirstOrDefault();
        if (root == null) return;
        IsBusy = true;
        try { await root.ReloadAsync(); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

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

    public void Select(FileTreeNodeViewModel node, bool toggle = false)
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
    }

    public void SelectAll(IReadOnlyList<FileTreeNodeViewModel> nodes)
    {
        foreach (var item in selected) item.IsSelected = false;
        selected.Clear();
        foreach (var node in nodes) { selected.Add(node); node.IsSelected = true; }
        primary = nodes.Count > 0 ? nodes[^1] : null;
        OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedPath));
    }

    /// <summary>展开或收起：把子行插进或移出扁平行列表，动画由行控件负责。</summary>
    public void NotifyExpanded(FileTreeNodeViewModel node, bool expanded)
    {
        if (expanded) tree.InsertDescendants(node);
        else tree.CollapseDescendants(node);
    }

    /// <summary>双击：目录展开或收起，文件直接下载。</summary>
    public async Task ActivateAsync(FileTreeNodeViewModel node)
    {
        if (node.IsDirectory) { await node.ToggleCommand.ExecuteAsync(null); return; }
        await DownloadItemAsync(node);
    }

    /// <summary>这个面板不向外拖拽，只接收本地文件。</summary>
    public FileDragDrop.Payload? CreateDragPayload(FileTreeNodeViewModel node) => null;

    /// <summary>本地文件拖到目录行：上传到该目录。</summary>
    public async Task DropLocalAsync(FileTreeNodeViewModel node, IReadOnlyList<string> paths)
    {
        var target = node is RemoteTreeNodeViewModel remote ? remote.Entry.FullPath : "/";
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
        targetDirectory ??= "/";
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

    /// <summary>把本地路径拖到面板空白处：上传到根目录。</summary>
    public async Task UploadDroppedAsync(IReadOnlyList<string> paths) => await UploadPathsAsync(paths, "/");

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
        tree.Clear(); selected.Clear(); primary = null; files = null;
    }
}
