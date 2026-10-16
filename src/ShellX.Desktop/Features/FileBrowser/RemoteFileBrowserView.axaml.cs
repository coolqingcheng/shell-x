using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.Shell;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class RemoteFileBrowserView : UserControl
{
    private MainWindowViewModel? _layoutVm;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(RemoteEntry))]
    public RemoteFileBrowserView()
    {
        InitializeComponent();
        FileGridInteraction.SelectOnRightClick(FileGrid); FileGridInteraction.EnableColumnSizing(FileGrid); FileDragDrop.Attach(this, FileGrid, true);
        DataContextChanged += (_, _) => AttachLayout();
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
    private void DetachLayout()
    {
        if (_layoutVm != null) _layoutVm.LayoutChanged -= ApplyLayout;
        _layoutVm = null;
    }
    private void ApplyLayout(AppSettings settings) => RemoteTree.Width = Math.Clamp(settings.RemoteTreeWidth, 100, 320);
    private async void RemoteTreeSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (_layoutVm == null) return;
        await _layoutVm.SaveRemoteTreeWidthAsync(RemoteTree.Bounds.Width > 0 ? RemoteTree.Bounds.Width : RemoteTree.Width);
    }
    private async void FileDoubleTapped(object? sender, TappedEventArgs e)
    { if (FileGridInteraction.IsFileRow(e.Source) && DataContext is FileBrowserViewModel vm) await vm.OpenEntryAsync(vm.SelectedEntry); }
    private async void AddressKeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && DataContext is FileBrowserViewModel vm) { e.Handled = true; await vm.GoCommand.ExecuteAsync(null); } }
    private async void UsePathHistory(object? sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string path } && DataContext is FileBrowserViewModel vm) { PathHistoryButton.Flyout?.Hide(); await vm.NavigateAsync(path); } }
    private void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not FileBrowserViewModel vm) return;
        vm.SelectedEntries.Clear(); vm.SelectedEntries.AddRange(FileGrid.SelectedItems.OfType<RemoteEntry>());
        vm.NotifySelectionChanged();
    }
}
