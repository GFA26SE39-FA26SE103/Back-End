using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Infrastructure.Ai;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

public sealed class RecommendationModelTests
{
    private static readonly RecommendationSignal Signal = new("LONG_QUEUE", "WARNING", 7, "PEOPLE", 5, 10, 30, "HIGH");

    [Theory]
    [InlineData("completed", "{\"action\":\"MONITOR_CONDITION\",\"confidence\":\"HIGH\"}", true)]
    [InlineData("incomplete", "{\"action\":\"MONITOR_CONDITION\",\"confidence\":\"HIGH\"}", false)]
    [InlineData("completed", "{\"action\":\"CREATE_TASK\",\"confidence\":\"HIGH\"}", false)]
    [InlineData("completed", "{\"action\":\"NONE\",\"confidence\":\"HIGH\",\"extra\":1}", false)]
    public async Task StructuredResponseMustBeCompleteAndWithinCatalog(string status, string text, bool valid)
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("/v1/responses", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            using var input = JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
            Assert.Equal(7, input.RootElement.GetProperty("metric").GetInt32());
            Assert.False(input.RootElement.TryGetProperty("eventId", out _));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status, output = new[]
            { new { type = "message", content = new[] { new { type = "output_text", text } } } } }) };
        })) { BaseAddress = new Uri("https://unit-test.invalid/v1/") };
        var model = new OpenAiRecommendationModel(http, Options.Create(new RecommendationAiOptions { ApiKey = "test-key", Model = "test-model" }));
        if (valid)
            Assert.Equal(new RecommendationChoice("MONITOR_CONDITION", "HIGH", "test-model"), await model.Choose(Signal, ["MONITOR_CONDITION", "NONE"], default));
        else
            Assert.Equal("RECOMMENDATION_AI_INVALID_RESPONSE", (await Assert.ThrowsAsync<AppError>(() => model.Choose(Signal, ["MONITOR_CONDITION", "NONE"], default))).Code);
    }

    [Fact]
    public async Task HistoricalDraftUsesTheConfiguredModelAndSendsAggregatesWithoutIdentifiers()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("/v1/responses", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.Equal("test-model", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.True(root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            using var input = JsonDocument.Parse(root.GetProperty("input").GetString()!);
            var signal = input.RootElement[0];
            Assert.Equal(19, signal.GetProperty("localHour").GetInt32());
            Assert.Equal(3, signal.GetProperty("daysWithEvents").GetInt32());
            Assert.False(signal.TryGetProperty("zoneId", out _));
            Assert.False(signal.TryGetProperty("zoneName", out _));
            Assert.False(signal.TryGetProperty("eventId", out _));
            const string text = """{"recommendations":[{"candidateIndex":0,"title":"Compare coverage","description":"Review staffing at the recurring hour."}]}""";
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "completed", output = new[]
                { new { type = "message", content = new[] { new { type = "output_text", text } } } } }) };
        })) { BaseAddress = new Uri("https://unit-test.invalid/v1/") };
        var model = new OpenAiRecommendationModel(http, Options.Create(new RecommendationAiOptions { ApiKey = "test-key", Model = "test-model" }));
        var result = await model.DraftHistorical([new(0, "LONG_QUEUE", "REVIEW_HISTORICAL_CHECKOUT_COVERAGE", 19, 5, 3, 7)], default);

        Assert.Equal("test-model", result.Model);
        Assert.Equal(new HistoricalRecommendationDraft(0, "Compare coverage", "Review staffing at the recurring hour."),
            Assert.Single(result.Recommendations));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
