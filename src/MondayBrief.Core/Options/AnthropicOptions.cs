namespace MondayBrief.Core.Options;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    public string Model { get; set; } = "claude-sonnet-5";

    public string ApiVersion { get; set; } = "2023-06-01";

    public int MaxTokes { get; set; } = 1024;

    /// <summary>Zero, so the same question gives the same answer in evals and in the demo.</summary>
    public double Temperature { get; set; }

    /// <summary>One round is a model class plus its tool executions.</summary>
    public int MaxToolRounds { get; set; } = 4;
}