using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using ShellX.Core.Sessions;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileBrowser;

/// <summary>
/// 文件管理器里的一个页签。同一连接可以有多个页签，每个页签拥有独立的 FileBrowserViewModel；
/// 标题按连接分组编号，如“连接 A (1)”“连接 A (2)”。
/// </summary>
public sealed class FileManagerTabViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceTabViewModel _connection;
    private readonly FileBrowserViewModel _files;
    private bool _initialized;
    private bool _disposed;

    public FileManagerTabViewModel(WorkspaceTabViewModel connection, int number,
        IDialogService dialogs, ILoggerFactory logs, ShellX.Core.Files.ILocalFileSystem localFiles)
    {
        _connection = connection;
        Number = number;
        _files = new FileBrowserViewModel(dialogs, logs.CreateLogger<FileBrowserViewModel>(), localFiles);
        connection.PropertyChanged += ConnectionChanged;
        connection.SessionChanged += ConnectionSessionChanged;
    }

    public WorkspaceTabViewModel Connection => _connection;
    public FileBrowserViewModel Files => _files;
    public int Number { get; private set; }
    public string Title => $"{_connection.Name} ({Number})";
    public string StatusColor => _connection.StatusColor;

    public void SetNumber(int number)
    {
        if (Number == number) return;
        Number = number;
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(Title));
    }

    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        await AttachAsync(_connection.Session);
    }

    private async void ConnectionSessionChanged(object? sender, IRemoteSession? session)
        => await AttachAsync(session);

    private async Task AttachAsync(IRemoteSession? session)
    {
        if (_disposed) return;
        _files.CancelTransfers();
        if (session?.Files is { } remote)
        {
            _files.Attach(remote, _connection.ConnectionToken, session, $"{_connection.Name} · {_connection.Profile.Host}:{_connection.Profile.Port}");
            try { await _files.NavigateAsync(_connection.Profile.InitialDirectory); }
            catch (Exception ex)
            {
                if (!_disposed && ReferenceEquals(_connection.Session, session)) _files.Error = ex.Message;
            }
        }
        else
        {
            _files.Attach(null, CancellationToken.None, null, "");
        }
    }

    private void ConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceTabViewModel.Name)) OnPropertyChanged(nameof(Title));
        if (e.PropertyName == nameof(WorkspaceTabViewModel.StatusColor)) OnPropertyChanged(nameof(StatusColor));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.PropertyChanged -= ConnectionChanged;
        _connection.SessionChanged -= ConnectionSessionChanged;
        _files.CancelTransfers();
        _files.Attach(null, CancellationToken.None, null, "");
        _files.Dispose();
    }
}