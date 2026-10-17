using Avalonia.Controls;
using Avalonia.Interactivity;
using ShellX.Core.Connections;
using ShellX.Desktop.Features.Monitoring;

namespace ShellX.Desktop.Features.Shell;

public partial class MainWindow : Window
{
    private bool _shutdown;
    private bool _closing;
    private MonitorPanelWindow? _monitorPanels;
    public MainWindow()
    {
        InitializeComponent();
        Closing += async (_, e) =>
        {
            if (_shutdown) return; e.Cancel = true;
            if (_closing || DataContext is not MainWindowViewModel vm) return;
            _closing = true;
            _monitorPanels?.Close();
            try { if (await vm.ShutdownAsync()) { _shutdown = true; Close(); } }
            finally { _closing = false; }
        };
    }
    private async void ShowMonitorPanel(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedTab is not { } tab) return;
        _monitorPanels ??= CreateMonitorPanels();
        _monitorPanels.Title = tab.Name;
        if (!_monitorPanels.TryAdd(tab.Profile, tab.Name, tab.Address, tab.Monitor))
        {
            _monitorPanels.Activate();
            await vm.ShowMonitorPanelExistsAsync(tab.Name);
            return;
        }
        if (!_monitorPanels.IsVisible) _monitorPanels.Show(this);
        _monitorPanels.Activate();
    }
    private MonitorPanelWindow CreateMonitorPanels()
    {
        var window = new MonitorPanelWindow();
        window.Closed += (_, _) => _monitorPanels = null;
        return window;
    }
    private async void EditConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.EditConnectionCommand.ExecuteAsync(profile); }
    private async void DeleteConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.DeleteConnectionCommand.ExecuteAsync(profile); }
}
