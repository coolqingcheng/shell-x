using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace ShellX.Desktop.Controls;

public sealed class SecretTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    public SecretTextBox() { PasswordChar = '●'; }
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
