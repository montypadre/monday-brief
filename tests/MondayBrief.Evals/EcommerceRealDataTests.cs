using MondayBrief.Core.Ingestion;
using MondayBrief.Core.Ingestion.Adapters;
using Xunit;

namespace MondayBrief.Evals;

/// <summary>Reads the committed export and checks the totals in data/raw/SEED_CHECK.md</summary>
public sealed class EcommerceRealDataTests
{
    [Fact]
    public async Task Online_export_matches_the_seed_check_totals()
    {
        var raw = RepoPaths.RawDataPath;
        Assert.NotNull(raw);

        var adapter = new EcommerceJsonAdapter(BusinessClock.FromId("America/Chicago"));
        Assert.True(adapter.CanRead(raw!), "Online export is missing; run the seed generator.");

        var batch = await adapter.ReadAsync(raw!);

        Assert.Equal(3_533, batch.Orders.Count);
        Assert.Equal(19_650_750L, batch.Orders.Sum(o => o.SubtotalCents));

        // The store was closed on Aug 12 but the website kept selling.
        Assert.Equal(3, batch.Orders.Count(o => o.BusinessDate == new DateOnly(2026, 8, 12)));
    }
}