namespace ShellX.Core.Settings;

/// <summary>按连接端点保存的监控网卡选择。做成不可变并带值相等语义，整份配置可以直接比较。</summary>
public sealed class MonitorNetworkMap : IEquatable<MonitorNetworkMap>
{
    public static readonly MonitorNetworkMap Empty = new(new Dictionary<string, string>(StringComparer.Ordinal));
    private readonly Dictionary<string, string> _items;
    private MonitorNetworkMap(Dictionary<string, string> items) => _items = items;
    public static MonitorNetworkMap From(IEnumerable<KeyValuePair<string, string>>? items)
        => items == null ? Empty : new(items
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    /// <summary>取某个连接端点上次选择的网卡。</summary>
    public string? Find(string key)
        => !string.IsNullOrWhiteSpace(key) && _items.TryGetValue(key, out var value) ? value : null;
    /// <summary>记录某个连接端点选择的网卡。</summary>
    public MonitorNetworkMap With(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return this;
        var items = new Dictionary<string, string>(_items, StringComparer.Ordinal) { [key] = value };
        return new(items);
    }
    public IReadOnlyDictionary<string, string> Items => _items;
    public int Count => _items.Count;
    public bool Equals(MonitorNetworkMap? other)
        => other is not null && _items.Count == other._items.Count
            && _items.All(item => other._items.TryGetValue(item.Key, out var value) && value == item.Value);
    public override bool Equals(object? obj) => Equals(obj as MonitorNetworkMap);
    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in _items.OrderBy(item => item.Key, StringComparer.Ordinal)) hash = HashCode.Combine(hash, item.Key, item.Value);
        return hash;
    }
}
