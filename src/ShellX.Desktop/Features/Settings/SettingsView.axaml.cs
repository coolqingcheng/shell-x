using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace ShellX.Desktop.Features.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView() { InitializeComponent(); }

    private void GenerateMcpToken(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.GenerateMcpTokenCommand.Execute(null);
    }

    private async void CopyMcpJsonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(vm.McpJson);
        if (sender is Button button)
        {
            button.Content = "已复制";
            await Task.Delay(1200);
            button.Content = "复制 MCP JSON";
        }
    }
}