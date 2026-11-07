using Avalonia.Controls;
using Avalonia.Interactivity;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Features.Workspace;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>统一文件管理器窗口的连接页签；关闭页签只移除文件标签，不关闭终端连接。</summary>
public partial class FileManagerWindowView : UserControl
{
    public FileManagerWindowView() => InitializeComponent();

    private void CloseFileManagerTab(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WorkspaceTabViewModel tab } && DataContext is MainWindowViewModel vm)
            vm.CloseFileManagerTab(tab);
    }
}