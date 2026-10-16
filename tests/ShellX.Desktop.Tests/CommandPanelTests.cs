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
        var reads = 0; string? opened = null;
        var node = new DirectoryNodeViewModel("/", "/", _ =>
        {
            reads++;
            return Task.FromResult<IReadOnlyList<RemoteEntry>>([
                new("folder", "/folder", true, false, 0, DateTime.UtcNow, 493, 0, 0),
                new("link", "/link", true, true, 0, DateTime.UtcNow, 493, 0, 0),
                new("file", "/file", false, false, 0, DateTime.UtcNow, 420, 0, 0)]);
        }, path => { opened = path; return Task.CompletedTask; }, error => throw new Exception(error));
        Assert.Equal(0, reads); await node.ToggleCommand.ExecuteAsync(null); Assert.Equal(1, reads);
        Assert.Equal("folder", Assert.Single(node.Children).Name);
        await node.Children[0].OpenCommand.ExecuteAsync(null); Assert.Equal("/folder", opened);
        await node.ToggleCommand.ExecuteAsync(null); await node.ToggleCommand.ExecuteAsync(null); Assert.Equal(1, reads);
    }
}
