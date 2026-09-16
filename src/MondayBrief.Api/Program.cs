using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MondayBrief.Core.Data;
using MondayBrief.Core.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

var rawConnectionString = builder.Configuration.GetConnectionString("MondayBrief")
    ?? throw new InvalidOperationException("Connection string 'MondayBrief' is missing.");
var connectionString = SqlitePaths.ResolveConnectionString(rawConnectionString, builder.Environment.ContentRootPath);

builder.Services.AddDbContext<MondayBriefDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

// Code below Build() does not run under `dotnet ef`, so migrating here is safe for design-time tooling.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MondayBriefDbContext>();
    await db.Database.MigrateAsync();
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
        trafficDays = await db.DailyTraffic.CountAsync(ct),
    });
});

app.Run();

// Lets WebApplicationFactory reach the entry point from test projects later.
public partial class Program;