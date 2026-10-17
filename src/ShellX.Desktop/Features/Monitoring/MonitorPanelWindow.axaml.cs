using Avalonia.Controls;

namespace ShellX.Desktop.Features.Monitoring;

public partial class MonitorPanelWindow : Window
{
    public MonitorPanelViewModel ViewModel { get; } = new();
    public MonitorPanelWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
    public bool TryAdd(Core.Connections.ConnectionProfile profile, string displayName, string address, ServerMonitorViewModel monitor)
        => ViewModel.TryAdd(profile, displayName, address, monitor);
}