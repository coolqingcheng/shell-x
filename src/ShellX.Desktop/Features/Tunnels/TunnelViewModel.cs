using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Tunnels;

namespace ShellX.Desktop.Features.Tunnels;

public partial class TunnelViewModel(TunnelDefinition definition, Func<TunnelViewModel, Task> toggle,
    Func<TunnelViewModel, Task> remove, Func<TunnelViewModel, Task> check, Func<TunnelViewModel, Task> edit) : ObservableObject
{
    public TunnelDefinition Definition => definition;
    public string Name => definition.Name;
    public string Description => definition.Description;
    [ObservableProperty] private bool _running;
    [ObservableProperty] private TunnelState _state;
    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private DateTimeOffset? _checkedAt;
    [ObservableProperty] private bool _hasTraffic;
    [ObservableProperty] private string _trafficText = "";
    public string Status => State switch
    {
        TunnelState.Starting => "正在启动", TunnelState.Checking => "正在检测",
        TunnelState.Connected => "TCP 可达", TunnelState.Failed => Running ? "连接异常" : "未连通",
        TunnelState.Unverified => "已监听 · 待验证", _ => "已停止"
    };
    public string StatusColor => State switch
    {
        TunnelState.Connected => "#73C991", TunnelState.Failed => "#F48771",
        TunnelState.Starting or TunnelState.Checking or TunnelState.Unverified => "#D7BA7D", _ => "#858585"
    };
    public bool HasReason => !string.IsNullOrEmpty(Reason);
    public string CheckedText => CheckedAt.HasValue ? "最近检测 " + CheckedAt.Value.ToLocalTime().ToString("HH:mm:ss") : "";
    public bool IsBusy => State is TunnelState.Starting or TunnelState.Checking;
    private bool CanChange => !IsBusy;
    private bool CanCheck => Running && !IsBusy;
    public string ActionText => Running ? "停止" : "启动";
    partial void OnRunningChanged(bool value) { OnPropertyChanged(nameof(ActionText)); OnPropertyChanged(nameof(Status)); CheckCommand.NotifyCanExecuteChanged(); }
    partial void OnStateChanged(TunnelState value)
    {
        OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(IsBusy));
        ToggleCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged(); CheckCommand.NotifyCanExecuteChanged(); EditCommand.NotifyCanExecuteChanged();
    }
    partial void OnReasonChanged(string value) => OnPropertyChanged(nameof(HasReason));
    partial void OnCheckedAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(CheckedText));
    public void Apply(TunnelStatus value)
    {
        if (value.TunnelId != definition.Id) return;
        Running = value.IsRunning; State = value.State; Reason = value.Reason; CheckedAt = value.CheckedAt;
        // 只有上一秒真的有字节经过才显示速度和动画，空闲时保持安静。
        var active = value.SentRate > 0 || value.ReceivedRate > 0;
        HasTraffic = active;
        TrafficText = active ? $"↑ {TunnelSpeed.Format(value.SentRate)}   ↓ {TunnelSpeed.Format(value.ReceivedRate)}" : "";
    }
    [RelayCommand(CanExecute = nameof(CanChange))] private Task ToggleAsync() => toggle(this);
    [RelayCommand(CanExecute = nameof(CanChange))] private Task RemoveAsync() => remove(this);
    [RelayCommand(CanExecute = nameof(CanCheck))] private Task CheckAsync() => check(this);
    [RelayCommand(CanExecute = nameof(CanChange))] private Task EditAsync() => edit(this);
}
