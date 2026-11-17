using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>文件树的一行：显示节点、处理展开收起动画、拖拽上传下载和双击打开。</summary>
public partial class FileTreeNodeView : UserControl
{
    private FileTreeNodeViewModel? _node;
    private PointerPressedEventArgs? _pressed;
    private Point _origin;
    private bool _dragging;
    /// <summary>行还没挂到视觉树上，等挂上之后再播出现动画。</summary>
    private bool _pendingReveal;
    /// <summary>出现动画正在播，播完之前不再重复触发。</summary>
    private bool _revealRunning;

    public FileTreeNodeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachNode();
        AttachedToVisualTree += (_, _) =>
        {
            if (!_pendingReveal) return;
            _pendingReveal = false;
            _ = StartRevealAsync();
        };
    }

    /// <summary>位移变换：出现动画靠它上滑，属于渲染属性，不触发布局。</summary>
    private TranslateTransform? Slide => RenderTransform as TranslateTransform;

    private void AttachNode()
    {
        if (_node != null) _node.PropertyChanged -= NodePropertyChanged;
        _node = DataContext as FileTreeNodeViewModel;
        if (_node != null) _node.PropertyChanged += NodePropertyChanged;
        ApplyVisualState();
    }

    private void NodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileTreeNodeViewModel.IsCollapsing) or nameof(FileTreeNodeViewModel.IsRevealing))
            ApplyVisualState();
    }

    /// <summary>按节点状态切换动画：出现时淡入上滑，收起时淡出上移。</summary>
    private void ApplyVisualState()
    {
        if (_node == null) return;
        if (_node.IsCollapsing) { StartCollapse(); return; }
        if (_node.IsRevealing)
        {
            // 过渡要在行挂到视觉树上之后启动，否则会直接跳到最终状态。
            if (!this.IsAttachedToVisualTree()) { _pendingReveal = true; return; }
            _ = StartRevealAsync();
            return;
        }
        Opacity = 1;
        if (Slide is { } slide) slide.Y = 0;
    }

    /// <summary>
    /// 出现动画：先藏到起点，再按顺序错峰淡入上滑。
    /// 只改透明度和位移，行高始终固定，虚拟列表不需要重新测量。
    /// </summary>
    private async Task StartRevealAsync()
    {
        // 已经在播就不要再起一次，否则行被复用时会不停地闪。
        if (_revealRunning) return;
        _revealRunning = true;
        try
        {
            var node = _node; if (node == null) return;
            SetVisualImmediate(0, node.RevealSlide ? -FileTreeNodeViewModel.RowHeight : 0);
            var delay = Math.Clamp(node.RevealOrder, 0, 16) * 14;
            if (delay > 0) await Task.Delay(delay);
            if (_node == null || !_node.IsRevealing || _node.IsCollapsing) return;
            Opacity = 1;
            if (Slide is { } slide) slide.Y = 0;
        }
        finally { _revealRunning = false; }
    }

    /// <summary>收起动画：淡出并轻微上移，动画结束后由面板移除这一行。</summary>
    private void StartCollapse()
    {
        Opacity = 0;
        if (Slide is { } slide) slide.Y = -8;
    }

    /// <summary>直接改渲染属性而不走过渡，避免起点被反向动画掉。</summary>
    private void SetVisualImmediate(double opacity, double offset)
    {
        var transitions = Transitions;
        var slide = Slide;
        var slideTransitions = slide?.Transitions;
        Transitions = null;
        if (slide != null) slide.Transitions = null;
        Opacity = opacity;
        if (slide != null) slide.Y = offset;
        Transitions = transitions;
        if (slide != null) slide.Transitions = slideTransitions;
    }

    private void RowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_node == null) return;
        var point = e.GetCurrentPoint(Row);
        // 右键先选中节点，右键菜单才能作用到当前行；已选中时保留多选。
        if (point.Properties.IsRightButtonPressed)
        {
            if (!_node.IsSelected) _node.Sink.Select(_node, fromContextMenu: true);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (IsChevron(e.Source)) { _pressed = null; return; }
        // Ctrl 点击是多选，普通点击是单选。
        var toggle = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        _node.Sink.Select(_node, toggle);
        if (e.ClickCount >= 2) { _pressed = null; _ = ActivateAsync(); return; }
        _pressed = e;
        _origin = e.GetPosition(Row);
        _dragging = false;
    }

    private void RowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging || _pressed == null || _node == null) return;
        if (!e.GetCurrentPoint(Row).Properties.IsLeftButtonPressed) { _pressed = null; return; }
        var delta = e.GetPosition(Row) - _origin;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
        var trigger = _pressed; _pressed = null; _dragging = true;
        _ = StartDragAsync(trigger);
    }

    private void RowPointerReleased(object? sender, PointerReleasedEventArgs e) => _pressed = null;

    private void RowRightTapped(object? sender, TappedEventArgs e)
    {
        if (_node != null && !_node.IsSelected) _node.Sink.Select(_node, fromContextMenu: true);
    }

    private static bool IsChevron(object? source)
        => source is Visual visual && visual.FindAncestorOfType<Button>() is { Name: "Chevron" };

    private async Task ActivateAsync()
    {
        if (_node == null) return;
        try { await _node.OpenCommand.ExecuteAsync(null); }
        catch (Exception ex) { _node.Sink.ReportError(ex.Message); }
    }

    private async Task StartDragAsync(PointerPressedEventArgs trigger)
    {
        if (_node == null) return;
        FileDragDrop.Payload? payload = null;
        try
        {
            payload = _node.Sink.CreateDragPayload(_node);
            if (payload == null) return;
            await AttachLocalFileAsync(payload);
            trigger.Pointer.Capture(null);
            await DragDrop.DoDragDropAsync(trigger, payload.Data, DragDropEffects.Copy);
        }
        catch (Exception ex) { _node.Sink.ReportError(ex.Message); }
        finally { payload?.Dispose(); _dragging = false; }
    }

    /// <summary>拖出去时带上真实文件：本地节点直接给路径，远程条目先导出到本地缓存。</summary>
    private async Task AttachLocalFileAsync(FileDragDrop.Payload payload)
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider == null) return;
        string? path = _node is LocalTreeNodeViewModel local ? local.Entry.FullPath : null;
        if (path == null && payload.ExportLocalPath is { } export)
        {
            try { path = await export(); }
            catch (Exception) { path = null; }
        }
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            IStorageItem? item = Directory.Exists(path)
                ? await provider.TryGetFolderFromPathAsync(path)
                : await provider.TryGetFileFromPathAsync(path);
            if (item != null) payload.Data.Add(DataTransferItem.CreateFile(item));
        }
        catch (Exception) { }
    }

}
