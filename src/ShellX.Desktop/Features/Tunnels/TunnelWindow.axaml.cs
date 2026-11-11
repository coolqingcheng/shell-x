using Avalonia.Controls;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Tunnels;

/// <summary>非模态的端口转发窗口：一个连接一个实例，重复打开由入口按钮激活已有窗口。</summary>
public partial class TunnelWindow : Window
{
    // XAML 运行时加载器需要无参构造；实际窗口都通过带 ViewModel 的构造创建。
    public TunnelWindow() { InitializeComponent(); WindowTitles.ApplyVersion(this); }

    public TunnelWindow(TunnelWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = $"端口转发 · {viewModel.Target}";
        WindowTitles.ApplyVersion(this);
        Closed += (_, _) => viewModel.Dispose();
    }
}
