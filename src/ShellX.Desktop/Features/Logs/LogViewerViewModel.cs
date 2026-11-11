using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Logs;

namespace ShellX.Desktop.Features.Logs;

public enum LogViewMode { Tail, Follow, Search }

/// <summary>
/// 远程日志查看器。尾部、跟随、检索三种模式都走服务端命令，文件不会被整体读进内存；
/// 跟随模式按行号增量拉取，用户手动上滚时暂停跟随。
/// </summary>
public partial class LogViewerViewModel : ObservableObject, IDisposable
{
    private readonly IRemoteLogReader? _reader;
    private readonly DispatcherTimer _timer;
    private long _lastLine;
    private bool _disposed;

    public LogViewerViewModel(IRemoteLogReader? reader, string path, string target)
    {
        _reader = reader; Path = path; Target = target;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public IRemoteLogReader? Reader => _reader;
    public string Path { get; }
    public string Target { get; }
    public string FileName => string.IsNullOrEmpty(Path) ? "日志" : System.IO.Path.GetFileName(Path);
    public ObservableCollection<LogLine> Lines { get; } = [];
    public IReadOnlyList<int> FollowIntervals { get; } = [1, 2, 5, 10];

    [ObservableProperty] private LogViewMode _mode = LogViewMode.Tail;
    [ObservableProperty] private string _status = "准备中…";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private int _contextLines = 2;
    [ObservableProperty] private int _followSeconds = 2;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private long _totalLines;

    public bool IsFollowMode => Mode == LogViewMode.Follow;
    public bool IsSearchMode => Mode == LogViewMode.Search;

    partial void OnModeChanged(LogViewMode value)
    {
        OnPropertyChanged(nameof(IsFollowMode));
        OnPropertyChanged(nameof(IsSearchMode));
        if (value != LogViewMode.Follow) StopFollowing();
        _ = RefreshAsync();
    }

    partial void OnFollowSecondsChanged(int value)
    {
        if (value <= 0) return;
        _timer.Interval = TimeSpan.FromSeconds(value);
    }

    /// <summary>用户把内容往上滚时暂停跟随，避免新行把正在看的内容顶走。</summary>
    public void PauseFollow()
    {
        if (!IsFollowMode || IsPaused) return;
        IsPaused = true;
        Status = "已暂停跟随 · 点击“恢复跟随”继续";
    }

    [RelayCommand]
    public void ResumeFollow() => IsPaused = false;

    [RelayCommand]
    public void SetMode(string? mode)
    {
        if (!Enum.TryParse<LogViewMode>(mode, ignoreCase: true, out var parsed)) return;
        Mode = parsed;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_disposed || _reader == null) return;
        IsBusy = true;
        try
        {
            switch (Mode)
            {
                case LogViewMode.Search: await SearchAsync(); break;
                case LogViewMode.Follow:
                    await LoadTailAsync();
                    StartFollowing();
                    break;
                default: await LoadTailAsync(); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "读取失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (_reader == null) return;
        if (string.IsNullOrWhiteSpace(SearchText)) { Status = "请输入检索内容"; return; }
        var result = await _reader.SearchAsync(Path, new LogSearchQuery(SearchText, UseRegex, ContextLines));
        Lines.Clear();
        foreach (var line in result.Lines) Lines.Add(line);
        TotalLines = result.Lines.Count;
        Summary = result.MatchCount > 0
            ? $"命中 {result.MatchCount} 处" + (result.Truncated ? "（结果过多已截断）" : "")
            : "没有匹配的内容";
        Status = $"检索完成 · {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    public void ClearCommand()
    {
        Lines.Clear();
        _lastLine = 0;
        Summary = "";
    }

    private async Task LoadTailAsync()
    {
        var result = await _reader!.TailAsync(Path, 200);
        Lines.Clear();
        foreach (var line in result.Lines) Lines.Add(line);
        _lastLine = Lines.Count > 0 ? Lines[^1].Number : 0;
        TotalLines = result.TotalLines;
        Summary = $"文件共 {TotalLines} 行，显示末尾 {Lines.Count} 行";
        Status = $"已加载 · {DateTime.Now:HH:mm:ss}";
    }

    private void StartFollowing()
    {
        IsPaused = false;
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, FollowSeconds));
        _timer.Start();
    }

    private void StopFollowing() => _timer.Stop();

    private async Task PollAsync()
    {
        if (_disposed || _reader == null || IsPaused || Mode != LogViewMode.Follow) return;
        try
        {
            var result = await _reader.ReadAsync(Path, _lastLine + 1);
            // 文件被轮转或截断时总行数会变小，这时按最新尾部重新开始。
            if (result.TotalLines < _lastLine) { await LoadTailAsync(); return; }
            if (result.Lines.Count == 0) { Summary = $"文件共 {result.TotalLines} 行，暂无新增"; return; }
            foreach (var line in result.Lines) Lines.Add(line);
            _lastLine = Lines[^1].Number;
            TotalLines = result.TotalLines;
            Summary = $"文件共 {TotalLines} 行，已显示 {Lines.Count} 行";
            Status = $"已更新 · {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "读取失败：" + ex.Message; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}