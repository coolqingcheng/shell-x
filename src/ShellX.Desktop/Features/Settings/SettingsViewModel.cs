using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Security;
using ShellX.Core.Settings;
using ShellX.Core.Updates;
using ShellX.Desktop.Services;
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

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;
    private readonly ILockPasswordHasher _passwords;
    private readonly UpdateManager? _updates;
    private readonly IDialogService? _dialogs;

    public SettingsViewModel(AppSettings original, ILockPasswordHasher passwords, IReadOnlyList<string> fonts,
        bool systemSessionSupported = true, UpdateManager? updates = null, IDialogService? dialogs = null)
    {
        _original = original; _passwords = passwords; _updates = updates; _dialogs = dialogs;
        Fonts = fonts; SystemSessionSupported = systemSessionSupported;
        ThemeIndex = original.Theme == AppTheme.Light ? 1 : 0;
        TerminalFontFamily = fonts.FirstOrDefault(name => name.Equals(original.TerminalFontFamily, StringComparison.OrdinalIgnoreCase)) ?? original.TerminalFontFamily;
        TerminalFontSize = (decimal)original.TerminalFontSize;
        EditorFontFamily = fonts.FirstOrDefault(name => name.Equals(original.EditorFontFamily, StringComparison.OrdinalIgnoreCase)) ?? original.EditorFontFamily;
        EditorFontSize = (decimal)original.EditorFontSize;
        ShowHidden = original.ShowHiddenFiles;
        HighlightText = original.HighlightTerminalText;
        AutoReconnect = original.AutoReconnectEnabled;
        ReconnectIntervalSeconds = Math.Clamp(original.ReconnectIntervalSeconds, 1, 86400);
        AutoLock = original.AutoLockEnabled;
        AutoLockOnlyWhenSystemLocked = original.AutoLockOnlyWhenSystemLocked;
        Duration = original.AutoLockMinutes >= 60 && original.AutoLockMinutes % 60 == 0 ? original.AutoLockMinutes / 60 : original.AutoLockMinutes;
        DurationUnit = original.AutoLockMinutes >= 60 && original.AutoLockMinutes % 60 == 0 ? 1 : 0;
        McpEnabled = original.McpEnabled;
        McpAddress = original.McpAddress;
        McpToken = string.IsNullOrWhiteSpace(original.McpToken) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : original.McpToken;
        CheckUpdateOnStartup = original.CheckUpdateOnStartup;
        AutoDownloadUpdate = original.AutoDownloadUpdate;
        if (updates is not null) updates.PropertyChanged += (_, _) => RefreshUpdateState();
        RefreshUpdateState();
    }

    public IReadOnlyList<string> Fonts { get; }
    public bool HasPassword => !string.IsNullOrEmpty(_original.LockPasswordHash);
    public string PasswordSummary => HasPassword ? "已设置锁定密码" : "尚未设置锁定密码";
    public bool ShowPasswordFields => ChangePassword || RemovePassword;
    public bool ShowNewPassword => ChangePassword && !RemovePassword;
    public string[] Themes { get; } = ["暗色", "亮色"];
    [ObservableProperty] private int _themeIndex;
    public string[] DurationUnits { get; } = ["分钟", "小时"];
    public bool SystemSessionSupported { get; }
    public string AutoLockDelayLabel => AutoLockOnlyWhenSystemLocked ? "系统锁屏持续" : "闲置";
    public event Action<AppSettings>? Saved;
    public FontFamily TerminalPreviewFont => new(TerminalFontFamily);
    public FontFamily EditorPreviewFont => new(EditorFontFamily);
    public double TerminalPreviewSize => (double)TerminalFontSize;
    public double EditorPreviewSize => (double)EditorFontSize;
    public bool HasUpdateService => _updates is not null;
    public string CurrentVersionText => _updates?.CurrentVersionText ?? Core.AppVersion.Value;
    public string UpdateStatus => _updates?.Status ?? "更新功能不可用";
    public double UpdateProgress => _updates?.Progress ?? 0;
    public string UpdateSummary => _updates?.UpdateSummary ?? "";
    public bool UpdateAvailable => _updates?.UpdateAvailable ?? false;
    public bool HasPendingUpdate => _updates?.LoadPending() is not null;
    public bool ShowUpdatePrompt => HasPendingUpdate && !UpdateDeferred;
    public string ReleaseUrl => _updates?.ReleaseUrl ?? "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TerminalPreviewFont))] private string _terminalFontFamily = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TerminalPreviewSize))] private decimal _terminalFontSize = 14;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditorPreviewFont))] private string _editorFontFamily = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditorPreviewSize))] private decimal _editorFontSize = 14;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _highlightText;
    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private decimal _reconnectIntervalSeconds = 5;
    [ObservableProperty] private bool _autoLock;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AutoLockDelayLabel))] private bool _autoLockOnlyWhenSystemLocked;
    [ObservableProperty] private decimal _duration = 15;
    [ObservableProperty] private int _durationUnit;
    [ObservableProperty] private bool _changePassword;
    [ObservableProperty] private bool _removePassword;
    [ObservableProperty] private string _currentPassword = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _mcpEnabled;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(McpJson))] private string _mcpAddress = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(McpJson))] private string _mcpToken = "";
    [ObservableProperty] private bool _checkUpdateOnStartup;
    [ObservableProperty] private bool _autoDownloadUpdate;
    [ObservableProperty] private bool _updateDeferred;
    partial void OnChangePasswordChanged(bool value) { OnPropertyChanged(nameof(ShowPasswordFields)); OnPropertyChanged(nameof(ShowNewPassword)); }
    partial void OnRemovePasswordChanged(bool value) { OnPropertyChanged(nameof(ShowPasswordFields)); OnPropertyChanged(nameof(ShowNewPassword)); }
    public string McpJson => JsonSerializer.Serialize(new McpClientConfiguration(new Dictionary<string, McpServerConfiguration>
    {
        ["shellx"] = new("http", McpAddress.TrimEnd('/') + "/mcp", new Dictionary<string, string> { ["Authorization"] = "Bearer " + McpToken.Trim() })
    }), SettingsJsonContext.Default.McpClientConfiguration);

    private void RefreshUpdateState()
    {
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(UpdateProgress));
        OnPropertyChanged(nameof(UpdateSummary));
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(CurrentVersionText));
        OnPropertyChanged(nameof(HasPendingUpdate));
        OnPropertyChanged(nameof(ShowUpdatePrompt));
        OnPropertyChanged(nameof(ReleaseUrl));
    }

    [RelayCommand]
    private void GenerateMcpToken() => McpToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>直接下载最新版，下载完成后提示立即更新或关闭时更新。</summary>
    [RelayCommand]
    private async Task DownloadLatestAsync()
    {
        if (_updates is null) { Error = "更新功能不可用。"; return; }
        Error = "";
        var release = await _updates.CheckAsync(_original.UpdateRepositoryOwner, _original.UpdateRepositoryName);
        if (release is null) { Error = _updates.Status; RefreshUpdateState(); return; }
        if (!UpdateVersion.IsNewer(release.Tag, _updates!.CurrentVersion))
        {
            Error = _updates.Status;
            RefreshUpdateState();
            return;
        }
        if (await _updates.DownloadAsync(release) is null) { Error = _updates.Status; RefreshUpdateState(); return; }
        UpdateDeferred = false;
        RefreshUpdateState();
        await PromptUpdateAsync(release.Version);
    }

    private async Task PromptUpdateAsync(string version)
    {
        if (_dialogs is null || _updates is null) return;
        var apply = await _dialogs.ConfirmAsync("发现新版本",
            $"已下载新版本 {version}。\n点“确认”立即更新并重启；点“取消”则在关闭 ShellX 时自动安装。");
        if (!apply) { UpdateDeferred = true; OnPropertyChanged(nameof(ShowUpdatePrompt)); return; }
        _updates.ApplyAndRestart();
    }

    /// <summary>立即更新并重启。</summary>
    [RelayCommand]
    private void InstallUpdate()
    {
        if (_updates is null) { Error = "更新功能不可用。"; return; }
        Error = "";
        if (!_updates.TryApply(restart: true)) Error = _updates.Status;
        RefreshUpdateState();
    }

    /// <summary>稍后处理，关闭 ShellX 时自动安装已下载的更新。</summary>
    [RelayCommand]
    private void DeferUpdate()
    {
        UpdateDeferred = true;
        OnPropertyChanged(nameof(ShowUpdatePrompt));
    }

    /// <summary>另存安装包到本地，不立即安装。</summary>
    [RelayCommand]
    private async Task SaveInstallerAsync()
    {
        if (_updates is null || _dialogs is null) { Error = "更新功能不可用。"; return; }
        Error = "";
        var release = _updates.Latest ?? await _updates.CheckAsync(_original.UpdateRepositoryOwner, _original.UpdateRepositoryName);
        if (release?.Installer is not { } asset) { Error = "暂无可下载的安装包。"; return; }
        var path = await _dialogs.PickDownloadAsync(asset.Name);
        if (path is null) return;
        await _updates.DownloadAsync(release, path);
        RefreshUpdateState();
    }

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
            var hash = _original.LockPasswordHash;
            if (ChangePassword || RemovePassword)
            {
                if (HasPassword && !await Task.Run(() => _passwords.Verify(CurrentPassword, _original.LockPasswordHash))) throw new ArgumentException("当前锁定密码不正确。");
                if (RemovePassword) hash = "";
                else
                {
                    if (NewPassword != ConfirmPassword) throw new ArgumentException("两次输入的新密码不一致。");
                    hash = await Task.Run(() => _passwords.Hash(NewPassword));
                }
            }
            if (AutoLock && string.IsNullOrEmpty(hash) && !RemovePassword) throw new ArgumentException("请先设置锁定密码，再启用自动锁定。");
            if (AutoLock && !RemovePassword && AutoLockOnlyWhenSystemLocked && !SystemSessionSupported)
                throw new ArgumentException("当前系统暂不支持锁屏检测，请取消“仅在系统锁屏后计时”或关闭自动锁定。");
            var result = _original with { Theme = ThemeIndex == 1 ? AppTheme.Light : AppTheme.Dark, TerminalFontFamily = TerminalFontFamily, TerminalFontSize = (double)TerminalFontSize,
                EditorFontFamily = EditorFontFamily, EditorFontSize = (double)EditorFontSize,
                ShowHiddenFiles = ShowHidden, HighlightTerminalText = HighlightText,
                AutoReconnectEnabled = AutoReconnect, ReconnectIntervalSeconds = (int)ReconnectIntervalSeconds,
                LockPasswordHash = hash, AutoLockEnabled = AutoLock && !string.IsNullOrEmpty(hash), AutoLockOnlyWhenSystemLocked = AutoLockOnlyWhenSystemLocked, AutoLockMinutes = (int)minutes, McpEnabled = McpEnabled, McpAddress = McpAddress.TrimEnd('/' ), McpToken = McpToken.Trim(),
                UpdateRepositoryOwner = _original.UpdateRepositoryOwner, UpdateRepositoryName = _original.UpdateRepositoryName,
                CheckUpdateOnStartup = CheckUpdateOnStartup, AutoDownloadUpdate = AutoDownloadUpdate };
            ClearPasswords(); Saved?.Invoke(result);
        }
        catch (Exception ex) { Error = ex.Message; CurrentPassword = ""; }
        finally { IsSaving = false; }
    }
    public void ClearPasswords() { CurrentPassword = ""; NewPassword = ""; ConfirmPassword = ""; }
}