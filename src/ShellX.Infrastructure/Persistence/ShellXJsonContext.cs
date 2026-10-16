using System.Text.Json.Serialization;
using ShellX.Core.Connections;
using ShellX.Core.Security;

namespace ShellX.Infrastructure.Persistence;

internal sealed record ConnectionConfiguration(int Version, List<StoredConnection> Connections)
{
    public List<string> Folders { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConnectionConfiguration))]
[JsonSerializable(typeof(StoredSettings))]
[JsonSerializable(typeof(ExportedConfiguration))]
[JsonSerializable(typeof(List<KnownHost>))]
[JsonSerializable(typeof(ConnectionSecret))]
internal partial class ShellXJsonContext : JsonSerializerContext;
