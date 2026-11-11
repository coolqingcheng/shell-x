using Avalonia.Headless.XUnit;
using ShellX.Core.Logs;
using Avalonia.Controls;
using ShellX.Desktop.Features.Logs;

namespace ShellX.Desktop.Tests;

public sealed class LogViewerTests
{
    [AvaloniaFact]
    public async Task TailModeShowsLastLinesWithLineNumbers()
    {
        var reader = new FakeLogReader();
        reader.Tail = new([new LogLine(98, "倒数第三行"), new LogLine(99, "倒数第二行"), new LogLine(100, "最后一行")], 100, false);
        var vm = new LogViewerViewModel(reader, "/var/log/app.log", "服务器 · 1.2.3.4:22");
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Lines.Count);
        Assert.Equal(100, vm.Lines[^1].Number);
        Assert.Equal("最后一行", vm.Lines[^1].Text);
        Assert.Equal(100, vm.TotalLines);
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task SearchModeReplacesContentWithMatches()
    {
        var reader = new FakeLogReader();
        reader.Search = new([new LogLine(7, "包含 error 的行", true)], 1, false);
        var vm = new LogViewerViewModel(reader, "/var/log/app.log", "服务器");
        vm.SearchText = "error";
        vm.UseRegex = true;
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Single(vm.Lines);
        Assert.True(vm.Lines[0].IsMatch);
        Assert.Equal("error", reader.LastQuery?.Pattern);
        Assert.True(reader.LastQuery?.UseRegex);
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task ScrollingUpPausesFollowing()
    {
        var reader = new FakeLogReader();
        reader.Tail = new([new LogLine(1, "第一行")], 1, false);
        var vm = new LogViewerViewModel(reader, "/var/log/app.log", "服务器");
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Mode = LogViewMode.Follow;
        vm.PauseFollow();
        Assert.True(vm.IsPaused);
        Assert.Equal("已暂停跟随 · 点击“恢复跟随”继续", vm.Status);
        vm.ResumeFollow();
        Assert.False(vm.IsPaused);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void WindowLoadsWithoutError()
    {
        var reader = new FakeLogReader();
        var vm = new LogViewerViewModel(reader, "/var/log/app.log", "服务器 · 1.2.3.4:22");
        var window = new LogViewerWindow(vm);
        window.Show();
        Assert.Contains("日志查看", window.Title);
        window.Close();
        vm.Dispose();
    }

    private sealed class FakeLogReader : IRemoteLogReader
    {
        public LogReadResult Tail { get; set; } = new([], 0, false);
        public LogSearchResult Search { get; set; } = new([], 0, false);
        public LogSearchQuery? LastQuery { get; private set; }
        public Task<LogFileInfo> StatAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new LogFileInfo(path, 0, default));
        public Task<LogReadResult> TailAsync(string path, int lines, CancellationToken cancellationToken = default) => Task.FromResult(Tail);
        public Task<LogReadResult> ReadAsync(string path, long fromLine, CancellationToken cancellationToken = default) => Task.FromResult(new LogReadResult([], 0, false));
        public Task<LogSearchResult> SearchAsync(string path, LogSearchQuery query, CancellationToken cancellationToken = default)
        { LastQuery = query; return Task.FromResult(Search); }
    }
}