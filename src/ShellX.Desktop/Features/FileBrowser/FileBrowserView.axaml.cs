using Avalonia.Controls;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.Shell;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class FileBrowserView : UserControl
{
    private MainWindowViewModel? _layoutVm;
    private FileBrowserViewModel? _transferVm;
    public FileBrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { AttachLayout(); AttachTransfers(); };
        AttachedToVisualTree += (_, _) => { AttachLayout(); AttachTransfers(); };
        DetachedFromVisualTree += (_, _) => { DetachLayout(); DetachTransfers(); };
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
    private void AttachTransfers()
    {
        if (ReferenceEquals(_transferVm, DataContext as FileBrowserViewModel)) return;
        DetachTransfers();
        _transferVm = DataContext as FileBrowserViewModel;
        if (_transferVm != null) _transferVm.PropertyChanged += TransferVmChanged;
        ApplyTransferPanel();
    }

    private void DetachTransfers()
    {
        if (_transferVm != null) _transferVm.PropertyChanged -= TransferVmChanged;
        _transferVm = null;
    }

    private void TransferVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileBrowserViewModel.ShowTransfers) or nameof(FileBrowserViewModel.TransferPanelHeight))
            ApplyTransferPanel();
    }

    private void ApplyTransferPanel()
    {
        if (RootLayout is null || RootLayout.RowDefinitions.Count < 3) return;
        var show = _transferVm?.ShowTransfers ?? false;
        RootLayout.RowDefinitions[1].Height = show ? new(5, GridUnitType.Pixel) : new(0, GridUnitType.Pixel);
        RootLayout.RowDefinitions[2].Height = show
            ? new(_transferVm?.TransferPanelHeight ?? 180, GridUnitType.Pixel)
            : new(30, GridUnitType.Pixel);
    }

    private void TransferSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (_transferVm == null || RootLayout.RowDefinitions.Count < 3) return;
        var height = RootLayout.RowDefinitions[2].Height;
        if (height.IsAbsolute) _transferVm.SetTransferPanelHeight(height.Value);
        ApplyTransferPanel();
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