using System.Text;

namespace MondayBrief.Core.Ingestion;

/// <summary>
/// Minimal RFC 4180 field splitter: handles quoted fields, embedded commas and doubled quotes ("").
/// One record per line - a quoted field containing a newline is not supported, and is reported as an
/// unterminated field rather than siltently mis-parsed.
/// </summary>
public static class CsvParser
{
    public static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
        
            if (inQuotes)
            {
                if (c != '"')
                {
                    value.Append(c);
                }
                else if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (c == '"' && value.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(c);
            }
        }

        if (inQuotes)
        {
            throw new FormatException("Unterminated quoted field.");
        }

        fields.Add(value.ToString());
        return [.. fields];
    }
}