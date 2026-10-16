using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class RemoteFileBrowserView : UserControl
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(RemoteEntry))]
    public RemoteFileBrowserView() { InitializeComponent(); FileGridInteraction.SelectOnRightClick(FileGrid); FileGridInteraction.EnableColumnSizing(FileGrid); FileDragDrop.Attach(this, FileGrid, true); }
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
