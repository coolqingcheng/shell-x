using Avalonia.Controls;
using Avalonia.Interactivity;
using ShellX.Desktop.Features.Shell;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>统一文件管理器窗口的连接页签；同一连接可有多个独立编号页签。</summary>
public partial class FileManagerWindowView : UserControl
{
    public FileManagerWindowView() => InitializeComponent();

    private async void AddFileManagerTabClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.AddFileManagerTabAsync();
    }
}