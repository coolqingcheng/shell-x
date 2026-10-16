using Avalonia.Controls;
using Avalonia.Input;

namespace ShellX.Desktop.Features.Security;

public partial class LockScreen : UserControl
{
    public LockScreen() { InitializeComponent(); }
    public LockScreen(AppLockService state) : this() { DataContext = state; }
    public void SetInputVisible(bool visible) { InputPanel.IsVisible = visible; }
    public void FocusPassword() { if (DataContext is AppLockService { IsLocked: true } && InputPanel.IsVisible && UnlockPassword.IsEffectivelyEnabled) UnlockPassword.Focus(); }
    private async void PasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (DataContext is AppLockService state) await state.UnlockCommand.ExecuteAsync(null);
        FocusPassword();
    }
}
