using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using ShellX.Desktop.Features.Workspace;

namespace ShellX.Desktop.Features.Tunnels;

/// <summary>端口转发独立窗口的视图模型：规则与操作都复用连接标签，这里只负责搜索与失败重试开关。</summary>
public partial class TunnelWindowViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceTabViewModel _tab;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private const int MinRetrySeconds = 3;
    private const int MaxRetrySeconds = 600;

    public TunnelWindowViewModel(WorkspaceTabViewModel tab, string target)
    {
        _tab = tab;
        Target = target;
        _autoRetry = tab.Profile.TunnelAutoRetry;
        _retrySeconds = Math.Clamp(tab.Profile.TunnelRetrySeconds, 3, 600);
        _retrySecondsText = _retrySeconds.ToString();
        _tab.PropertyChanged += TabChanged;
    }

    /// <summary>窗口标题后缀：连接名称与地址。</summary>
    public string Target { get; }
    public ObservableCollection<TunnelViewModel> Tunnels => _tab.Tunnels;
    public IReadOnlyList<TunnelViewModel> VisibleTunnels => _tab.FilteredTunnels;
    public string Search { get => _tab.TunnelSearch; set => _tab.TunnelSearch = value; }
    public bool CanStartAll => _tab.IsConnected;
    public bool CanStopAll => _tab.CanStopAllTunnels;
    public ICommand AddCommand => _tab.AddTunnelCommand;
    public ICommand StartAllCommand => _tab.StartAllTunnelsCommand;
    public ICommand StopAllCommand => _tab.StopAllTunnelsCommand;
    /// <summary>失败后按间隔自动重试；关闭后不再重试，规则仍可手动启动。</summary>
    [ObservableProperty] private bool _autoRetry;
    /// <summary>自动重试的间隔秒数。</summary>
    [ObservableProperty] private int _retrySeconds;
    /// <summary>间隔秒数的文本形式：只接受数字，不带上下调节按钮。</summary>
    [ObservableProperty] private string _retrySecondsText = string.Empty;

    /// <summary>由文本框改动驱动，避免用户输入过程中被回写打断。</summary>
    private bool _editingFromText;

    partial void OnAutoRetryChanged(bool value) => QueueSave();

    partial void OnRetrySecondsChanged(int value)
    {
        // 文本框正在编辑时不要回写文本，否则输入 "1" 会被立刻改写成 "3"。
        if (!_editingFromText) RetrySecondsText = value.ToString();
        QueueSave();
    }

    partial void OnRetrySecondsTextChanged(string value)
    {
        var digits = SanitizeDigits(value);
        if (!string.Equals(digits, value, StringComparison.Ordinal))
        {
            RetrySecondsText = digits;
            return;
        }
        if (digits.Length == 0) return;
        if (!int.TryParse(digits, out var seconds)) return;
        var clamped = Math.Clamp(seconds, MinRetrySeconds, MaxRetrySeconds);
        if (clamped == _retrySeconds) return;
        _editingFromText = true;
        try { RetrySeconds = clamped; }
        finally { _editingFromText = false; }
    }

    /// <summary>去掉非数字字符。</summary>
    private static string SanitizeDigits(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());

    private void QueueSave() => _ = SaveAsync();
    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try { await _tab.SetTunnelAutoRetryAsync(AutoRetry, RetrySeconds); }
        catch (Exception ex) { await _tab.Dialogs.ShowErrorAsync("隧道重试设置保存失败：" + ex.Message); }
        finally { _saveGate.Release(); }
    }
    private void TabChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceTabViewModel.FilteredTunnels): OnPropertyChanged(nameof(VisibleTunnels)); break;
            case nameof(WorkspaceTabViewModel.IsConnected): OnPropertyChanged(nameof(CanStartAll)); break;
            case nameof(WorkspaceTabViewModel.CanStopAllTunnels): OnPropertyChanged(nameof(CanStopAll)); break;
        }
    }
    public void Dispose() => _tab.PropertyChanged -= TabChanged;
}
