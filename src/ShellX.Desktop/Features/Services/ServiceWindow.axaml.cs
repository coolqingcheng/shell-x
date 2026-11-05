using Avalonia.Controls;
using Avalonia.Threading;
using ShellX.Core.Sessions;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Services;

/// <summary>非模态的服务状态窗口：一个连接一个实例，重复打开由入口按钮激活已有窗口。</summary>
public partial class ServiceWindow : Window
{
    private readonly IRemoteSession? _session;

    // XAML 运行时加载器需要无参构造；实际窗口都通过下面带 ViewModel 的构造创建。
    public ServiceWindow() : this(new ServiceWindowViewModel(null, "", _ => Task.FromResult(true))) { }
    public ServiceWindow(ServiceWindowViewModel viewModel, IRemoteSession? session = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = $"服务状态 · {viewModel.Target}";
        WindowTitles.ApplyVersion(this);
        _session = session;
        if (session != null)
        {
            session.Disconnected += SessionDisconnected;
            Closed += (_, _) => session.Disconnected -= SessionDisconnected;
        }
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }

    // 连接断开后窗口没有可用会话，直接关闭，避免继续对失效会话执行 systemctl。
    private void SessionDisconnected(object? sender, string reason) => Dispatcher.UIThread.Post(Close);
}