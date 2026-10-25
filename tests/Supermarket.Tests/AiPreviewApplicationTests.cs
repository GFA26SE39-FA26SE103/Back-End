using System.Linq.Expressions;
using System.Text.Json;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;

namespace Supermarket.Tests;

public sealed class AiPreviewApplicationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task StartRejectsInactiveCamera()
    {
        var harness = Harness.Ready();
        harness.Camera.Status = "INACTIVE";

        var error = await Assert.ThrowsAsync<AppError>(() => harness.UseCase.Start(harness.Camera.CameraId, Ct));

        Assert.Equal("CAMERA_NOT_ACTIVE", error.Code);
        Assert.Empty(harness.Client.StartedConnections);
    }

    [Fact]
    public async Task StartRejectsUntestedConnection()
    {
        var harness = Harness.Ready();
        harness.Connection.LastTestedAt = null;
        harness.Connection.LastTestResult = null;

        var error = await Assert.ThrowsAsync<AppError>(() => harness.UseCase.Start(harness.Camera.CameraId, Ct));

        Assert.Equal("CONNECTION_NOT_READY", error.Code);
    }

    [Fact]
    public async Task StartRejectsDisabledConnection()
    {
        var harness = Harness.Ready();
        harness.Connection.IsEnabled = false;

        var error = await Assert.ThrowsAsync<AppError>(() => harness.UseCase.Start(harness.Camera.CameraId, Ct));

        Assert.Equal("CONNECTION_DISABLED", error.Code);
    }

    [Theory]
    [InlineData("DEMO", "HTTP")]
    [InlineData("LIVE", "WEBRTC")]
    public async Task StartRejectsSourcesThatNativePreviewCannotRead(string sourceType, string protocol)
    {
        var harness = Harness.Ready();
        harness.Connection.SourceType = sourceType;
        harness.Connection.Protocol = protocol;

        var error = await Assert.ThrowsAsync<AppError>(() => harness.UseCase.Start(harness.Camera.CameraId, Ct));

        Assert.Equal("AI_SOURCE_UNSUPPORTED", error.Code);
        Assert.Empty(harness.Client.StartedConnections);
    }

    [Fact]
    public async Task StartUsesCurrentConnectionAndDoesNotExposeSecrets()
    {
        var harness = Harness.Ready();

        var result = await harness.UseCase.Start(harness.Camera.CameraId, Ct);
        var json = JsonSerializer.Serialize(result);

        Assert.Same(harness.Connection, Assert.Single(harness.Client.StartedConnections));
        Assert.DoesNotContain(harness.Connection.StreamUri, json);
        Assert.DoesNotContain(harness.Connection.CredentialSecretRef!, json);
        Assert.Equal("LIVE", result.State);
    }

    [Fact]
    public async Task StartAcceptsTestedEnabledRecordedVideo()
    {
        var harness = Harness.Ready();
        harness.Connection.SourceType = "RECORDED";
        harness.Connection.Protocol = "FILE";
        harness.Connection.StreamUri = "file:///C:/videos/sample.mp4";

        await harness.UseCase.Start(harness.Camera.CameraId, Ct);

        Assert.Same(harness.Connection, Assert.Single(harness.Client.StartedConnections));
    }

    [Fact]
    public async Task TwoStartsReturnDistinctLeasesAndReleasingOneKeepsTheOtherUsable()
    {
        var harness = Harness.Ready();

        var first = await harness.UseCase.Start(harness.Camera.CameraId, Ct);
        var second = await harness.UseCase.Start(harness.Camera.CameraId, Ct);
        var released = await harness.UseCase.Stop(harness.Camera.CameraId, first.ViewerLeaseId!.Value, Ct);
        var remaining = await harness.UseCase.Status(harness.Camera.CameraId, second.ViewerLeaseId!.Value, Ct);

        Assert.NotEqual(first.ViewerLeaseId, second.ViewerLeaseId);
        Assert.Equal("LIVE", released.State);
        Assert.Equal("LIVE", remaining.State);
        Assert.Equal([first.ViewerLeaseId], harness.Client.ReleasedLeases);
        Assert.Equal(0, harness.Client.ForceStops);
    }

    [Fact]
    public async Task UnknownViewerLeaseIsPropagatedWithoutSubstitution()
    {
        var harness = Harness.Ready();
        await harness.UseCase.Start(harness.Camera.CameraId, Ct);
        var unknownLease = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<AppError>(
            () => harness.UseCase.Status(harness.Camera.CameraId, unknownLease, Ct));

        Assert.Equal("AI_VIEWER_LEASE_EXPIRED", error.Code);
        Assert.Equal(unknownLease, harness.Client.LastLeaseRead);
    }

    [Fact]
    public async Task NonAdminIsRejected()
    {
        var harness = Harness.Ready(role: "STAFF");

        var error = await Assert.ThrowsAsync<AppError>(() => harness.UseCase.Start(harness.Camera.CameraId, Ct));

        Assert.Equal("FORBIDDEN", error.Code);
        Assert.Empty(harness.Client.StartedConnections);
    }

    [Fact]
    public async Task ChangedConnectionIsUsedAfterStopAndRestart()
    {
        var harness = Harness.Ready();
        var first = await harness.UseCase.Start(harness.Camera.CameraId, Ct);
        await harness.UseCase.Stop(harness.Camera.CameraId, first.ViewerLeaseId!.Value, Ct);
        var replacement = Harness.ConnectionFor(harness.Camera.CameraId);
        replacement.StreamUri = "http://camera-new.local:8080/video";
        harness.Store.Connection = replacement;

        await harness.UseCase.Start(harness.Camera.CameraId, Ct);

        Assert.Equal(2, harness.Client.StartedConnections.Count);
        Assert.Same(replacement, harness.Client.StartedConnections[1]);
    }

    private sealed class Harness
    {
        private Harness(TestStore store, Camera camera, CameraConnection connection, RecordingAiPreviewClient client, AiPreview useCase)
        {
            Store = store;
            Camera = camera;
            Connection = connection;
            Client = client;
            UseCase = useCase;
        }

        public TestStore Store { get; }
        public Camera Camera { get; }
        public CameraConnection Connection { get; }
        public RecordingAiPreviewClient Client { get; }
        public AiPreview UseCase { get; }

        public static Harness Ready(string role = "ADMIN")
        {
            var camera = new Camera { CameraId = Guid.NewGuid(), Status = "ACTIVE" };
            var connection = ConnectionFor(camera.CameraId);
            var store = new TestStore(camera, connection);
            var client = new RecordingAiPreviewClient();
            return new Harness(store, camera, connection, client, new AiPreview(store, new TestUser(role), client));
        }

        public static CameraConnection ConnectionFor(Guid cameraId) => new()
        {
            ConnectionId = Guid.NewGuid(),
            CameraId = cameraId,
            SourceType = "LIVE",
            Protocol = "HTTP",
            StreamUri = "http://camera.local:8080/video",
            Username = "viewer",
            CredentialSecretRef = "protected-camera-password",
            IsEnabled = true,
            LastTestedAt = DateTime.UtcNow,
            LastTestResult = "SUCCESS"
        };
    }

    private sealed class TestUser(string role) : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role { get; } = role;
    }

    private sealed class TestStore(Camera camera, CameraConnection connection) : ISetupStore
    {
        public CameraConnection Connection { get; set; } = connection;

        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
            => Task.FromResult(typeof(T) == typeof(Camera) && camera.CameraId == id ? (T?)(Entity)camera : null);

        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new()
        {
            IEnumerable<T> values = typeof(T) == typeof(CameraConnection) ? [(T)(Entity)Connection] : [];
            return Task.FromResult(filter is null ? values.ToList() : values.AsQueryable().Where(filter).ToList());
        }

        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default) => work();
    }

    private sealed class RecordingAiPreviewClient : IAiPreviewClient
    {
        private readonly Dictionary<Guid, AiPreviewStatusView> states = [];
        private readonly HashSet<Guid> leases = [];
        public List<CameraConnection> StartedConnections { get; } = [];
        public List<Guid?> ReleasedLeases { get; } = [];
        public Guid? LastLeaseRead { get; private set; }
        public int ForceStops { get; private set; }

        public Task<AiPreviewStatusView> Start(CameraConnection connection, CancellationToken ct, decimal? confidence = null)
        {
            if (!states.TryGetValue(connection.CameraId, out var status) || status.State == "STOPPED")
            {
                StartedConnections.Add(connection);
            }
            var lease = Guid.NewGuid();
            leases.Add(lease);
            status = View(connection.CameraId, "LIVE") with { ViewerLeaseId = lease };
            states[connection.CameraId] = status;
            return Task.FromResult(status);
        }

        public Task<AiPreviewStatusView> Status(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
        {
            LastLeaseRead = viewerLeaseId;
            if (!leases.Contains(viewerLeaseId))
                throw new AppError("AI_VIEWER_LEASE_EXPIRED", "AI viewer lease is missing or expired.", 409);
            return Task.FromResult(states[cameraId] with { ViewerLeaseId = viewerLeaseId });
        }

        public Task<AiPreviewStatusView> ReleaseViewer(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
        {
            ReleasedLeases.Add(viewerLeaseId);
            leases.Remove(viewerLeaseId);
            var state = leases.Count == 0 ? "STOPPED" : "LIVE";
            var status = View(cameraId, state);
            states[cameraId] = status;
            return Task.FromResult(status);
        }

        public Task<AiPreviewStatusView> ForceStop(Guid cameraId, CancellationToken ct)
        {
            ForceStops++;
            leases.Clear();
            var status = View(cameraId, "STOPPED");
            states[cameraId] = status;
            return Task.FromResult(status);
        }

        public Task<AiPreviewStatusView> Status(Guid cameraId, CancellationToken ct)
            => Task.FromResult(states.GetValueOrDefault(cameraId, View(cameraId, "STOPPED")));

        public Task<PreviewFrame> Frame(Guid cameraId, CancellationToken ct)
            => Task.FromResult(new PreviewFrame([0xFF, 0xD8, 0xFF, 0xD9], "image/jpeg"));

        public Task<SequencedPreviewFrame?> NextFrame(Guid cameraId, long afterSequence, Guid? afterSessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<AiPreviewStatusView> Stop(Guid cameraId, CancellationToken ct)
        {
            if (states.TryGetValue(cameraId, out var existing) && existing.State == "STOPPED")
                return Task.FromResult(existing);
            var status = View(cameraId, "STOPPED");
            states[cameraId] = status;
            return Task.FromResult(status);
        }

        private static AiPreviewStatusView View(Guid cameraId, string state)
            => new(cameraId, state, DateTime.UtcNow, DateTime.UtcNow, state == "LIVE" ? 1 : 0, null);
    }
}
