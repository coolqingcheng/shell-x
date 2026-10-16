using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileBrowser;

// 原生拖放只携带短期令牌；远端会话和选择保留在进程内，不把远端路径伪装成本地文件。
public static class FileDragDrop
{
    private static readonly DataFormat<string> SelectionFormat = DataFormat.CreateStringApplicationFormat("ShellX.FileSelection");
    private static readonly Dictionary<string, Selection> ActiveSelections = [];
    private sealed record Selection(FileBrowserViewModel Owner, int SessionVersion, string[]? LocalPaths, RemoteFileSelection? Remote);

    public sealed class Payload : IDisposable
    {
        private readonly string _id = Guid.NewGuid().ToString("N");
        public DataTransfer Data { get; } = new();
        internal Payload(FileBrowserViewModel owner, string[]? paths, RemoteFileSelection? remote)
        {
            ActiveSelections.Add(_id, new(owner, owner.SessionVersion, paths, remote));
            Data.Add(DataTransferItem.Create(SelectionFormat, _id));
        }
        public void Dispose() => ActiveSelections.Remove(_id);
    }

    public static Payload LocalPayload(FileBrowserViewModel owner, IEnumerable<string> paths) => new(owner, paths.ToArray(), null);
    public static Payload RemotePayload(RemoteFileSelection selection) => new(selection.Owner, null, selection);

    public static void Attach(UserControl view, DataGrid grid, bool remote)
    {
        var hint = view.FindControl<Border>("DropHint")!;
        var hintText = view.FindControl<TextBlock>("DropHintText")!;
        PointerPressedEventArgs? pressed = null;
        Point origin = default;
        object? deferredSelection = null;
        bool dragging = false;
        FileBrowserViewModel? Browser() => remote ? view.DataContext as FileBrowserViewModel
            : view.FindAncestorOfType<FileBrowserView>()?.DataContext as FileBrowserViewModel;

        grid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            pressed = null; deferredSelection = null;
            if (e.ClickCount != 1 || !e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed || Browser() is not { IsAvailable: true }) return;
            var visual = e.Source as Visual;
            var row = visual as DataGridRow ?? visual?.FindAncestorOfType<DataGridRow>();
            if (row?.DataContext is not (LocalEntry or RemoteEntry)) return;
            pressed = e; origin = e.GetPosition(grid);
            if (grid.SelectedItems.Count > 1 && grid.SelectedItems.Contains(row.DataContext) && e.KeyModifiers == KeyModifiers.None)
            { deferredSelection = row.DataContext; e.Handled = true; grid.Focus(); }
        }, RoutingStrategies.Tunnel);
        grid.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            pressed = null;
            if (!dragging && deferredSelection != null) grid.SelectedItem = deferredSelection;
            deferredSelection = null;
        }, RoutingStrategies.Tunnel);
        grid.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (dragging || pressed == null) return;
            if (!e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed) { pressed = null; return; }
            var delta = e.GetPosition(grid) - origin;
            if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6 || Browser() is not { IsAvailable: true } browser) return;
            var trigger = pressed; pressed = null; dragging = true;
            try
            {
                using var payload = remote ? browser.CaptureDownloadSelection() is { IsValid: true } selection ? RemotePayload(selection) : null
                    : LocalPayload(browser, grid.SelectedItems.OfType<LocalEntry>().Select(x => x.FullPath));
                if (payload == null) return;
                trigger.Pointer.Capture(null);
                await DragDrop.DoDragDropAsync(trigger, payload.Data, DragDropEffects.Copy);
            }
            catch (Exception ex) { await browser.ReportTransferErrorAsync(ex.Message); }
            finally { dragging = false; deferredSelection = null; hint.IsVisible = false; }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        DragDrop.SetAllowDrop(view, true);
        void DragOver(object? sender, DragEventArgs e)
        {
            var browser = Browser();
            var accepted = view.IsEffectivelyEnabled && view.IsEffectivelyVisible && browser != null && CanDrop(browser, remote, e.DataTransfer)
                && e.DragEffects.HasFlag(DragDropEffects.Copy);
            e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true; hint.IsVisible = accepted;
            if (accepted) hintText.Text = remote ? $"释放以上传到远程目录\n{browser!.Path}" : $"释放以下载到本地目录\n{browser!.Local.Path}";
        }
        DragDrop.AddDragEnterHandler(view, DragOver);
        DragDrop.AddDragOverHandler(view, DragOver);
        DragDrop.AddDragLeaveHandler(view, (_, _) => hint.IsVisible = false);
        DragDrop.AddDropHandler(view, async (_, e) =>
        {
            hint.IsVisible = false; e.Handled = true;
            if (!view.IsEffectivelyEnabled || !view.IsEffectivelyVisible || Browser() is not { } browser || !CanDrop(browser, remote, e.DataTransfer))
            { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy;
            // 在第一个 await 前复制数据，系统拖放数据在回调结束后会释放。
            try
            {
                var selection = Read(e.DataTransfer);
                if (remote)
                {
                    var paths = selection?.LocalPaths ?? e.DataTransfer.TryGetFiles()?.Select(x => x.TryGetLocalPath()).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
                    if (paths.Length == 0) { await browser.ReportTransferErrorAsync("请选择可访问的本地文件或文件夹。"); return; }
                    await browser.UploadPathsAsync(paths);
                }
                else if (selection?.Remote is { IsValid: true } source)
                    await browser.DownloadEntriesAsync(source, browser.Local.Path);
            }
            catch (Exception ex) { await browser.ReportTransferErrorAsync(ex.Message); }
        });
    }

    private static Selection? Read(IDataTransfer data)
        => data.TryGetValue(SelectionFormat) is { } id ? ActiveSelections.GetValueOrDefault(id) : null;

    private static bool CanDrop(FileBrowserViewModel browser, bool remote, IDataTransfer data)
    {
        if (!browser.IsAvailable) return false;
        var selection = Read(data);
        if (selection != null)
            return ReferenceEquals(selection.Owner, browser) && selection.SessionVersion == browser.SessionVersion && (remote ? selection.LocalPaths is { Length: > 0 }
                : browser.Local.Path.Length > 0 && selection.Remote is { IsValid: true });
        return remote && !data.Contains(SelectionFormat) && data.Contains(DataFormat.File);
    }
}
