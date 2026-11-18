using System.ComponentModel;
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
    /// <summary>要恢复的滚动位置：切回标签时按它把列表拉回原处。</summary>
    private double _pendingScroll;
    /// <summary>是否等着恢复滚动位置：切换标签时为真；普通刷新不动滚动位置。</summary>
    private bool _restoreRequested;
    /// <summary>恢复的轮次：新一轮开始时作废上一轮，免得两个循环抢着设偏移。</summary>
    private int _restoreGeneration;

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
        TreeList.ScrollChanged += OnScrollChanged;
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private RemoteFilesPaneViewModel? ViewModel => DataContext as RemoteFilesPaneViewModel;

    /// <summary>换绑视图模型时重新挂事件，避免重复订阅。</summary>
    private void AttachViewModel()
    {
        if (_attached != null)
        {
            _attached.LogRequested -= OpenLogViewer;
            _attached.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _attached = ViewModel;
        if (_attached != null)
        {
            _attached.LogRequested += OpenLogViewer;
            _attached.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>列表换掉了：可能是切换标签（要恢复滚动位置），也可能只是刷新（不动位置）。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RemoteFilesPaneViewModel.Entries)) return;
        var vm = _attached; if (vm == null) return;
        // 待恢复的位置只取一次：取走后清掉，列表第二次变化（真正加载出来）不会把它冲掉。
        if (vm.PendingScrollOffset >= 0)
        {
            _pendingScroll = vm.PendingScrollOffset;
            vm.PendingScrollOffset = -1;
            _restoreRequested = true;
        }
        if (!_restoreRequested) return;
        // 列表又换了一次（内容加载出来了）：重新开始等布局排好。
        _restoreGeneration++;
        _ = RestoreScrollAsync(_restoreGeneration);
    }

    /// <summary>等列表排好版，再把滚动位置设回去。</summary>
    /// <remarks>列表刚换掉时高度还没算出来，这时设偏移会被裁成 0，得等布局跑完。</remarks>
    private async Task RestoreScrollAsync(int generation)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (generation != _restoreGeneration) return;      // 已经有新一轮在跑了
            if (ApplyPendingScroll()) return;
            await Task.Delay(16);
        }
        // 实在等不到（比如目录内容本来就比窗口矮），就按能滚的范围落位。
        if (generation == _restoreGeneration) ApplyPendingScroll(force: true);
    }

    /// <summary>把滚动位置设回列表：内容还没排好就返回 false，等下一轮再试。</summary>
    private bool ApplyPendingScroll(bool force = false)
    {
        var vm = _attached; if (vm == null) return true;
        if (vm.Entries.Count == 0)
        {
            if (!force) return false;
            _pendingScroll = 0; _restoreRequested = false;
            return true;
        }
        var max = Math.Max(0, TreeList.Extent.Height - TreeList.Viewport.Height);
        var target = Math.Min(Math.Max(0, _pendingScroll), max);
        TreeList.Offset = new Vector(0, target);
        vm.ScrollOffset = target;
        // 能滚的范围够得着目标位置，说明内容已经排好，收工。
        if (force || max >= _pendingScroll) { _pendingScroll = 0; _restoreRequested = false; return true; }
        return false;
    }

    /// <summary>记录滚动位置：切走标签时按它恢复。恢复过程中不记，免得把 0 当成当前位置。</summary>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var vm = _attached; if (vm == null) return;
        if (_restoreRequested) return;
        vm.ScrollOffset = TreeList.Offset.Y;
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
