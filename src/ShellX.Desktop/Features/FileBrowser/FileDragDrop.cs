using Avalonia.Input;
using Avalonia.Platform.Storage;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

// 原生拖放只携带短期令牌；远端会话和选择保留在进程内，不把远端路径伪装成本地文件。
public static class FileDragDrop
{
    private static readonly DataFormat<string> SelectionFormat = DataFormat.CreateStringApplicationFormat("ShellX.FileSelection");
    private static readonly Dictionary<string, Selection> ActiveSelections = [];
    private sealed record Selection(FileBrowserViewModel Owner, int SessionVersion, string[]? LocalPaths, RemoteFileSelection? Remote);

    public sealed class Payload : IDisposable
    {
        private readonly string _id = Guid.NewGuid().ToString("N");
        public DataTransfer Data { get; } = new();
        internal Payload(FileBrowserViewModel? owner, string[]? paths, RemoteFileSelection? remote,
            Func<Task<string?>>? exportLocalPath = null)
        {
            // 导出型载荷没有宿主进程内选择，拖出去时只用到本地路径。
            if (owner != null) ActiveSelections.Add(_id, new(owner, owner.SessionVersion, paths, remote));
            ExportLocalPath = exportLocalPath;
            Data.Add(DataTransferItem.Create(SelectionFormat, _id));
        }
        /// <summary>导出成本地文件的回调：拖到系统文件管理器前先调用，返回本地路径。</summary>
        public Func<Task<string?>>? ExportLocalPath { get; }
        public void Dispose() => ActiveSelections.Remove(_id);
    }

    public static Payload LocalPayload(FileBrowserViewModel owner, IEnumerable<string> paths) => new(owner, paths.ToArray(), null);
    public static Payload RemotePayload(RemoteFileSelection selection) => new(selection.Owner, null, selection);
    /// <summary>导出型载荷：拖到系统文件管理器时先把远程内容落到本地。</summary>
    public static Payload ExportPayload(Func<Task<string?>> export) => new(null, null, null, export);

    /// <summary>读取进程内的本地路径载荷（本地树节点拖到远程树上。。</summary>
    public static string[]? ReadLocalPaths(IDataTransfer data) => Read(data)?.LocalPaths;

    /// <summary>读取进程内的远程条目载荷（远程树节点拖到本地树上）。</summary>
    public static RemoteFileSelection? ReadRemoteSelection(IDataTransfer data) => Read(data)?.Remote;

    /// <summary>读取系统文件管理器拖进来的本地路径。</summary>
    public static string[]? ReadFilePaths(IDataTransfer data)
    {
        if (!data.Contains(DataFormat.File)) return null;
        var items = data.TryGetFiles();
        if (items == null) return null;
        var paths = items.Select(x => x.TryGetLocalPath()).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return paths.Length > 0 ? paths : null;
    }

    /// <summary>是否是从本应用内部拖起来的：载荷里带着本应用的选择令牌。</summary>
    /// <remarks>系统文件管理器拖进来的文件没有这个令牌；应用内部往外拖时即便挂了真实文件也带着它。</remarks>
    public static bool IsInternalDrag(IDataTransfer data) => data.Contains(SelectionFormat);

    private static Selection? Read(IDataTransfer data)
        => data.TryGetValue(SelectionFormat) is { } id ? ActiveSelections.GetValueOrDefault(id) : null;

    /// <summary>判断这次拖放是否落在当前会话里：切换连接后旧的拖动立即失效。</summary>
    public static bool CanDrop(FileBrowserViewModel browser, bool remote, IDataTransfer data)
    {
        if (!browser.IsAvailable) return false;
        var selection = Read(data);
        if (selection != null)
            return ReferenceEquals(selection.Owner, browser) && selection.SessionVersion == browser.SessionVersion && (remote ? selection.LocalPaths is { Length: > 0 }
                : browser.Local.Path.Length > 0 && selection.Remote is { IsValid: true });
        return remote && !data.Contains(SelectionFormat) && data.Contains(DataFormat.File);
    }
}
