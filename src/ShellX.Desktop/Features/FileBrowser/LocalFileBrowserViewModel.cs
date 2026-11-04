using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Files;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class LocalFileBrowserViewModel(ILocalFileSystem files, IDialogService dialogs,
    Func<IReadOnlyList<string>, Task> upload) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<LocalEntry> _all = [];
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private int _navigationGeneration;
    public ObservableCollection<LocalEntry> Entries { get; } = [];
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
    [ObservableProperty] private bool _isRemoteAvailable;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool CanEditText => !IsEditingText && SelectedEntries.Count <= 1 && SelectedEntry is { IsDirectory: false, IsSymbolicLink: false };
    public void NotifySelectionChanged() => EditTextCommand.NotifyCanExecuteChanged();
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnSearchChanged(string value) => Filter();
    partial void OnShowHiddenChanged(bool value) => Filter();

    public Task InitializeAsync() => Path.Length > 0 || IsBusy ? Task.CompletedTask : NavigateAsync(files.HomeDirectory);
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
        var directory = Path; if (directory.Length == 0) return;
        var name = await dialogs.PromptAsync(folder ? "新建本地文件夹" : "新建本地文件", "名称", folder ? "新建文件夹" : "untitled.txt");
        if (name != null) await RunAsync(async () => { await files.CreateAsync(directory, name, folder); await RefreshAsync(); });
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
        if (entries.Length == 0) return;
        if (!await dialogs.ConfirmAsync("删除本地项目", $"永久删除以下 {entries.Length} 个项目？目录会递归删除，符号链接只删除链接本身。\n\n" + string.Join("\n", entries.Take(8).Select(e => e.FullPath)) + "\n\n此操作不会进入回收站。", true)) return;
        await RunAsync(async () =>
        {
            foreach (var entry in entries) await files.DeleteAsync(entry.FullPath, _lifetime.Token);
            await RefreshAsync();
        });
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
    public void Dispose() { ++_navigationGeneration; _lifetime.Cancel(); _lifetime.Dispose(); }
}
