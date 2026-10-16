using CommunityToolkit.Mvvm.ComponentModel;
using ShellX.Core.Tunnels;

namespace ShellX.Desktop.Features.Tunnels;

public partial class TunnelEditorViewModel : ObservableObject
{
    private readonly Guid _id;
    public TunnelEditorViewModel(TunnelDefinition? definition = null)
    {
        _id = definition?.Id ?? Guid.NewGuid();
        if (definition == null) return;
        Name = definition.Name; DirectionIndex = (int)definition.Direction;
        BindHost = definition.BindHost; BindPort = definition.BindPort.ToString();
        TargetHost = definition.TargetHost; TargetPort = definition.TargetPort.ToString();
    }
    [ObservableProperty] private string _name = "数据库隧道";
    [ObservableProperty] private int _directionIndex;
    [ObservableProperty] private string _bindHost = "127.0.0.1";
    [ObservableProperty] private string _bindPort = "3307";
    [ObservableProperty] private string _targetHost = "127.0.0.1";
    [ObservableProperty] private string _targetPort = "3306";
    [ObservableProperty] private string _error = "";
    public TunnelDefinition Build()
    {
        if (!int.TryParse(BindPort, out var bind) || !int.TryParse(TargetPort, out var target)) throw new ArgumentException("端口必须为数字。");
        if (DirectionIndex is < 0 or > 1) throw new ArgumentException("请选择正确的转发方向。");
        var value = new TunnelDefinition { Id = _id, Name = Name.Trim(), Direction = (TunnelDirection)DirectionIndex,
            BindHost = BindHost.Trim(), BindPort = bind, TargetHost = TargetHost.Trim(), TargetPort = target };
        value.Validate(); return value;
    }
}
