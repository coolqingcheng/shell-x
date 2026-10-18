using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Services;

public partial class McpLogWindow : Window
{
    private const int MaxVisibleRows = 5000;
    private readonly McpLogFileReader _reader;
    private readonly ObservableCollection<McpLogRow> _rows = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ScrollViewer? _scrollViewer;
    private bool _autoScroll = true;

    public McpLogWindow() : this(new AppPaths()) { }

    public McpLogWindow(AppPaths paths)
    {
        InitializeComponent();
        _reader = new(Path.Combine(paths.Root, "logs"));
        LogRows.ItemsSource = _rows;
        LogPath.Text = _reader.CurrentPath;
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            AttachScrollWatcher();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_scrollViewer != null) _scrollViewer.ScrollChanged -= LogScrollChanged;
        };
    }

    private void AttachScrollWatcher()
    {
        _scrollViewer ??= LogRows.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scrollViewer == null) return;
        _scrollViewer.ScrollChanged -= LogScrollChanged;
        _scrollViewer.ScrollChanged += LogScrollChanged;
    }

    private void LogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer == null) return;
        var atBottom = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= _scrollViewer.Extent.Height - 2;
        if (e.OffsetDelta.Y < -0.5) _autoScroll = false;
        else if (atBottom) _autoScroll = true;
    }

    private void RefreshNow(object? sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var rows = _reader.ReadNewRows();
        if (!string.IsNullOrEmpty(_reader.CurrentPath)) LogPath.Text = _reader.CurrentPath;
        foreach (var row in rows) _rows.Add(row);
        while (_rows.Count > MaxVisibleRows) _rows.RemoveAt(0);
        EmptyText.IsVisible = _rows.Count == 0;
        if (_autoScroll && _rows.Count > 0)
            Dispatcher.UIThread.Post(() => _scrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
    }
}