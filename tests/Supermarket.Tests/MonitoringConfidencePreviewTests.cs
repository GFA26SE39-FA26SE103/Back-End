using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Tests;

public sealed class MonitoringConfidencePreviewTests
{
    [Fact]
    public async Task ZonePreviewUsesSavedDraftConfidenceAndRestartsTrackerWithoutActivation()
    {
        var h = new MonitoringSetupTests.Harness();
        h.Configuration.ConfidenceThreshold = .8123m;
        var client = new RecordingClient();
        var preview = new AiPreview(h.Store, new MonitoringSetupTests.User("ADMIN"), client);
        await preview.Start(h.Camera.CameraId, default, h.Zone.ZoneId);
        Assert.Equal(.8123m, client.Confidence);
        Assert.Equal(new[] { "stop", "start" }, client.Calls);
        Assert.Equal("DRAFT", h.Configuration.Status);
    }
    [Theory]
    [InlineData("unmapped", "ZONE_NOT_MAPPED")]
    [InlineData("cross-floor", "CROSS_FLOOR_MAPPING")]
    [InlineData("roi", "INVALID_POLYGON")]
    [InlineData("no-config", "NOT_FOUND")]
    [InlineData("confidence", "INVALID_CONFIDENCE")]
    [InlineData("operator", "FORBIDDEN")]
    public async Task InvalidZonePreviewNeverRestartsTheSession(string reason, string code)
    {
        var h = new MonitoringSetupTests.Harness();
        switch (reason)
        {
            case "unmapped": h.Store.Values.RemoveAll(e => e is CameraZoneMapping); break;
            case "cross-floor": h.Zone.FloorId = Guid.NewGuid(); break;
            case "roi": h.Store.Values.OfType<CameraZoneMapping>().Single().RoiPolygon = "[]"; break;
            case "no-config": h.Store.Values.Remove(h.Configuration); break;
            case "confidence": h.Configuration.ConfidenceThreshold = 2; break;
        }
        var client = new RecordingClient();
        var preview = new AiPreview(h.Store, new MonitoringSetupTests.User(reason == "operator" ? "OPERATOR" : "ADMIN"), client);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => preview.Start(h.Camera.CameraId, default, h.Zone.ZoneId));
        Assert.Equal(code, error switch { DomainException d => d.Code, AppError a => a.Code, _ => throw new InvalidOperationException("Unexpected failure type.", error) });
        Assert.Empty(client.Calls);
    }
    [Fact]
    public async Task PlainOperatorPreviewStillUsesDefaultClientConfidenceWithoutRestart()
    {
        var h = new MonitoringSetupTests.Harness();
        var client = new RecordingClient();
        await new AiPreview(h.Store, new MonitoringSetupTests.User("OPERATOR"), client).Start(h.Camera.CameraId, default);
        Assert.Null(client.Confidence);
        Assert.Equal(new[] { "start" }, client.Calls);
    }
    private sealed class RecordingClient : IAiPreviewClient
    {
        public List<string> Calls = [];
        public decimal? Confidence;
        private static AiPreviewStatusView View(Guid id) => new(id, "LIVE", DateTime.UtcNow, DateTime.UtcNow, 1, null);
        public Task<AiPreviewStatusView> Start(CameraConnection connection, CancellationToken ct, decimal? confidence = null) { Calls.Add("start"); Confidence = confidence; return Task.FromResult(View(connection.CameraId)); }
        public Task<AiPreviewStatusView> Stop(Guid cameraId, CancellationToken ct) { Calls.Add("stop"); return Task.FromResult(View(cameraId)); }
        public Task<AiPreviewStatusView> Status(Guid cameraId, CancellationToken ct) => Task.FromResult(View(cameraId));
        public Task<PreviewFrame> Frame(Guid cameraId, CancellationToken ct) => throw new NotSupportedException();
    }
}
