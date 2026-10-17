using ShellX.Desktop.Services;

namespace ShellX.Desktop.Tests;

public sealed class McpLogFileReaderTests
{
    [Fact]
    public async Task ReaderReturnsNewRowsOneByOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-mcp-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mcp-audit-20261017.jsonl");
            await File.WriteAllLinesAsync(path, [
                """{"Timestamp":"2026-10-17T10:00:00+08:00","ConnectionName":"本地","Status":"succeeded","Command":"echo one","ExitCode":0,"DurationMilliseconds":5}""",
                "invalid-json"
            ], TestContext.Current.CancellationToken);
            var reader = new McpLogFileReader(root);

            var first = reader.ReadNewRows();
            Assert.Equal(2, first.Count);
            Assert.Contains("成功", first[0].Text);
            Assert.Contains("echo one", first[0].Text);
            Assert.Empty(reader.ReadNewRows());

            await File.AppendAllTextAsync(path, """{"Timestamp":"2026-10-17T10:01:00+08:00","ConnectionName":"本地","Status":"error","Command":"echo two","ExitCode":1,"DurationMilliseconds":8,"Error":"failed"}""" + Environment.NewLine, TestContext.Current.CancellationToken);
            var second = reader.ReadNewRows();
            var row = Assert.Single(second);
            Assert.Contains("[错误]", row.Text);
            Assert.Contains("echo two", row.Text);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}