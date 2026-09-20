using MondayBrief.Core.Ingestion;
using MondayBrief.Evals.Live;
using Xunit;

namespace MondayBrief.Evals;

public sealed class CsvParserTests
{
    [Fact]
    public void Quoted_field_keeps_its_comma()
    {
        var fields = CsvParser.SplitLine("CB-DROP-12,\"Drip Coffee, 12 oz\",Coffee Bar");
        Assert.Equal(3, fields.Length);
        Assert.Equal("Drip Coffee, 12 oz", fields[1]);
    }

    [Fact]
    public void Doubled_quotes_become_one_quote()
    {
        var fields = CsvParser.SplitLine("a,\"say \"\"hi\"\"\",b");
        Assert.Equal("say \"hi\"", fields[1]);
    }

    [Fact]
    public void Empty_fields_are_preserved()
    {
        var fields = CsvParser.SplitLine("a,,c");
        Assert.Equal(3, fields.Length);
        Assert.Equal(string.Empty, fields[1]);
    }

    [Fact]
    public void Unterminated_quote_is_rejected()
    {
        Assert.Throws<FormatException>(() => CsvParser.SplitLine("a,\"b,c"));
    }

    [Fact]
    public void Checker_ignores_date_ranges_and_handles_curly_apostrophes()
    {
        var figures = NumberCheck.Extract("Total revenue for the last 30 days (August 1\u201330, 2026) was 55851.25.");
        Assert.Equal([55851.25m], figures);
    }
}