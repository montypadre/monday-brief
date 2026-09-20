using System.Globalization;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;

using Xunit.Sdk;

namespace MondayBrief.Evals.Live;

/// <summary>
/// Pulls every figure out of an answer and checks each one against the number the tools actually
/// returned. This is what cataches an invented number even when the conclusion happens to be right.
/// </summary>
public static partial class NumberCheck
{
    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}\s*[^\d\s]\s*\d{1,2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpelledDateRange();

    [GeneratedRegex(@"\b\d{1,2}\s+(January|February|March|April|May|June|July|August|September|October|November|December)\b|\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpelledDate();

    [GeneratedRegex(@"\b(last|past|previous)\s+\d+\s+(day|days|week|weeks|month|months)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RelativePeriod();

    [GeneratedRegex(@"-?\$?\d{1,3}(?:,\d{3})+(?:\.\d+)?%?|-?\$?\d+(?:\.\d+)?%?")]
    private static partial Regex Figure();

    /// <summary>Figures a reader would take as data. Dates and years are not claims about the business,
    /// so "24 August" and "last 30 days" are removed before extraction.
    /// </summary>
    public static List<decimal> Extract(string text)
    {
        var cleanedText = IsoDate().Replace(text, " ");
        cleanedText = SpelledDateRange().Replace(cleanedText, " ");
        cleanedText = SpelledDate().Replace(cleanedText, " ");
        cleanedText = RelativePeriod().Replace(cleanedText, " ");

        var numbers = new List<decimal>();

        foreach (Match match in Figure().Matches(cleanedText))
        {
            var cleaned = match.Value.Replace("$", string.Empty).Replace(",", string.Empty).Replace("%", string.Empty);
            if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            // Years read as dates, not figures.
            if (value is >= 1900 and <= 2100 && value == Math.Truncate(value) && !match.Value.Contains('$'))
            {
                continue;
            }

            numbers.Add(value);
        }

        return numbers;
    }

    /// <summary>Every number in a tool payload, at any depth.</summary>
    public static List<decimal> FromPayload(string json)
    {
        var values = new List<decimal>();

        try
        {
            using var document = JsonDocument.Parse(json);
            Walk(document.RootElement, values);
        }
        catch (JsonException)
        {
            // A payload that is not JSON contributes no numbers.
        }

        return values;
    }

    private static void Walk(JsonElement element, List<decimal> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDecimal(out var number):
                values.Add(number);
                break;
            case JsonValueKind.String when element.GetString() is { } text:
                values.AddRange(Extract(text));
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, values);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, values);
                }

                break;
        }
    }

    /// <summary>
    /// Figures in the answer that no tool returned. Comparison ignores sign ("down 48%" for -48) and
    /// matches at the precision the answer used, so 15560.5 covers "$15,560.50".
    /// </summary>
    public static List<decimal> Unsupported(string answer, IEnumerable<decimal> allowed)
    {
        var supported = allowed.Select(Math.Abs).ToList();

        return Extract(answer)
            .Where(number => !supported.Any(value => Matches(Math.Abs(number), value)))
            .ToList();
    }

    private static bool Matches(decimal claimed, decimal actual)
    {
        if (claimed == actual)
        {
            return true;
        }

        var decimals = Scale(claimed);
        return Math.Round(actual, decimals, MidpointRounding.AwayFromZero) == claimed;
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;
}