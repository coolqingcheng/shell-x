using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>本地文件树节点：目录可以展开加载子项，文件是叶子。</summary>
public sealed class LocalTreeNodeViewModel : FileTreeNodeViewModel
{
    private readonly ILocalFileSystem files;
    private readonly CancellationToken token;

    /// <summary>对应的本地条目。</summary>
    public LocalEntry Entry { get; }

    public LocalTreeNodeViewModel(LocalEntry entry, ILocalFileSystem files, CancellationToken token, IFileTreeSink sink)
        : base(sink, entry.Name, entry.FullPath, entry.IsDirectory, entry.Icon, entry.SizeText, entry.ModifiedText)
    {
        Entry = entry;
        this.files = files;
        this.token = token;
        // 条目进入删除状态时，行上要同步显示加载指示。
        entry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalEntry.IsDeleting)) OnPropertyChanged(nameof(IsDeleting));
        };
    }

    /// <summary>符号链接不展开，避免目录循环。</summary>
    private bool Expandable => IsDirectory && !Entry.IsSymbolicLink;

    protected override async Task LoadChildrenAsync()
    {
        if (Loaded || !Expandable) return;
        Loaded = true;
        IsLoading = true;
        try
        {
            var directory = await files.ListAsync(Path, token);
            var showHidden = Sink.ShowHiddenFiles;
            var items = directory.Entries
                .Where(e => showHidden || !e.IsHidden)
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Children.Clear();
            foreach (var item in items)
            {
                var child = new LocalTreeNodeViewModel(item, files, token, Sink);
                child.Parent = this;
                child.Level = Level + 1;
                Children.Add(child);
            }
            UpdateVisibility();
        }
        catch (OperationCanceledException) { Loaded = false; }
        catch (Exception ex)
        {
            Loaded = false;
            Sink.ReportError("目录读取失败：" + ex.Message);
        }
        finally { IsLoading = false; }
    }

    protected override Task ActivateAsync() => Sink.ActivateAsync(this);

    /// <summary>符号链接用链接样式的图标。</summary>
    public override bool IsLink => Entry.IsSymbolicLink;
    /// <summary>删除中：行上显示加载指示。</summary>
    public override bool IsDeleting => Entry.IsDeleting;
}