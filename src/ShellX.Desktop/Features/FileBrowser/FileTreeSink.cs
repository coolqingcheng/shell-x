using Avalonia.Input;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>文件树节点依赖的宿主操作：远程树和本地树各实现一份。</summary>
public interface IFileTreeSink
{
    /// <summary>当前是否显示隐藏文件。</summary>
    bool ShowHiddenFiles { get; }
    /// <summary>当前的搜索关键字，为空表示不过滤。</summary>
    string SearchText { get; }
    /// <summary>是否接受本地文件拖入（远程树：上传到该目录）。</summary>
    bool AcceptsLocalDrop { get; }
    /// <summary>是否接受远程文件拖入（本地树：下载到该目录）。</summary>
    bool AcceptsRemoteDrop { get; }
    /// <summary>节点是否支持下载。</summary>
    bool CanDownload { get; }
    /// <summary>节点是否支持上传。</summary>
    bool CanUpload { get; }
    /// <summary>是否在每一行里显示下载按钮（右侧远程文件面板用）。</summary>
    bool ShowRowDownload { get; }
    /// <summary>是否用紧凑行：窄面板下省掉时间列，把空间让给文件名和操作按钮。</summary>
    bool CompactRows { get; }
    /// <summary>是否显示展开箭头：只有树形列表才需要，按层级浏览的面板不显示。</summary>
    bool ShowRowChevron { get; }
    /// <summary>是否按层级缩进：树形列表要缩进，按层级浏览的面板不缩进。</summary>
    bool ShowRowIndent { get; }
    /// <summary>把节点下载到本地：保存位置由宿主决定。</summary>
    Task DownloadItemAsync(FileTreeNodeViewModel node);

    /// <summary>选中节点：同步面板的当前条目，右键菜单据此工作。</summary>
    void Select(FileTreeNodeViewModel node, bool toggle = false, bool fromContextMenu = false);
    /// <summary>全选：Ctrl+A 选中当前可见的所有行。</summary>
    void SelectAll(IReadOnlyList<FileTreeNodeViewModel> nodes);
    /// <summary>节点展开或收起：面板负责增删展平行列表里的行。</summary>
    void NotifyExpanded(FileTreeNodeViewModel node, bool expanded);
    /// <summary>双击节点：目录进入该目录，文件直接打开。</summary>
    Task ActivateAsync(FileTreeNodeViewModel node);
    /// <summary>生成拖拽载荷，返回 null 表示该节点不能拖动。</summary>
    FileDragDrop.Payload? CreateDragPayload(FileTreeNodeViewModel node);
    /// <summary>把本地路径拖到该节点（上传到该目录）。</summary>
    Task DropLocalAsync(FileTreeNodeViewModel node, IReadOnlyList<string> paths);
    /// <summary>把远程条目拖到该节点（下载到该目录）。</summary>
    Task DropRemoteAsync(FileTreeNodeViewModel node, RemoteFileSelection selection);
    /// <summary>读取目录失败时上报。</summary>
    void ReportError(string message);
    /// <summary>判断这次拖放是否有效：连接没换、传输还在当前会话里。</summary>
    bool CanAcceptDrop(IDataTransfer data);
}