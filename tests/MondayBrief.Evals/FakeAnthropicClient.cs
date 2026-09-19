using System.Text.Json;
using System.Text.Json.Nodes;
using MondayBrief.Core.Ai;

namespace MondayBrief.Evals;

/// <summary>Replays scripted replies so the tool-use loop can be tested without a network call.</summary>
public sealed class FakeAnthropicClient(params ModelReply[] replies) : IAnthropicClient
{
    private int _index;

    public List<ModelRequest> Requests { get; } = [];

    public Task<ModelReply> SendAsync(ModelRequest request, CancellationToken cancellationToken=default)
    {
        Requests.Add(request);
        var reply = replies[Math.Min(_index, replies.Length - 1)];
        _index++;
        return Task.FromResult(reply);
    }

    public static ModelReply Text(string text) =>
        new(text, [], "end_turn", "fake-model", [new JsonObject { ["type"] = "text", ["text"] = text }]);

    public static ModelReply Calls(string tool, object arugments)
    {
        var input = JsonSerializer.SerializeToElement(arugments);
        var block = new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = "toolu_test",
            ["name"] = tool,
            ["input"] = JsonNode.Parse(input.GetRawText()),
        };

        return new ModelReply(string.Empty, [new ToolCall("toolu_test", tool, input)], "tool_use", "fake-model", [block]);
    }
}