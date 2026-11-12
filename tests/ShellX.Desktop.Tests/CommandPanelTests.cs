using ShellX.Desktop.Features.Commands;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Core.Files;

namespace ShellX.Desktop.Tests;
public class CommandPanelTests
{
    [Fact]
    public async Task SendingCommandsKeepsHistoryAndRestoresDraft()
    {
        var sent = new List<string>();
        var vm = new CommandPanelViewModel(null!, text => { sent.Add(text); return Task.CompletedTask; }, _ => Task.CompletedTask) { IsAvailable = true };
        vm.Input = "ls -la"; await vm.SendCommand.ExecuteAsync(null);
        vm.Input = "pwd"; await vm.SendCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "ls -la", "pwd" }, sent); Assert.Equal("pwd", vm.History[0].Text); Assert.Equal("", vm.Input);
        vm.Input = "unfinished draft"; vm.PreviousHistoryCommand.Execute(null); Assert.Equal("pwd", vm.Input);
        vm.PreviousHistoryCommand.Execute(null); Assert.Equal("ls -la", vm.Input);
        vm.NextHistoryCommand.Execute(null); vm.NextHistoryCommand.Execute(null); Assert.Equal("unfinished draft", vm.Input);
    }
    [Fact]
    public async Task DisconnectedOrMultilineInputIsNotSent()
    {
        var sent = new List<string>();
        var vm = new CommandPanelViewModel(null!, text => { sent.Add(text); return Task.CompletedTask; }, _ => Task.CompletedTask);
        vm.Input = "pwd"; Assert.False(vm.SendCommand.CanExecute(null)); await vm.SendCommand.ExecuteAsync(null); Assert.Empty(sent);
        vm.IsAvailable = true; vm.Input = "pwd\nrm anything"; await vm.SendCommand.ExecuteAsync(null); Assert.Empty(sent); Assert.Empty(vm.History);
    }
    [Fact]
    public async Task DirectoryTreeLoadsLazilyAndDoesNotFollowLinks()
    {
        var reads = 0;
        var files = new CountingRemoteFileSystem(() => reads++);
        using var sink = new FileBrowserViewModel(new ShellX.Desktop.Services.DialogService(new ShellX.Infrastructure.Files.TextCodec()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FileBrowserViewModel>.Instance, new ShellX.Infrastructure.Files.LocalFileSystem());
        var node = new RemoteTreeNodeViewModel(new RemoteEntry("/", "/", true, false, 0, DateTime.UtcNow, 493, 0, 0),
            files, CancellationToken.None, sink);
        Assert.Equal(0, reads); await node.ToggleCommand.ExecuteAsync(null); Assert.Equal(1, reads);
        Assert.Equal(["file", "folder", "link"], node.Children.Select(c => c.Name).Order(StringComparer.Ordinal).ToArray());
        // 符号链接不再展开，避免目录循环。
        var link = node.Children.Single(c => c.Name == "link");
        await link.ToggleCommand.ExecuteAsync(null); Assert.Equal(1, reads); Assert.Empty(link.Children);
        // 收起再展开不会重复读取目录。
        await node.ToggleCommand.ExecuteAsync(null); await node.ToggleCommand.ExecuteAsync(null); Assert.Equal(1, reads);
    }

    /// <summary>远程文件系统替身：记录被读取了几次。</summary>
    private sealed class CountingRemoteFileSystem(Action onRead) : IRemoteFileSystem
    {
        public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            onRead();
            return Task.FromResult(new RemoteDirectory(path, [
                new("folder", "/folder", true, false, 0, DateTime.UtcNow, 493, 0, 0),
                new("link", "/link", true, true, 0, DateTime.UtcNow, 493, 0, 0),
                new("file", "/file", false, false, 0, DateTime.UtcNow, 420, 0, 0)]));
        }
        public Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(original);
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
