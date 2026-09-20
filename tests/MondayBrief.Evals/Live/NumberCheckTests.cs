using MondayBrief.Evals.Live;
using Xunit;

namespace MondayBrief.Evals;

/// <summary>
/// The number check is what makes the eval suite mean anything, so it gets its own tests. On the first
/// live run it produced evelen false failures - date words read as figures, numbers inside tools strings
/// missed, and a curly apostrophe. These cases pin down each fix.
/// </summary>
public sealed class NumberCheckTests
{
    [Fact]
    public void Currency_formatting_is_stripped()
    {
        Assert.Equal([15_560.50m], NumberCheck.Extract("Online revenue in March was $15,560.50"));
    }

    [Fact]
    public void Perecentages_and_negatives_are_kept_as_figures()
    {
        Assert.Equal([48m], NumberCheck.Extract("Units fell 48% this quarter."));
        Assert.Equal([-48.0m], NumberCheck.Extract("Change was -48.0% overall."));
    }

    [Fact]
    public void Iso_dates_are_not_figures()
    {
        Assert.Equal([100.5m], NumberCheck.Extract("From 2026-03-01 to 2026-03-31 revenue was 100.5"));
    }

    [Fact]
    public void Spelled_dates_are_not_figures()
    {
        // "12 August" must not contribute a 12.
        Assert.Equal([0m], NumberCheck.Extract("On 12 August 2026 in-store revenue was 0."));
    }

    [Fact]
    public void Date_ranges_with_an_en_dash_are_not_figures()
    {
        // "August 1-30" must not contribute a 30, and "last 30 days" must not contribute another.
        var answer = "Total revenue for the last 30 days (August 1" + '\u2013' + "30, 2026) was 55851.25.";
        
        Assert.Equal([55_851.25m], NumberCheck.Extract(answer));
    }

    [Fact]
    public void Years_are_dates_rather_than_data()
    {
        Assert.Equal([1000m], NumberCheck.Extract("In 2026 revenue was 1000."));
    }

    [Fact]
    public void Payload_numbers_are_collected_at_any_deapth_including_inside_string()
    {
        // Alert messages carry their figures as prose, and those figures came from the database.
        var payload = """{"value":15560.5,"nested":{"units":311},"note":"sold 51 units"}""";

        var numbers = NumberCheck.FromPayload(payload);

        Assert.Contains(15_560.5m, numbers);
        Assert.Contains(311m, numbers);
        Assert.Contains(51m, numbers);
    }

    [Fact]
    public void A_payload_that_is_not_json_contributes_nothing()
    {
        Assert.Empty(NumberCheck.FromPayload("not json"));
    }

    [Fact]
    public void A_tool_value_quoted_with_formatting_counts_as_supported()
    {
        // The tool emits 15560.5; the model writes "$15,560.50".
        Assert.Empty(NumberCheck.Unsupported("Revenue was $15,560.50.", [15_560.5m]));
    }

    [Fact]
    public void Sign_is_ignored_so_a_decline_can_be_described_in_words()
    {
        // The tool returns -48.0; the model writes "down 48%".
        Assert.Empty(NumberCheck.Unsupported("Units are down 48%.", [-48.0m]));
    }

    [Fact]
    public void Rounding_to_fewer_decimals_is_allowed()
    {
        // Deliberate tolerance: quoting 15560.5 as $15,561 is a rounding choice, not an invention.
        Assert.Empty(NumberCheck.Unsupported("Revenue was about $15,561.", [15_560.5m]));
    }

    [Fact]
    public void A_figure_no_tool_returned_is_reported()
    {
        Assert.Equal([16_000m], NumberCheck.Unsupported("Revenue was 16000.", [15_560.5m]));
        Assert.Equal([15_560.75m], NumberCheck.Unsupported("Revenuw was 15560.75.", [15_560.5m]));
    }
}