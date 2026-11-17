using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Files;
using ShellX.Desktop.Features.Logs;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class RemoteFileBrowserView : UserControl
{
    private FileBrowserViewModel? _files;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(RemoteEntry))]
    public RemoteFileBrowserView()
    {
        InitializeComponent();
        AddHandler(InputElement.KeyDownEvent, SelectAllRows, RoutingStrategies.Tunnel);
        // 整棵树是一个放置区：落在目录行就放进那个目录，落在空白处就放进当前目录。
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragEnterHandler(this, TreeDragOver);
        DragDrop.AddDragOverHandler(this, TreeDragOver);
        DragDrop.AddDropHandler(this, TreeDrop);
        DataContextChanged += (_, _) => AttachFiles();
    }

    /// <summary>拖入悬停：这次拖放能不能接收。</summary>
    private void TreeDragOver(object? sender, DragEventArgs e)
    {
        var accepted = (DataContext as FileBrowserViewModel)?.CanAcceptDrop(e.DataTransfer) == true;
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>落在目录行就上传到那个目录，落在空白处就上传到当前目录。</summary>
    private async void TreeDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not FileBrowserViewModel vm) return;
        if (!vm.CanAcceptDrop(e.DataTransfer)) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Copy;
        var node = FileTreeDropTarget.Find(TreeList, e.GetPosition(TreeList));
        if (node != null && node.CanAcceptDrop(e.DataTransfer)) { await node.HandleDropAsync(e.DataTransfer); return; }
        var paths = FileDragDrop.ReadLocalPaths(e.DataTransfer) ?? FileDragDrop.ReadFilePaths(e.DataTransfer);
        if (paths is not { Length: > 0 }) return;
        await vm.UploadPathsAsync(paths);
    }

    /// <summary>远程文件面板的日志入口：在独立窗口里打开选中的日志文件。</summary>
    private void AttachFiles()
    {
        if (_files != null) _files.LogRequested -= OpenLogViewer;
        _files = DataContext as FileBrowserViewModel;
        if (_files != null) _files.LogRequested += OpenLogViewer;
    }

    private void OpenLogViewer(string path)
    {
        var vm = _files;
        if (vm?.LogReader == null) return;
        // 同一个文件重复打开时只激活已有窗口。
        if (LogViewerWindow.TryActivate(vm.LogReader, path)) return;
        var viewModel = new LogViewerViewModel(vm.LogReader, path, $"{vm.LogTarget} · {path}");
        var window = new LogViewerWindow(viewModel, vm.Session);
        vm.Dialogs.TrackOwnerWindow(window);
        window.Closed += (_, _) => vm.Dialogs.UntrackOwnerWindow(window);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }

    private async void PathNavigate(object? sender, string path)
    { if (DataContext is FileBrowserViewModel vm) await vm.NavigateAsync(path); }
    private async void UsePathHistory(object? sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string path } && DataContext is FileBrowserViewModel vm) { PathHistoryButton.Flyout?.Hide(); await vm.NavigateAsync(path); } }

    /// <summary>右键“复制路径”：把选中节点的完整路径放进剪贴板。</summary>
    private async void CopyPathClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileBrowserViewModel vm) return;
        await ClipboardHelper.CopyAsync(this, vm.SelectedPath);
    }

    /// <summary>Ctrl+A 选中树里当前可见的所有行。</summary>
    private void SelectAllRows(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (DataContext is FileBrowserViewModel vm) vm.SelectAll(vm.FlatNodes.ToArray());
        e.Handled = true;
    }
}
