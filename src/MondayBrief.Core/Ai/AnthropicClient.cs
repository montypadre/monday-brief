using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MondayBrief.Core.Options;

namespace MondayBrief.Core.Ai;

public sealed class AnthropicClient(HttpClient http, IOptions<AnthropicOptions> options) : IAnthropicClient
{
    private readonly AnthropicOptions _options = options.Value;

    public async Task<ModelReply> SendAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new AnthropicException("No Anthropic API key configured. Set Anthropic:ApiKey in user secrets.");
        }

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokes,
            ["temperature"] = _options.Temperature,
            ["system"] = request.System,
            ["messages"] = request.Messages.DeepClone(),
            ["tools"] = request.Tools.DeepClone(),
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add("x-api-key", _options.ApiKey);
        message.Headers.Add("anthropic-version", _options.ApiVersion);

        using var response = await http.SendAsync(message, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AnthropicException($"Anthropic API returned {(int)response.StatusCode}: {Truncate(json)}");
        }

        return Parse(json);
    }

    private static ModelReply Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var content = root.GetProperty("content");
        var text = string.Join("\n", content.EnumerateArray()
            .Where(block => block.GetProperty("type").GetString() == "text")
            .Select(block => block.GetProperty("text").GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        var calls = content.EnumerateArray()
            .Where(block => block.GetProperty("type").GetString() == "tool_use")
            .Select(block => new ToolCall(
                block.GetProperty("id").GetString() ?? string.Empty,
                block.GetProperty("name").GetString() ?? string.Empty,
                block.GetProperty("input").Clone()))
            .ToList();

        return new ModelReply(
            text,
            calls,
            root.TryGetProperty("stop_reason", out var stop) ? stop.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("mode", out var model) ? model.GetString() ?? string.Empty : string.Empty,
            JsonNode.Parse(content.GetRawText())!.AsArray());
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "...";
}