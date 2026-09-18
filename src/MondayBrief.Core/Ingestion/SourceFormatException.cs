namespace MondayBrief.Core.Ingestion;

/// <summary>Thrown when a source file doesn't match the format its adapter expects.</summary>
public sealed class SourceFormatException(string file, int line, string message)
    : Exception($"{Path.GetFileName(file)}, line {line}: {message}")
{
    public string File { get; } = file;

    public int Line { get; } = line;
}