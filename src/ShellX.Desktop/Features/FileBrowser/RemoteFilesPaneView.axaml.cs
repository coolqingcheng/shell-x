using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using ShellX.Desktop.Features.Logs;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>主窗口右侧的远程文件面板：按层级浏览，点目录进入下一层，行内可下载，本地文件拖进来即上传。</summary>
public partial class RemoteFilesPaneView : UserControl
{
    /// <summary>当前绑定的视图模型，用来接“查看日志”的事件。</summary>
    private RemoteFilesPaneViewModel? _attached;

    public RemoteFilesPaneView()
    {
        InitializeComponent();
        // 整块面板就是一个放置区：AllowDrop 往下继承，
        // 拖到文件行、文件夹行还是空白处都一样，统一上传到当前目录。
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragEnterHandler(this, PanelDragOver);
        DragDrop.AddDragOverHandler(this, PanelDragOver);
        DragDrop.AddDragLeaveHandler(this, PanelDragLeave);
        DragDrop.AddDropHandler(this, PanelDrop);
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private RemoteFilesPaneViewModel? ViewModel => DataContext as RemoteFilesPaneViewModel;

    /// <summary>换绑视图模型时重新挂事件，避免重复订阅。</summary>
    private void AttachViewModel()
    {
        if (_attached != null) _attached.LogRequested -= OpenLogViewer;
        _attached = ViewModel;
        if (_attached != null) _attached.LogRequested += OpenLogViewer;
    }

    /// <summary>在独立的日志窗口里查看远程日志文件：同一个文件重复打开只激活已有窗口。</summary>
    private void OpenLogViewer(string path)
    {
        var vm = _attached;
        if (vm?.LogReader == null) return;
        if (LogViewerWindow.TryActivate(vm.LogReader, path)) return;
        var viewModel = new LogViewerViewModel(vm.LogReader, path, $"{vm.LogTarget} · {path}");
        var window = new LogViewerWindow(viewModel, vm.Session);
        vm.Dialogs.TrackOwnerWindow(window);
        window.Closed += (_, _) => vm.Dialogs.UntrackOwnerWindow(window);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }

    /// <summary>拖入悬停：整块面板高亮成可放置的样子，落点不再决定目标目录。</summary>
    private void PanelDragOver(object? sender, DragEventArgs e)
    {
        var vm = ViewModel;
        var accepted = vm?.CanAcceptDrop(e.DataTransfer) == true;
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (vm != null && vm.IsDropTarget != accepted) vm.IsDropTarget = accepted;
    }

    /// <summary>拖出面板：取消高亮。指针还在面板内只是换了个子控件时不取消。</summary>
    private void PanelDragLeave(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var vm = ViewModel; if (vm == null) return;
        if (IsPointerInside(e)) return;
        vm.IsDropTarget = false;
    }

    /// <summary>落在面板任意位置：上传到当前目录。</summary>
    private async void PanelDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var vm = ViewModel; if (vm == null) return;
        vm.IsDropTarget = false;
        if (!vm.CanAcceptDrop(e.DataTransfer)) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Copy;
        var paths = FileDragDrop.ReadLocalPaths(e.DataTransfer) ?? FileDragDrop.ReadFilePaths(e.DataTransfer);
        if (paths is not { Length: > 0 }) return;
        await vm.UploadDroppedAsync(paths);
    }

    /// <summary>拖放位置是否还在面板范围内。</summary>
    private bool IsPointerInside(DragEventArgs e)
    {
        var position = e.GetPosition(this);
        return new Rect(this.Bounds.Size).Contains(position);
    }

    /// <summary>面包屑跳转：点哪一段就进入哪一层，也可以直接改成路径。</summary>
    private async void PathNavigate(object? sender, string path)
    {
        var vm = ViewModel; if (vm == null) return;
        await vm.NavigateAsync(path);
    }

    /// <summary>复制选中项的远程路径。</summary>
    private async void CopyPathClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;
        await ClipboardHelper.CopyAsync(this, vm.SelectedPath);
    }
}
