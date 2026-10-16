using Avalonia.Controls;
using Avalonia.Interactivity;
using ShellX.Core.Connections;
using ShellX.Desktop.Features.Shell;

namespace ShellX.Desktop.Features.Connections;
public partial class ConnectionManagerView : UserControl
{
    public ConnectionManagerView() => InitializeComponent();
    private void PickConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) vm.PickConnectionCommand.Execute(profile); }
    private async void EditConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.EditConnectionCommand.ExecuteAsync(profile); }
    private async void DeleteConnection(object? sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: ConnectionProfile profile } && DataContext is MainWindowViewModel vm) await vm.DeleteConnectionCommand.ExecuteAsync(profile); }
}
