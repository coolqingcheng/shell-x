using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

public sealed class FileManagerWindowTests
{
    [AvaloniaFact]
    public async Task ToolbarOpensFileManagerInItsOwnWindowAndMainViewNoLongerEmbedsIt()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-filewindow-" + Guid.NewGuid().ToString("N"))).FullName;
        var tab = new WorkspaceTabViewModel(
            new ConnectionProfile { Name = "测试连接", Host = "127.0.0.1", Port = 22, Username = "root" },
            new UnusedFactory(), new FakeDialogs(), NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true),
            _ => Task.CompletedTask, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(root));
        var view = new WorkspaceView { DataContext = tab };
        var window = new Window { Content = view, Width = 1380, Height = 900 };
        window.Show();
        try
        {
            var button = view.FindControl<Button>("OpenFileManagerButton");
            Assert.NotNull(button);
            Assert.True(button!.IsEnabled);                                   // 没连接也能打开本地目录
            Assert.Empty(view.GetVisualDescendants().OfType<FileBrowserView>());   // 主窗口不再内嵌文件管理器

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var fileWindow = Assert.Single(window.OwnedWindows);
            var browser = fileWindow.GetVisualDescendants().OfType<FileBrowserView>().Single();
            Assert.Same(tab.Files, browser.DataContext);                      // 复用同一个 VM，状态不丢
            Assert.StartsWith("文件管理 · 测试连接", fileWindow.Title);
            Assert.NotNull(browser.FindControl<LocalFileBrowserView>("LocalPane"));
            Assert.NotNull(browser.FindControl<RemoteFileBrowserView>("RemotePane"));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));         // 重复点击只激活已有窗口
            await Task.Delay(40);
            Assert.Single(window.OwnedWindows);

            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                await Task.Delay(60);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)fileWindow.ClientSize.Width, (int)fileWindow.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(fileWindow);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "file-manager-window.png"), PngBitmapEncoderOptions.Default);
            }

            fileWindow.Close(); await Task.Delay(40);
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToArray()) owned.Close();
            window.Close();
            await tab.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    private sealed class UnusedFactory : IRemoteSessionFactory
    {
        public Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret,
            Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
            => throw new InvalidOperationException("该测试只验证界面入口，不应建立连接。");
    }

    private sealed class FakeDialogs : IDialogService
    {
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret)
            => Task.FromResult<ConnectionEditResult?>(null);
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }
}