using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.Tunnels;

namespace ShellX.Desktop.Tests;

public sealed class TunnelViewModelTests
{
    [Fact]
    public void ConnectionFailureKeepsStopAvailableAndShowsItsOwnReason()
    {
        var definition = new TunnelDefinition();
        var vm = new TunnelViewModel(definition, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        vm.Apply(new(definition.Id, TunnelState.Connected, true, "目标 TCP 可达。", DateTimeOffset.Now));
        Assert.Equal("#73C991", vm.StatusColor); Assert.Equal("停止", vm.ActionText); Assert.True(vm.CheckCommand.CanExecute(null));
        vm.Apply(new(definition.Id, TunnelState.Failed, true, "目标端口拒绝连接。", DateTimeOffset.Now));
        Assert.Equal("#F48771", vm.StatusColor); Assert.Equal("连接异常", vm.Status); Assert.True(vm.HasReason);
        Assert.Equal("停止", vm.ActionText); Assert.True(vm.ToggleCommand.CanExecute(null));
        vm.Apply(new(Guid.NewGuid(), TunnelState.Connected, true));
        Assert.Equal("目标端口拒绝连接。", vm.Reason);
        vm.Apply(new(definition.Id, TunnelState.Stopped, false));
        Assert.Equal("#858585", vm.StatusColor); Assert.Equal("启动", vm.ActionText); Assert.False(vm.CheckCommand.CanExecute(null));
    }
    [Fact]
    public void StartingAndCheckingBlockRepeatedActionsAndUnverifiedIsNotGreen()
    {
        var definition = new TunnelDefinition();
        var vm = new TunnelViewModel(definition, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        vm.Apply(new(definition.Id, TunnelState.Starting, false));
        Assert.False(vm.ToggleCommand.CanExecute(null)); Assert.False(vm.RemoveCommand.CanExecute(null));
        Assert.False(vm.EditCommand.CanExecute(null));
        vm.Apply(new(definition.Id, TunnelState.Checking, true));
        Assert.False(vm.CheckCommand.CanExecute(null)); Assert.False(vm.ToggleCommand.CanExecute(null));
        vm.Apply(new(definition.Id, TunnelState.Unverified, true, "服务端限制检测通道。"));
        Assert.Equal("#D7BA7D", vm.StatusColor); Assert.True(vm.CheckCommand.CanExecute(null));
        Assert.True(vm.EditCommand.CanExecute(null));
    }
    [Fact]
    public void EditingKeepsRuleIdentityAndPrefillsBothForwardingDirections()
    {
        var original = new TunnelDefinition { Name = "原隧道", Direction = TunnelDirection.Remote, BindHost = "0.0.0.0", BindPort = 10022, TargetHost = "localhost", TargetPort = 22 };
        var editor = new TunnelEditorViewModel(original);
        Assert.Equal(original, editor.Build());
        editor.Name = "  新隧道  "; editor.DirectionIndex = 0; editor.BindPort = "10023"; editor.TargetPort = "2222";
        var changed = editor.Build(); Assert.Equal(original.Id, changed.Id); Assert.Equal("新隧道", changed.Name);
        Assert.Equal(TunnelDirection.Local, changed.Direction); Assert.Equal(10023, changed.BindPort); Assert.Equal(2222, changed.TargetPort);
        editor.BindPort = "65536"; Assert.Throws<ArgumentException>(() => editor.Build());
        editor.BindPort = "1234"; editor.DirectionIndex = -1; Assert.Throws<ArgumentException>(() => editor.Build());
    }
}
