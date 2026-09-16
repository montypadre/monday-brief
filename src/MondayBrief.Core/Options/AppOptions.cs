namespace MondayBrief.Core.Options;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>User-facing product name. The code name stays "MondayBrief"; renaming is a config change.</summary>
    public string DisplayName { get; set; } = "Monday Brief";

    /// <summary>The demo's fixed "today". All relative ranges are computed from this.</summary>
    public DateOnly AsOfDate { get; set; } = new(2026, 8, 31);

    /// <summary>IANA zone used to turn UTC timestamps into business dates.</summary>
    public string TimeZoneId { get; set; } = "America/Chicago";

    /// <summary>Folder holding the raw source files. Relative paths resolve from the content root.</summary>
    public string RawDataPath { get; set; } = "../../data/raw";
}