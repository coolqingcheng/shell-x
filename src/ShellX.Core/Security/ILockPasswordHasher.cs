namespace ShellX.Core.Security;

public interface ILockPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);
}
