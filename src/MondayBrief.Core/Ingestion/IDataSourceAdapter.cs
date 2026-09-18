namespace MondayBrief.Core.Ingestion;

/// <summary>
/// Reads one client system's export and returns it in canonical terms.
/// Adapters never touch the database; the ingestion service owns all writes.
/// Supporting a new client system means writing one new implementation.
/// </summary>
public interface IDataSourceAdapter
{
    /// <summary>Stable source name stored on each order (see Entities.SourceSystems).
    string SourceSystem { get; }

    /// <summary>Human-readable name for logs and the ingestion report.</summary>
    string DisplayName { get; }

    /// <summary>Returns false when this adapter's files are missing from <paramref name="rawDataPath"/>.</summary>
    bool CanRead(string rawDataPath);

    Task<SourceBatch> ReadAsync(string rawDataPath, CancellationToken cancellationToken = default);
}