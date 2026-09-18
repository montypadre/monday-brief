using MondayBrief.Core.Ingestion.Adapters;
using Xunit;

namespace MondayBrief.Evals;

/// <summary>Reads the committed export and checks the totals in data/raw/SEED_CHECK.md.</summary>
public sealed class AnalyticsRealDataTests
{
    [Fact]
    public async Task Analytics_export_matches_the_seed_check_totals()
    {
        var raw = RepoPaths.RawDataPath;
        Assert.NotNull(raw);

        var adapter = new AnalyticsCsvAdapter();
        Assert.True(adapter.CanRead(raw!), "Analytics export is missing; run the seed generator.");

        var batch = await adapter.ReadAsync(raw!);

        // 52 Monday-to-Sunday weeks, one row per day.
        Assert.Equal(364, batch.Traffic.Count);
        Assert.Equal(new DateOnly(2025, 9, 1), batch.Traffic[0].Date);
        Assert.Equal(new DateOnly(2026, 8, 30), batch.Traffic[^1].Date);
        Assert.Equal(154_341, batch.Traffic.Sum(t => t.Sessions));
    }
}