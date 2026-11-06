using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.Security;
using ShellX.Infrastructure.Security;

namespace ShellX.Desktop.Tests;

public sealed class SecretTextBoxTests
{
    [AvaloniaFact]
    public void PasswordBoxesNeverUseInputMethod()
    {
        var box = new SecretTextBox();
        Assert.False(InputMethod.GetIsInputMethodEnabled(box));           // 密码框一律禁用输入法

        // 锁屏解锁框就是 SecretTextBox，同样处于“英文直输”状态。
        var lockScreen = new LockScreen(new AppLockService(new LockPasswordHasher()));
        var unlock = lockScreen.FindControl<SecretTextBox>("UnlockPassword");
        Assert.NotNull(unlock);
        Assert.False(InputMethod.GetIsInputMethodEnabled(unlock!));
    }
}