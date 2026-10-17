using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Tests;

public sealed class McpLogLayoutTests
{
    [AvaloniaFact]
    public async Task LongMcpLogRowWrapsInsideFixedWidthWindow()
    {
        var view = new McpLogRowView { DataContext = new McpLogRow(new string('x', 240), "detail") };
        var window = new Window { Width = 320, Height = 220, Content = view };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var text = view.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.True(text.Bounds.Width < window.Width);
            Assert.True(text.Bounds.Height > 24, $"height={text.Bounds.Height}, width={text.Bounds.Width}");
        }
        finally { window.Close(); }
    }
}