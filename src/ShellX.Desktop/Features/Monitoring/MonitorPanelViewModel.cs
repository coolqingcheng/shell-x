using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Connections;

namespace ShellX.Desktop.Features.Monitoring;

public sealed class MonitorPanelViewModel : ObservableObject
{
    public MonitorPanelViewModel() => Panels.CollectionChanged += (_, _) =>
    {
        OnPropertyChanged(nameof(HasPanels));
        OnPropertyChanged(nameof(NoPanels));
    };
    public ObservableCollection<MonitorPanelItemViewModel> Panels { get; } = [];
    public bool HasPanels => Panels.Count > 0;
    public bool NoPanels => Panels.Count == 0;
    public bool TryAdd(ConnectionProfile profile, string displayName, string address, ServerMonitorViewModel monitor)
    {
        var endpoint = ConnectionEndpoint.From(profile);
        if (Panels.Any(panel => panel.Endpoint == endpoint)) return false;
        Panels.Add(new(endpoint, displayName, address, monitor, panel => Panels.Remove(panel)));
        return true;
    }
}

public sealed partial class MonitorPanelItemViewModel(ConnectionEndpoint endpoint, string name, string address,
    ServerMonitorViewModel monitor, Action<MonitorPanelItemViewModel> close) : ObservableObject
{
    public ConnectionEndpoint Endpoint { get; } = endpoint;
    public string Name { get; } = name;
    public string Address { get; } = address;
    public ServerMonitorViewModel Monitor { get; } = monitor;
    [RelayCommand] private void Close() => close(this);
}