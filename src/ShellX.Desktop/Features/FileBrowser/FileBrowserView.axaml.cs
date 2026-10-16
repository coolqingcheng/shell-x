using Avalonia.Controls;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.Shell;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class FileBrowserView : UserControl
{
    private MainWindowViewModel? _layoutVm;
    public FileBrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachLayout();
        AttachedToVisualTree += (_, _) => AttachLayout();
        DetachedFromVisualTree += (_, _) => DetachLayout();
    }
    private void AttachLayout()
    {
        var vm = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
        if (ReferenceEquals(_layoutVm, vm)) return;
        DetachLayout();
        _layoutVm = vm;
        if (_layoutVm == null) return;
        _layoutVm.LayoutChanged += ApplyLayout;
        ApplyLayout(_layoutVm.LayoutSettings);
    }
    private void DetachLayout()
    {
        if (_layoutVm != null) _layoutVm.LayoutChanged -= ApplyLayout;
        _layoutVm = null;
    }
    private void ApplyLayout(AppSettings settings)
    {
        var local = Math.Clamp(settings.LocalPaneStar, 0.3, 8);
        FileLayout.ColumnDefinitions[0].Width = new(local, GridUnitType.Star);
        FileLayout.ColumnDefinitions[2].Width = new(1, GridUnitType.Star);
    }
    private async void FileSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (_layoutVm == null) return;
        var local = FileLayout.ColumnDefinitions[0].Width;
        var remote = FileLayout.ColumnDefinitions[2].Width;
        await _layoutVm.SaveFilePaneLayoutAsync(local.GridUnitType == GridUnitType.Star ? local.Value : 1,
            remote.GridUnitType == GridUnitType.Star ? remote.Value : 1);
    }
}