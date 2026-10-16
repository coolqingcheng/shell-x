using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ShellX.Infrastructure.Persistence;

internal sealed class JsonStore<T>(string path, Func<T> defaultValue, JsonTypeInfo<T> typeInfo)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> ReadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await ReadCoreAsync(ct); }
        finally { _gate.Release(); }
    }

    private async Task<T> ReadCoreAsync(CancellationToken ct)
    {
        if (!System.IO.File.Exists(path)) return defaultValue();
        await using var stream = System.IO.File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct)
            ?? throw new InvalidDataException($"配置文件内容为空：{path}");
    }

    public async Task WriteAsync(T value, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await WriteCoreAsync(value, ct); }
        finally { _gate.Release(); }
    }

    public async Task UpdateAsync(Func<T, T> update, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await WriteCoreAsync(update(await ReadCoreAsync(ct)), ct); }
        finally { _gate.Release(); }
    }

    private async Task WriteCoreAsync(T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, typeInfo, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            System.IO.File.Move(temporary, path, true);
        }
        finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
    }
}
