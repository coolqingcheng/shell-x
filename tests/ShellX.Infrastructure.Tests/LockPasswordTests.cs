using ShellX.Core.Settings;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;

namespace ShellX.Infrastructure.Tests;

public sealed class LockPasswordTests
{
    [Fact]
    public void PasswordHashUsesFreshSaltAndRejectsWrongOrMalformedValues()
    {
        var passwords = new LockPasswordHasher();
        var hash = passwords.Hash("test-lock-password");
        Assert.NotEqual(hash, passwords.Hash("test-lock-password"));
        Assert.DoesNotContain("test-lock-password", hash);
        Assert.True(passwords.Verify("test-lock-password", hash));
        Assert.False(passwords.Verify("wrong", hash));
        foreach (var value in new[] { "", "broken", "pbkdf2-sha256$1$YWJj$YWJj", "pbkdf2-sha256$999999999$YWJj$YWJj", "pbkdf2-sha256$600000$invalid!$invalid!" })
            Assert.False(passwords.Verify("test-lock-password", value));
        Assert.Throws<ArgumentException>(() => passwords.Hash(""));
    }
    [Fact]
    public async Task FontsAndLockSettingsRoundTripWithoutStoringThePassword()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-lock-store-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        try
        {
            var store = new JsonSettingsStore(paths);
            await File.WriteAllTextAsync(paths.File("settings.json"), "{\"TerminalFontSize\":16}");
            var old = await store.LoadAsync();
            Assert.Equal(new AppSettings().TerminalFontFamily, old.TerminalFontFamily);
            Assert.Equal(14, old.EditorFontSize); Assert.Empty(old.LockPasswordHash); Assert.False(old.AutoLockEnabled);
            var hash = new LockPasswordHasher().Hash("test-store-password");
            var settings = old with { TerminalFontFamily = "Consolas", EditorFontFamily = "Courier New", EditorFontSize = 18, LockPasswordHash = hash, AutoLockEnabled = true, AutoLockMinutes = 120 };
            await store.SaveAsync(settings);
            Assert.Equal(settings, await new JsonSettingsStore(paths).LoadAsync());
            Assert.DoesNotContain("test-store-password", await File.ReadAllTextAsync(paths.File("settings.json")));
        }
        finally { Directory.Delete(root, true); }
    }
}
