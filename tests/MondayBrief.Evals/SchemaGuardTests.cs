using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MondayBrief.Core.Data;
using MondayBrief.Core.Entities;
using Xunit;

namespace MondayBrief.Evals;

public sealed class SchemaGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MondayBriefDbContext _db;

    public SchemaGuardTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<MondayBriefDbContext>().UseSqlite(_connection).Options;
        _db = new MondayBriefDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public void Schema_creates_with_two_channels()
    {
        var codes = _db.Channels.OrderBy(c => c.Id).Select(c => c.Code).ToList();
        Assert.Equal(["InStore", "Online"], codes);
    }

    [Fact]
    public void Three_alert_rules_are_seeded()
    {
        var keys = _db.AlertRules.OrderBy(r => r.Id).Select(r => r.Key).ToList();
        Assert.Equal(["weekly-revenue-drop", "conversion-below-2pct", "product-units-drop"], keys);
    }

    [Fact]
    public void Product_has_no_cost_data_so_margin_questions_stay_refusals()
    {
        var entity = _db.Model.FindEntityType(typeof(Product))!;
        var forbidden = new[] { "cost", "margin", "cogs", "wholesale", "profit" };
        var offenders = entity.GetProperties()
            .Select(p => p.Name)
            .Where(name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Money_is_stored_as_integer_cents()
    {
        var properties = _db.Model.GetEntityTypes().SelectMany(e => e.GetProperties());
        var money = properties.Where(p => p.Name.EndsWith("Cents", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void Utc_timestamps_round_trip_with_utc_kind()
    {
        var product = new Product { Sku = "TEST-1", Name = "Test", Category = "Apparel", ListPriceCents = 100 };
        var placed = new DateTime(2026, 8, 12, 4, 30, 0, DateTimeKind.Utc);
        _db.Orders.Add(new Order
        {
            ChannelId = ChannelIds.Online,
            SourceSystem = SourceSystems.Ecommerce,
            ExternalId = "1",
            PlacedUtc = placed,
            BusinessDate = new DateOnly(2026, 8, 11),
            SubtotalCents = 100,
            Lines = [new OrderLine { Product = product, Quantity = 1, UnitPriceCents = 100, LineTotalCents = 100}],
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var loaded = _db.Orders.Single();
        Assert.Equal(DateTimeKind.Utc, loaded.PlacedUtc.Kind);
        Assert.Equal(placed, loaded.PlacedUtc);
        Assert.Equal(new DateOnly(2026, 8, 11), loaded.BusinessDate);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}