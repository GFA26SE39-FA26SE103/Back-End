using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.Ai;

public sealed class RecommendationAiOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class OpenAiRecommendationModel(HttpClient http, IOptions<RecommendationAiOptions> configured) : IRecommendationModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RecommendationChoice> Choose(RecommendationSignal signal, IReadOnlyList<string> actions, CancellationToken ct)
    {
        var options = configured.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.Model))
            throw new AppError("RECOMMENDATION_AI_NOT_CONFIGURED", "The operational recommendation model is not configured.", 503);

        var schema = new
        {
            type = "object", additionalProperties = false,
            properties = new
            {
                action = new { type = "string", @enum = actions },
                confidence = new { type = "string", @enum = new[] { "LOW", "MEDIUM", "HIGH" } }
            },
            required = new[] { "action", "confidence" }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = JsonContent.Create(new
            {
                model = options.Model, store = false, max_output_tokens = 400,
                instructions = "Choose an advisory supermarket operations action from the supplied schema using only the supplied measurement and thresholds. Choose NONE when the input does not support an action. Confidence is a qualitative self-assessment. A human decides whether to act.",
                input = JsonSerializer.Serialize(signal, Json),
                text = new { format = new { type = "json_schema", name = "operational_recommendation", strict = true, schema } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            await response.Content.LoadIntoBufferAsync(64 * 1024, timeout.Token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String || status.GetString() != "completed"
                || !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
                throw new JsonException();

            string? result = null;
            foreach (var item in output.EnumerateArray())
            {
                if (!IsType(item, "message") || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (IsType(part, "refusal")) throw new JsonException();
                    if (!IsType(part, "output_text")) continue;
                    if (result is not null || !part.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
                        throw new JsonException();
                    result = value.GetString();
                }
            }

            using var choiceJson = JsonDocument.Parse(result ?? throw new JsonException());
            var choice = choiceJson.RootElement;
            if (choice.ValueKind != JsonValueKind.Object || choice.EnumerateObject().Count() != 2
                || !choice.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String || !actions.Contains(action.GetString()!)
                || !choice.TryGetProperty("confidence", out var confidence) || confidence.ValueKind != JsonValueKind.String
                || confidence.GetString() is not ("LOW" or "MEDIUM" or "HIGH")) throw new JsonException();
            return new(action.GetString()!, confidence.GetString()!, options.Model);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (IOException) { throw Unavailable(); }
        catch (JsonException) { throw new AppError("RECOMMENDATION_AI_INVALID_RESPONSE", "The recommendation model returned an invalid choice.", 503); }
    }

    private static bool IsType(JsonElement item, string expected) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == expected;

    private static AppError Unavailable() => new("RECOMMENDATION_AI_UNAVAILABLE", "The operational recommendation model is unavailable.", 503);
}
