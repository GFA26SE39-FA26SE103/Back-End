using System.Linq.Expressions;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;

namespace Supermarket.Tests;

public sealed class CameraPlaybackTests
{
    [Fact]
    public async Task PrepareReturnsGatewayEndpointWithoutExposingSource()
    {
        var camera = new Camera { CameraId = Guid.NewGuid(), Status = "ACTIVE" };
        var connection = ReadyConnection(camera.CameraId);
        var gateway = new Gateway();
        var useCase = new CameraPlayback(new Store(camera, connection), new User("ADMIN"), gateway);

        var result = await useCase.Prepare(camera.CameraId, default);

        Assert.Same(connection, gateway.Connection);
        Assert.Equal("WEBRTC", result.Transport);
        Assert.Equal("https://stream.example.test/cam/whep", result.Url);
        Assert.DoesNotContain(connection.StreamUri, System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task PrepareRejectsConnectionThatIsNotEnabled()
    {
        var camera = new Camera { CameraId = Guid.NewGuid(), Status = "ACTIVE" };
        var connection = ReadyConnection(camera.CameraId);
        connection.IsEnabled = false;
        var gateway = new Gateway();
        var useCase = new CameraPlayback(new Store(camera, connection), new User("ADMIN"), gateway);

        var error = await Assert.ThrowsAsync<AppError>(() => useCase.Prepare(camera.CameraId, default));

        Assert.Equal("CONNECTION_NOT_READY", error.Code);
        Assert.Null(gateway.Connection);
    }

    [Fact]
    public async Task PrepareRejectsRecordedFileSource()
    {
        var camera = new Camera { CameraId = Guid.NewGuid(), Status = "ACTIVE" };
        var connection = ReadyConnection(camera.CameraId);
        connection.SourceType = "RECORDED";
        connection.Protocol = "FILE";
        connection.StreamUri = "file:///videos/test.mp4";
        var useCase = new CameraPlayback(new Store(camera, connection), new User("ADMIN"), new Gateway());

        var error = await Assert.ThrowsAsync<AppError>(() => useCase.Prepare(camera.CameraId, default));

        Assert.Equal("PLAYBACK_UNSUPPORTED", error.Code);
        Assert.Equal(422, error.Status);
    }

    [Fact]
    public async Task PrepareRejectsStaff()
    {
        var camera = new Camera { CameraId = Guid.NewGuid(), Status = "ACTIVE" };
        var useCase = new CameraPlayback(new Store(camera, ReadyConnection(camera.CameraId)), new User("STAFF"), new Gateway());

        var error = await Assert.ThrowsAsync<AppError>(() => useCase.Prepare(camera.CameraId, default));

        Assert.Equal("FORBIDDEN", error.Code);
    }

    private static CameraConnection ReadyConnection(Guid cameraId) => new()
    {
        ConnectionId = Guid.NewGuid(), CameraId = cameraId, SourceType = "LIVE", Protocol = "RTSP",
        StreamUri = "rtsp://camera.test:554/stream", IsEnabled = true,
        LastTestedAt = DateTime.UtcNow, LastTestResult = "SUCCESS"
    };

    private sealed class Gateway : IMediaGateway
    {
        public CameraConnection? Connection { get; private set; }
        public Task<PlaybackEndpoint> EnsurePlayback(CameraConnection connection, CancellationToken ct)
        {
            Connection = connection;
            return Task.FromResult(new PlaybackEndpoint("WEBRTC", "https://stream.example.test/cam/whep"));
        }
    }

    private sealed class User(string role) : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role { get; } = role;
    }

    private sealed class Store(Camera camera, CameraConnection connection) : ISetupStore
    {
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
            => Task.FromResult(typeof(T) == typeof(Camera) && camera.CameraId == id ? (T?)(Entity)camera : null);

        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new()
        {
            IEnumerable<T> rows = typeof(T) == typeof(CameraConnection) ? [(T)(Entity)connection] : [];
            return Task.FromResult(filter is null ? rows.ToList() : rows.AsQueryable().Where(filter).ToList());
        }

        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default) => work();
    }
}
