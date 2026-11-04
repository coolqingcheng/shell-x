using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShellX.Desktop.Features.Transfers;

public partial class TransferViewModel(string name, string direction, long total) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private long _transferred;
    private long _total = total;
    public string Name => name;
    public string Direction => direction;
    // 传输方向用线条图标显示，标签去掉箭头字符（原来的方向文字是字体图标）。
    public string DirectionIcon => direction.StartsWith('↑') ? "upload" : "download";
    public string DirectionLabel => direction.Length > 1 && direction[0] is '↑' or '↓' ? direction[2..] : direction;
    public CancellationToken Token => _cancel.Token;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _status = "等待中";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    private string _details = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    private string _progressText = $"0 B / {FormatSize(total)}";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isActive = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelling;
    public bool CanCancel => IsActive && !IsCancelling;
    public string DetailText => string.IsNullOrEmpty(Details) ? ProgressText : Details;

    public void Report(long bytes)
    {
        // Progress<T> 会把回调排到 UI 队列，任务结束后仍可能收到先前排队的回调。
        if (!IsActive || IsCancelling) return;
        _transferred = Math.Max(_transferred, Math.Max(0, bytes));
        Percent = _total <= 0 ? 0 : Math.Clamp(_transferred * 100d / _total, 0, 100);
        ProgressText = $"{FormatSize(_transferred)} / {FormatSize(_total)} · {Percent:0}%";
        Status = "传输中";
    }

    public void Complete()
    {
        if (!IsActive) return;
        Percent = 100; ProgressText = $"{FormatSize(Math.Max(_transferred, _total))} / {FormatSize(_total)} · 100%";
        Status = "已完成"; IsActive = false;
    }

    public void SetTotal(long bytes) { _total = bytes; Details = ""; Report(0); }
    public void Skip(string reason)
    { if (IsActive) { Status = "已跳过"; Details = reason; IsActive = false; } }

    public void FinishCanceled(bool uploading)
    {
        if (!IsActive) return;
        Status = "已取消";
        Details = uploading ? "远端可能保留部分文件" : "";
        IsActive = false;
    }

    public void Fail(string error)
    {
        if (!IsActive) return;
        Status = "失败"; Details = error; IsActive = false;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel) return;
        IsCancelling = true; Status = "取消中"; _cancel.Cancel();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{Math.Max(0, bytes)} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KiB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MiB",
        _ => $"{bytes / (1024d * 1024 * 1024):0.#} GiB"
    };
    public void Dispose() => _cancel.Dispose();
}
