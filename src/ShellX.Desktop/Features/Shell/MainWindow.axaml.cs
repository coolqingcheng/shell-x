using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShellX.Core.Connections;
using ShellX.Core.Settings;

namespace ShellX.Desktop.Features.Shell;

public partial class MainWindow : Window
{
    private bool _shutdown;
    private bool _closing;
    private MainWindowViewModel? _layoutVm;
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachLayout();
        Opened += (_, _) => AttachLayout();
        Closing += async (_, e) =>
        {
            if (_shutdown) return; e.Cancel = true;
            if (_closing || DataContext is not MainWindowViewModel vm) return;
            _closing = true;
            try { if (await vm.ShutdownAsync()) { _shutdown = true; Close(); } }
            finally { _closing = false; }
        };
    }
    private void AttachLayout()
    {
        if (ReferenceEquals(_layoutVm, DataContext)) return;
        if (_layoutVm != null) _layoutVm.LayoutChanged -= ApplyLayout;
        _layoutVm = DataContext as MainWindowViewModel;
        if (_layoutVm == null) return;
        _layoutVm.LayoutChanged += ApplyLayout;
        ApplyLayout(_layoutVm.LayoutSettings);
    }
    private void ApplyLayout(AppSettings settings)
    {
        RootLayout.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(settings.MonitorPaneWidth, 160, 420), GridUnitType.Pixel);
    }
    private async void MonitorSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (_layoutVm != null) await _layoutVm.SaveMonitorPaneWidthAsync(RootLayout.ColumnDefinitions[0].Width.Value);
    }
    private async void EditConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.EditConnectionCommand.ExecuteAsync(profile); }
    private async void DeleteConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.DeleteConnectionCommand.ExecuteAsync(profile); }
}
