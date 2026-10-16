using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Files;

namespace ShellX.Desktop.Features.FileEditor;

public partial class FileEditorViewModel(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save, ITextCodec codec) : ObservableObject
{
    private TextDocument _original = document;
    [ObservableProperty] private string _content = document.Content;
    [ObservableProperty] private string _status = "Ctrl+S 保存";
    [ObservableProperty] private string _encodingNote = document.EncodingNote;
    [ObservableProperty] private TextEncodingInfo _selectedEncoding = document.Encoding;
    [ObservableProperty] private EditorSyntax _selectedSyntax = EditorSyntaxes.Detect(document.Path, document.Content);
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isReloading;
    [ObservableProperty] private bool _isSaveSuccessful;
    public event Action<string>? SaveFailed;
    public IReadOnlyList<TextEncodingInfo> Encodings => TextEncodingInfo.Supported;
    public IReadOnlyList<EditorSyntax> Syntaxes => EditorSyntaxes.All;
    public bool HasTextChanges => Content != _original.Content;
    public bool IsDirty => HasTextChanges || SelectedEncoding != _original.Encoding;
    public string DocumentInfo => $"{SelectedEncoding.Name} · {(Content.Contains("\r\n") ? "CRLF" : "LF")} · {Content.Length:N0} 字符";
    partial void OnContentChanged(string value)
    {
        if (IsSaveSuccessful) { IsSaveSuccessful = false; Status = "未保存的修改 · Ctrl+S 保存"; }
        OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(HasTextChanges)); OnPropertyChanged(nameof(DocumentInfo));
    }
    partial void OnSelectedEncodingChanged(TextEncodingInfo value)
    {
        IsSaveSuccessful = false;
        OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(DocumentInfo));
        Status = value == _original.Encoding ? "Ctrl+S 保存" : "将按 " + value.Name + " 保存；如需修复乱码，请点击“按此编码重读”";
    }
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsSaving || IsReloading) return;
        IsSaveSuccessful = false;
        IsReloading = true;
        try
        {
            if (_original.SourceBytes.IsEmpty && _original.Size > 0) throw new IOException("原始内容不可用，请关闭后重新打开文件。");
            var selected = SelectedEncoding;
            var decoded = await Task.Run(() => codec.Decode(_original.SourceBytes, selected));
            _original = _original with { Content = decoded.Content, Encoding = decoded.Encoding, SaveEncoding = null, EncodingNote = decoded.Note };
            Content = decoded.Content; SelectedEncoding = decoded.Encoding; EncodingNote = decoded.Note;
            Status = "已按 " + decoded.Encoding.Name + " 重新读取";
            OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(HasTextChanges)); OnPropertyChanged(nameof(DocumentInfo));
        }
        catch (Exception ex) { Status = "重读失败：" + ex.Message; }
        finally { IsReloading = false; }
    }
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving || IsReloading) return;
        IsSaveSuccessful = false;
        IsSaving = true;
        Status = "正在保存…";
        try
        {
            TextEditorLimits.ValidateContent(Content);
            _original = await save(_original with { SaveEncoding = SelectedEncoding }, Content);
            EncodingNote = _original.EncodingNote;
            IsSaveSuccessful = true;
            Status = "保存成功 · " + DateTime.Now.ToString("HH:mm:ss") + (IsDirty ? " · 仍有未保存的修改" : "");
            OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(HasTextChanges)); OnPropertyChanged(nameof(DocumentInfo));
        }
        catch (Exception ex) { Status = "保存失败：" + ex.Message; SaveFailed?.Invoke(ex.Message); }
        finally { IsSaving = false; }
    }
}
