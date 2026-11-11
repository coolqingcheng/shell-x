using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

/// <summary>批量上传/下载的覆盖确认：勾选“保持选择”后同批次只确认一次。</summary>
public sealed class TransferConfirmTests
{
    [AvaloniaFact]
    public async Task BatchUploadConfirmsOnceWhenKeepSelectionIsChecked()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-upload-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var names = new[] { "a.txt", "b.txt", "c.txt" };
            foreach (var name in names) await File.WriteAllTextAsync(Path.Combine(root, name), "x");
            var dialogs = new RecordingDialogs { Outcome = new ConfirmOutcome(true, true) };
            using var browser = new FileBrowserViewModel(dialogs, NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
            browser.Attach(new FakeRemoteFileSystem(names), CancellationToken.None, null, "测试");

            await browser.UploadPathsAsync(names.Select(name => Path.Combine(root, name)).ToArray());
            await browser.DrainTransfersAsync();

            Assert.Equal(1, dialogs.ConfirmCalls);          // 勾选“保持选择”后只确认一次
            Assert.Equal(names.Length, browser.Transfers.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task BatchUploadConfirmsEveryFileWithoutKeepSelection()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-upload-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var names = new[] { "a.txt", "b.txt", "c.txt" };
            foreach (var name in names) await File.WriteAllTextAsync(Path.Combine(root, name), "x");
            var dialogs = new RecordingDialogs { Outcome = new ConfirmOutcome(true, false) };
            using var browser = new FileBrowserViewModel(dialogs, NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
            browser.Attach(new FakeRemoteFileSystem(names), CancellationToken.None, null, "测试");

            await browser.UploadPathsAsync(names.Select(name => Path.Combine(root, name)).ToArray());
            await browser.DrainTransfersAsync();

            Assert.Equal(names.Length, dialogs.ConfirmCalls);   // 没勾选时逐个确认
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task OverwriteDialogShowsKeepSelectionCheckbox()
    {
        var owner = new Window(); owner.Show();
        var dialogs = new DialogService(new TextCodec()) { Owner = owner };
        try
        {
            var pending = dialogs.ConfirmWithRememberAsync("覆盖文件", "远程文件已存在，是否覆盖？");
            await Task.Delay(80);
            var window = owner.OwnedWindows.OfType<Window>().Single();
            var check = window.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.Contains("保持选择", check.Content?.ToString() ?? "");
            check.IsChecked = true;
            var confirm = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == "确认操作" || button.Content?.ToString() == "确认");
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var outcome = await pending;
            Assert.True(outcome.Confirmed);
            Assert.True(outcome.Remember);
        }
        finally { owner.Close(); }
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public ConfirmOutcome Outcome { get; set; } = new(false, false);
        public int ConfirmCalls { get; private set; }
        public Task<ConfirmOutcome> ConfirmWithRememberAsync(string title, string message, bool danger = false)
        {
            ConfirmCalls++;
            return Task.FromResult(Outcome);
        }
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
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

    private sealed class FakeRemoteFileSystem(string[] existing) : IRemoteFileSystem
    {
        public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new RemoteDirectory(path, []));
        public Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default) => Task.FromResult(original);
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(existing.Any(name => path.EndsWith(name, StringComparison.Ordinal)));
    }
}