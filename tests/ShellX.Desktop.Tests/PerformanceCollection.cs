/// <summary>
/// 性能用例必须独占运行：并行跑别的用例会污染 CPU 与分配量的测量结果。
/// </summary>
[CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceCollection;
