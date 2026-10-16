using System.Text;
using ShellX.Core.Files;
using ShellX.Infrastructure.Files;

namespace ShellX.Infrastructure.Tests;

public sealed class TextEncodingTests
{
    public static IEnumerable<object[]> Encodings => TextEncodingInfo.Supported.Select(info => new object[] { info.CodePage, info.HasBom });
    public static Encoding StandardEncoding(int codePage, bool bom) => codePage switch
    {
        65001 => new UTF8Encoding(bom, true), 1200 or 1201 => new UnicodeEncoding(codePage == 1201, bom, true),
        12000 or 12001 => new UTF32Encoding(codePage == 12001, bom, true),
        _ => CodePagesEncodingProvider.Instance.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            ?? Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
    };
    public static string Sample(int codePage) => codePage switch
    {
        1252 or 28591 => "name=café\r\n", 1251 => "name=Привет\r\n", 932 => "name=日本語\r\n", 949 => "name=한국어\r\n",
        _ => "name=中文配置\r\n"
    };

    [Theory, MemberData(nameof(Encodings))]
    public async Task LocalFilesPreserveEncodingBomAndNewlinesAfterEditing(int codePage, bool bom)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-encoding-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "sample.conf"); var reference = StandardEncoding(codePage, bom);
        var content = Sample(codePage); var originalBytes = reference.GetPreamble().Concat(reference.GetBytes(content)).ToArray();
        var codec = new TextCodec(); var local = new LocalFileSystem(root);
        try
        {
            await File.WriteAllBytesAsync(path, originalBytes);
            var document = await local.ReadTextAsync(path);
            var decoded = codec.Decode(document.SourceBytes, TextEncodingInfo.Find(codePage, bom));
            Assert.Equal(content, decoded.Content); Assert.Equal(bom, decoded.Encoding.HasBom);
            document = document with { Content = decoded.Content, Encoding = decoded.Encoding };
            var unchanged = await local.SaveTextAsync(document, document.Content);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
            var updated = await local.SaveTextAsync(unchanged, content + "# edited\r\n");
            Assert.Equal(reference.GetPreamble().Concat(reference.GetBytes(content + "# edited\r\n")).ToArray(), await File.ReadAllBytesAsync(path));
            Assert.Equal(codePage, updated.Encoding.CodePage); Assert.Equal(bom, updated.Encoding.HasBom);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DetectsUnicodeChineseFallbackAndXmlDeclarationsWithoutReplacementCharacters()
    {
        var codec = new TextCodec();
        foreach (var codePage in new[] { 65001, 1200, 1201, 12000, 12001 })
        foreach (var bom in new[] { false, true })
        {
            var encoding = StandardEncoding(codePage, bom);
            var sample = Sample(codePage);
            var detected = codec.Decode(encoding.GetPreamble().Concat(encoding.GetBytes(sample)).ToArray());
            Assert.Equal(sample, detected.Content); Assert.Equal(codePage, detected.Encoding.CodePage); Assert.Equal(bom, detected.Encoding.HasBom);
        }
        var gbk = StandardEncoding(936, false);
        var guessed = codec.Decode(gbk.GetBytes(Sample(936)));
        Assert.Equal(Sample(936), guessed.Content); Assert.Contains("推测", guessed.Note);
        const string xml = "<?xml version=\"1.0\" encoding=\"GB2312\"?><root>中文</root>";
        var declared = codec.Decode(gbk.GetBytes(xml));
        Assert.Equal(936, declared.Encoding.CodePage); Assert.Equal(xml, declared.Content);
        Assert.Throws<IOException>(() => codec.Encode("emoji 😀", TextEncodingInfo.Find(936)));
    }
}
