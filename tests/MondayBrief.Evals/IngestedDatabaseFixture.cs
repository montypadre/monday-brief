using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MondayBrief.Core.Data;
using MondayBrief.Core.Ingestion;
using MondayBrief.Core.Ingestion.Adapters;
using Xunit;

namespace MondayBrief.Evals;

/// <summary>
/// An in-memory SQLite database with the committed export ingested into it. Built once per test run:
/// the connection must stay open, because closing it discards the database.
/// </summary>
public sealed class IngestedDatabaseFixture : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public MondayBriefDbContext Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var raw = RepoPaths.RawDataPath
            ?? throw new InvalidOperationException("data/raw not found; run the seed generator.");

        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MondayBriefDbContext>().UseSqlite(_connection).Options;
        Db = new MondayBriefDbContext(options);
        await Db.Database.EnsureCreatedAsync();

        var clock = BusinessClock.FromId("America/Chicago");
        IDataSourceAdapter[] adapters =
        [
            new PosCsvAdapter(clock),
            new EcommerceJsonAdapter(clock),
            new AnalyticsCsvAdapter(),
        ];

        await new IngestionService(Db, adapters).IngestAsync(raw);
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}