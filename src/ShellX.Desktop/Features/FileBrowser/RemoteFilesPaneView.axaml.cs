using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>主窗口右侧的远程文件面板：按层级浏览，点目录进入下一层，行内可下载，本地文件拖进来即上传。</summary>
public partial class RemoteFilesPaneView : UserControl
{
    public RemoteFilesPaneView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(DropArea, true);
        DragDrop.AddDragOverHandler(DropArea, DropAreaDragOver);
        DragDrop.AddDragLeaveHandler(DropArea, DropAreaDragLeave);
        DragDrop.AddDropHandler(DropArea, DropAreaDrop);
    }

    private RemoteFilesPaneViewModel? ViewModel => DataContext as RemoteFilesPaneViewModel;

    /// <summary>拖到空白处：整个面板高亮成可放置的样子。</summary>
    private void DropAreaDragOver(object? sender, DragEventArgs e)
    {
        var accepted = ViewModel?.CanAcceptDrop(e.DataTransfer) == true;
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropAreaDragLeave(object? sender, DragEventArgs e) => e.Handled = true;

    /// <summary>落在空白处：上传到当前目录。</summary>
    private async void DropAreaDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var vm = ViewModel; if (vm == null) return;
        var paths = FileDragDrop.ReadLocalPaths(e.DataTransfer) ?? FileDragDrop.ReadFilePaths(e.DataTransfer);
        if (paths is not { Length: > 0 }) return;
        await vm.UploadDroppedAsync(paths);
    }

    /// <summary>面包屑跳转：点哪一段就进入哪一层，也可以直接改成路径。</summary>
    private async void PathNavigate(object? sender, string path)
    {
        var vm = ViewModel; if (vm == null) return;
        await vm.NavigateAsync(path);
    }

    /// <summary>右键下载：选本地保存目录后写入。</summary>
    private async void DownloadClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel; if (vm?.Selected is { } node) await vm.DownloadItemAsync(node);
    }

    /// <summary>复制选中项的远程路径。</summary>
    private async void CopyPathClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;
        await ClipboardHelper.CopyAsync(this, vm.SelectedPath);
    }
}
