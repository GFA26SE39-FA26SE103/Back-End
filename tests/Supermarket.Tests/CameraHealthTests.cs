using System.Linq.Expressions;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class CameraHealthTests
{
    [Fact]
    public async Task VisualIssueUsesHysteresisAndDoesNotChangeConnectionStatus()
    {
        var h = Harness.Create(
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlocked]),
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlocked]),
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlocked]));

        var first = await h.Health.CheckInternal(h.Camera.CameraId, default);
        var second = await h.Health.CheckInternal(h.Camera.CameraId, default);

        Assert.Empty(h.Store.Values.OfType<CameraHealthEvent>());
        var third = await h.Health.CheckInternal(h.Camera.CameraId, default);

        Assert.Equal("ONLINE", third.ConnectionStatus);
        Assert.Equal("NOT_READY", third.MonitoringReadiness);
        Assert.Single(h.Store.Values.OfType<CameraHealthEvent>(), e => e.EventType == CameraHealthEventTypes.ViewBlocked && e.Status == "OPEN");
        Assert.DoesNotContain(CameraHealthEventTypes.ViewBlocked, first.ActiveHealthIssues);
        Assert.DoesNotContain(CameraHealthEventTypes.ViewBlocked, second.ActiveHealthIssues);
        Assert.Contains(CameraHealthEventTypes.ViewBlocked, third.ActiveHealthIssues);
    }

    [Fact]
    public async Task RecoveryResolvesOnlyMatchingEventAfterGoodObservationThreshold()
    {
        var h = Harness.Create(
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlurred, CameraHealthEventTypes.ViewBlocked]),
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlurred, CameraHealthEventTypes.ViewBlocked]),
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlurred, CameraHealthEventTypes.ViewBlocked]),
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlocked]),
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlocked]));
        h.Store.Values.Add(new CameraHealthEvent
        {
            HealthEventId = Guid.NewGuid(), CameraId = h.Camera.CameraId,
            EventType = CameraHealthEventTypes.ViewBlocked, Status = "OPEN", DetectedAt = h.Clock.UtcNow
        });

        await h.Health.CheckInternal(h.Camera.CameraId, default);
        await h.Health.CheckInternal(h.Camera.CameraId, default);
        await h.Health.CheckInternal(h.Camera.CameraId, default);
        await h.Health.CheckInternal(h.Camera.CameraId, default);
        var recovered = await h.Health.CheckInternal(h.Camera.CameraId, default);

        Assert.DoesNotContain(CameraHealthEventTypes.ViewBlurred, recovered.ActiveHealthIssues);
        Assert.Equal("RESOLVED", h.Store.Values.OfType<CameraHealthEvent>().Single(e => e.EventType == CameraHealthEventTypes.ViewBlurred).Status);
        Assert.Equal("OPEN", h.Store.Values.OfType<CameraHealthEvent>().Single(e => e.EventType == CameraHealthEventTypes.ViewBlocked).Status);
    }

    [Fact]
    public async Task ProcessingOutageKeepsCameraOnlineWithoutCreatingCameraFailure()
    {
        var h = Harness.Create(new ProbeResult(true, "FRAME_RECEIVED", false, []));

        var result = await h.Health.CheckInternal(h.Camera.CameraId, default);

        Assert.Equal("ONLINE", result.ConnectionStatus);
        Assert.Equal("UNAVAILABLE", result.ProcessingAvailability);
        Assert.Equal("NOT_READY", result.MonitoringReadiness);
        Assert.Empty(h.Store.Values.OfType<CameraHealthEvent>());
    }

    [Fact]
    public async Task StreamRecoveryDoesNotResolveIndependentVisualIssue()
    {
        var h = Harness.Create(
            new(false, "STREAM_UNAVAILABLE", null, []),
            new(true, "FRAME_RECEIVED", true, [CameraHealthEventTypes.ViewBlocked]));
        h.Store.Values.Add(new CameraHealthEvent
        {
            HealthEventId = Guid.NewGuid(), CameraId = h.Camera.CameraId,
            EventType = CameraHealthEventTypes.ViewBlocked, Status = "OPEN", DetectedAt = h.Clock.UtcNow
        });

        await h.Health.CheckInternal(h.Camera.CameraId, default);
        var result = await h.Health.CheckInternal(h.Camera.CameraId, default);

        Assert.Equal("ONLINE", result.ConnectionStatus);
        Assert.Equal("RESOLVED", h.Store.Values.OfType<CameraHealthEvent>().Single(e => e.EventType == CameraHealthEventTypes.StreamUnavailable).Status);
        Assert.Equal("OPEN", h.Store.Values.OfType<CameraHealthEvent>().Single(e => e.EventType == CameraHealthEventTypes.ViewBlocked).Status);
    }

    private sealed class Harness
    {
        private Harness(MemoryStore store, Camera camera, TestClock clock, CameraHealth health)
        {
            Store = store; Camera = camera; Clock = clock; Health = health;
        }
        public MemoryStore Store { get; }
        public Camera Camera { get; }
        public TestClock Clock { get; }
        public CameraHealth Health { get; }

        public static Harness Create(params ProbeResult[] probes)
        {
            var store = new MemoryStore();
            var camera = new Camera { CameraId = Guid.NewGuid(), FloorId = Guid.NewGuid(), Code = "CAM-01", Status = "ACTIVE" };
            store.Values.Add(camera);
            store.Values.Add(new CameraConnection
            {
                ConnectionId = Guid.NewGuid(), CameraId = camera.CameraId, SourceType = "LIVE", Protocol = "HTTP",
                StreamUri = "http://camera.example/video", IsEnabled = true, LastTestResult = "SUCCESS", LastTestedAt = DateTime.UtcNow
            });
            var clock = new TestClock();
            var state = new CameraHealthRuntimeState(badObservationsToOpen: 3, goodObservationsToResolve: 2);
            return new(store, camera, clock, new CameraHealth(store, new AdminUser(), new QueueStream(probes), clock, state));
        }
    }

    private sealed class QueueStream(IEnumerable<ProbeResult> probes) : ICameraStream
    {
        private readonly Queue<ProbeResult> results = new(probes);
        public Task<ProbeResult> Test(CameraConnection connection, CancellationToken ct) => Task.FromResult(results.Dequeue());
        public Task<PreviewFrame> Preview(CameraConnection connection, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class AdminUser : ICurrentUser { public Guid UserId { get; } = Guid.NewGuid(); public string Role => "ADMIN"; }
    internal sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; private set; } = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    }
    internal sealed class MemoryStore : ISetupStore
    {
        public List<Entity> Values { get; } = [];
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
            => Task.FromResult(Values.OfType<T>().FirstOrDefault(e => (Guid)e.GetType().GetProperties().First(p => p.Name.EndsWith("Id")).GetValue(e)! == id));
        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new()
            => Task.FromResult(Values.OfType<T>().Where(filter?.Compile() ?? (_ => true)).ToList());
        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() { Values.Add(entity); return Task.CompletedTask; }
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Task.CompletedTask;
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() { Values.Remove(entity); return Task.CompletedTask; }
        public Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default) => work();
    }
}
