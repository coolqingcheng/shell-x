using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;

namespace ShellX.Desktop.Features.Workspace;
public partial class WorkspaceView : UserControl
{
    private WorkspaceTabViewModel? _vm;
    private GridLength _top = new(1.3, GridUnitType.Star);
    private GridLength _bottom = new(1, GridUnitType.Star);
    private bool _maximized;
    public WorkspaceView()
    {
        InitializeComponent();
        Terminal.AddHandler(PointerWheelChangedEvent, TerminalWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => { if (_vm != null) _vm.PropertyChanged -= VmChanged; _vm = null; };
    }
    private void Subscribe()
    {
        if (_vm != null) _vm.PropertyChanged -= VmChanged;
        _vm = DataContext as WorkspaceTabViewModel;
        if (_vm != null) { _vm.PropertyChanged += VmChanged; UpdateLayoutMode(); }
    }
    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(WorkspaceTabViewModel.IsTerminalMaximized)) UpdateLayoutMode(); }
    private void UpdateLayoutMode()
    {
        var maximize = _vm?.IsTerminalMaximized == true;
        if (maximize == _maximized) return;
        if (maximize)
        {
            _top = WorkspaceLayout.RowDefinitions[0].Height; _bottom = WorkspaceLayout.RowDefinitions[2].Height;
            WorkspaceLayout.RowDefinitions[0].Height = new(1, GridUnitType.Star);
            WorkspaceLayout.RowDefinitions[1].Height = new(0); WorkspaceLayout.RowDefinitions[2].Height = new(0);
        }
        else
        {
            WorkspaceLayout.RowDefinitions[0].Height = _top; WorkspaceLayout.RowDefinitions[1].Height = new(5); WorkspaceLayout.RowDefinitions[2].Height = _bottom;
        }
        _maximized = maximize;
    }
    private async void CommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        if (e.Key == Key.Enter) { e.Handled = true; await _vm.Commands.SendCommand.ExecuteAsync(null); }
        else if (e.Key == Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = true; _vm.Commands.PreviousHistoryCommand.Execute(null); }
        else if (e.Key == Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = true; _vm.Commands.NextHistoryCommand.Execute(null); }
        else if (e.Key == Key.Escape) { e.Handled = true; Terminal.FocusTerminal(); }
    }
    private void TerminalWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm == null || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0) return;
        if (e.Delta.Y > 0) _vm.IncreaseFontCommand.Execute(null);
        else _vm.DecreaseFontCommand.Execute(null);
        e.Handled = true;
    }
    private async void CopyTerminal(object? sender, RoutedEventArgs e) => await Terminal.CopyAsync();
    private async void PasteTerminal(object? sender, RoutedEventArgs e) => await Terminal.PasteAsync();
    private void ClearTerminal(object? sender, RoutedEventArgs e) => Terminal.ClearScreen();
    private void UseHistory(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: string command }) return;
        _vm.Commands.FillCommand.Execute(command); HistoryButton.Flyout?.Hide(); CommandInput.Focus();
    }
}
