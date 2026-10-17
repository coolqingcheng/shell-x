using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Services;

public partial class McpLogWindow : Window
{
    private readonly McpLogFileReader _reader;
    private readonly StringBuilder _content = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public McpLogWindow() : this(new AppPaths()) { }
    public McpLogWindow(AppPaths paths)
    {
        InitializeComponent();
        _reader = new(Path.Combine(paths.Root, "logs"));
        LogPath.Text = _reader.CurrentPath;
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }
    private void RefreshNow(object? sender, RoutedEventArgs e) => Refresh();
    private void Refresh()
    {
        var rows = _reader.ReadNewRows();
        if (!string.IsNullOrEmpty(_reader.CurrentPath)) LogPath.Text = _reader.CurrentPath;
        foreach (var row in rows) _content.AppendLine(row.Text);
        if (rows.Count == 0 && _content.Length == 0) return;
        LogText.Text = _content.ToString();
        LogText.CaretIndex = LogText.Text.Length;
        EmptyText.IsVisible = _content.Length == 0;
    }
}