using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShellX.Core.Connections;

namespace ShellX.Infrastructure.Persistence;

internal sealed class ExportedCredentialVault
{
    public string Format { get; set; } = "ShellX.Credentials";
    public int Version { get; set; } = 1;
    public string Algorithm { get; set; } = "AES-256-GCM/PBKDF2-SHA256";
    public int Iterations { get; set; } = ConfigurationCredentialCrypto.Iterations;
    public string Salt { get; set; } = "";
    public string Nonce { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Data { get; set; } = "";
}

internal sealed record ExportedCredential(Guid Id, string Password, string Passphrase);

/// <summary>
/// 备份文件中的凭据加密：PBKDF2-SHA256 派生密钥，AES-256-GCM 认证加密。
/// 加密密码只用于当次导出/导入，不写入文件、不写入日志。
/// </summary>
internal static class ConfigurationCredentialCrypto
{
    public const int Iterations = 600_000;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 2_000_000;

    public static ExportedCredentialVault Encrypt(string encryptionPassword, IReadOnlyDictionary<Guid, ConnectionSecret> secrets)
    {
        if (string.IsNullOrEmpty(encryptionPassword)) throw new ArgumentException("请设置备份加密密码。", nameof(encryptionPassword));
        if (secrets.Count == 0) throw new ArgumentException("没有可导出的凭据。", nameof(secrets));

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var key = new byte[32];
        byte[] plain = [];
        byte[] cipher = [];
        var passwordBytes = Encoding.UTF8.GetBytes(encryptionPassword);
        try
        {
            key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, Iterations, HashAlgorithmName.SHA256, 32);
            plain = JsonSerializer.SerializeToUtf8Bytes(
                secrets.Select(pair => new ExportedCredential(pair.Key, pair.Value.Password, pair.Value.Passphrase)).ToList(),
                ShellXJsonContext.Default.ListExportedCredential);
            cipher = new byte[plain.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag);
            return new ExportedCredentialVault
            {
                Iterations = Iterations,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Data = Convert.ToBase64String(cipher)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
            if (plain.Length > 0) CryptographicOperations.ZeroMemory(plain);
            if (cipher.Length > 0) CryptographicOperations.ZeroMemory(cipher);
        }
    }

    public static IReadOnlyDictionary<Guid, ConnectionSecret> Decrypt(ExportedCredentialVault vault, string encryptionPassword)
    {
        ArgumentNullException.ThrowIfNull(vault);
        if (!vault.Format.Equals("ShellX.Credentials", StringComparison.Ordinal) || vault.Version != 1)
            throw new InvalidDataException("备份文件的凭据格式或版本不受支持。");
        if (!vault.Algorithm.Equals("AES-256-GCM/PBKDF2-SHA256", StringComparison.Ordinal))
            throw new InvalidDataException($"不支持的凭据加密算法：{vault.Algorithm}。");
        if (vault.Iterations is < MinimumIterations or > MaximumIterations)
            throw new InvalidDataException("备份文件的密钥派生参数无效。");
        if (string.IsNullOrEmpty(encryptionPassword)) throw new ArgumentException("请输入导出时设置的备份密码。", nameof(encryptionPassword));

        byte[] salt;
        byte[] nonce;
        byte[] tag;
        byte[] cipher;
        try
        {
            salt = Convert.FromBase64String(vault.Salt);
            nonce = Convert.FromBase64String(vault.Nonce);
            tag = Convert.FromBase64String(vault.Tag);
            cipher = Convert.FromBase64String(vault.Data);
        }
        catch (FormatException) { throw new InvalidDataException("备份文件中的加密数据已损坏。"); }
        if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16 || cipher.Length is 0 || cipher.Length > 10 * 1024 * 1024)
            throw new InvalidDataException("备份文件中的加密数据长度无效。");

        var passwordBytes = Encoding.UTF8.GetBytes(encryptionPassword);
        var key = new byte[32];
        byte[] plain = new byte[cipher.Length];
        try
        {
            key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, vault.Iterations, HashAlgorithmName.SHA256, 32);
            using var aes = new AesGcm(key, 16);
            try { aes.Decrypt(nonce, cipher, tag, plain); }
            catch (CryptographicException) { throw new InvalidDataException("备份密码错误，或凭据数据已被修改。"); }

            var credentials = JsonSerializer.Deserialize(plain, ShellXJsonContext.Default.ListExportedCredential)
                ?? throw new InvalidDataException("备份文件中的凭据内容无效。");
            return credentials
                .Where(item => item.Id != Guid.Empty)
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group =>
                {
                    var item = group.Last();
                    return new ConnectionSecret(item.Password ?? "", item.Passphrase ?? "");
                });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}