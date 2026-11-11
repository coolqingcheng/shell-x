using CommunityToolkit.Mvvm.ComponentModel;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>远程目录树节点：按需加载子目录，符号链接不自动展开。</summary>
public partial class DirectoryNodeViewModel(string name, string path,
    Func<string, Task<IReadOnlyList<RemoteEntry>>> load, Func<string, Task> navigate, Action<string> reportError)
    : DirectoryTreeNodeViewModel(name, path)
{
    private bool _loaded;
    private readonly Func<string, Task<IReadOnlyList<RemoteEntry>>> _load = load;
    private readonly Func<string, Task> _navigate = navigate;
    private readonly Action<string> _reportError = reportError;

    protected override async Task LoadChildrenAsync()
    {
        if (_loaded || IsLoading) return;
        IsLoading = true;
        try
        {
            var entries = await _load(Path);
            Children.Clear();
            // 链接不自动展开，避免目录树循环；仍可通过文件列表打开目录链接。
            foreach (var entry in entries.Where(e => e.IsDirectory && !e.IsSymbolicLink && !e.Name.StartsWith('.'))
                                         .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                Children.Add(new DirectoryNodeViewModel(entry.Name, entry.FullPath, _load, _navigate, _reportError));
            _loaded = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _reportError(ex.Message); IsExpanded = false; }
        finally { IsLoading = false; }
    }

    protected override Task NavigateAsync() => _navigate(Path);
}
