namespace ShellX.Core.Commands;

public sealed record SavedCommand
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
}
