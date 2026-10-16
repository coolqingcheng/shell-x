using CommunityToolkit.Mvvm.ComponentModel;

namespace ShellX.Desktop.Features.FileBrowser;

public partial class PermissionBitViewModel(string label, int mask, bool selected) : ObservableObject
{
    public string Label => label;
    public int Mask => mask;
    [ObservableProperty] private bool _isSet = selected;
}

public partial class PermissionsViewModel : ObservableObject
{
    private bool _updating;
    [ObservableProperty] private string _octal;
    public PermissionBitViewModel[] Bits { get; }
    public PermissionsViewModel(int mode)
    {
        _octal = Convert.ToString(mode, 8).PadLeft(4, '0');
        string[] labels = ["所有者 · 读", "所有者 · 写", "所有者 · 执行", "组 · 读", "组 · 写", "组 · 执行", "其他 · 读", "其他 · 写", "其他 · 执行", "Set UID", "Set GID", "Sticky"];
        int[] masks = [256, 128, 64, 32, 16, 8, 4, 2, 1, 2048, 1024, 512];
        Bits = masks.Select((mask, i) => new PermissionBitViewModel(labels[i], mask, (mode & mask) != 0)).ToArray();
        foreach (var bit in Bits) bit.PropertyChanged += (_, _) =>
        {
            if (_updating) return;
            _updating = true;
            Octal = Convert.ToString(Bits.Where(x => x.IsSet).Sum(x => x.Mask), 8).PadLeft(4, '0');
            _updating = false;
        };
    }
    partial void OnOctalChanged(string value)
    {
        if (_updating) return;
        try
        {
            var mode = Build(); _updating = true;
            foreach (var bit in Bits) bit.IsSet = (mode & bit.Mask) != 0;
        }
        catch (ArgumentException) { }
        finally { _updating = false; }
    }
    public int Build()
    {
        if (Octal.Length is < 3 or > 4 || Octal.Any(c => c is < '0' or > '7')) throw new ArgumentException("权限应为 3–4 位八进制数，例如 0644、0755。");
        return Convert.ToInt32(Octal, 8);
    }
}
