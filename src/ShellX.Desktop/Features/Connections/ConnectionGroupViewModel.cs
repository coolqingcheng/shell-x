using System.Collections.ObjectModel;

namespace ShellX.Desktop.Features.Connections;

public sealed class ConnectionGroupViewModel(string path, string name, int count)
{
    public string Path => path;
    public string Name => name;
    public string Count => count.ToString();
    public ObservableCollection<ConnectionGroupViewModel> Children { get; } = [];
}
