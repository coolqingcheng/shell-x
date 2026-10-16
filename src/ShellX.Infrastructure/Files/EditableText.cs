using ShellX.Core.Files;

namespace ShellX.Infrastructure.Files;

internal static class EditableText
{
    private static readonly TextCodec Codec = new();
    public static TextDocument Read(Stream input, string path, long size, DateTime modified, CancellationToken token)
    {
        var bytes = ReadBytes(input, token); var decoded = Codec.Decode(bytes);
        return new(path, decoded.Content, size, modified) { Encoding = decoded.Encoding, SourceBytes = bytes, EncodingNote = decoded.Note };
    }
    public static byte[] ReadBytes(Stream input, CancellationToken token)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = input.Read(buffer);
            token.ThrowIfCancellationRequested();
            if (count == 0) break;
            TextEditorLimits.ValidateSize(memory.Length + count);
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }
    public static bool Matches(TextDocument original, ReadOnlyMemory<byte> current) => !original.SourceBytes.IsEmpty
        ? original.SourceBytes.Span.SequenceEqual(current.Span) : original.Content == Codec.Decode(current, original.Encoding).Content;
    public static byte[] Encode(TextDocument original, string content)
    {
        var target = original.SaveEncoding ?? original.Encoding;
        if (!original.SourceBytes.IsEmpty && target == original.Encoding && content == original.Content) return original.SourceBytes.ToArray();
        return Codec.Encode(content, target);
    }
    public static TextDocument Saved(TextDocument original, string content, byte[] bytes, DateTime modified) =>
        new(original.Path, content, bytes.Length, modified) { Encoding = original.SaveEncoding ?? original.Encoding, SourceBytes = bytes,
            EncodingNote = (original.SaveEncoding ?? original.Encoding).Name + " · 保存时保留所选编码" };
}
