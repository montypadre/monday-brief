using MondayBrief.Core.Entities;

namespace MondayBrief.Core.Ingestion.Adapters;

/// <summary>
/// Reads the web analytics export. Source quirks handled here: a "#" comment preamble before the 
/// header, yyyyMMdd dates, adn report column names ("Total users", "Views") that differ from ours.
/// Traffic is already daily and already in the shop's own time zone, so no conversion is needed.
/// </summary>
public sealed class AnalyticsCsvAdapter : IDataSourceAdapter
{
    public const string TrafficFileName = "web_analytics_daily.csv";
    
    private const string DateFormat = "yyyyMMdd";

    public string SourceSystem => SourceSystems.Analytics;

    public string DisplayName => "Web analytics (CSV export)";

    public bool CanRead(string rawDataPath) => File.Exists(Path.Combine(rawDataPath, TrafficFileName));

    public async Task<SourceBatch> ReadAsync(string rawDataPath, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(rawDataPath, TrafficFileName);
        var traffic = new List<SourceTraffic>();
        var seen = new HashSet<DateOnly>();

        await foreach (var row in new CsvFile(path).ReadRowsAsync(cancellationToken))
        {
            var date = row.Date("Date", DateFormat);
            if (!seen.Add(date))
            {
                throw row.Error($"Duplicate row for {date:yyyy-MM-dd}.");
            }

            var sessions = row.Int("Sessions");
            var users = row.Int("Total users");
            var pageViews = row.Int("Views");

            if (sessions < 0 || users < 0 || pageViews < 0)
            {
                throw row.Error("Traffic counts cannot be negative.");
            }

            if (users > sessions)
            {
                throw row.Error($"Users ({users}) cannot exceed sessions ({sessions}).");
            }

            traffic.Add(new SourceTraffic(date, sessions, users, pageViews));
        }

        traffic.Sort((a, b) => a.Date.CompareTo(b.Date));
        return new SourceBatch(SourceSystem, [], [], traffic);
    }
}