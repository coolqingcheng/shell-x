using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Desktop.Styles;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Desktop.Tests;

/// <summary>主窗口顶部：图标按钮的悬浮背景是正方形且不贴边，标签的关闭按钮固定在右侧。</summary>
public sealed class MainWindowHeaderTests
{
    [AvaloniaFact]
    public async Task HeaderIconButtonsHaveSquareHoverAreaAndTabCloseButtonSitsAtTheRightEdge()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-header-" + Guid.NewGuid().ToString("N"))).FullName;
        MainWindowViewModel? vm = null;
        Window? window = null;
        try
        {
            var profile = new ConnectionProfile { Name = "短名字", Host = "127.0.0.1", Port = 22, Username = "root" };
            var dialogs = new Dialogs();
            vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), new Settings(), new SshSessionFactory(NullLoggerFactory.Instance),
                dialogs, NullLoggerFactory.Instance, new LocalFileSystem(root), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
            window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
            window.Show();
            await Task.Delay(120);

            // 顶部图标按钮：悬浮背景是正方形，左右都留出间距，不再和相邻元素贴在一起。
            foreach (var key in new[] { "OpenConnectionManager", "ResetLayout" })
            {
                var button = window.FindControl<Button>(key)!;
                Assert.Equal(button.Bounds.Width, button.Bounds.Height);
                Assert.True(button.Bounds.Width >= 24 && button.Bounds.Width <= 32, $"{key} 的悬浮背景不是正方形：{button.Bounds}");
            }
            var manager = window.FindControl<Button>("OpenConnectionManager")!;
            var tabs = window.FindControl<ListBox>("ConnectionTabs")!;
            var header = (Control)manager.Parent!;
            var iconRight = manager.TranslatePoint(new Point(manager.Bounds.Width, 0), window)!.Value.X;
            var tabsLeft = tabs.TranslatePoint(new Point(0, 0), window)!.Value.X;
            var headerLeft = header.TranslatePoint(new Point(0, 0), window)!.Value.X;
            var iconLeft = manager.TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.True(iconLeft - headerLeft >= 5, $"连接管理器图标贴住了左侧分隔线：{iconLeft - headerLeft}");
            Assert.True(tabsLeft - iconRight >= 5, $"连接管理器图标和标签挨太近：{tabsLeft - iconRight}");

