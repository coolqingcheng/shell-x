using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Settings;

public partial class SettingsView : UserControl
{
    private McpLogWindow? _mcpLogWindow;

    public SettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => _mcpLogWindow?.Close();
    }

    private void ShowMcpLog(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (_mcpLogWindow is { IsVisible: true }) { _mcpLogWindow.Activate(); return; }
        _mcpLogWindow = new McpLogWindow();
        _mcpLogWindow.Closed += (_, _) => _mcpLogWindow = null;
        _mcpLogWindow.Show(owner);
    }

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
