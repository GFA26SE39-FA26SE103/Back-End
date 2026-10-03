using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Supermarket.Api;
using Supermarket.Api.Controllers;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
using Store = Supermarket.Domain.Supermarket;
namespace Supermarket.Tests;

// Operator screens (floor map, camera live) read setup data; only Admin may change it.
public sealed class OperatorAccessTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Point[] Triangle = [new(.1m, .1m), new(.6m, .1m), new(.1m, .6m)];

    [Theory]
    [InlineData(typeof(StoreController))]
    [InlineData(typeof(CamerasController))]
    public void EveryActionDeclaresItsRolesAndMutationsStayAdminOnly(Type controller)
    {
        var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var roles = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>()).Roles;
            var verbs = action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToArray();
            var isRead = verbs.All(v => v == "GET");
            var expected = isRead && action.Name != nameof(CamerasController.Connection) ? AccessRoles.Viewer : AccessRoles.Admin;
            Assert.True(roles == expected, $"{controller.Name}.{action.Name} ({string.Join(',', verbs)}) has roles '{roles}', expected '{expected}'.");
        }
        Assert.Null(controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    [Fact]
    public void LiveAiPreviewIsLimitedToAdminAndOperator()
    {
        Assert.Equal(AccessRoles.LiveView, typeof(AiPreviewController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    [Theory, InlineData("OPERATOR"), InlineData("MANAGER"), InlineData("ADMIN")]
    public async Task OperationalRolesCanReadTheFloorMapData(string role)
    {
        var world = World.Create(role);

        Assert.Single(await world.Stores.Stores(Ct));
        Assert.Single(await world.Stores.Floors(world.StoreId, Ct));
        Assert.Single(await world.Stores.Zones(world.FloorId, Ct));
        Assert.Equal(world.ZoneId, (await world.Stores.Zone(world.ZoneId, Ct)).ZoneId);
        Assert.Single(await world.Cameras.List(world.FloorId, Ct));
        Assert.Equal(world.CameraId, (await world.Cameras.Get(world.CameraId, Ct)).CameraId);
        Assert.Single(await world.Cameras.Mappings(world.CameraId, Ct));
        Assert.Equal("image/jpeg", (await world.Cameras.Preview(world.CameraId, Ct)).ContentType);
        Assert.Equal("image/png", (await world.FloorPlans.Open(world.FloorId, Ct)).ContentType);
        Assert.Empty(world.Store.Mutations);
    }

    [Fact]
    public async Task StaffCannotReadSetupData()
    {
        var world = World.Create("STAFF");

        await Forbidden(() => world.Stores.Stores(Ct));
        await Forbidden(() => world.Stores.Zones(world.FloorId, Ct));
        await Forbidden(() => world.Cameras.List(world.FloorId, Ct));
        await Forbidden(() => world.Cameras.Preview(world.CameraId, Ct));
        await Forbidden(() => world.FloorPlans.Open(world.FloorId, Ct));
        Assert.Equal(0, world.Stream.Calls);
    }

    [Theory, InlineData("OPERATOR"), InlineData("MANAGER")]
    public async Task OperationalRolesCannotChangeSetupOrReadConnectionSecrets(string role)
    {
        var world = World.Create(role);

        await Forbidden(() => world.Stores.SaveZone(null, world.FloorId, new ZoneRequest("NEW", "New zone", null, Triangle), Ct));
        await Forbidden(() => world.Cameras.Save(world.CameraId, null, new CameraRequest("CAM-01", "Camera", null, null, null, DateTime.UtcNow, DateTime.UtcNow.AddYears(1), null, null, null, "ACTIVE"), Ct));
        await Forbidden(() => world.Cameras.Connection(world.CameraId, Ct));
        await Forbidden(() => world.Cameras.Configure(world.CameraId, new ConnectionRequest("DEMO", "HTTP", "demo://camera/main"), Ct));
        await Forbidden(() => world.Cameras.Test(world.CameraId, Ct));
        await Forbidden(() => world.Cameras.Enable(world.CameraId, false, Ct));
        await Forbidden(() => world.Cameras.Map(world.CameraId, world.ZoneId, new MappingRequest(Triangle), Ct));
        await Forbidden(() => world.Cameras.Unmap(world.CameraId, world.ZoneId, Ct));
        Assert.Empty(world.Store.Mutations);
        Assert.Equal(0, world.Stream.Calls);
    }

    [Fact]
    public async Task OperatorCanRunTheLiveAiView()
    {
        var world = World.Create("OPERATOR");

        Assert.Equal("LIVE", (await world.Ai.Start(world.CameraId, Ct)).State);
        Assert.Equal("LIVE", (await world.Ai.Status(world.CameraId, Ct)).State);
        Assert.Equal("STOPPED", (await world.Ai.Stop(world.CameraId, Ct)).State);
    }

    [Theory, InlineData("MANAGER"), InlineData("STAFF")]
    public async Task OtherRolesCannotStartTheLiveAiView(string role)
    {
        var world = World.Create(role);

        await Forbidden(() => world.Ai.Start(world.CameraId, Ct));
        await Forbidden(() => world.Ai.Frame(world.CameraId, Ct));
        Assert.Equal(0, world.AiClient.Calls);
    }

    private static async Task Forbidden(Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<AppError>(action);
        Assert.Equal(403, error.Status);
        Assert.Equal("FORBIDDEN", error.Code);
    }

    private sealed class World
    {
        public required MemoryStore Store
        {
            get; init;
        }
        public required CountingStream Stream
        {
            get; init;
        }
        public required CountingAiClient AiClient
        {
            get; init;
        }
        public required StoreSetup Stores
        {
            get; init;
        }
        public required CameraSetup Cameras
        {
            get; init;
        }
        public required FloorPlanUpload FloorPlans
        {
            get; init;
        }
        public required AiPreview Ai
        {
            get; init;
        }
        public Guid StoreId { get; } = Guid.NewGuid();
        public Guid FloorId { get; } = Guid.NewGuid();
        public Guid ZoneId { get; } = Guid.NewGuid();
        public Guid CameraId { get; } = Guid.NewGuid();

        public static World Create(string role)
        {
            var store = new MemoryStore();
            var user = new User(role);
            var stream = new CountingStream();
            var ai = new CountingAiClient();
            var world = new World
            {
                Store = store,
                Stream = stream,
                AiClient = ai,
                Stores = new StoreSetup(store, user),
                Cameras = new CameraSetup(store, user, stream, new PlainSecrets(), new FixedClock()),
                FloorPlans = new FloorPlanUpload(store, user, new OneFloorPlan()),
                Ai = new AiPreview(store, user, ai),
            };
            store.Seed(new Store { SupermarketId = world.StoreId, Code = "CQ1", Name = "Central" });
            store.Seed(new Floor { FloorId = world.FloorId, SupermarketId = world.StoreId, FloorNumber = 1, Name = "Ground", MapAssetUrl = "http://api.test/api/floors/map?v=plan.png" });
            store.Seed(new Zone { ZoneId = world.ZoneId, FloorId = world.FloorId, Code = "CHECKOUT", Name = "Checkout", MapPolygon = "[{\"X\":0.1,\"Y\":0.1},{\"X\":0.6,\"Y\":0.1},{\"X\":0.1,\"Y\":0.6}]" });
            store.Seed(new Camera { CameraId = world.CameraId, FloorId = world.FloorId, Code = "CAM-01", Name = "Checkout", Status = "ACTIVE", HealthStatus = "ONLINE" });
            store.Seed(new CameraZoneMapping { CameraZoneId = Guid.NewGuid(), CameraId = world.CameraId, ZoneId = world.ZoneId, RoiPolygon = "[]" });
            store.Seed(new CameraConnection { ConnectionId = Guid.NewGuid(), CameraId = world.CameraId, SourceType = "LIVE", Protocol = "RTSP", StreamUri = "rtsp://camera/main", IsEnabled = true, LastTestedAt = DateTime.UtcNow, LastTestResult = "SUCCESS" });
            return world;
        }
    }

    private sealed class User(string role) : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => role;
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class PlainSecrets : ICredentialProtector
    {
        public string Protect(string secret) => secret;
        public string Unprotect(string secret) => secret;
    }

    private sealed class CountingStream : ICameraStream
    {
        public int Calls
        {
            get; private set;
        }
        public Task<ProbeResult> Test(CameraConnection connection, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new ProbeResult(true, "FRAME_RECEIVED"));
        }
        public Task<PreviewFrame> Preview(CameraConnection connection, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new PreviewFrame([0xFF, 0xD8, 0xFF, 0xD9], "image/jpeg"));
        }
    }

    private sealed class CountingAiClient : IAiPreviewClient
    {
        public int Calls
        {
            get; private set;
        }
        public Task<AiPreviewStatusView> Start(CameraConnection connection, CancellationToken ct, decimal? confidence = null) => View(connection.CameraId, "LIVE");
        public Task<AiPreviewStatusView> Status(Guid cameraId, CancellationToken ct) => View(cameraId, "LIVE");
        public Task<PreviewFrame> Frame(Guid cameraId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new PreviewFrame([0xFF, 0xD8], "image/jpeg"));
        }
        public Task<AiPreviewStatusView> Stop(Guid cameraId, CancellationToken ct) => View(cameraId, "STOPPED");
        private Task<AiPreviewStatusView> View(Guid cameraId, string state)
        {
            Calls++;
            return Task.FromResult(new AiPreviewStatusView(cameraId, state, null, DateTime.UtcNow, 1, null));
        }
    }

    private sealed class OneFloorPlan : IFloorPlanStorage
    {
        public Task<StoredFloorPlan> Save(Guid floorId, Stream content, string filename, string contentType, long length, CancellationToken ct) => throw new NotSupportedException();
        public Task<FloorPlanFile?> Open(Guid floorId, string token, CancellationToken ct)
            => Task.FromResult<FloorPlanFile?>(new FloorPlanFile(new MemoryStream([1, 2, 3]), "image/png"));
        public Task Delete(Guid floorId, string token, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>In-memory store; any Add/Update/Remove is recorded so tests can prove nothing changed.</summary>
    private sealed class MemoryStore : ISetupStore
    {
        private readonly List<Entity> rows = [];
        public List<string> Mutations { get; } = [];

        public void Seed(Entity entity) => rows.Add(entity);

        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
        {
            var key = typeof(T).GetProperty($"{typeof(T).Name}Id") ?? typeof(T).GetProperty("ConnectionId");
            return Task.FromResult(rows.OfType<T>().FirstOrDefault(row => Equals(key?.GetValue(row), id)));
        }

        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new()
        {
            var query = rows.OfType<T>().AsQueryable();
            return Task.FromResult((filter is null ? query : query.Where(filter)).ToList());
        }

        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Record("Add", entity);
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Record("Update", entity);
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Record("Remove", entity);
        public Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default) => work();

        private Task Record(string kind, Entity entity)
        {
            Mutations.Add($"{kind} {entity.GetType().Name}");
            return Task.CompletedTask;
        }
    }
}
