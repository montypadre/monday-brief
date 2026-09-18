using MondayBrief.Core.Ingestion;
using MondayBrief.Core.Ingestion.Adapters;
using Xunit;

namespace MondayBrief.Evals;

/// <summary>Reads the committed export and checks the toals in data/raw/SEED_CHECK.md.</summary>
public sealed class PosRealDataTests
{
    [Fact]
    public async Task Pos_export_matches_the_seed_check_totals()
    {
        var raw = RepoPaths.RawDataPath;
        Assert.NotNull(raw);

        var adapter = new PosCsvAdapter(BusinessClock.FromId("America/Chicago"));
        Assert.True(adapter.CanRead(raw!), "POS export files are missing; run the seed generator.");

        var batch = await adapter.ReadAsync(raw!);

        Assert.Equal(24, batch.Products.Count);
        Assert.Equal(31_795, batch.Orders.Count);
        Assert.Equal(63_422_825L, batch.Orders.Sum(o => o.SubtotalCents));

        // The store was closed for the storm.
        Assert.Equal(0, batch.Orders.Count(o => o.BusinessDate == new DateOnly(2026, 8, 12)));
    }
}