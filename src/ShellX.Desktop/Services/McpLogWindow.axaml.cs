using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Services;

public partial class McpLogWindow : Window
{
    private const int MaxVisibleRows = 5000;
    private readonly McpLogFileReader _reader;
    private readonly ObservableCollection<McpLogRow> _rows = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public McpLogWindow() : this(new AppPaths()) { }
    public McpLogWindow(AppPaths paths)
    {
        InitializeComponent();
        _reader = new(Path.Combine(paths.Root, "logs"));
        LogRows.ItemsSource = _rows;
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
        foreach (var row in rows) _rows.Add(row);
        while (_rows.Count > MaxVisibleRows) _rows.RemoveAt(0);
        EmptyText.IsVisible = _rows.Count == 0;
        if (_rows.Count > 0) Dispatcher.UIThread.Post(() => LogRows.ScrollIntoView(_rows[^1]), DispatcherPriority.Background);
    }
}