using Avalonia.Controls;
using Avalonia.Threading;
using ShellX.Core.Sessions;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Ports;

/// <summary>非模态的端口占用窗口：一个连接一个实例，重复打开由入口按钮激活已有窗口。</summary>
public partial class PortUsageWindow : Window
{
    private readonly IRemoteSession? _session;

    // XAML 运行时加载器需要无参构造；实际窗口都通过带 ViewModel 的构造创建。
    public PortUsageWindow() : this(new PortUsageWindowViewModel(null, "")) { }

    public PortUsageWindow(PortUsageWindowViewModel viewModel, IRemoteSession? session = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = $"端口占用 · {viewModel.Target}";
        WindowTitles.ApplyVersion(this);
        _session = session;
        if (session != null)
        {
            session.Disconnected += SessionDisconnected;
            Closed += (_, _) => session.Disconnected -= SessionDisconnected;
        }
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }

    // 连接断开后窗口没有可用会话，直接关闭，避免继续对失效会话发起查询。
    private void SessionDisconnected(object? sender, string reason) => Dispatcher.UIThread.Post(Close);
}