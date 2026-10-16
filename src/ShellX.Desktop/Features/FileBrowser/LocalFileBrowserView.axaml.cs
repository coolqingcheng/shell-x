using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class LocalFileBrowserView : UserControl
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(LocalEntry))]
    public LocalFileBrowserView() { InitializeComponent(); FileGridInteraction.SelectOnRightClick(LocalGrid); FileGridInteraction.EnableColumnSizing(LocalGrid); FileDragDrop.Attach(this, LocalGrid, false); }
    private async void ViewLoaded(object? sender, RoutedEventArgs e)
    { if (DataContext is LocalFileBrowserViewModel vm) await vm.InitializeAsync(); }
    private async void FileDoubleTapped(object? sender, TappedEventArgs e)
    { if (FileGridInteraction.IsFileRow(e.Source) && DataContext is LocalFileBrowserViewModel vm) await vm.OpenEntryAsync(vm.SelectedEntry); }
    private async void AddressKeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && DataContext is LocalFileBrowserViewModel vm) { e.Handled = true; await vm.GoCommand.ExecuteAsync(null); } }
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
