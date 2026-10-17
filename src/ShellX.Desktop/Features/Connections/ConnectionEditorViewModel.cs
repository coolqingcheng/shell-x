using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using ShellX.Core.Connections;
using ShellX.Core.Sessions;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Connections;

public partial class ConnectionEditorViewModel : ObservableObject
{
    private readonly Guid _id;
    private readonly List<Core.Tunnels.TunnelDefinition> _tunnels;
    private readonly List<Core.Commands.SavedCommand> _quickCommands;
    private readonly Func<ConnectionEditResult, CancellationToken, Task<ConnectionTestResult>>? _test;
    private CancellationTokenSource? _testCancellation;
    private bool _closed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))] private bool _isTesting;
    [ObservableProperty] private string _testStatus = "";
    [ObservableProperty] private bool _testSucceeded;
    public bool CanEdit => !IsTesting;
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!IsTesting && e.PropertyName is nameof(Host) or nameof(Port) or nameof(Username) or nameof(AuthenticationIndex) or nameof(PrivateKeyPath) or nameof(Password) or nameof(Passphrase))
        { TestStatus = ""; TestSucceeded = false; }
    }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _group;
    [ObservableProperty] private string _accentColor;
    [ObservableProperty] private ConnectionAccentChoice _selectedAccent;
    public IReadOnlyList<ConnectionAccentChoice> AccentChoices => ConnectionAccentChoice.All;
    partial void OnSelectedAccentChanged(ConnectionAccentChoice value)
    { if (value?.Value is { } color) AccentColor = color; }
    partial void OnAccentColorChanged(string value) => SelectedAccent = ConnectionAccentChoice.Find(value);
    [ObservableProperty] private string _host;
    [ObservableProperty] private string _port;
    [ObservableProperty] private string _username;
    [ObservableProperty] private int _authenticationIndex;
    [ObservableProperty] private string _privateKeyPath;
    [ObservableProperty] private string _password;
    [ObservableProperty] private string _passphrase;
    [ObservableProperty] private bool _rememberSecret;
    [ObservableProperty] private bool _mcpEnabled;
    [ObservableProperty] private string _initialDirectory;
    [ObservableProperty] private string _error = "";
    public bool IsPassword => AuthenticationIndex == 0;
    public bool IsKey => !IsPassword;
    partial void OnAuthenticationIndexChanged(int value) { OnPropertyChanged(nameof(IsPassword)); OnPropertyChanged(nameof(IsKey)); }

    public ConnectionEditorViewModel(ConnectionProfile? profile, ConnectionSecret? secret,
        Func<ConnectionEditResult, CancellationToken, Task<ConnectionTestResult>>? test = null)
    {
        _test = test;
        profile ??= new(); secret ??= new();
        _id = profile.Id; _tunnels = profile.Tunnels; _quickCommands = profile.QuickCommands;
        _name = profile.Name; _group = profile.Group; _host = profile.Host; _port = profile.Port.ToString();
        _accentColor = profile.AccentColor; _selectedAccent = ConnectionAccentChoice.Find(_accentColor);
        _username = profile.Username; _authenticationIndex = (int)profile.Authentication;
        _privateKeyPath = profile.PrivateKeyPath; _password = secret.Password; _passphrase = secret.Passphrase;
        _rememberSecret = profile.RememberSecret; _mcpEnabled = profile.McpEnabled; _initialDirectory = profile.InitialDirectory;
    }
    private bool CanTest => !IsTesting && !_closed && _test != null;
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestConnectionAsync()
    {
        if (_test == null || _closed) return;
        ConnectionEditResult snapshot;
        try { snapshot = Build(); }
        catch (Exception ex) { TestSucceeded = false; TestStatus = "无法测试：" + ex.Message; return; }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _testCancellation = cancellation;
        IsTesting = true; TestSucceeded = false; TestStatus = "正在连接并验证 SSH 认证…"; Error = "";
        try
        {
            var result = await _test(snapshot, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            TestSucceeded = true;
            TestStatus = result.SftpAvailable ? "连接成功：SSH 认证通过，SFTP 可用。" : "SSH 连接成功，但 SFTP 不可用：" + result.FileError;
        }
        catch (OperationCanceledException) { TestStatus = _closed ? "" : "测试已取消或超时。"; }
        catch (Exception ex) { TestStatus = "连接失败：" + ex.Message; }
        finally { _testCancellation = null; IsTesting = false; }
    }
    [RelayCommand] private void CancelTest() => _testCancellation?.Cancel();
    public void Close()
    {
        _closed = true; _testCancellation?.Cancel();
        Password = ""; Passphrase = ""; TestConnectionCommand.NotifyCanExecuteChanged();
    }

    public ConnectionEditResult Build()
    {
        if (!int.TryParse(Port, out var port)) throw new ArgumentException("请输入有效的端口。");
        var profile = new ConnectionProfile { Id = _id, Name = Name.Trim(), Group = string.IsNullOrWhiteSpace(Group) ? "默认分组" : Group.Trim(),
            AccentColor = AccentColor.Trim().ToUpperInvariant(), Host = Host.Trim(), Port = port, Username = Username.Trim(), Authentication = (AuthenticationKind)AuthenticationIndex,
            PrivateKeyPath = PrivateKeyPath.Trim(), RememberSecret = RememberSecret, McpEnabled = McpEnabled,
            InitialDirectory = string.IsNullOrWhiteSpace(InitialDirectory) ? "." : InitialDirectory.Trim(), Tunnels = _tunnels, QuickCommands = _quickCommands };
        profile.Validate();
        return new(profile, new(Password, Passphrase));
    }
}
