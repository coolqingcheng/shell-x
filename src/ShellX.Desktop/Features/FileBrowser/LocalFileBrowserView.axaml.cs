using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.Shell;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class LocalFileBrowserView : UserControl
{
    private MainWindowViewModel? _layoutVm;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(LocalEntry))]
    public LocalFileBrowserView()
    {
        InitializeComponent();
        FileGridInteraction.SelectOnRightClick(LocalGrid); FileGridInteraction.EnableColumnSizing(LocalGrid); FileDragDrop.Attach(this, LocalGrid, false);
        LocalGrid.AddHandler(InputElement.KeyDownEvent, SelectAllFiles, RoutingStrategies.Tunnel);
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

    private void ApplyLayout(AppSettings settings) => LocalTree.Width = Math.Clamp(settings.LocalTreeWidth, 100, 320);

    private async void LocalTreeSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (_layoutVm == null) return;
        await _layoutVm.SaveLocalTreeWidthAsync(LocalTree.Bounds.Width > 0 ? LocalTree.Bounds.Width : LocalTree.Width);
    }
    /// <summary>Ctrl+A 全选本地列表。</summary>
    private void SelectAllFiles(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        LocalGrid.SelectionChanged -= SelectionChanged;
        try { FileGridInteraction.SelectAll(LocalGrid); }
        finally { LocalGrid.SelectionChanged += SelectionChanged; }
        if (DataContext is LocalFileBrowserViewModel vm)
        {
            vm.SelectedEntries.Clear(); vm.SelectedEntries.AddRange(LocalGrid.SelectedItems.OfType<LocalEntry>());
            vm.NotifySelectionChanged();
        }
        e.Handled = true;
    }
    private async void ViewLoaded(object? sender, RoutedEventArgs e)
    { if (DataContext is LocalFileBrowserViewModel vm) await vm.InitializeAsync(); }
    private async void FileDoubleTapped(object? sender, TappedEventArgs e)
    { if (FileGridInteraction.IsFileRow(e.Source) && DataContext is LocalFileBrowserViewModel vm) await vm.OpenEntryAsync(vm.SelectedEntry); }
    private async void PathNavigate(object? sender, string path)
    { if (DataContext is LocalFileBrowserViewModel vm) await vm.NavigateAsync(path); }
    private async void UsePathHistory(object? sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string path } && DataContext is LocalFileBrowserViewModel vm) { PathHistoryButton.Flyout?.Hide(); await vm.NavigateAsync(path); } }
    private async void OpenRoot(object? sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string path } && DataContext is LocalFileBrowserViewModel vm) { RootsButton.Flyout?.Hide(); await vm.NavigateAsync(path); } }
    private void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not LocalFileBrowserViewModel vm) return;
        vm.SelectedEntries.Clear(); vm.SelectedEntries.AddRange(LocalGrid.SelectedItems.OfType<LocalEntry>());
        vm.NotifySelectionChanged();
    }
}
