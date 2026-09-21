using System.Threading.RateLimiting;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MondayBrief.Core.Ai;
using MondayBrief.Core.Alerts;
using MondayBrief.Core.Data;
using MondayBrief.Core.Ingestion;
using MondayBrief.Core.Ingestion.Adapters;
using MondayBrief.Core.Kpis;
using MondayBrief.Core.Options;
using Microsoft.AspNetCore.RateLimiting;
using MondayBrief.Core.Briefs;

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
builder.Services.AddScoped<TimeSeriesService>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<BusinessTools>();
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.AddHttpClient<IAnthropicClient, AnthropicClient>();
builder.Services.AddScoped<AskService>();
builder.Services.AddScoped<BriefService>();

// The demo is public; /ask costs money per call.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddFixedWindowLimiter("ask", options =>
    {
        options.PermitLimit = 10;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueLimit = 0;
    });
});

var app = builder.Build();

app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

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

app.MapGet("/api/timeseries", async (
    string? metric,
    string? by,
    string? bucket,
    string? range,
    TimeSeriesService series,
    IOptions<AppOptions> options,
    CancellationToken ct) =>
{
    if (!TimeSeriesRequest.TryParse(metric, by, bucket, range, options.Value.AsOfDate, out var request, out var error))
    {
        return Results.BadRequest(new { error });
    }

    return Results.Ok(await series.GetAsync(request, ct));
});

app.MapGet("/api/alerts", async (
    AlertService alerts,
    IOptions<AppOptions> options,
    CancellationToken ct) =>
    Results.Ok(await alerts.EvaluateAsOfAsync(options.Value.AsOfDate, ct)));

app.MapPost("/api/ask", async (AskRequest body, AskService ask, ILogger<Program> Logger, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Question))
    {
        return Results.BadRequest(new { error = "Ask a question about the business." });
    }

    if (body.Question.Length > 500)
    {
        return Results.BadRequest(new { error = "Question is too long; keep it under 500 characrers." });
    }

    try
    {
        return Results.Ok(await ask.AskAsync(body.Question, ct));
    }
    catch (AnthropicException ex)
    {
        Logger.LogError(ex, "Anthropic call failed");
        return Results.Problem("The assistant is unavailable right now.", statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireRateLimiting("ask");

app.MapGet("/api/brief/latest", async (BriefService briefs, CancellationToken ct) =>
{
    var brief = await briefs.GetLatestAsync(ct);
    return brief is null
        ? Results.NotFound(new { error = "No brief has been generated yet." })
        : Results.Ok(new
        {
            weekStart = brief.WeekStart,
            text = brief.RenderedText,
            content = JsonDocument.Parse(brief.Json).RootElement,
            model = brief.Model,
            generatedUtc = brief.CreatedUtc,
        });
});

app.MapPost("/api/brief/generate", async (BriefService briefs, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var brief = await briefs.GeneratedAsync(cancellationToken: ct);
        return Results.Ok(new { weekStart = brief.WeekStart, text = brief.RenderedText, model = brief.Model });
    }
    catch (AnthropicException ex)
    {
        logger.LogError(ex, "Brief generation failed");
        return Results.Problem("Could not generate a brief right now.", statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireRateLimiting("ask");

// For the WordPress block: the brief plus three headline cards, in one call, behind a key.
app.MapGet("/api/wp/summary", async (
   HttpRequest request,
   IConfiguration config,
   BriefService briefs,
   KpiService kpis,
   IOptions<AppOptions> options,
   CancellationToken ct) =>
{
    var expected = config["WordPress:ApiKey"];
    if (string.IsNullOrWhiteSpace(expected))
    {
        return Results.Problem("WordPress access is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var supplied = request.Headers["X-Api-Key"].ToString();

    // Constant-time comparison, so response timing can't be used to guess the key.
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
    {
        return Results.Unauthorized();
    }

    var brief = await briefs.GetLatestAsync(ct);
    var summary = await kpis.GetSummaryAsync(DateRange.LastDays(options.Value.AsOfDate, 30), ct);

    return Results.Ok(new
    {
        weekStart = brief?.WeekStart,
        text = brief?.RenderedText,
        model = brief?.Model,
        range = summary.Range,
        cards = summary.Cards.Where(c => c.Key is "revenue" or "orders" or "conversion"),
    });
});

app.Run();

// Lets WebApplicationFactory reach the entry point from test projects later.
public partial class Program;

public sealed record AskRequest(string Question);