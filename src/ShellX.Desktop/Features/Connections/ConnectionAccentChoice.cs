namespace ShellX.Desktop.Features.Connections;

public sealed record ConnectionAccentChoice(string Name, string? Value)
{
    public static IReadOnlyList<ConnectionAccentChoice> All { get; } =
    [
        new("不标记", ""), new("红色", "#BF6969"), new("橙色", "#B78B56"), new("绿色", "#749C7B"),
        new("蓝色", "#6B96B8"), new("紫色", "#9880B4"), new("青色", "#639E9A"), new("自定义", null)
    ];
    public static ConnectionAccentChoice Find(string value) => All.FirstOrDefault(choice => choice.Value != null && choice.Value.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? All[^1];
}
