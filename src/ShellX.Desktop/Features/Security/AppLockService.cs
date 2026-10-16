using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Security;
using ShellX.Core.Settings;

namespace ShellX.Desktop.Features.Security;

public partial class AppLockService(ILockPasswordHasher passwords, TimeProvider? time = null) : ObservableObject
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _lastActivity;
    private DateTimeOffset _retryAfter;
    public ILockPasswordHasher Passwords { get; } = passwords;
    public AppSettings Settings { get; private set; } = new();
    public event Action? SettingsChanged;
    [ObservableProperty] private bool _isLocked = true;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isVerifying;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _error = "";
    public bool CanLock => IsReady && !string.IsNullOrEmpty(Settings.LockPasswordHash);
    public void Configure(AppSettings settings, bool startup = false)
    {
        var wasLocked = IsReady && IsLocked;
        Settings = settings;
        IsReady = true; Error = ""; Password = "";
        IsLocked = (startup || wasLocked) && CanLock;
        _lastActivity = _time.GetTimestamp();
        OnPropertyChanged(nameof(CanLock)); SettingsChanged?.Invoke();
    }
    public void ConfigurationFailed() { IsReady = false; IsLocked = true; Error = "配置加载失败，请检查本地配置文件后重新启动。"; }
    public bool Lock()
    {
        if (!CanLock || IsLocked) return false;
        Password = ""; Error = ""; IsLocked = true; return true;
    }
    public void RecordActivity() { if (!IsLocked) _lastActivity = _time.GetTimestamp(); }
    public void CheckIdle()
    {
        if (CanLock && !IsLocked && Settings.AutoLockEnabled && Settings.AutoLockMinutes > 0 &&
            _time.GetElapsedTime(_lastActivity) >= TimeSpan.FromMinutes(Settings.AutoLockMinutes)) Lock();
    }
    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (!IsReady || !IsLocked || IsVerifying) return;
        if (_time.GetUtcNow() < _retryAfter) { Error = "请稍后重试。"; Password = ""; return; }
        var candidate = Password; Password = ""; Error = ""; IsVerifying = true;
        try
        {
            var hash = Settings.LockPasswordHash;
            if (await Task.Run(() => Passwords.Verify(candidate, hash)))
            { IsLocked = false; _lastActivity = _time.GetTimestamp(); }
            else { Error = "密码错误，请重新输入。"; _retryAfter = _time.GetUtcNow().AddSeconds(1); }
        }
        catch { Error = "解锁失败，请重试。"; }
        finally { IsVerifying = false; }
    }
}
