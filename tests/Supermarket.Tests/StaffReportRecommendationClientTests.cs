using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Infrastructure.Ai;
using Xunit;

namespace Supermarket.Tests;

public sealed class StaffReportRecommendationClientTests
{
    private static StaffReportDraftRequest Draft() => new("wet floor", [new("image/jpeg", "/9j/AA==")],
        [new(Guid.NewGuid(), "SPILL_BROKEN_EQUIPMENT", "Spill", null)]);

    [Fact]
    public async Task SendsBoundedPrivateStructuredDraftDirectlyToOpenAi()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("configured-model", root.GetProperty("model").GetString());
            Assert.Equal("json_schema", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
            Assert.Contains("data:image/jpeg;base64,/9j/AA==", root.ToString());
            Assert.Contains("SPILL_BROKEN_EQUIPMENT", root.ToString());
            return Json(new { output = new[] { new { type = "message", content = new[] { new { type = "output_text",
                text = """{"title":"Wet floor","description":"Wet floor","location_description":"Aisle 1","report_category":"POTENTIAL_INCIDENT","incident_type_code":"SPILL_BROKEN_EQUIPMENT","needs_review":false}""" } } } } });
        });
        var client = Create(handler);
        var result = await client.Analyze(Draft(), default);
        Assert.Equal("SPILL_BROKEN_EQUIPMENT", result.IncidentTypeCode);
        Assert.True(result.NeedsReview); // Staff confirmation is never bypassed.
    }

    [Fact]
    public async Task InvalidCatalogCodeIsDiscarded()
    {
        var client = Create(new Handler(_ => Task.FromResult(Json(new { output = new[] { new { type = "message",
            content = new[] { new { type = "output_text", text = """{"title":"Wet floor","description":"","location_description":"","report_category":"POTENTIAL_INCIDENT","incident_type_code":"MADE_UP","needs_review":false}""" } } } } }))));
        var result = await client.Analyze(Draft(), default);
        Assert.Null(result.IncidentTypeCode);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public async Task MissingProviderConfigurationDoesNotSendAnything()
    {
        var client = new OpenAiStaffReportDraftClient(new HttpClient(new Handler(_ => throw new Xunit.Sdk.XunitException("Must not call provider")))
        { BaseAddress = new("https://api.openai.com/v1/") }, Options.Create(new StaffReportAiOptions()));
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => client.Analyze(Draft(), default));
        Assert.Equal("REPORT_AI_NOT_CONFIGURED", error.Code);
    }

    [Fact]
    public async Task ProviderFailureIsSanitizedForMobileFallback()
    {
        var client = Create(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("private provider details") })));
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => client.Analyze(Draft(), default));
        Assert.Equal("REPORT_AI_UNAVAILABLE", error.Code);
        Assert.DoesNotContain("private provider details", error.Message);
    }

    [Fact]
    public async Task MalformedProviderEnvelopeFailsClosed()
    {
        var client = Create(new Handler(_ => Task.FromResult(Json(new { output = new[] { new { type = "message" } } }))));
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => client.Analyze(Draft(), default));
        Assert.Equal("REPORT_AI_INVALID_RESPONSE", error.Code);
    }

    private static OpenAiStaffReportDraftClient Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new("https://api.openai.com/v1/") },
            Options.Create(new StaffReportAiOptions { ApiKey = "test-key", Model = "configured-model" }));

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request);
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
