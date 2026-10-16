namespace ShellX.Infrastructure.Persistence;

public sealed class AppPaths
{
    public string Root { get; }
    public AppPaths() : this(Environment.GetEnvironmentVariable("SHELLX_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellX")) { }
    public AppPaths(string root) { Root = root; Directory.CreateDirectory(root); }
    public string File(string name) => Path.Combine(Root, name);
}
