using System.Text.Json;
using System.Text.Json.Nodes;

namespace MondayBrief.Core.Ai;

/// <summary>A tool the model asked to run.</summary>
public sealed record ToolCall(string Id, string Name, JsonElement Input);

/// <summary>One model response: its text, any tool calls, and the raw content to append to the conversation.</summary>
public sealed record ModelReply(
    string Text,
    IReadOnlyList<ToolCall> ToolCalls,
    string StopReason,
    string Model,
    JsonArray Content);

public sealed record ModelRequest(string System, JsonArray Messages, JsonArray Tools);

public sealed class AnthropicException(string message) : Exception(message);

public interface IAnthropicClient
{
    Task<ModelReply> SendAsync(ModelRequest request, CancellationToken cancellationToken = default);
}
