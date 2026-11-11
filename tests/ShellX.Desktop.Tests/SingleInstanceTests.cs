using ShellX.Desktop.Services;

namespace ShellX.Desktop.Tests;

public sealed class SingleInstanceTests
{
    /// <summary>每个用例使用独立的命名，避免并行/串行执行时互相干扰。</summary>
    private static (string Mutex, string Event) NewNames()
    {
        var id = Guid.NewGuid().ToString("N");
        return ($@"Local\ShellX.Test.{id}", $@"Local\ShellX.Test.{id}.Activate");
    }

    [Fact]
    public void OnlyTheFirstInstanceIsPrimary()
    {
        var (mutexName, eventName) = NewNames();
        using var first = new SingleInstanceService(mutexName, eventName);
        Assert.True(first.IsPrimary);
        using var second = new SingleInstanceService(mutexName, eventName);
        Assert.False(second.IsPrimary);          // 已有实例在运行，第二个实例应当退出
    }

    [Fact]
    public async Task ListenerMustNotActivateWithoutRequest()
    {
        var (mutexName, eventName) = NewNames();
        using var instance = new SingleInstanceService(mutexName, eventName);
        Assert.True(instance.IsPrimary);
        var activated = 0;
        instance.Listen(() => Interlocked.Increment(ref activated));

        // 没有收到激活请求时，等待多久都不应该回调（否则窗口会不停被顶到前台且关不掉）。
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref activated));
    }

    [Fact]
    public async Task ActivationRequestReachesThePrimaryInstance()
    {
        var (mutexName, eventName) = NewNames();
        using var instance = new SingleInstanceService(mutexName, eventName);
        Assert.True(instance.IsPrimary);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.Listen(() => activated.TrySetResult());
        SingleInstanceService.RequestActivation(eventName);
        var finished = await Task.WhenAny(activated.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.Same(activated.Task, finished);
    }

    [Fact]
    public async Task DisposedInstanceDoesNotSwallowActivationRequest()
    {
        var (mutexName, eventName) = NewNames();

        // 前一个实例释放后残留的监听线程不能把激活信号吞掉。
        using (var stale = new SingleInstanceService(mutexName, eventName))
        {
            stale.Listen(() => { });
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        using var instance = new SingleInstanceService(mutexName, eventName);
        Assert.True(instance.IsPrimary);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.Listen(() => activated.TrySetResult());
        SingleInstanceService.RequestActivation(eventName);
        var finished = await Task.WhenAny(activated.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.Same(activated.Task, finished);
    }
}