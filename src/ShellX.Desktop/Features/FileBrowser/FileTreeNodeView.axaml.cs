using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

    public FileTreeNodeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachNode();
        AttachedToVisualTree += (_, _) =>
        {
            if (!_pendingReveal) return;
            _pendingReveal = false;
            StartReveal();
        };
        // 拖放是附加事件，只能在代码里挂；每一行都是独立的放置目标。
        DragDrop.SetAllowDrop(Row, true);
        DragDrop.AddDragOverHandler(Row, RowDragOver);
        DragDrop.AddDragLeaveHandler(Row, RowDragLeave);
        DragDrop.AddDropHandler(Row, RowDrop);
    }

    private void AttachNode()
    {
        if (_node != null) _node.PropertyChanged -= NodePropertyChanged;
        _node = DataContext as FileTreeNodeViewModel;
        if (_node != null) _node.PropertyChanged += NodePropertyChanged;
        ApplyHeight();
    }

    private void NodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileTreeNodeViewModel.IsCollapsing) or nameof(FileTreeNodeViewModel.IsRevealing))
            ApplyHeight();
    }

    /// <summary>新展开的行从 0 长出来，收起的行缩回 0。</summary>
    private void ApplyHeight()
    {
        if (_node == null) return;
        if (_node.IsCollapsing) { Height = 0; return; }
        if (_node.IsRevealing)
        {
            SetHeightImmediate(0);
            // 过渡要在行挂到视觉树上之后启动，否则会直接跳到最终高度。
            if (!this.IsAttachedToVisualTree()) { _pendingReveal = true; return; }
            StartReveal();
            return;
        }
        SetHeightImmediate(FileTreeNodeViewModel.RowHeight);
    }

    /// <summary>播放出现动画：先压到 0，再在下一帧过渡到固定高度。</summary>
    private void StartReveal()
    {
        SetHeightImmediate(0);
        Dispatcher.UIThread.Post(() =>
        {
            if (_node == null) return;
            Height = FileTreeNodeViewModel.RowHeight;
        }, DispatcherPriority.Render);
    }

    /// <summary>直接改高度而不走过渡，避免自动高度参与插值导致动画失效。</summary>
    private void SetHeightImmediate(double value)
    {
        var transitions = Transitions;
        Transitions = null;
        Height = value;
        Transitions = transitions;
    }

    private void RowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_node == null) return;
        var point = e.GetCurrentPoint(Row);
        // 右键先选中节点，右键菜单才能作用到当前行；已选中时保留多选。
        if (point.Properties.IsRightButtonPressed)
        {
            if (!_node.IsSelected) _node.Sink.Select(_node);
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
        if (_node != null && !_node.IsSelected) _node.Sink.Select(_node);
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

    /// <summary>本地节点带上真实文件，拖到系统文件管理器可以直接落地。</summary>
    private async Task AttachLocalFileAsync(FileDragDrop.Payload payload)
    {
        if (_node is not LocalTreeNodeViewModel local) return;
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider == null) return;
        try
        {
            var item = await provider.TryGetFileFromPathAsync(local.Entry.FullPath);
            if (item != null) payload.Data.Add(DataTransferItem.CreateFile(item));
        }
        catch (Exception) { }
    }

    private void RowDragOver(object? sender, DragEventArgs e)
    {
        if (_node == null) { e.DragEffects = DragDropEffects.None; e.Handled = true; return; }
        var accepted = _node.CanAcceptDrop(e.DataTransfer) && e.DragEffects.HasFlag(DragDropEffects.Copy);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (_node.IsDropTarget != accepted) _node.IsDropTarget = accepted;
    }

    private void RowDragLeave(object? sender, DragEventArgs e)
    {
        if (_node != null) _node.IsDropTarget = false;
    }

    private async void RowDrop(object? sender, DragEventArgs e)
    {
        if (_node == null) return;
        e.Handled = true;
        _node.IsDropTarget = false;
        if (!_node.CanAcceptDrop(e.DataTransfer)) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Copy;
        try { await _node.HandleDropAsync(e.DataTransfer); }
        catch (Exception ex) { _node.Sink.ReportError(ex.Message); }
    }
}
