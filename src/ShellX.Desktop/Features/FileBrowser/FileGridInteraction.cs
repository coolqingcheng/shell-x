using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ShellX.Desktop.Features.FileBrowser;

internal static class FileGridInteraction
{
    public static void EnableColumnSizing(DataGrid grid)
    {
        grid.CanUserResizeColumns = true;
        grid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed) return;
            var source = e.Source as Visual;
            var header = source as DataGridColumnHeader ?? source?.FindAncestorOfType<DataGridColumnHeader>();
            if (header is null) return;
            var column = grid.Columns.FirstOrDefault(c => c.IsVisible && Equals(c.Header, header.Content));
            if (column is null) return;
            var x = e.GetPosition(header).X;
            if (x <= 5)
                column = grid.Columns.Where(c => c.IsVisible && c.DisplayIndex < column.DisplayIndex).MaxBy(c => c.DisplayIndex);
            else if (x < header.Bounds.Width - 5) return;
            if (column is null || !column.CanUserResize) return;
            column.Width = new DataGridLength(column.ActualWidth);
            column.Width = DataGridLength.Auto;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    public static bool IsFileRow(object? source) => source is DataGridRow || source is Visual visual && visual.FindAncestorOfType<DataGridRow>() is not null;

    /// <summary>Ctrl+A 全选当前表格的所有行。</summary>
    public static void SelectAll(DataGrid grid)
    {
        grid.SelectedItems.Clear();
        if (grid.ItemsSource is not System.Collections.IEnumerable items) return;
        foreach (var item in items) grid.SelectedItems.Add(item);
    }

    public static void SelectOnRightClick(DataGrid grid) => grid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
    {
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        var source = e.Source as Visual;
        var row = source as DataGridRow ?? source?.FindAncestorOfType<DataGridRow>();
        if (row?.DataContext is not { } item) { grid.SelectedItems.Clear(); grid.SelectedItem = null; }
        else if (!grid.SelectedItems.Contains(item)) grid.SelectedItem = item;
    }, RoutingStrategies.Tunnel);
}
