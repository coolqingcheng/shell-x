using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShellX.Core.Logs;
using ShellX.Core.Sessions;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Logs;

/// <summary>非模态的日志查看窗口：同一个文件重复打开时激活已有窗口，不会开出第二个。</summary>
public partial class LogViewerWindow : Window
{
    private static readonly object Sync = new();
    private static readonly List<(IRemoteLogReader? Reader, string Path, WeakReference<LogViewerWindow> Window)> OpenWindows = [];
    private readonly LogViewerViewModel _viewModel;
    private readonly IRemoteSession? _session;
    private ScrollViewer? _scroller;
    private bool _stickToBottom = true;

    // XAML 运行时加载器需要无参构造；实际窗口都通过带 ViewModel 的构造创建。
    public LogViewerWindow() : this(new LogViewerViewModel(null, "", "")) { }

    public LogViewerWindow(LogViewerViewModel viewModel, IRemoteSession? session = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _session = session;
        Title = $"日志查看 · {viewModel.FileName}";
        WindowTitles.ApplyVersion(this);
        Loaded += async (_, _) =>
        {
            _scroller = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_scroller != null) _scroller.ScrollChanged += ScrollerScrollChanged;
            ((INotifyCollectionChanged)_viewModel.Lines).CollectionChanged += LinesChanged;
            await viewModel.RefreshAsync();
        };
        Closed += (_, _) =>
        {
            if (_scroller != null) _scroller.ScrollChanged -= ScrollerScrollChanged;
            ((INotifyCollectionChanged)_viewModel.Lines).CollectionChanged -= LinesChanged;
            lock (Sync) OpenWindows.RemoveAll(entry => entry.Window.TryGetTarget(out var target) && ReferenceEquals(target, this));
            _viewModel.Dispose();
            if (session != null) session.Disconnected -= SessionDisconnected;
        };
        if (session != null) session.Disconnected += SessionDisconnected;
        Register(viewModel.Reader, viewModel.Path);
    }

    // 连接断开后窗口没有可用会话，直接关闭，避免继续对失效会话发起查询。
    private void SessionDisconnected(object? sender, string reason) => Dispatcher.UIThread.Post(Close);

    /// <summary>同一个连接里的同一个文件只开一个窗口，重复打开时把已有窗口带到前台。</summary>
    public static bool TryActivate(IRemoteLogReader? reader, string path)
    {
        lock (Sync)
        {
            for (var i = OpenWindows.Count - 1; i >= 0; i--)
            {
                var entry = OpenWindows[i];
                if (!ReferenceEquals(entry.Reader, reader) || !string.Equals(entry.Path, path, StringComparison.Ordinal)) continue;
                if (!entry.Window.TryGetTarget(out var window)) { OpenWindows.RemoveAt(i); continue; }
                if (!window.IsVisible) { OpenWindows.RemoveAt(i); continue; }
                window.Activate();
                return true;
            }
        }
        return false;
    }

    private void Register(IRemoteLogReader? reader, string path)
    {
        lock (Sync)
        {
            OpenWindows.RemoveAll(entry => !entry.Window.TryGetTarget(out _));
            OpenWindows.Add((reader, path, new WeakReference<LogViewerWindow>(this)));
        }
    }

    // 用户把内容往上滚时暂停跟随；滚回底部自动恢复。
    private void ScrollerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroller == null) return;
        var atBottom = _scroller.Offset.Y >= _scroller.Extent.Height - _scroller.Viewport.Height - 6;
        _stickToBottom = atBottom;
        if (atBottom) _viewModel.ResumeFollow();
        else _viewModel.PauseFollow();
    }

    // 只在原本就贴着底部时继续跟随，避免把正在翻看的内容顶走。
    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_stickToBottom || _viewModel.Lines.Count == 0) return;
        var last = _viewModel.Lines[^1];
        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(last));
    }
}