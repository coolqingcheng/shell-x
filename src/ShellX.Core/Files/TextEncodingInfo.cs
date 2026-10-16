namespace ShellX.Core.Files;

public sealed record TextEncodingInfo(string Name, int CodePage, bool HasBom = false)
{
    public static TextEncodingInfo Utf8 { get; } = new("UTF-8", 65001);
    public static IReadOnlyList<TextEncodingInfo> Supported { get; } =
    [
        Utf8, new("UTF-8 BOM", 65001, true),
        new("UTF-16 LE", 1200), new("UTF-16 LE BOM", 1200, true),
        new("UTF-16 BE", 1201), new("UTF-16 BE BOM", 1201, true),
        new("UTF-32 LE", 12000), new("UTF-32 LE BOM", 12000, true),
        new("UTF-32 BE", 12001), new("UTF-32 BE BOM", 12001, true),
        new("GB18030", 54936), new("GBK / GB2312", 936), new("Big5", 950),
        new("Shift-JIS", 932), new("Korean (CP949)", 949),
        new("Windows-1252", 1252), new("Windows-1251", 1251), new("ISO-8859-1", 28591)
    ];
    public static TextEncodingInfo Find(int codePage, bool bom = false) => Supported.First(e => e.CodePage == codePage && e.HasBom == bom);
}

public sealed record DecodedText(string Content, TextEncodingInfo Encoding, string Note);
public interface ITextCodec
{
    DecodedText Decode(ReadOnlyMemory<byte> bytes, TextEncodingInfo? encoding = null);
    byte[] Encode(string content, TextEncodingInfo encoding);
}
