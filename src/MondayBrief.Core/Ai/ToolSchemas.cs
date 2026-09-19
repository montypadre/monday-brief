using System.Text.Json.Nodes;

namespace MondayBrief.Core.Ai;

/// <summary>
/// What the model is allowed to ask for. Descriptions matter as much as names: they are the only
/// guidance the model has about when each tool applies and what the data does not contain.
/// </summary>
public static class ToolSchemas
{
    public static JsonArray All() =>
    [
        Tool(
            BusinessTools.GetMetric,
            "Get one metric for one date range. Use this for any single figure.",
            new JsonObject
            {
                ["metric"] = Enum("revenue, orders, aov (average order value), sessions, conversion (online conversion rate, percent)",
                    "revenue", "orders", "aov", "sessions", "conversion"),
                ["start"] = Date("First day of the range, YYYY-MM-DD."),
                ["end"] = Date("Last day of the range, inclusive, YYYY-MM-DD."),
                ["channel"] = Enum("Sales channel. Omit for both. sessions and conversion cannot be filtered by channel.",
                    "instore", "online"),
            },
            "metric", "start", "end"),

        Tool(
            BusinessTools.ComparePeriods,
            "Compare one metric across two date ranges. Always use this for changes, trends or 'how does X compare to Y' - never substract numbers yourself.",
            new JsonObject
            {
                ["metric"] = Enum("Metric to compare.", "revenue", "orders", "aov", "sessions","conversion"),
                ["period_a_start"] = Date("First day of the earlier period."),
                ["period_a_end"] = Date("Last day of the earlier period."),
                ["period_b_start"] = Date("First day of the later period."),
                ["period_b_end"] = Date("Last day of the later period."),
                ["channel"] = Enum("Sales cahnnel. Omit for both.", "instore", "online"),
            },
            "metric", "period_a_start", "period_a_end", "period_b_start", "period_b_end"),

        Tool(
            BusinessTools.TopProducts,
            "Rank products for a date range: best sellers by revenue, or the steepest declines in units against the equal-length period before.",
            new JsonObject
            {
                ["n"] = new JsonObject { ["type"] = "integer", ["description"] = "How many products, 1 to 20." },
                ["start"] = Date("First day of the range."),
                ["end"] = Date("Last day of the range."),
                ["direction"] = Enum("'top' for best sellers by revenue, 'declining' for the biggest unit declines.", "top", "declining"),
            },
            "n", "start", "end"),

        Tool(
            BusinessTools.ListAlerts,
            "List threshold rules that are currently triggered. Omit the dates for the current picture, or supply both to check a specific window.",
            new JsonObject
            {
                ["start"] = Date("Optional first day of the window."),
                ["end"] = Date("Optional last day of the window."),
            }),
    ];

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => 
        new()
        {
            ["name"] = name,
            ["description"] = description,
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray([.. required.Select(r => (JsonNode)r!)]),
            },
        };

    private static JsonObject Date(string description) =>
        new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Enum(string description, params string[] values) =>
        new()
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v!)]),
        };

}