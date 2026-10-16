namespace ShellX.Core.Files;

public static class TextEditorLimits
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxCharacters = 256 * 1024;
    public const int MaxLines = 10_000;
    public const int MaxLineLength = 8_192;
    private const string ExternalEditor = "请使用外部文本编辑器处理，远程文件可先下载到本地。";

    public static void ValidateSize(long bytes)
    { if (bytes > MaxBytes) throw new IOException("文件超过 2 MiB，未加载到内置编辑器。" + ExternalEditor); }

    public static void ValidateContent(string text)
    {
        if (text.Length > MaxCharacters) throw new IOException("文本超过 256K 字符，未加载到内置编辑器。" + ExternalEditor);
        var lines = 1; var length = 0; var previousCr = false;
        foreach (var character in text)
        {
            if (character == '\0') throw new IOException("此文件似乎是二进制文件，无法作为文本编辑。");
            if (character is '\r' or '\n' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029')
            {
                if (character != '\n' || !previousCr) lines++;
                if (lines > MaxLines) throw new IOException("文本超过 10000 行，未加载到内置编辑器。" + ExternalEditor);
                length = 0;
            }
            else if (++length > MaxLineLength) throw new IOException("文本包含超过 8192 字符的长行，未加载到内置编辑器。" + ExternalEditor);
            previousCr = character == '\r';
        }
    }
}
