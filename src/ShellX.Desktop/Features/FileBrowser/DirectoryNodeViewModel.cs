using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class DirectoryNodeViewModel(string name, string path,
    Func<string, Task<IReadOnlyList<RemoteEntry>>> load, Func<string, Task> navigate, Action<string> reportError) : ObservableObject
{
    private bool _loaded;
    public string Name => name;
    public string Path => path;
    public ObservableCollection<DirectoryNodeViewModel> Children { get; } = [];
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;
    public string ExpanderText => IsLoading ? "…" : IsExpanded ? "⌄" : "›";
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpanderText));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ExpanderText));

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (IsLoading) return;
        IsExpanded = !IsExpanded;
        if (!IsExpanded || _loaded) return;
        IsLoading = true;
        try
        {
            var entries = await load(Path);
            Children.Clear();
            // 链接不自动展开，避免目录树循环；仍可通过文件列表打开目录链接。
            foreach (var entry in entries.Where(e => e.IsDirectory && !e.IsSymbolicLink && !e.Name.StartsWith('.')).OrderBy(e => e.Name))
                Children.Add(new(entry.Name, entry.FullPath, load, navigate, reportError));
            _loaded = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { reportError(ex.Message); IsExpanded = false; }
        finally { IsLoading = false; }
    }
    [RelayCommand] private Task OpenAsync() => navigate(Path);
}
