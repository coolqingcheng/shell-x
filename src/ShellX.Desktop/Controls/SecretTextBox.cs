using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;

namespace ShellX.Desktop.Controls;

public sealed class SecretTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    public SecretTextBox()
    {
        PasswordChar = '●';
        // 密码输入用不到输入法：禁用 IME，中文输入法下也不弹候选窗、不转全角，密码始终按英文/ASCII 直接输入（剪贴板粘贴不受影响）。
        InputMethod.SetIsInputMethodEnabled(this, false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new SecretPeer(this);
    private sealed class SecretPeer(SecretTextBox owner) : ControlAutomationPeer(owner), IValueProvider
    {
        public bool IsReadOnly => owner.IsReadOnly;
        public string Value => "";
        public void SetValue(string? value)
        {
            if (!owner.IsEffectivelyEnabled || owner.IsReadOnly) throw new InvalidOperationException("密码输入框当前不可编辑。");
            owner.Text = value;
        }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
        protected override string GetNameCore() => AutomationProperties.GetName(owner) ?? owner.PlaceholderText ?? "密码";
        protected override string? GetPlaceholderTextCore() => owner.PlaceholderText;
    }
}
