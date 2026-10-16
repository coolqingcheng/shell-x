using System.Security.Cryptography;
using ShellX.Core.Security;

namespace ShellX.Infrastructure.Security;

public sealed class LockPasswordHasher : ILockPasswordHasher
{
    private const int Iterations = 600_000;
    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 256) throw new ArgumentException("锁定密码需为 1–256 个字符。");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try { return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"; }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }
    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 256) return false;
        try
        {
            var parts = encodedHash.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations) || iterations is < Iterations or > 2_000_000) return false;
            var salt = Convert.FromBase64String(parts[2]); var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            try { return CryptographicOperations.FixedTimeEquals(expected, actual); }
            finally { CryptographicOperations.ZeroMemory(actual); }
        }
        catch (FormatException) { return false; }
    }
}
