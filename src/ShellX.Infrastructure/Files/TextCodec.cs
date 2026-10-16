using System.Text;
using System.Text.RegularExpressions;
using ShellX.Core.Files;

namespace ShellX.Infrastructure.Files;

public sealed partial class TextCodec : ITextCodec
{
    private static Encoding GetEncoding(TextEncodingInfo info) => info.CodePage switch
    {
        65001 => new UTF8Encoding(info.HasBom, true),
        1200 or 1201 => new UnicodeEncoding(info.CodePage == 1201, info.HasBom, true),
        12000 or 12001 => new UTF32Encoding(info.CodePage == 12001, info.HasBom, true),
        _ => CodePagesEncodingProvider.Instance.GetEncoding(info.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            ?? Encoding.GetEncoding(info.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
    };

    private static (int CodePage, int Length) Bom(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return (12000, 4);
        if (bytes.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return (12001, 4);
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return (65001, 3);
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) return (1200, 2);
        if (bytes.StartsWith(new byte[] { 0xfe, 0xff })) return (1201, 2);
        return (0, 0);
    }

    public DecodedText Decode(ReadOnlyMemory<byte> bytes, TextEncodingInfo? encoding = null)
    {
        try { return DecodeCore(bytes, encoding); }
        catch (DecoderFallbackException ex) { throw new IOException("当前编码无法正确解码文件内容，请选择其他编码后重新读取。", ex); }
    }
    private static DecodedText DecodeCore(ReadOnlyMemory<byte> bytes, TextEncodingInfo? encoding)
    {
        TextEditorLimits.ValidateSize(bytes.Length);
        var bom = Bom(bytes.Span);
        if (encoding != null) return DecodeAs(bytes, encoding.CodePage, bom, "手动读取");
        if (bom.Length > 0) return DecodeAs(bytes, bom.CodePage, bom, "根据 BOM 识别");
        var unicode = GuessUnicode(bytes.Span);
        if (unicode != 0) return DecodeAs(bytes, unicode, bom, "无 BOM，按字节特征推测");
        var declaration = XmlEncoding().Match(Encoding.ASCII.GetString(bytes.Span[..Math.Min(bytes.Length, 256)]));
        if (declaration.Success && DeclaredCodePage(declaration.Groups[1].Value) is { } declared)
            return DecodeAs(bytes, declared, bom, "根据 XML 编码声明读取");
        try { return DecodeAs(bytes, 65001, bom, "无 BOM，严格 UTF-8 读取"); }
        catch (DecoderFallbackException) { }
        foreach (var legacy in new[] { 54936, 936, 1252 })
        {
            try { return DecodeAs(bytes, legacy, bom, "推测编码，如有乱码请选择正确编码后重读"); }
            catch (DecoderFallbackException) { }
        }
        throw new IOException("无法识别文本编码，请下载后使用外部文本编辑器处理。");
    }

    private static DecodedText DecodeAs(ReadOnlyMemory<byte> bytes, int codePage, (int CodePage, int Length) bom, string note)
    {
        var info = TextEncodingInfo.Find(codePage, bom.CodePage == codePage && bom.Length > 0);
        var content = GetEncoding(info).GetString(bytes.Span[(bom.CodePage == codePage ? bom.Length : 0)..]);
        TextEditorLimits.ValidateContent(content);
        return new(content, info, info.Name + " · " + note);
    }

    private static int GuessUnicode(ReadOnlySpan<byte> bytes)
    {
        var sample = bytes[..Math.Min(bytes.Length, 4096)];
        if (sample.Length < 4) return 0;
        var pairs = sample.Length / 2; var even = 0; var odd = 0;
        for (var i = 0; i < pairs; i++) { if (sample[i * 2] == 0) even++; if (sample[i * 2 + 1] == 0) odd++; }
        var quads = sample.Length / 4; var le32 = 0; var be32 = 0;
        for (var i = 0; i < quads; i++)
        {
            if (sample[i * 4 + 1] == 0 && sample[i * 4 + 2] == 0 && sample[i * 4 + 3] == 0) le32++;
            if (sample[i * 4] == 0 && sample[i * 4 + 1] == 0 && sample[i * 4 + 2] == 0) be32++;
        }
        if (bytes.Length % 4 == 0 && le32 > quads * .6) return 12000;
        if (bytes.Length % 4 == 0 && be32 > quads * .6) return 12001;
        if (bytes.Length % 2 == 0 && odd > pairs * .4 && even < pairs * .1) return 1200;
        if (bytes.Length % 2 == 0 && even > pairs * .4 && odd < pairs * .1) return 1201;
        return 0;
    }

    private static int? DeclaredCodePage(string name) => name.ToUpperInvariant().Replace('_', '-') switch
    {
        "UTF-8" or "UTF8" => 65001, "GB2312" or "GBK" or "CP936" => 936, "GB18030" => 54936,
        "BIG5" => 950, "SHIFT-JIS" or "SJIS" => 932, "WINDOWS-1252" => 1252,
        "WINDOWS-1251" => 1251, "ISO-8859-1" => 28591, _ => null
    };
    [GeneratedRegex("<\\?xml[^>]*encoding\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex XmlEncoding();

    public byte[] Encode(string content, TextEncodingInfo encoding)
    {
        TextEditorLimits.ValidateContent(content);
        try
        {
            var codec = GetEncoding(encoding); var payload = codec.GetBytes(content); var prefix = codec.GetPreamble();
            TextEditorLimits.ValidateSize(payload.LongLength + prefix.Length);
            return prefix.Length == 0 ? payload : [.. prefix, .. payload];
        }
        catch (EncoderFallbackException ex)
        { throw new IOException(encoding.Name + " 无法表示部分字符，未保存。请选择 UTF-8 等支持这些字符的编码。", ex); }
    }
}
