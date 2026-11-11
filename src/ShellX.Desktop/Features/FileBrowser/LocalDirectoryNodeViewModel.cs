using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>本地目录树节点：按需加载子目录，隐藏目录不显示。</summary>
public partial class LocalDirectoryNodeViewModel(string name, string path,
    Func<string, Task<IReadOnlyList<LocalEntry>>> load, Func<string, Task> navigate, Action<string> reportError)
    : DirectoryTreeNodeViewModel(name, path)
{
    private bool _loaded;
    private readonly Func<string, Task<IReadOnlyList<LocalEntry>>> _load = load;
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
            foreach (var entry in entries.Where(e => e.IsDirectory && !e.IsSymbolicLink && !e.IsHidden)
                                         .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                Children.Add(new LocalDirectoryNodeViewModel(entry.Name, entry.FullPath, _load, _navigate, _reportError));
            _loaded = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _reportError(ex.Message); IsExpanded = false; }
        finally { IsLoading = false; }
    }

    protected override Task NavigateAsync() => _navigate(Path);

    /// <summary>Windows 路径不区分大小写。</summary>
    protected override bool PathEquals(string left, string right)
        => string.Equals(left.TrimEnd('/', '\\'), right.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Windows 路径比较不区分大小写。</summary>
    protected override bool IsAncestor(string ancestor, string target)
    {
        var prefix = ancestor.TrimEnd('/', '\\');
        if (prefix.Length == 0) return true;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (target.Length == prefix.Length) return true;
        var next = target[prefix.Length];
        return next == '/' || next == '\\';
    }
}
