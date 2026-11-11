using ShellX.Infrastructure.Ssh;

namespace ShellX.Infrastructure.Tests;

public sealed class LogReaderTests
{
    [Fact]
    public void NumberedOutputKeepsTotalAndRelativeLines()
    {
        var output = "__SX_LOG_TOTAL__1200\n   1\t第一行\n   2\t第二行\n";
        var (total, rows) = SshLogReader.ParseNumbered(output);
        Assert.Equal(1200, total);
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Relative);
        Assert.Equal("第一行", rows[0].Text);
        Assert.Equal("第二行", rows[1].Text);
    }

    [Fact]
    public void NumberedOutputWithoutMarkerStillReturnsRows()
    {
        var (total, rows) = SshLogReader.ParseNumbered("   1\t只有一行\n");
        Assert.Equal(0, total);
        Assert.Single(rows);
        Assert.Equal("只有一行", rows[0].Text);
    }

    [Fact]
    public void SearchOutputSeparatesMatchesFromContext()
    {
        var output = "10:命中行\n9-上下文\n--\n20:另一处命中\n";
        var result = SshLogReader.ParseSearch(output, 100);
        Assert.Equal(2, result.MatchCount);
        Assert.Equal(3, result.Lines.Count);
        Assert.True(result.Lines[0].IsMatch);
        Assert.False(result.Lines[1].IsMatch);
        Assert.True(result.Lines[2].IsMatch);
        Assert.Equal(9, result.Lines[1].Number);
    }

    [Fact]
    public void SearchOutputIsTruncatedAtLimit()
    {
        var output = "1:一\n2:二\n3:三\n";
        var result = SshLogReader.ParseSearch(output, 2);
        Assert.Equal(2, result.Lines.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void QuoteEscapesSingleQuotes()
    {
        Assert.Equal("'a'\\''b'", SshLogReader.Quote("a'b"));
        Assert.Equal("'/var/log/app.log'", SshLogReader.Quote("/var/log/app.log"));
    }

    [Fact]
    public void LongLineIsTruncated()
    {
        var text = new string('x', 5000);
        var trimmed = SshLogReader.Trim(text);
        Assert.True(trimmed.Length < 5000);
        Assert.Contains("已截断", trimmed);
    }
}