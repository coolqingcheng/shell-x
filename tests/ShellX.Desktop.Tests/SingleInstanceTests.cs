using ShellX.Desktop.Services;

namespace ShellX.Desktop.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void OnlyTheFirstInstanceIsPrimary()
    {
        using var first = new SingleInstanceService();
        Assert.True(first.IsPrimary);
        using var second = new SingleInstanceService();
        Assert.False(second.IsPrimary);          // 已有实例在运行，第二个实例应当退出
    }

    [Fact]
    public async Task ActivationRequestReachesThePrimaryInstance()
    {
        using var instance = new SingleInstanceService();
        Assert.True(instance.IsPrimary);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.Listen(() => activated.TrySetResult());
        SingleInstanceService.RequestActivation();
        var finished = await Task.WhenAny(activated.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.Same(activated.Task, finished);
    }
}
