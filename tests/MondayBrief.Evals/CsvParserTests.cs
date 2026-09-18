using MondayBrief.Core.Ingestion;
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
}