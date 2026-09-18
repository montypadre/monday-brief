namespace MondayBrief.Evals;

/// <summary>Finds the repo root from the test binaries, so tests can read data/raw.</summary>
public static class RepoPaths
{
    public static string? RawDataPath
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (dir.GetFiles("MondayBrief.sln").Length > 0 || dir.GetFiles("MondayBrief.slnx").Length > 0)
                {
                    var raw = Path.Combine(dir.FullName, "data", "raw");
                    return Directory.Exists(raw) ? raw : null;
                }
            }

            return null;
        }
    }
}