using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Tests;

public sealed class McpLogLayoutTests
{
    [AvaloniaFact]
    public async Task ScrollingUpPausesAutoScrollUntilUserReturnsToBottom()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-mcp-log-scroll-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var logs = Path.Combine(root, "logs");
            Directory.CreateDirectory(logs);
            var file = Path.Combine(logs, "mcp-audit-20261018.jsonl");
            var rows = Enumerable.Range(0, 200).Select(i => "{\"Timestamp\":\"2026-10-18T10:00:00+08:00\",\"ConnectionName\":\"本地\",\"Status\":\"succeeded\",\"Command\":\"row-" + i + "\",\"ExitCode\":0,\"DurationMilliseconds\":1}");
            await File.WriteAllLinesAsync(file, rows, TestContext.Current.CancellationToken);
            var window = new McpLogWindow(paths);
            window.Show();
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => { });
                var list = window.FindControl<ListBox>("LogRows")!;
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                await Task.Delay(50);
                scroll.Offset = new Vector(0, 0);
                await Dispatcher.UIThread.InvokeAsync(() => { });
                await File.AppendAllTextAsync(file, """{"Timestamp":"2026-10-18T10:01:00+08:00","ConnectionName":"本地","Status":"succeeded","Command":"new-row","ExitCode":0,"DurationMilliseconds":1}""" + Environment.NewLine, TestContext.Current.CancellationToken);
                await Task.Delay(1200);
                Assert.Equal(0, scroll.Offset.Y);

                scroll.ScrollToEnd();
                await Dispatcher.UIThread.InvokeAsync(() => { });
                await File.AppendAllTextAsync(file, """{"Timestamp":"2026-10-18T10:02:00+08:00","ConnectionName":"本地","Status":"succeeded","Command":"new-row-2","ExitCode":0,"DurationMilliseconds":1}""" + Environment.NewLine, TestContext.Current.CancellationToken);
                await Task.Delay(1200);
                Assert.True(scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 2);
            }
            finally { window.Close(); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task LongMcpLogCommandWrapsInsideVirtualizedList()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-mcp-log-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var logs = Path.Combine(root, "logs");
            Directory.CreateDirectory(logs);
            await File.AppendAllTextAsync(Path.Combine(logs, "mcp-audit-20261018.jsonl"),
                """{"Timestamp":"2026-10-18T10:00:00+08:00","ConnectionName":"本地","Status":"succeeded","Command":"this-is-a-very-long-command-that-must-wrap-automatically-0123456789-0123456789-0123456789-0123456789-0123456789","ExitCode":0,"DurationMilliseconds":12}""" + Environment.NewLine,
                TestContext.Current.CancellationToken);
            var window = new McpLogWindow(paths);
            window.Show();
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => { });
                var list = window.FindControl<ListBox>("LogRows")!;
                // 列表是虚拟化的，等一次布局后再取行，保证换行后的高度已经算出来。
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                window.UpdateLayout();
                var row = list.GetVisualDescendants().OfType<McpLogRowView>().Single();
                var command = row.GetVisualDescendants().OfType<TextBlock>().First(x => x.Text?.Contains("very-long-command") == true);
                Assert.Equal(TextWrapping.Wrap, command.TextWrapping);
                Assert.True(command.Bounds.Height > 24, $"h={command.Bounds.Height} w={command.Bounds.Width} row={row.Bounds.Width} win={window.Bounds.Width}");
                Assert.True(command.Bounds.Width < row.Bounds.Width);
            }
            finally { window.Close(); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
