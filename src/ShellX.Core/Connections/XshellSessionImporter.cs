using System.Globalization;
using System.Text;

namespace ShellX.Core.Connections;

/// <summary>
/// 读取 Xshell 的 .xsh 会话文件并转换为 ShellX 连接。
/// 只导入 SSH 主机、账号、端口、初始目录和可解析的私钥路径；密码始终不读取、不导入。
/// </summary>
public static class XshellSessionImporter
{
    public const string DefaultGroup = "Xshell";

    public static ConnectionProfile ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择 Xshell 会话文件。", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Xshell 会话文件不存在。", path);
        var content = Decode(File.ReadAllBytes(path));
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        return Parse(content, name, Path.GetDirectoryName(path));
    }

    public static ConnectionProfile Parse(string content, string sourceName, string? sourceDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("文件内容为空，不是有效的 Xshell 会话。");
        var sections = ParseSections(content);
        if (!sections.TryGetValue("CONNECTION", out var connection) || connection.Count == 0)
            throw new InvalidDataException("没有找到 [CONNECTION] 配置段，不是有效的 Xshell 会话。");

        var protocol = GetValue(connection, "Protocol").Trim();
        if (protocol.Length == 0 || !protocol.Equals("SSH", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"仅支持导入 SSH 会话，当前协议为“{(protocol.Length == 0 ? "未知" : protocol)}”。");

        var host = GetValue(connection, "Host").Trim();
        if (host.Length == 0) throw new InvalidDataException("Xshell 会话没有主机地址。");
        if (!int.TryParse(GetValue(connection, "Port").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new InvalidDataException($"Xshell 会话端口无效：“{GetValue(connection, "Port")}”。");

        sections.TryGetValue("CONNECTION:AUTHENTICATION", out var authentication);
        sections.TryGetValue("CONNECTION:SSH", out var ssh);
        authentication ??= [];
        ssh ??= [];

        var username = GetValue(authentication, "UserName").Trim();
        if (username.Length == 0) username = "root";
        var userKey = GetValue(authentication, "UserKey").Trim();
        var methods = GetValue(authentication, "AuthMethodList").Trim();
        var keyPath = userKey.Length > 0 && (methods.Length == 0 || methods.Contains("11", StringComparison.Ordinal))
            ? ResolvePrivateKey(userKey, sourceDirectory)
            : null;

        var initialDirectory = GetValue(ssh, "InitRemoteDirectory").Trim();
        var name = sourceName.Trim();
        if (name.Length == 0) name = host;

        return new ConnectionProfile
        {
            Name = name,
            Group = DefaultGroup,
            Host = host,
            Port = port,
            Username = username,
            Authentication = keyPath == null ? AuthenticationKind.Password : AuthenticationKind.PrivateKey,
            PrivateKeyPath = keyPath ?? "",
            RememberSecret = false,
            InitialDirectory = initialDirectory.Length == 0 ? "." : initialDirectory
        };
    }

    /// <summary>将 Xshell 里的 UserKey 名称解析为本机可读取的私钥文件；找不到时返回 null，由连接时再选择。</summary>
    public static string? ResolvePrivateKey(string userKey, string? sourceDirectory = null)
    {
        var value = Environment.ExpandEnvironmentVariables(userKey.Trim());
        if (value.Length == 0) return null;
        var candidates = new List<string>();
        if (Path.IsPathRooted(value)) candidates.Add(value);
        else
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(sourceDirectory)) candidates.Add(Path.Combine(sourceDirectory, value));
            candidates.Add(Path.Combine(profile, ".ssh", value));
            var netSarang = Path.Combine(profile, "Documents", "NetSarang Computer");
            if (Directory.Exists(netSarang))
            {
                foreach (var version in Directory.EnumerateDirectories(netSarang).Take(8))
                {
                    var profiles = Path.Combine(version, "Xshell", "Profiles");
                    if (!Directory.Exists(profiles)) continue;
                    foreach (var keys in Directory.EnumerateDirectories(profiles).Take(8).Select(p => Path.Combine(p, "Keys")))
                        candidates.Add(Path.Combine(keys, value));
                }
            }
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static Dictionary<string, Dictionary<string, string>> ParseSections(string content)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = "";
        foreach (var raw in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                current = line[1..^1].Trim();
                if (!result.ContainsKey(current)) result[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var separator = line.IndexOf('=');
            if (separator <= 0 || current.Length == 0) continue;
            if (!result.TryGetValue(current, out var values)) result[current] = values = new(StringComparer.OrdinalIgnoreCase);
            values[line[..separator].Trim()] = line[(separator + 1)..];
        }
        return result;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : "";

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // 无 BOM 但按 UTF-16 保存的旧 Xshell 文件：偶数位多数为 0 时按 UTF-16 解码。
        if (bytes.Length >= 4 && bytes[1] == 0 && bytes[3] == 0)
        {
            var zeros = 0;
            for (var i = 1; i < bytes.Length; i += 2) if (bytes[i] == 0) zeros++;
            if (zeros * 4 >= bytes.Length) return Encoding.Unicode.GetString(bytes);
        }
        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);   // 仅用于兼容旧 ANSI 文件中的 ASCII 主机与账号字段
        }
    }
}