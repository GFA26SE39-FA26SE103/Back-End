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
    private static readonly Guid ZoneId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static StaffReportDraftRequest Draft() => new("wet floor in Frozen", [new("image/jpeg", "/9j/AA==")],
        [new(Guid.NewGuid(), "SPILL_BROKEN_EQUIPMENT", "Spill", null) { AllowedTaskTypeCodes = ["TT-06"] }])
    {
        TaskTypes = [new(Guid.NewGuid(), "TT-06", "Clean spill", "Spills")],
        ZoneCandidates = [new(ZoneId, Guid.NewGuid(), "Floor 1", "FROZEN", "Frozen")]
    };

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
                text = $$"""{"title":"Wet floor","description":"Wet floor","location_description":"Frozen","report_category":"POTENTIAL_INCIDENT","incident_type_code":"SPILL_BROKEN_EQUIPMENT","needs_review":false,"suggested_task_type_codes":["TT-06"],"suggested_zone_id":"{{ZoneId}}","confidence_level":"MEDIUM","evidence":"Staff note mentions Frozen"}""" } } } } });
        });
        var client = Create(handler);
        var result = await client.Analyze(Draft(), default);
        Assert.Equal("SPILL_BROKEN_EQUIPMENT", result.IncidentTypeCode);
        Assert.Equal(["TT-06"], result.SuggestedTaskTypeCodes);
        Assert.Equal(ZoneId, result.SuggestedZoneId);
        Assert.Equal("MEDIUM", result.ConfidenceLevel);
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
    public async Task UnapprovedTaskAndZoneSuggestionsAreDiscarded()
    {
        var client = Create(new Handler(_ => Task.FromResult(Json(new { output = new[] { new { type = "message",
            content = new[] { new { type = "output_text", text = """{"title":"Wet floor","description":"","location_description":"","report_category":"POTENTIAL_INCIDENT","incident_type_code":"SPILL_BROKEN_EQUIPMENT","needs_review":false,"suggested_task_type_codes":["TT-99"],"suggested_zone_id":"00000000-0000-4000-8000-000000000099","confidence_level":"HIGH","evidence":"guess"}""" } } } } }))));
        var result = await client.Analyze(Draft(), default);
        Assert.Empty(result.SuggestedTaskTypeCodes);
        Assert.Null(result.SuggestedZoneId);
    }

    [Fact]
    public async Task VoiceIsTranscribedBeforeDraftingAndTranscriptIsReturned()
    {
        var calls = new List<string>();
        var handler = new Handler(async request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/v1/audio/transcriptions")
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                var multipart = await request.Content!.ReadAsStringAsync();
                Assert.Contains("configured-transcribe-model", multipart);
                return Json(new { text = "Wet floor in Frozen" });
            }
            var payload = await request.Content!.ReadAsStringAsync();
            Assert.Contains("Wet floor in Frozen", payload);
            return Json(new { output = new[] { new { type = "message", content = new[] { new { type = "output_text",
                text = """{"title":"Wet floor","description":"","location_description":"","report_category":"POTENTIAL_INCIDENT","incident_type_code":"SPILL_BROKEN_EQUIPMENT","needs_review":true}""" } } } } });
        });
        var client = new OpenAiStaffReportDraftClient(new HttpClient(handler) { BaseAddress = new("https://api.openai.com/v1/") },
            Options.Create(new StaffReportAiOptions { ApiKey = "test-key", Model = "configured-model", TranscriptionModel = "configured-transcribe-model" }));
        var draft = Draft() with { Voice = new StaffReportDraftAudio([0, 0, 0, 0, 0x66, 0x74, 0x79, 0x70, 0, 0, 0, 0]) };
        var result = await client.Analyze(draft, default);
        Assert.Equal("Wet floor in Frozen", result.Transcript);
        Assert.Equal(["/v1/audio/transcriptions", "/v1/responses"], calls);
    }

    [Fact]
    public async Task PretranscribedVoiceDoesNotSendAudioTwiceAndKeepsZoneCandidate()
    {
        var calls = new List<string>();
        var client = Create(new Handler(async request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            var payload = await request.Content!.ReadAsStringAsync();
            Assert.Contains("Frozen", payload);
            return Json(new { output = new[] { new { type = "message", content = new[] { new { type = "output_text",
                text = $$"""{"title":"Wet floor","description":"","location_description":"Frozen","report_category":"POTENTIAL_INCIDENT","incident_type_code":"SPILL_BROKEN_EQUIPMENT","needs_review":true,"suggested_zone_id":"{{ZoneId}}"}""" } } } } });
        }));
        var result = await client.Analyze(Draft() with { Note = "", Transcript = "Wet floor in Frozen" }, default);
        Assert.Equal(["/v1/responses"], calls);
        Assert.Equal("Wet floor in Frozen", result.Transcript);
        Assert.Equal(ZoneId, result.SuggestedZoneId);
    }

    [Fact]
    public async Task MissingProviderConfigurationDoesNotSendAnything()
    {
        var client = new OpenAiStaffReportDraftClient(new HttpClient(new Handler(_ => throw new Xunit.Sdk.XunitException("Must not call provider")))
        { BaseAddress = new("https://api.openai.com/v1/") }, Options.Create(new StaffReportAiOptions { ApiKey = "test-key", TranscriptionModel = "configured-transcribe-model" }));
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => client.Analyze(Draft(), default));
        Assert.Equal("REPORT_AI_NOT_CONFIGURED", error.Code);
    }

    [Fact]
    public async Task MissingVoiceProviderConfigurationDoesNotSendAudio()
    {
        var client = new OpenAiStaffReportDraftClient(new HttpClient(new Handler(_ => throw new Xunit.Sdk.XunitException("Must not call provider")))
        { BaseAddress = new("https://api.openai.com/v1/") }, Options.Create(new StaffReportAiOptions { ApiKey = "test-key", TranscriptionModel = "configured-transcribe-model" }));
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() =>
            client.Transcribe(new StaffReportDraftAudio([0, 0, 0, 0, 0x66, 0x74, 0x79, 0x70]), default));
        Assert.Equal("REPORT_AI_NOT_CONFIGURED", error.Code);
    }

    [Fact]
    public async Task UnsupportedVoiceBytesAreRejectedBeforeProviderCall()
    {
        var client = new OpenAiStaffReportDraftClient(new HttpClient(new Handler(_ => throw new Xunit.Sdk.XunitException("Must not call provider")))
        { BaseAddress = new("https://api.openai.com/v1/") }, Options.Create(new StaffReportAiOptions
        { ApiKey = "test-key", Model = "configured-model", TranscriptionModel = "configured-transcribe-model" }));
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() =>
            client.Transcribe(new StaffReportDraftAudio([1, 2, 3, 4, 5, 6, 7, 8]), default));
        Assert.Equal("REPORT_VOICE_FORMAT", error.Code);
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
