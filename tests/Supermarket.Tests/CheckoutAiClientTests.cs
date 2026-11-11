using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Ai;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutAiClientTests
{
    private static readonly DateTime Version = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
    [Fact]
    public async Task CheckoutOnlyCameraUsesProtocolTwoAndPreservesBothPolygons()
    {
        var camera = Guid.NewGuid(); var counter = Guid.NewGuid(); var mapping = Guid.NewGuid();
        var handler = new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(2, body.RootElement.GetProperty("protocol_version").GetInt32());
            Assert.Empty(body.RootElement.GetProperty("zones").EnumerateArray());
            var source = Assert.Single(body.RootElement.GetProperty("checkout_mappings").EnumerateArray());
            Assert.Equal(counter, source.GetProperty("counter_id").GetGuid());
            Assert.Equal(mapping, source.GetProperty("mapping_id").GetGuid());
            Assert.Equal(3, source.GetProperty("lane_roi").GetArrayLength());
            Assert.Equal(3, source.GetProperty("staff_roi").GetArrayLength());
            Assert.True(source.GetProperty("is_queue_source").GetBoolean());
            Assert.Equal(.7m, body.RootElement.GetProperty("confidence").GetDecimal());
            return Response(new { camera_id = camera, state = "STARTING", updated_at = Version, frame_sequence = 0 });
        });
        Point[] polygon = [new(0, 0), new(1, 0), new(0, 1)];
        var snapshot = new MonitoringCameraSnapshot(camera, new CameraConnection { CameraId = camera, StreamUri = "http://camera.test/video" }, "f", [],
            CheckoutMappings: [new(mapping, camera, counter, Guid.NewGuid(), "Checkout", Guid.NewGuid(), Version, Version,
                polygon, polygon, true, .7m, [])]);
        await Client(handler).Start(snapshot, Guid.NewGuid(), default);
    }

    [Theory, InlineData(false, 4), InlineData(true, 4), InlineData(false, -1)]
    public async Task SourceMeasurementsRequireValidUniqueMappingIdentities(bool duplicate, int queue)
    {
        var session = Guid.NewGuid();
        var observation = new { mapping_id = Guid.NewGuid(), counter_id = Guid.NewGuid(), config_id = Guid.NewGuid(),
            config_version = Version, mapping_version = Version, person_present = (bool?)null, queue_count = queue, usable = true };
        var handler = new Handler(_ => Task.FromResult(Response(new { protocol_version = 2, session_id = session,
            oldest_sequence = 1, newest_sequence = 1, gap = false, state = "LIVE", batches = new[] {
                new { protocol_version = 2, session_id = session, continuity_id = Guid.NewGuid(), sequence = 1,
                    captured_at = Version, source_elapsed_ms = 0, configuration_fingerprint = "f", zones = Array.Empty<object>(),
                    checkouts = duplicate ? new[] { observation, observation } : new[] { observation } } } })));
        var client = Client(handler);
        if (duplicate || queue < 0)
        {
            var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => client.Read(Guid.NewGuid(), Guid.NewGuid(), null, 0, 16, default));
            Assert.Equal("AI_MEASUREMENT_INVALID", error.Code);
        }
        else
        {
            var page = await client.Read(Guid.NewGuid(), Guid.NewGuid(), null, 0, 16, default);
            var measured = Assert.Single(Assert.Single(page.Batches).Checkouts!);
            Assert.Null(measured.PersonPresent); Assert.Equal(4, measured.QueueCount);
        }
    }

    private static AiMonitoringClient Client(HttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = new("http://ai.test") },
        Options.Create(new AiPreviewOptions()), new Protector());

    [Fact]
    public async Task ResponseSizeLimitRejectsOtherwiseValidOversizedPayload()
    {
        var handler=new Handler(_=>Task.FromResult(Response(new { protocol_version=2,session_id=Guid.NewGuid(),
            oldest_sequence=1,newest_sequence=0,state="LIVE",batches=Array.Empty<object>(),padding=new string('x',3000) })));
        var options=new AiPreviewOptions { MaxMonitoringResponseBytes=1024 };
        var client=new AiMonitoringClient(new HttpClient(handler){BaseAddress=new("http://ai.test")},Options.Create(options),new Protector());
        Assert.Equal("AI_MEASUREMENT_INVALID",(await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(
            ()=>client.Read(Guid.NewGuid(),Guid.NewGuid(),null,0,16,default))).Code);
    }
    private sealed class Protector : ICredentialProtector { public string Protect(string value) => value; public string Unprotect(string value) => value; }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request); }
    private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
