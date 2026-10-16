using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;

namespace ShellX.Desktop.Features.Monitoring;
public partial class ServerMonitorView : UserControl
{
    public ServerMonitorView() => InitializeComponent();
    private async void CopyAddress(object? sender, RoutedEventArgs e)
    { if (DataContext is ServerMonitorViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(vm.Address); }
}
