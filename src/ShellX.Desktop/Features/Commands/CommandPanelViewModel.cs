using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Commands;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Commands;

public sealed record CommandHistoryEntry(string Text, DateTime SentAt)
{
    public string Time => SentAt.ToString("HH:mm:ss");
}

public partial class CommandPanelViewModel(IDialogService dialogs, Func<string, Task> send,
    Func<IReadOnlyList<SavedCommand>, Task> save) : ObservableObject
{
    private int _historyIndex = -1;
    private string _draft = "";
    private bool _recalling;
    public ObservableCollection<CommandHistoryEntry> History { get; } = [];
    public ObservableCollection<SavedCommand> QuickCommands { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isAvailable;
    [ObservableProperty] private string _status = "Enter 发送 · Alt+↑/↓ 历史 · Esc 返回终端";
    partial void OnInputChanged(string value) { if (!_recalling) _historyIndex = -1; }
    public void Load(IReadOnlyList<SavedCommand> commands)
    { QuickCommands.Clear(); foreach (var command in commands) QuickCommands.Add(command); }
    private bool CanSend() => IsAvailable && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var original = Input; var command = original.Trim();
        if (string.IsNullOrEmpty(command) || !IsAvailable) return;
        if (command.Contains('\n') || command.Contains('\r') || command.Any(c => char.IsControl(c)))
        { Status = "命令输入框只支持单行文本，请在终端中处理多行命令。"; return; }
        try
        {
            await send(command);
            if (History.Count == 0 || History[0].Text != command) History.Insert(0, new(command, DateTime.Now));
            while (History.Count > 200) History.RemoveAt(History.Count - 1);
            if (Input == original) Input = "";
            _historyIndex = -1; Status = "已发送至终端 · " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex) { Status = "发送失败：" + ex.Message; }
    }
    [RelayCommand] private void Fill(string? text) { if (text != null) Input = text; }
    [RelayCommand] private Task ExecuteQuickAsync(SavedCommand? command)
    { if (command == null) return Task.CompletedTask; Input = command.Text; return SendAsync(); }
    [RelayCommand] private void ClearHistory() => History.Clear();
    [RelayCommand]
    private void PreviousHistory()
    {
        if (_historyIndex + 1 >= History.Count) return;
        if (_historyIndex < 0) _draft = Input;
        _recalling = true; Input = History[++_historyIndex].Text; _recalling = false;
    }
    [RelayCommand]
    private void NextHistory()
    {
        if (_historyIndex < 0) return;
        _recalling = true; --_historyIndex; Input = _historyIndex < 0 ? _draft : History[_historyIndex].Text; _recalling = false;
    }
    [RelayCommand]
    private async Task AddQuickAsync()
    {
        var name = await dialogs.PromptAsync("添加快捷命令", "名称"); if (string.IsNullOrWhiteSpace(name)) return;
        var command = await dialogs.PromptAsync("添加快捷命令", "单行命令", Input); if (string.IsNullOrWhiteSpace(command)) return;
        if (command.Any(char.IsControl)) { await dialogs.ShowErrorAsync("快捷命令只支持单行文本。"); return; }
        try { await save([.. QuickCommands, new() { Name = name.Trim(), Text = command.Trim() }]); }
        catch (Exception ex) { await dialogs.ShowErrorAsync(ex.Message); }
    }
    [RelayCommand]
    private async Task RemoveQuickAsync(SavedCommand? command)
    {
        if (command == null) return;
        try { await save(QuickCommands.Where(c => c.Id != command.Id).ToArray()); }
        catch (Exception ex) { await dialogs.ShowErrorAsync(ex.Message); }
    }
}
