using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Tests;

public sealed class McpLogLayoutTests
{
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
                var row = list.GetVisualDescendants().OfType<McpLogRowView>().Single();
                var command = row.GetVisualDescendants().OfType<TextBlock>().First(x => x.Text?.Contains("very-long-command") == true);
                Assert.Equal(TextWrapping.Wrap, command.TextWrapping);
                Assert.True(command.Bounds.Height > 24);
                Assert.True(command.Bounds.Width < row.Bounds.Width);
            }
            finally { window.Close(); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}