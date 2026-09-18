using System.Globalization;
using System.Runtime.CompilerServices;

namespace MondayBrief.Core.Ingestion;

/// <summary>
/// Streams a delimited file row by row, mapping the first non-comment line as the header.
/// Streaming matters: the POS export is ~50,000 lines and is never held in memory all at once.
/// </summary>
public sealed class CsvFile(string path, char? commentPrefix = '#')
{
    public string Path { get; } = path;

    public async IAsyncEnumerable<CsvRow> ReadRowsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(Path);
        Dictionary<string, int>? columns = null;
        var lineNumber = 0;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0 || (commentPrefix is { } prefix && line[0] == prefix))
            {
                continue;
            }

            var fields = Split(line, lineNumber);

            if (columns is null)
            {
                columns = BuildColumnMap(fields, lineNumber);
                continue;
            }

            yield return new CsvRow(Path, lineNumber, columns, fields);
        }

        if (columns is null)
        {
            throw new SourceFormatException(Path, lineNumber, "No header row found.");
        }
    }

    private string[] Split(string line, int lineNumber)
    {
        try
        {
            return CsvParser.SplitLine(line);
        }
        catch (FormatException ex)
        {
            throw new SourceFormatException(Path, lineNumber, ex.Message);
        }
    }

    private Dictionary<string, int> BuildColumnMap(string[] header, int lineNumber)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (!map.TryAdd(name, i))
            {
                throw new SourceFormatException(Path, lineNumber, $"Duplicate column '{name}'.");
            }
        }

        return map;
    }
}

/// <summary>One data row, with typed accessors that fail loudly and point at the offending line.</summary>
public sealed class CsvRow(string file, int lineNumber, IReadOnlyDictionary<string, int> columns, string[] fields)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public int LineNumber => lineNumber;

    public string Text(string column)
    {
        if (!columns.TryGetValue(column, out var index))
        {
            throw Error($"Missing column '{column}'.");
        }

        if (index >= fields.Length)
        {
            throw Error($"Row has {fields.Length} fields; column '{column}' needs at least {index + 1}.");
        }

        return fields[index].Trim();
    }

    public int Int(string column) =>
        int.TryParse(Text(column), NumberStyles.Integer, Inv, out var value)
            ? value
            : throw Error($"Column '{column}' is not a whole number: '{Text(column)}'.");

    /// <summary>Parses a dollar amount ("24.00", "1,240.50") into integer cents.</summary>
    public long Cents(string column)
    {
        var raw = Text(column).Replace("$", string.Empty);
        return decimal.TryParse(raw, NumberStyles.Number, Inv, out var dollars)
            ? (long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero)
            : throw Error($"Column '{column}' is not an amount: '{Text(column)}'.");
    }

    /// <summary>Parses a timestamp in exactly the given format. No Kind is assumed; the adapter decides.</summary>
    public DateTime Timestamp(string column, string format) =>
        DateTime.TryParseExact(Text(column), format, Inv, DateTimeStyles.None, out var value)
            ? value
            : throw Error($"Column '{column}' is not a '{format}' timestamp: '{Text(column)}'.");
    
    public DateOnly Date(string column, string format) => 
        DateOnly.TryParseExact(Text(column), format, Inv, DateTimeStyles.None, out var value)
            ? value
            : throw Error($"Column '{column}' is not a '{format}' date: '{Text(column)}'.");

    public SourceFormatException Error(string message) => new(file, lineNumber, message);
}