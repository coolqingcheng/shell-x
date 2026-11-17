using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class LocalFileBrowserView : UserControl
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(LocalEntry))]
    public LocalFileBrowserView()
    {
        InitializeComponent();
        AddHandler(InputElement.KeyDownEvent, SelectAllRows, RoutingStrategies.Tunnel);
        // 整棵树是一个放置区：落在目录行就下载到那个目录，落在空白处就下载到当前目录。
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragEnterHandler(this, TreeDragOver);
        DragDrop.AddDragOverHandler(this, TreeDragOver);
        DragDrop.AddDropHandler(this, TreeDrop);
    }

    /// <summary>拖入悬停：这次拖放能不能接收。</summary>
    private void TreeDragOver(object? sender, DragEventArgs e)
    {
        var accepted = (DataContext as LocalFileBrowserViewModel)?.CanAcceptDrop(e.DataTransfer) == true;
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>落在目录行就下载到那个目录，落在空白处就下载到当前目录。</summary>
    private async void TreeDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not LocalFileBrowserViewModel vm) return;
        if (!vm.CanAcceptDrop(e.DataTransfer)) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Copy;
        var node = FileTreeDropTarget.Find(TreeList, e.GetPosition(TreeList));
        if (node != null && node.CanAcceptDrop(e.DataTransfer)) { await node.HandleDropAsync(e.DataTransfer); return; }
        if (FileDragDrop.ReadRemoteSelection(e.DataTransfer) is { } remote && vm.Browser != null)
            await vm.Browser.DownloadEntriesAsync(remote, vm.Path);
    }

    private async void ViewLoaded(object? sender, RoutedEventArgs e)
    { if (DataContext is LocalFileBrowserViewModel vm) await vm.InitializeAsync(); }
    private async void PathNavigate(object? sender, string path)
    { if (DataContext is LocalFileBrowserViewModel vm) await vm.NavigateAsync(path); }
    private async void UsePathHistory(object? sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string path } && DataContext is LocalFileBrowserViewModel vm) { PathHistoryButton.Flyout?.Hide(); await vm.NavigateAsync(path); } }
    private async void OpenRoot(object? sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string path } && DataContext is LocalFileBrowserViewModel vm) { RootsButton.Flyout?.Hide(); await vm.NavigateAsync(path); } }

    /// <summary>右键“复制路径”：把选中节点的完整路径放进剪贴板。</summary>
    private async void CopyPathClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LocalFileBrowserViewModel vm) return;
        await ClipboardHelper.CopyAsync(this, vm.SelectedPath);
    }

    /// <summary>Ctrl+A 选中树里当前可见的所有行。</summary>
    private void SelectAllRows(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (DataContext is LocalFileBrowserViewModel vm) vm.SelectAll(vm.FlatNodes.ToArray());
        e.Handled = true;
    }
}
