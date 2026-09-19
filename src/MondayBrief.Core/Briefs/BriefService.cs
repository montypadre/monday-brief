using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MondayBrief.Core.Ai;
using MondayBrief.Core.Alerts;
using MondayBrief.Core.Data;
using MondayBrief.Core.Entities;
using MondayBrief.Core.Kpis;
using MondayBrief.Core.Options;

namespace MondayBrief.Core.Briefs;

public sealed class BriefService(
    MondayBriefDbContext db,
    KpiService kpis,
    AlertService alerts,
    IAnthropicClient client,
    IOptions<AppOptions> appOptions,
    IOptions<AnthropicOptions> aiOptions)
{
    /// <summary>Weekly product ranking floor. Lower than the 4-week floor because a week holds fewer units.</summary>
    private const int MinWeeklyUnits = 30;

    private const int MinWords = 110;
    private const int MaxWords = 170;

    private static readonly JsonSerializerOptions FactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    /// <summary>The Monday of the last complete week before the demo's today.</summary>
    public DateOnly LatestWeekStart()
    {
        var lastComplete = appOptions.Value.AsOfDate.AddDays(-1);
        var monday = lastComplete.AddDays(-(((int)lastComplete.DayOfWeek + 6) % 7));
        return lastComplete.DayOfWeek == DayOfWeek.Sunday ? monday : monday.AddDays(-7);
    }

    public async Task<BriefFacts> BuildFactsAsync(DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        var week = new DateRange(weekStart, weekStart.AddDays(6));
        var previous = week.Previous();

        var current = await kpis.GetTotalsAsync(week, null, cancellationToken);
        var before = await kpis.GetTotalsAsync(previous, null, cancellationToken);
        var currentInStore = await kpis.GetTotalsAsync(week, ChannelIds.InStore, cancellationToken);
        var beforeInStore = await kpis.GetTotalsAsync(previous, ChannelIds.InStore, cancellationToken);
        var currentOnline = await kpis.GetTotalsAsync(week, ChannelIds.Online, cancellationToken);
        var beforeOnline = await kpis.GetTotalsAsync(previous, ChannelIds.Online, cancellationToken);

        var currentProducts = await kpis.GetProductTotalsAsync(week, cancellationToken);
        var previousProducts = await kpis.GetProductTotalsAsync(previous, cancellationToken);
        var previousUnits = previousProducts.ToDictionary(p => p.Sku, p => p.Units, StringComparer.Ordinal);

        var top = currentProducts
            .OrderByDescending(p => p.RevenueCents)
            .ThenBy(p => p.Sku, StringComparer.Ordinal)
            .Take(3)
            .Select(p => new FactProduct(p.Name, Money(p.RevenueCents), p.Units, null, null))
            .ToList();

        var decliners = currentProducts
            .Where(p => previousUnits.GetValueOrDefault(p.Sku) >= MinWeeklyUnits)
            .Select(p => new FactProduct(
                p.Name,
                Money(p.RevenueCents),
                p.Units,
                previousUnits[p.Sku],
                Round1(100.0 * (p.Units - previousUnits[p.Sku]) / previousUnits[p.Sku])))
            .Where(p => p.ChangePct < 0)
            .OrderBy(p => p.ChangePct)
            .Take(3)
            .ToList();

        // Alerts run on their own windows, which is how the four-week product decline reaches a weekly brief.
        var triggered = await alerts.EvaluateAsOfAsync(appOptions.Value.AsOfDate, cancellationToken);

        return new BriefFacts(
            week.Start, week.End, previous.Start, previous.End,
            Value(Money(current.RevenueCents), Money(before.RevenueCents), "currency"),
            Value(Money(currentInStore.RevenueCents), Money(beforeInStore.RevenueCents), "currency"),
            Value(Money(currentOnline.RevenueCents), Money(beforeOnline.RevenueCents), "currency"),
            Value(current.Orders, before.Orders, "number"),
            Value(Round2(current.AverageOrderValueCents / 100m), Round2(before.AverageOrderValueCents / 100m), "currency"),
            Value(Round2((decimal)current.ConversionPct), Round2((decimal)before.ConversionPct), "percent"),
            top,
            decliners,
            [.. triggered.Select(a => new FactAlert(a.Subject, a.Message))]);
    }

    public async Task<Brief> GeneratedAsync(DateOnly? weekStart = null, CancellationToken cancellationToken = default)
    {
        var start = weekStart ?? LatestWeekStart();
        var facts = await BuildFactsAsync(start, cancellationToken);

        var reply = await client.SendAsync(
            new ModelRequest(SystemPrompt(), UserMessage(facts), []), cancellationToken);

        var content = Parse(reply.Text);
        var rendered = Render(content);

        var wordCount = CountWords(rendered);
        if (wordCount is < MinWords or > MaxWords)
        {
            // One corrective round rather than shipping something the owner will not read.
            var retry = await client.SendAsync(
                new ModelRequest(SystemPrompt(), UserMessage(facts, wordCount), []), cancellationToken);
            content = Parse(retry.Text);
            rendered = Render(content);
        }

        var existing = await db.Briefs.FirstOrDefaultAsync(b => b.WeekStart == start, cancellationToken);
        if (existing is null)
        {
            existing = new Brief
            {
                WeekStart = start,
                Json = JsonSerializer.Serialize(content, FactJson),
                RenderedText = rendered,
                Model = string.IsNullOrEmpty(reply.Model) ? aiOptions.Value.Model : reply.Model,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Briefs.Add(existing);
        }
        else
        {
            existing.Json = JsonSerializer.Serialize(content, FactJson);
            existing.RenderedText = rendered;
            existing.Model = string.IsNullOrEmpty(reply.Model) ? aiOptions.Value.Model : reply.Model;
            existing.CreatedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public Task<Brief?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        db.Briefs.OrderByDescending(b => b.WeekStart).FirstOrDefaultAsync(cancellationToken);
    
    private string SystemPrompt() => 
        $$"""
        You write the Monday brief for {{appOptions.Value.BusinessName}}, read by the owner over coffee.

        You receive a JSON fact sheet. Every number you write must appear in it. Never calculate, estimate
        or add a figure of your own. If something is not in the fact sheet, leave it out.

        How to write:
        - Lead with the most important change, not a greeting.
        - Give numbers context ("down 6% from last week"), never bare figures.
        - Plain words. No "leverage", "synergy", "robust", "optimize" or "actionable".
        - Frame the action as a suggestion, not an order.
        - The whole brief must read in under a minute: 120 to 160 words once assembled.
        - Quote figures exactly as the fact sheet gives them. Never round, approximate, or write "nearly", "about" or "roughly" in front of a number.

        Reply with JSON only. No preamble, no markdown fences. Shape:
        {
            "headline": "one sentence naming the week's biggest change",
            "whats_up": ["one or two sentences, each about something that improved"],
            "whats_down": ["one or two sentences, each aabout something that fell"],
            "watch_list": ["one or two sentences about anything that needs attention soon"],
            "one_action": "one specific thing worth doing this week, phrased as a suggestion"
        }
        """;

    private JsonArray UserMessage(BriefFacts facts, int? previousWordCount = null)
    {
        var text = new StringBuilder();
        text.AppendLine($"Fact sheet for the week of {facts.WeekStart:yyyy-MM-dd} to {facts.WeekEnd:yyyy-MM-dd}:");
        text.AppendLine(JsonSerializer.Serialize(facts, FactJson));

        if (previousWordCount is { } count)
        {
            text.AppendLine();
            text.AppendLine($"Your previous attempt was {count} words. Rewrite it to land between 120 and 160 words.");
        }

        return [new JsonObject { ["role"] = "user", ["content"] = text.ToString() }];
    }

    private static BriefContent Parse(string reply)
    {
        var json = reply.Trim();

        // Models sometimes wrap JSON in fences despite instructions.
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = json.IndexOf('{');
            var lastBrace = json.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                json = json[firstBrace..(lastBrace + 1)];
            }
        }

        try
        {
            return JsonSerializer.Deserialize<BriefContent>(json, FactJson)
                ?? throw new InvalidOperationException("Brief JSON was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Brief response was not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Concatenation only. Computing anything here would make the renderer a second source of numbers.</summary>
    private static string Render(BriefContent content)
    {
        var text = new StringBuilder();
        text.AppendLine(content.Headline.Trim());
        text.AppendLine();

        foreach (var line in content.WhatsUp.Concat(content.WhatsDown).Concat(content.WatchList))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                text.AppendLine(line.Trim());
            }
        }

        text.AppendLine();
        text.Append(content.OneAction.Trim());
        return text.ToString().Trim();
    }

    public static int CountWords(string text) => 
        text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static FactValue Value(decimal current, decimal previous, string unit) => 
        new(current, previous, previous == 0 ? null : Round1((double)(100m * (current - previous) / previous)), unit);
    
    private static decimal Money(long cents) => Math.Round(cents / 100m, 2, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static double Round1(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}