using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShellX.Core.Files;
using ShellX.Infrastructure.Files;
using ShellX.Desktop.Features.Transfers;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class FileBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService dialogs;
    private readonly ILogger<FileBrowserViewModel> log;
    private readonly ILocalFileSystem _localFiles;
    public LocalFileBrowserViewModel Local { get; }
    public FileBrowserViewModel(IDialogService dialogs, ILogger<FileBrowserViewModel> log, ILocalFileSystem localFiles)
    {
        this.dialogs = dialogs; this.log = log; _localFiles = localFiles;
        Local = new(localFiles, dialogs, UploadPathsAsync);
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
    public ObservableCollection<DirectoryNodeViewModel> DirectoryRoots { get; } = [];
    public ObservableCollection<string> RecentPaths { get; } = [];
    public ObservableCollection<TransferViewModel> Transfers { get; } = [];
    public List<RemoteEntry> SelectedEntries { get; } = [];
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(EditTextCommand))] private RemoteEntry? _selectedEntry;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(EditTextCommand))] private bool _isEditingText;
    [ObservableProperty] private string _path = ".";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _summary = "连接后浏览远程文件";
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _showTransfers;
    [ObservableProperty] private bool _showTree;
    public bool HasActiveTransfers => Transfers.Any(t => t.IsActive);
    public bool HasError => !string.IsNullOrEmpty(Error);
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsAvailableChanged(bool value) { Local.IsRemoteAvailable = value; EditTextCommand.NotifyCanExecuteChanged(); }
    public bool CanEditText => IsAvailable && !IsEditingText && SelectedEntries.Count <= 1 && SelectedEntry is { IsDirectory: false, IsSymbolicLink: false };
    public void NotifySelectionChanged() => EditTextCommand.NotifyCanExecuteChanged();
    public void Attach(IRemoteFileSystem? files, CancellationToken token)
    {
        ++_navigationGeneration; ++SessionVersion; _files = files; _sessionToken = token; IsAvailable = files != null;
        _all = []; Entries.Clear(); _history.Clear(); _historyIndex = -1; SelectedEntries.Clear(); SelectedEntry = null;
        Error = ""; IsBusy = false; Summary = files == null ? "尚未连接 SFTP" : "SFTP 已就绪";
        DirectoryRoots.Clear(); RecentPaths.Clear();
        if (files != null)
        {
            var capturedToken = token;
            DirectoryRoots.Add(new("/", "/", async path => (await files.ListAsync(path, capturedToken)).Entries,
                path => NavigateAsync(path), error => { if (ReferenceEquals(_files, files)) Error = error; }));
        }
    }
    partial void OnSearchChanged(string value) => Filter();
    partial void OnShowHiddenChanged(bool value) => Filter();
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
            Path = result.Path; _all = result.Entries; Filter(); SelectedEntries.Clear(); SelectedEntry = null;
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
    [RelayCommand] private void ToggleTransfers() => ShowTransfers = !ShowTransfers;
    [RelayCommand] private void ToggleTree() => ShowTree = !ShowTree;
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
        await RunAsync(async () => { await _files.CreateAsync(RemotePath.Combine(Path, name), directory, _sessionToken); await RefreshAsync(); });
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
        if (entries.Length == 0 || _files == null) return;
        if (!await dialogs.ConfirmAsync("删除远程项目", $"永久删除以下 {entries.Length} 个项目？目录会递归删除，符号链接只删除链接本身。\n\n" + string.Join("\n", entries.Take(8).Select(e => e.FullPath)) + "\n\n此操作不会进入回收站。", true)) return;
        await RunAsync(async () => { foreach (var entry in entries) await _files.DeleteAsync(entry.FullPath, _sessionToken); await RefreshAsync(); });
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
    public async Task UploadPathsAsync(IReadOnlyList<string> paths)
    {
        var files = _files; var targetDirectory = Path; var sessionToken = _sessionToken;
        if (files == null) return;
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
                    Transfers.Add(folder); ShowTransfers = true;
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
                if (await files.ExistsAsync(target, sessionToken) && !await dialogs.ConfirmAsync("覆盖文件", $"远程文件已存在：\n{target}\n\n是否覆盖？", true)) continue;
                var transfer = new TransferViewModel(name, "↑ 上传", new FileInfo(local).Length); Transfers.Add(transfer); ShowTransfers = true;
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
        => _files == null || !IsAvailable ? null : new(this, _files, _sessionToken, SessionVersion,
            SelectedEntries.Count > 0 ? SelectedEntries.ToArray() : SelectedEntry == null ? [] : [SelectedEntry]);

    public bool IsCurrent(RemoteFileSelection selection)
        => ReferenceEquals(selection.Owner, this) && selection.SessionVersion == SessionVersion && ReferenceEquals(selection.Files, _files) && !selection.Token.IsCancellationRequested && IsAvailable;

    private Task DownloadEntriesAsync(string? directory)
        => CaptureDownloadSelection() is { } selection ? DownloadEntriesAsync(selection, directory) : Task.CompletedTask;

    public async Task DownloadEntriesAsync(RemoteFileSelection selection, string? directory)
    {
        if (!IsCurrent(selection)) return;
        var files = selection.Files; var sessionToken = selection.Token;
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
                    Transfers.Add(folder); ShowTransfers = true;
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
                if (directory != null && File.Exists(local) && !await dialogs.ConfirmAsync("覆盖本地文件", $"本地文件已存在：\n{local}\n\n是否覆盖？", true)) continue;
                sessionToken.ThrowIfCancellationRequested();
                var transfer = new TransferViewModel(entry.Name, "↓ 下载", entry.Size); Transfers.Add(transfer); ShowTransfers = true;
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
            if (uploading) await RefreshAsync();
            else await Local.RefreshCommand.ExecuteAsync(null);
        }
    }
    public void CancelTransfers() { foreach (var transfer in Transfers.Where(x => x.IsActive)) transfer.CancelCommand.Execute(null); }
    public Task DrainTransfersAsync() => Task.WhenAll(_transfers);
    [RelayCommand]
    private void ClearTransfers()
    { foreach (var item in Transfers.Where(t => !t.IsActive).ToArray()) { Transfers.Remove(item); item.Dispose(); } _transfers.RemoveAll(t => t.IsCompleted); }
    public void Dispose() { CancelTransfers(); foreach (var transfer in Transfers) transfer.Dispose(); Local.Dispose(); }
}


// 拖动时固定远端会话和选择；切换目录不改变来源，重连后旧拖动失效。
public sealed record RemoteFileSelection(FileBrowserViewModel Owner, IRemoteFileSystem Files, CancellationToken Token, int SessionVersion, IReadOnlyList<RemoteEntry> Entries)
{
    public bool IsValid => Entries.Count > 0 && Owner.IsCurrent(this);
}