            // 标签：关闭按钮固定在标签最右侧，标题与关闭按钮之间留出空白。
            var tab = new WorkspaceTabViewModel(profile, new SshSessionFactory(NullLoggerFactory.Instance), dialogs, NullLoggerFactory.Instance,
                () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true), _ => Task.CompletedTask, _ => Task.CompletedTask,
                new AppSettings(), new LocalFileSystem(root));
            vm.Tabs.Add(tab);
            vm.SelectedTab = tab;                       // 选中后标签才是激活态，预览图才能看到高亮条
            await Task.Delay(150);
            var item = tabs.GetVisualDescendants().OfType<ListBoxItem>().Single();
            var close = item.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, tab.CloseCommand));
            var title = item.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "短名字");
            var closeLeft = close.TranslatePoint(new Point(0, 0), item)!.Value.X;
            var closeRight = close.TranslatePoint(new Point(close.Bounds.Width, 0), item)!.Value.X;
            var titleLeft = title.TranslatePoint(new Point(0, 0), item)!.Value.X;
            // 标题文本块会被拉伸到标签右侧，按实际文字宽度算中间的空白。
            title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var textWidth = title.DesiredSize.Width;
            Assert.True(item.Bounds.Width - closeRight < 12, $"关闭按钮没有贴到标签右侧：{item.Bounds.Width - closeRight}");
            Assert.True(closeLeft - titleLeft - textWidth > 20, $"关闭按钮离标题太近：{closeLeft - titleLeft - textWidth}");
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, 120), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "main-header.png"), PngBitmapEncoderOptions.Default);

                // 悬浮背景是不是正方形：强制一下悬浮状态再截图。
                var presenter = manager.GetVisualDescendants().OfType<ContentPresenter>().First();
                presenter.Background = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#FF00AA"));
                window.UpdateLayout();
                using var hover = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, 48), new Vector(96, 96));
                hover.Render(window);
                hover.Save(Path.Combine(Path.GetDirectoryName(preview)!, "main-header-hover.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { if (vm != null) await vm.ShutdownAsync(); window?.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task IconButtonsAreSquareAndInputTextIsVerticallyCentered()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-icon-" + Guid.NewGuid().ToString("N"))).FullName;
        MainWindowViewModel? vm = null;
        Window? window = null;
        try
        {
            var profile = new ConnectionProfile { Name = "短名字", Host = "127.0.0.1", Port = 22, Username = "root" };
            var dialogs = new Dialogs();
            vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), new Settings(), new SshSessionFactory(NullLoggerFactory.Instance),
                dialogs, NullLoggerFactory.Instance, new LocalFileSystem(root), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
            window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
            window.Show();
            var tab = new WorkspaceTabViewModel(profile, new SshSessionFactory(NullLoggerFactory.Instance), dialogs, NullLoggerFactory.Instance,
                () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true), _ => Task.CompletedTask, _ => Task.CompletedTask,
                new AppSettings(), new LocalFileSystem(root));
            vm.Tabs.Add(tab);
            vm.SelectedTab = tab;
            await Task.Delay(250);

            // 纯图标按钮都是正方形，悬浮背景铺满整个按钮。
            var icons = window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("icon")).ToList();
            Assert.NotEmpty(icons);
            foreach (var button in icons)
            {
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0, $"图标按钮没有布局：{button.Name}");
                Assert.True(button.Bounds.Width == button.Bounds.Height, $"图标按钮不是正方形：{button.Name} {button.Bounds} | {string.Join(", ", icons.Select(b => (b.Name ?? "?") + "=" + b.Bounds))}");
                Assert.Equal(new Thickness(0), button.Padding);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, button.HorizontalContentAlignment);
                Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, button.VerticalContentAlignment);
                // 模板里的内容呈现器就是悬浮背景的画布区。
                Assert.NotNull(button.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault());
            }

            // 输入框文字垂直居中。
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(1380, 900), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "main-window.png"), PngBitmapEncoderOptions.Default);
            }

            var input = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.Name == "CommandInput");
            Assert.NotNull(input);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, input!.VerticalContentAlignment);
            var search = new TextBox();
            var combo = new ComboBox();
            var probe = new Window { Content = new StackPanel { Children = { search, combo } } };
            probe.Show();
            await Task.Delay(60);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, search.VerticalContentAlignment);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, combo.VerticalContentAlignment);
            probe.Close();
        }
        finally { if (vm != null) await vm.ShutdownAsync(); window?.Close(); Directory.Delete(root, true); }
    }

    /// <summary>右上角水滴按钮：列出全部主题，选中后立即切换并写回配置。</summary>
    [AvaloniaFact]
    public async Task ThemeSwitchButtonListsEveryThemeAndAppliesTheSelectedOne()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-theme-switch-" + Guid.NewGuid().ToString("N"))).FullName;
        MainWindowViewModel? vm = null;
        Window? window = null;
        var store = new Settings();
        try
        {
            var profile = new ConnectionProfile { Name = "短名字", Host = "127.0.0.1", Port = 22, Username = "root" };
            var dialogs = new Dialogs();
            vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), store, new SshSessionFactory(NullLoggerFactory.Instance),
                dialogs, NullLoggerFactory.Instance, new LocalFileSystem(root), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
            window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
            window.Show();
            await Task.Delay(120);

            var button = window.FindControl<Button>("ThemeSwitch");
            Assert.NotNull(button);
            // 主题按钮在设置按钮左边，位置不能互相压住。
            var settings = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.Command == vm.SettingsCommand);
            Assert.True(button!.TranslatePoint(new Point(0, 0), window)!.Value.X < settings.TranslatePoint(new Point(0, 0), window)!.Value.X);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var menu = button.ContextMenu;
            Assert.NotNull(menu);
            var menuItems = menu!.Items;
            Assert.Equal(ThemeCatalog.All.Count, menuItems.Count);
            // 菜单标题要写主题名，当前主题打勾。
            Assert.Equal(ThemeCatalog.All.Select(ThemeCatalog.DisplayName),
                menuItems.OfType<MenuItem>().Select(item => item.GetVisualDescendants().OfType<TextBlock>().First().Text!));

            var target = ThemeCatalog.All[2];
            var item = (MenuItem)menuItems[2]!;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(150);
            Assert.Equal(target, ThemeService.Current);
            Assert.Equal(target, store.Value.Theme);
        }
        finally
        {
            ThemeService.Apply(AppTheme.Dark);
            if (vm != null) await vm.ShutdownAsync();
            window?.Close();
            Directory.Delete(root, true);
        }
    }

    private sealed class Connections(ConnectionProfile profile) : IConnectionRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);
        public Task SaveAsync(IReadOnlyList<ConnectionProfile> connections, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Credentials : ICredentialStore
    {
        public Task<ConnectionSecret?> ReadAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ConnectionSecret?>(null);
        public Task SaveAsync(Guid id, ConnectionSecret secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Hosts : IHostKeyStore
    {
        public Task<string?> GetAsync(string host, int port, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task TrustAsync(HostKeyChallenge host, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Settings : ISettingsStore
    {
        public AppSettings Value { get; private set; } = new();
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) { Value = settings; return Task.CompletedTask; }
    }
    private sealed class Dialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }
}
