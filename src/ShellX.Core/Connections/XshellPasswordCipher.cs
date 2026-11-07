using System.Security.Cryptography;
using System.Text;

namespace ShellX.Core.Connections;

/// <summary>
/// Xshell 7/8 保存密码的本地解密：
/// Base64(RC4(SHA256(当前 Windows SID 反转 + 用户名)) + SHA256(明文))。
/// 只有 HMAC 校验通过才会返回密码，避免把损坏数据当成密码导入。
/// </summary>
internal static class XshellPasswordCipher
{
    internal static string BuildCurrentUserKey()
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(sid)) return "";
            return new string(sid.Reverse().ToArray()) + Environment.UserName;
        }
        catch { return ""; }
    }

    internal static string? Decrypt(string encrypted, string key)
    {
        if (string.IsNullOrWhiteSpace(encrypted) || string.IsNullOrWhiteSpace(key)) return null;
        byte[] data;
        try { data = Convert.FromBase64String(encrypted.Trim()); }
        catch (FormatException) { return null; }
        if (data.Length <= 32) return null;

        var payload = data.AsSpan(0, data.Length - 32).ToArray();
        var expected = data.AsSpan(data.Length - 32).ToArray();
        var derivedKey = SHA256.HashData(Encoding.ASCII.GetBytes(key));
        try
        {
            Rc4(derivedKey, payload);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), expected)) return null;
            try { return new UTF8Encoding(false, true).GetString(payload); }
            catch (DecoderFallbackException) { return Encoding.Latin1.GetString(payload); }
        }
        catch { return null; }
        finally { CryptographicOperations.ZeroMemory(derivedKey); }
    }

    /// <summary>测试用：按 Xshell 相同格式生成可校验的密文。</summary>
    internal static string Encrypt(string password, string key)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("密码不能为空。", nameof(password));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("解密密钥不能为空。", nameof(key));
        var payload = Encoding.UTF8.GetBytes(password);
        var cipher = payload.ToArray();
        var hash = SHA256.HashData(payload);
        var derivedKey = SHA256.HashData(Encoding.ASCII.GetBytes(key));
        byte[] result = [];
        try
        {
            Rc4(derivedKey, cipher);
            result = new byte[cipher.Length + hash.Length];
            cipher.CopyTo(result, 0);
            hash.CopyTo(result, cipher.Length);
            return Convert.ToBase64String(result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(hash);
            CryptographicOperations.ZeroMemory(derivedKey);
            if (result.Length > 0) CryptographicOperations.ZeroMemory(result);
        }
    }

    private static void Rc4(byte[] key, byte[] data)
    {
        var state = new byte[256];
        for (var i = 0; i < state.Length; i++) state[i] = (byte)i;
        var j = 0;
        for (var i = 0; i < state.Length; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 255;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var x = 0;
        j = 0;
        for (var k = 0; k < data.Length; k++)
        {
            x = (x + 1) & 255;
            j = (j + state[x]) & 255;
            (state[x], state[j]) = (state[j], state[x]);
            data[k] ^= state[(state[x] + state[j]) & 255];
        }
    }
}