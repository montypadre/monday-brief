using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MondayBrief.Core.Kpis;
using MondayBrief.Core.Options;

namespace MondayBrief.Core.Ai;

public sealed record AskSource(string Tool, string Arguments, bool Ok, string? Error, string Payload);

public sealed record AskResult(
    string Question,
    string Answer,
    IReadOnlyList<AskSource> Sources,
    string Model,
    int Rounds,
    long ElapsedMs);

/// <summary>
/// The tool-use loop. The model picks tools; this class runs them and hands back only what they 
/// returned. No figure in an answer can come from anywhere else.
/// </summary>
public sealed class AskService(
    IAnthropicClient client,
    BusinessTools tools,
    KpiService kpis,
    IOptions<AppOptions> appOptions,
    IOptions<AnthropicOptions> aiOptions)
{
    private static readonly JsonSerializerOptions ToolJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<AskResult> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question is empty.", nameof(question));
        }

        var stopwatch = Stopwatch.StartNew();
        var app = appOptions.Value;
        var ai = aiOptions.Value;

        var systemPrompt = await BuildSystemPromptAsync(cancellationToken);
        var schemas = ToolSchemas.All();
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = question },
        };

        var sources = new List<AskSource>();
        var model = ai.Model;

        for (var round = 1; round <= ai.MaxToolRounds; round++)
        {
            var reply = await client.SendAsync(new ModelRequest(systemPrompt, messages, schemas), cancellationToken);
            model = string.IsNullOrEmpty(reply.Model) ? model : reply.Model;
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = reply.Content.DeepClone() });

            if (reply.ToolCalls.Count == 0)
            {
                stopwatch.Stop();
                return new AskResult(question, reply.Text.Trim(), sources, model, round, stopwatch.ElapsedMilliseconds);
            }

            var results = new JsonArray();
            foreach (var call in reply.ToolCalls)
            {
                var result = await ExecuteAsync(call, cancellationToken);

                var payload = result.IsSuccess
                    ? JsonSerializer.Serialize(result.Data, ToolJson)
                    : JsonSerializer.Serialize(new { error = result.Error }, ToolJson);

                sources.Add(new AskSource(call.Name, Describe(call.Input), result.IsSuccess, result.Error, payload));

                results.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = call.Id,
                    ["is_error"] = !result.IsSuccess,
                    ["content"] = payload,
                });
            }

            messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
        }

        stopwatch.Stop();
        return new AskResult(
            question,
            "I wasn't able to finish looking that up. Try asking about one figure or one date range at a time.",
            sources,
            model,
            ai.MaxToolRounds,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var input = call.Input;
        var asOf = appOptions.Value.AsOfDate;

        try
        {
            return call.Name switch
            {
                BusinessTools.GetMetric => await tools.GetMetricAsync(
                    RequiredText(input, "metric"),
                    RequiredDate(input, "start"),
                    RequiredDate(input, "end"),
                    OptionalText(input, "channel"),
                    cancellationToken),
                
                BusinessTools.ComparePeriods => await tools.ComparePeriodsAsync(
                    RequiredText(input, "metric"),
                    RequiredDate(input, "period_a_start"),
                    RequiredDate(input, "period_a_end"),
                    RequiredDate(input, "period_b_start"),
                    RequiredDate(input, "period_b_end"),
                    OptionalText(input, "channel"),
                    cancellationToken),

                BusinessTools.TopProducts => await tools.TopProductsAsync(
                    RequiredInt(input, "n"),
                    RequiredDate(input, "start"),
                    RequiredDate(input, "end"),
                    OptionalText(input, "direction") ?? "top",
                    cancellationToken),

                BusinessTools.ListAlerts => await tools.ListAlertsAsync(
                    asOf,
                    OptionalDate(input, "start"),
                    OptionalDate(input, "end"),
                    cancellationToken),

                _ => ToolResult.Failure(call.Name, $"There is no tool called '{call.Name}'."),
            };
        }
        catch (ArgumentException ex)
        {
            // A malformed argument is something the model can fix on the next round.
            return ToolResult.Failure(call.Name, ex.Message);
        }
    }

    private async Task<string> BuildSystemPromptAsync(CancellationToken cancellationToken)
    {
        var app = appOptions.Value;
        var window = await kpis.GetDataWindowAsync(cancellationToken);
        var coverage = window is { } w
            ? $"{w.Start:yyyy-MM-dd} to {w.End:yyyy-MM-dd}"
            : "no data has been loaded";

        return $"""
            You are the assistant inside {app.DisplayName}, a KPI dashboard for {app.BusinessName}, a gift shop
            and coffee counter with an in-store channel and an online store.

            Today is {app.AsOfDate:yyyy-MM-dd}. The data covers {coverage}.
            "Last 30 days" and similar phrases mean complete days ending {app.AsOfDate.AddDays(-1):yyyy-MM-dd}.

            Rules you must follow:
            1. Every number in your answer must come from a tool result in this conversation. Never estimate a
               figure, never work one out yourself, and never use numbers from your own knowledge.
            2. Use compare_periods for any change, difference or trend. Do not subtract or divide numbers yourself.
            3. If the tools cannot answer, say plainly what you do not have. For example: "I don't have that data."
               Do not guess and do not offer a number as an approximation.
            4. This dataset has revenue, orders, average order value, sessions and online conversion rate. There is 
               no cost, margin, profit, staffing or inventory data.
            5. Say which dates you used, in plain words such as "in March" or "over the last 30 days".
            6. Answer in two to four sentences. Plain language, no jargon. Give figures context rather than listing 
               bare numbers.
            7. Quote figures exactly as the fact sheet gives them. Never round, approximate, or write "nearly", "about", or "roughly" in front of a number.
            8. Write money with a dollar sign and thousands separators ($15,560.50) and rates with a percent sign.
            """;
    }

    private static string Describe(JsonElement input) =>
        input.ValueKind != JsonValueKind.Object
            ? string.Empty
            : string.Join(", ", input.EnumerateObject().Select(p => $"{p.Name}={p.Value}"));

    private static string RequiredText(JsonElement input, string name) =>
        OptionalText(input, name) ?? throw new ArgumentException($"Missing required argument '{name}'.");

    private static string? OptionalText(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateOnly RequiredDate(JsonElement input, string name) =>
        OptionalDate(input, name) ?? throw new ArgumentException($"Missing or unreadable date argument '{name}'. Use YYYY-MM-DD.");

    private static DateOnly? OptionalDate(JsonElement input, string name)
    {
        var text = OptionalText(input, name);
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static int RequiredInt(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : throw new ArgumentException($"Missing or unreadable number argument '{name}'");
}