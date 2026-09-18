using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MondayBrief.Core.Data;
using MondayBrief.Core.Ingestion;
using MondayBrief.Core.Ingestion.Adapters;
using MondayBrief.Core.Kpis;
using MondayBrief.Core.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

var rawConnectionString = builder.Configuration.GetConnectionString("MondayBrief")
    ?? throw new InvalidOperationException("Connection string 'MondayBrief' is missing.");
var connectionString = SqlitePaths.ResolveConnectionString(rawConnectionString, builder.Environment.ContentRootPath);

builder.Services.AddDbContext<MondayBriefDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddSingleton(sp =>
    BusinessClock.FromId(sp.GetRequiredService<IOptions<AppOptions>>().Value.TimeZoneId));

// Adding a new client system means registering one more adapter here.
builder.Services.AddScoped<IDataSourceAdapter, PosCsvAdapter>();
builder.Services.AddScoped<IDataSourceAdapter, EcommerceJsonAdapter>();
builder.Services.AddScoped<IDataSourceAdapter, AnalyticsCsvAdapter>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<KpiService>();

var app = builder.Build();

// Code below Build() does not run under `dotnet ef`, so migrating here is safe for design-time tooling.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = scope.ServiceProvider.GetRequiredService<MondayBriefDbContext>();
    await db.Database.MigrateAsync();

    // Ingestion is idempotent, but a full pass costs a few seconds, so only run it on an empty database.
    if (!await db.Orders.AnyAsync())
    {
        var settings = services.GetRequiredService<IOptions<AppOptions>>().Value;
        var rawDataPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, settings.RawDataPath));
        var report = await services.GetRequiredService<IngestionService>().IngestAsync(rawDataPath);

        var logger = services.GetRequiredService<ILogger<Program>>();
        foreach (var source in report.Sources)
        {
            logger.LogInformation("Ingested {Source}: {Orders} orders, {Products} products, {Traffic} traffic days",
                source.DisplayName, source.OrdersRead, source.ProductsRead, source.TrafficRowsRead);
        }

        logger.LogInformation("Ingestion complete: {Report}", report);
    }
}

app.MapGet("/api/health", async (IOptions<AppOptions> options, MondayBriefDbContext db, CancellationToken ct) =>
{
    var settings = options.Value;
    return Results.Ok(new
    {
        status = "ok",
        name = settings.DisplayName,
        asOfDate = settings.AsOfDate,
        channels = await db.Channels.CountAsync(ct),
        products = await db.Products.CountAsync(ct),
        orders = await db.Orders.CountAsync(ct),
        orderLines = await db.OrderLines.CountAsync(ct),
        trafficDays = await db.DailyTraffic.CountAsync(ct),
    });
});

app.MapGet("/api/kpis", async (
    string? range,
    KpiService kpis,
    IOptions<AppOptions> options,
    CancellationToken ct) =>
{
    if (!RangeParser.TryParse(range, options.Value.AsOfDate, out var dateRange, out var error))
    {
        return Results.BadRequest(new { error });
    }

    return Results.Ok(await kpis.GetSummaryAsync(dateRange, ct));
}); 

app.Run();

// Lets WebApplicationFactory reach the entry point from test projects later.
public partial class Program;