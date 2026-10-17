using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Security;
using ShellX.Core.Settings;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace ShellX.Desktop.Features.Settings;

public sealed record McpClientConfiguration([property: JsonPropertyName("mcpServers")] IReadOnlyDictionary<string, McpServerConfiguration> McpServers);
public sealed record McpServerConfiguration([property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("url")] string Url, [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(McpClientConfiguration))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

public partial class SettingsViewModel(AppSettings original, ILockPasswordHasher passwords, IReadOnlyList<string> fonts) : ObservableObject
{
    public IReadOnlyList<string> Fonts { get; } = fonts;
    public bool HasPassword => !string.IsNullOrEmpty(original.LockPasswordHash);
    public string PasswordSummary => HasPassword ? "已设置锁定密码" : "尚未设置锁定密码";
    public bool ShowPasswordFields => ChangePassword || RemovePassword;
    public bool ShowNewPassword => ChangePassword && !RemovePassword;
    public string[] Themes { get; } = ["暗色", "亮色"];
    [ObservableProperty] private int _themeIndex = original.Theme == AppTheme.Light ? 1 : 0;
    public string[] DurationUnits { get; } = ["分钟", "小时"];
    public event Action<AppSettings>? Saved;
    public FontFamily TerminalPreviewFont => new(TerminalFontFamily);
    public FontFamily EditorPreviewFont => new(EditorFontFamily);
    public double TerminalPreviewSize => (double)TerminalFontSize;
    public double EditorPreviewSize => (double)EditorFontSize;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TerminalPreviewFont))] private string _terminalFontFamily = fonts.FirstOrDefault(name => name.Equals(original.TerminalFontFamily, StringComparison.OrdinalIgnoreCase)) ?? original.TerminalFontFamily;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TerminalPreviewSize))] private decimal _terminalFontSize = (decimal)original.TerminalFontSize;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditorPreviewFont))] private string _editorFontFamily = fonts.FirstOrDefault(name => name.Equals(original.EditorFontFamily, StringComparison.OrdinalIgnoreCase)) ?? original.EditorFontFamily;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditorPreviewSize))] private decimal _editorFontSize = (decimal)original.EditorFontSize;
    [ObservableProperty] private bool _showHidden = original.ShowHiddenFiles;
    [ObservableProperty] private bool _highlightText = original.HighlightTerminalText;
    [ObservableProperty] private bool _autoReconnect = original.AutoReconnectEnabled;
    [ObservableProperty] private decimal _reconnectIntervalSeconds = Math.Clamp(original.ReconnectIntervalSeconds, 1, 86400);
    [ObservableProperty] private bool _autoLock = original.AutoLockEnabled;
    [ObservableProperty] private decimal _duration = original.AutoLockMinutes >= 60 && original.AutoLockMinutes % 60 == 0 ? original.AutoLockMinutes / 60 : original.AutoLockMinutes;
    [ObservableProperty] private int _durationUnit = original.AutoLockMinutes >= 60 && original.AutoLockMinutes % 60 == 0 ? 1 : 0;
    [ObservableProperty] private bool _changePassword;
    [ObservableProperty] private bool _removePassword;
    [ObservableProperty] private string _currentPassword = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(McpJson))] private string _mcpAddress = original.McpAddress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(McpJson))] private string _mcpToken = string.IsNullOrWhiteSpace(original.McpToken) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : original.McpToken;
    partial void OnChangePasswordChanged(bool value) { OnPropertyChanged(nameof(ShowPasswordFields)); OnPropertyChanged(nameof(ShowNewPassword)); }
    partial void OnRemovePasswordChanged(bool value) { OnPropertyChanged(nameof(ShowPasswordFields)); OnPropertyChanged(nameof(ShowNewPassword)); }
    public string McpJson => JsonSerializer.Serialize(new McpClientConfiguration(new Dictionary<string, McpServerConfiguration>
    {
        ["shellx"] = new("http", McpAddress.TrimEnd('/') + "/mcp", new Dictionary<string, string> { ["Authorization"] = "Bearer " + McpToken.Trim() })
    }), SettingsJsonContext.Default.McpClientConfiguration);

    [RelayCommand]
    private void GenerateMcpToken() => McpToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving) return;
        IsSaving = true; Error = "";
        try
        {
            if (string.IsNullOrWhiteSpace(TerminalFontFamily) || string.IsNullOrWhiteSpace(EditorFontFamily)) throw new ArgumentException("请选择终端和编辑器字体。");
            if (TerminalFontSize is < 10 or > 28 || EditorFontSize is < 10 or > 36) throw new ArgumentException("终端字号范围为 10–28，编辑器为 10–36。");
            if (ReconnectIntervalSeconds is < 1 or > 86400 || ReconnectIntervalSeconds != decimal.Truncate(ReconnectIntervalSeconds)) throw new ArgumentException("重连间隔需为 1–86400 的整数秒。");
            if (!Uri.TryCreate(McpAddress, UriKind.Absolute, out var mcpUri) || mcpUri.Scheme != Uri.UriSchemeHttp || !string.IsNullOrEmpty(mcpUri.UserInfo)) throw new ArgumentException("MCP 地址必须是不带账号密码的 http:// 完整地址。");
            if (McpToken.Trim().Length < 16) throw new ArgumentException("MCP 密钥至少需要 16 个字符。");
            var minutes = Duration * (DurationUnit == 1 ? 60 : 1);
            if (minutes is < 1 or > 525600 || minutes != decimal.Truncate(minutes)) throw new ArgumentException("自动锁定时长需为整数分钟，范围为 1 分钟至 365 天。");
            var hash = original.LockPasswordHash;
            if (ChangePassword || RemovePassword)
            {
                if (HasPassword && !await Task.Run(() => passwords.Verify(CurrentPassword, original.LockPasswordHash))) throw new ArgumentException("当前锁定密码不正确。");
                if (RemovePassword) hash = "";
                else
                {
                    if (NewPassword != ConfirmPassword) throw new ArgumentException("两次输入的新密码不一致。");
                    hash = await Task.Run(() => passwords.Hash(NewPassword));
                }
            }
            if (AutoLock && string.IsNullOrEmpty(hash) && !RemovePassword) throw new ArgumentException("请先设置锁定密码，再启用自动锁定。");
            var result = original with { Theme = ThemeIndex == 1 ? AppTheme.Light : AppTheme.Dark, TerminalFontFamily = TerminalFontFamily, TerminalFontSize = (double)TerminalFontSize,
                EditorFontFamily = EditorFontFamily, EditorFontSize = (double)EditorFontSize,
                ShowHiddenFiles = ShowHidden, HighlightTerminalText = HighlightText,
                AutoReconnectEnabled = AutoReconnect, ReconnectIntervalSeconds = (int)ReconnectIntervalSeconds,
                LockPasswordHash = hash, AutoLockEnabled = AutoLock && !string.IsNullOrEmpty(hash), AutoLockMinutes = (int)minutes, McpAddress = McpAddress.TrimEnd('/' ), McpToken = McpToken.Trim() };
            ClearPasswords(); Saved?.Invoke(result);
        }
        catch (Exception ex) { Error = ex.Message; CurrentPassword = ""; }
        finally { IsSaving = false; }
    }
    public void ClearPasswords() { CurrentPassword = ""; NewPassword = ""; ConfirmPassword = ""; }
}
