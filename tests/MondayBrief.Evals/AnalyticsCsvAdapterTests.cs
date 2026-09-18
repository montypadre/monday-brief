using MondayBrief.Core.Ingestion;
using MondayBrief.Core.Ingestion.Adapters;
using Xunit;

namespace MondayBrief.Evals;

public sealed class AnalyticsCsvAdapterTests : IDisposable
{
    private const string Traffic = """
        # ----------------------------------------
        # Bayside Mercantile - Web analytics export
        # Report: Traffic by day
        # 2026811-20260813
        # ----------------------------------------
        Date,Sessions,Total users,Views
        20260811,405,318,1256
        20260813,344,270,1004
        20260812,322,248,920
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("analytics-adapter-tests").FullName;
    private readonly AnalyticsCsvAdapter _adapter = new();

    private void WriteSource(string csv = Traffic) =>
        File.WriteAllText(Path.Combine(_dir, AnalyticsCsvAdapter.TrafficFileName), csv);

    [Fact]
    public async Task Comment_preamble_is_skipped_and_columns_are_mapped()
    {
        WriteSource();
        var batch = await _adapter.ReadAsync(_dir);

        Assert.Equal(3, batch.Traffic.Count);
        var storm = batch.Traffic.Single(t => t.Date == new DateOnly(2026, 8, 12));
        Assert.Equal(322, storm.Sessions);
        Assert.Equal(248, storm.Users);
        Assert.Equal(920, storm.PageViews);
    }

    [Fact]
    public async Task Rows_come_back_in_date_order()
    {
        WriteSource();
        var batch = await _adapter.ReadAsync(_dir);

        Assert.Equal("2026-08-11|2026-08-12|2026-08-13", string.Join("|", batch.Traffic.Select(t => t.Date.ToString("yyyy-MM-dd"))));
    }

    [Fact]
    public async Task Traffic_only_export_has_no_orders_or_products()
    {
        WriteSource();
        var batch = await _adapter.ReadAsync(_dir);

        Assert.Empty(batch.Orders);
        Assert.Empty(batch.Products);
    }

    [Fact]
    public async Task Duplicate_day_is_rejected()
    {
        WriteSource("""
            Date,Sessions,Total users,Views
            20260811,405,318,1256
            20260811,405,318,1256
            """);

        var ex = await Assert.ThrowsAsync<SourceFormatException>(() => _adapter.ReadAsync(_dir));
        Assert.Equal(3, ex.Line);
        Assert.Contains("2026-08-11", ex.Message);
    }

    [Fact]
    public async Task More_users_than_sessions_is_rejected()
    {
        WriteSource("""
            Date,Sessions,Total users,Views
            20260811,405,900,1256
            """);

        var ex = await Assert.ThrowsAsync<SourceFormatException>(() => _adapter.ReadAsync(_dir));
        Assert.Equal(2, ex.Line);
    }

    [Fact]
    public async Task Wrong_date_format_names_the_column()
    {
        WriteSource("""
            Date,Sessions,Total users,Views
            2026-08-11,405,318,1256
            """);

        var ex = await Assert.ThrowsAsync<SourceFormatException>(() => _adapter.ReadAsync(_dir));
        Assert.Contains("Date", ex.Message);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}