using Microsoft.Data.Sqlite;

namespace MondayBrief.Core.Data;

public static class SqlitePaths
{
    /// <summary>
    /// Makes a realtive "Data Source" absolute against <paramref name="baseDirectory"/> and creates the folder,
    /// so the API, the EF tools and the eval project all hit the same file regardless of working directory.
    /// </summary>
    public static string ResolveConnectionString(string connectionString, string baseDirectory)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var source = builder.DataSource;

        var isMemory = string.IsNullOrWhiteSpace(source)
            || source.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || builder.Mode == SqliteOpenMode.Memory;
        if (isMemory)
        {
            return builder.ToString();
        }

        if (!Path.IsPathRooted(source))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(baseDirectory, source));
        }

        var directory = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return builder.ToString();
    }
}