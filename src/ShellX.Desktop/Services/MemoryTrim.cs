namespace ShellX.Desktop.Services;

/// <summary>
/// 连接全部关掉后把内存还给系统。GC 默认只把内存留在堆里等着复用，
/// 任务管理器里的工作集不会立刻变小，看着就像"连接关了内存还在"。
/// </summary>
public static class MemoryTrim
{
    public static void Release()
    {
        try
        {
            // 强制回收并整理堆，同时把空闲页交还给系统。
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        catch (Exception)
        {
            // 回收失败不影响使用，忽略即可。
        }
    }
}
