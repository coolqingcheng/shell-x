using Avalonia.Controls;
using Avalonia.Interactivity;
using ShellX.Core.Connections;
using ShellX.Desktop.Features.Shell;

namespace ShellX.Desktop.Features.Connections;

public partial class ConnectionManagerWindow : Window
{
    private readonly MainWindowViewModel? _model;
    private readonly Action<ConnectionProfile>? _picked;
    private bool _completed;
    public ConnectionManagerWindow() : this(null, null) { }
    public ConnectionManagerWindow(MainWindowViewModel? model, Action<ConnectionProfile>? picked)
    {
        InitializeComponent();
        
ShellX.Desktop.Services.WindowTitles.ApplyVersion(this);
        _model = model;
        _picked = picked;
        if (model != null) DataContext = model;
        if (_model != null) _model.ConnectionPicked += Pick;
        Closed += (_, _) => { if (_model != null) _model.ConnectionPicked -= Pick; };
    }
    private void Pick(ConnectionProfile profile)
    {
        if (_completed || _picked == null) return;
        _completed = true;
        _picked(profile);
        Close();
    }
    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}