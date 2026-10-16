using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Files;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.FileEditor;

public sealed class CodeEditor : TextEditor
{
    protected override Type StyleKeyOverride => typeof(TextEditor);
    private string _lastValid = "";
    private bool _restoring;
    public event Action<string>? InputRejected;
    public CodeEditor()
    {
        ShowLineNumbers = true; FontFamily = new("Cascadia Mono,Consolas,monospace"); FontSize = 14;
        FontWeight = FontWeight.Normal;
        ThemeService.Bind(this, BackgroundProperty, "AppBackground");
        ThemeService.Bind(this, ForegroundProperty, "EditorForeground");
        ThemeService.Bind(this, LineNumbersForegroundProperty, "MutedForeground");
        ActualThemeVariantChanged += (_, _) => TextArea.TextView.Redraw();
        Padding = new Thickness(10, 6);
        WordWrap = false; Options.ConvertTabsToSpaces = false;
        Options.EnableHyperlinks = false; Options.EnableEmailHyperlinks = false;
        TextArea.TextEntering += (_, e) => { if (!CanInsert(e.Text ?? "")) e.Handled = true; };
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if ((e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control) || (e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.Shift))
            { e.Handled = true; await PasteBoundedAsync(); }
        }, RoutingStrategies.Tunnel);
        TextChanged += (_, _) =>
        {
            if (_restoring) return;
            try { TextEditorLimits.ValidateContent(Text); _lastValid = Text; }
            catch (IOException ex)
            {
                _restoring = true; Text = _lastValid; Document.UndoStack.ClearAll(); _restoring = false;
                InputRejected?.Invoke(ex.Message);
            }
        };
        ContextMenu = new ContextMenu { ItemsSource = new MenuItem[]
        {
            new() { Header = "撤销", Command = new RelayCommand(() => Undo()) },
            new() { Header = "重做", Command = new RelayCommand(() => Redo()) },
            new() { Header = "剪切", Command = new RelayCommand(() => Cut()) },
            new() { Header = "复制", Command = new RelayCommand(() => Copy()) },
            new() { Header = "粘贴", Command = new AsyncRelayCommand(PasteBoundedAsync) },
            new() { Header = "全选", Command = new RelayCommand(SelectAll) }
        }};
    }
    private bool CanInsert(string text)
    {
        try
        {
            if (Document.TextLength - SelectionLength + text.Length > TextEditorLimits.MaxCharacters)
                throw new IOException("粘贴或输入会超过 256K 字符限制，未插入内容。");
            var current = Text;
            TextEditorLimits.ValidateContent(current[..SelectionStart] + text + current[(SelectionStart + SelectionLength)..]);
            return true;
        }
        catch (IOException ex) { InputRejected?.Invoke(ex.Message); return false; }
    }
    private async Task PasteBoundedAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            var data = await clipboard.TryGetDataAsync();
            var text = data == null ? null : await data.TryGetTextAsync();
            if (text != null && CanInsert(text)) SelectedText = text;
        }
        catch (Exception ex) { InputRejected?.Invoke("粘贴失败：" + ex.Message); }
    }
}
