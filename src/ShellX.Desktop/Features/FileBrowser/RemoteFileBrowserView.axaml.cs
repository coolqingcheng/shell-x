using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Features.Logs;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class RemoteFileBrowserView : UserControl
{
    private MainWindowViewModel? _layoutVm;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(RemoteEntry))]
    public RemoteFileBrowserView()
    {
        InitializeComponent();
        FileGridInteraction.SelectOnRightClick(FileGrid); FileGridInteraction.EnableColumnSizing(FileGrid); FileDragDrop.Attach(this, FileGrid, true);
        FileGrid.AddHandler(InputElement.KeyDownEvent, SelectAllFiles, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => { AttachLayout(); AttachFiles(); };
        AttachedToVisualTree += (_, _) => AttachLayout();
        DetachedFromVisualTree += (_, _) => DetachLayout();
    }
    private void AttachLayout()
    {
        var vm = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
        if (ReferenceEquals(_layoutVm, vm)) return;
        DetachLayout();
        _layoutVm = vm;
        if (_layoutVm == null) return;
        _layoutVm.LayoutChanged += ApplyLayout;
        ApplyLayout(_layoutVm.LayoutSettings);
    }
    private FileBrowserViewModel? _files;

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

    private void DetachLayout()
    {
        if (_layoutVm != null) _layoutVm.LayoutChanged -= ApplyLayout;
        _layoutVm = null;
        if (_files != null) _files.LogRequested -= OpenLogViewer;
        _files = null;
    }
    private void ApplyLayout(AppSettings settings) => RemoteTree.Width = Math.Clamp(settings.RemoteTreeWidth, 100, 320);
    private async void RemoteTreeSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (_layoutVm == null) return;
        await _layoutVm.SaveRemoteTreeWidthAsync(RemoteTree.Bounds.Width > 0 ? RemoteTree.Bounds.Width : RemoteTree.Width);
    }
    private async void FileDoubleTapped(object? sender, TappedEventArgs e)
    { if (FileGridInteraction.IsFileRow(e.Source) && DataContext is FileBrowserViewModel vm) await vm.OpenEntryAsync(vm.SelectedEntry); }
    private async void PathNavigate(object? sender, string path)
    { if (DataContext is FileBrowserViewModel vm) await vm.NavigateAsync(path); }
    private async void UsePathHistory(object? sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string path } && DataContext is FileBrowserViewModel vm) { PathHistoryButton.Flyout?.Hide(); await vm.NavigateAsync(path); } }
    private void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not FileBrowserViewModel vm) return;
        vm.SelectedEntries.Clear(); vm.SelectedEntries.AddRange(FileGrid.SelectedItems.OfType<RemoteEntry>());
        vm.NotifySelectionChanged();
    }
    /// <summary>Ctrl+A 全选远程列表。</summary>
    private void SelectAllFiles(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        FileGrid.SelectionChanged -= SelectionChanged;
        try { FileGridInteraction.SelectAll(FileGrid); }
        finally { FileGrid.SelectionChanged += SelectionChanged; }
        if (DataContext is FileBrowserViewModel vm)
        {
            vm.SelectedEntries.Clear(); vm.SelectedEntries.AddRange(FileGrid.SelectedItems.OfType<RemoteEntry>());
            vm.NotifySelectionChanged();
        }
        e.Handled = true;
    }
}
