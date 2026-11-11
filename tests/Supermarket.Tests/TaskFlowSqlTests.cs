using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence;
using Supermarket.Infrastructure.Tasks;
using Xunit;
using Db = Supermarket.Infrastructure.Persistence.Scaffolded;
using Task = System.Threading.Tasks.Task;

namespace Supermarket.Tests;

[Collection("SqlApi")]
[Trait("Category", "SqlIntegration")]
public sealed class TaskFlowSqlTests(SqlApiFixture fixture)
{
    [Fact]
    public async Task DispatchRingsAndEventClearBranchesPersistWithoutResettingSla()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Db.AppDbContext>();
        var clock = new TestClock { UtcNow = new(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc) }; // 10:00 store time
        var store = new RollbackStore(new EfSetupStore(db, clock), db);
        var calendar = new StoreCalendar(StoreCalendar.Zone(null));
        var schema = new EfTaskSchema(db);
        var roster = new StaffRoster(store, calendar);
        var notifier = new TaskNotifier(store, clock);
        var verification = new TaskVerification(store, clock, notifier);
        var dispatch = new TaskDispatch(store, roster, notifier, verification, clock, schema);
        var views = new TaskViews(store);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var staffRole = Assert.Single(await store.List<Role>(role => role.Name == "STAFF"));
            var adminRole = Assert.Single(await store.List<Role>(role => role.Name == "ADMIN"));
            var taskType = Assert.Single(await store.List<TaskType>(type => type.Code == "TT-01"));
            var ids = new FixtureIds();
            var staff = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

            await store.Transaction(async () =>
            {
                await store.Add(new UserAccount
                {
                    UserId = ids.Admin, RoleId = adminRole.RoleId, Email = $"d7-admin-{ids.Token}@test.local",
                    PasswordHash = "fixture-only", FullName = "D7 Admin", Code = UserAccount.CodeFor(ids.Admin)
                });
                await store.Add(new Supermarket.Domain.Supermarket { SupermarketId = ids.Market, Code = $"D7-{ids.Token}", Name = "D7 SQL market" });
                foreach (var floor in ids.Floors)
                    await store.Add(new Floor { FloorId = floor.Id, SupermarketId = ids.Market, FloorNumber = floor.Number, Name = $"Floor {floor.Number}" });
                foreach (var zone in ids.Zones)
                    await store.Add(new Zone { ZoneId = zone.Id, FloorId = zone.FloorId, Code = zone.Code, Name = zone.Code });
                await store.Add(new ZoneAdjacency { ZoneAdjacencyId = Guid.NewGuid(), ZoneId = ids.SourceZone, AdjacentZoneId = ids.AdjacentZone, CreatedAt = clock.UtcNow });
                await store.Add(new ZoneAdjacency { ZoneAdjacencyId = Guid.NewGuid(), ZoneId = ids.AdjacentZone, AdjacentZoneId = ids.SourceZone, CreatedAt = clock.UtcNow });
                await store.Add(new Camera { CameraId = ids.Camera, FloorId = ids.SourceFloor, Code = $"D7-CAM-{ids.Token}", Name = "D7 camera", Status = "ACTIVE" });
                await store.Add(new OperationalEventType
                {
                    EventTypeId = ids.EventType, Code = $"D7_QUEUE_{ids.Token}", Name = "Long queue",
                    MeasurementType = "QUEUE_LENGTH", Status = "ACTIVE"
                });
                await store.Add(new MonitoringConfiguration
                {
                    ConfigId = ids.Configuration, ZoneId = ids.SourceZone, Name = "D7 monitoring",
                    ConfidenceThreshold = .5m, Status = "ACTIVE", CreatedByUserId = ids.Admin
                });
                await store.Add(new MonitoringRule
                {
                    RuleId = ids.Rule, ConfigId = ids.Configuration, EventTypeId = ids.EventType,
                    OutcomeType = "OPERATIONAL_EVENT", WarningThreshold = 3, CriticalThreshold = 5,
                    ThresholdUnit = "PEOPLE", SustainSec = 1, CooldownSec = 30, ClearDurationSec = 1, Enabled = true
                });
                await store.Add(new SlaPolicy
                {
                    PolicyId = ids.Policy, EventTypeId = ids.EventType, ProblemDomain = "OPERATIONAL_EVENT",
                    PriorityCode = "MEDIUM", ResponseTargetSec = 300, ResolutionTargetSec = 1800,
                    OfferTimeoutSec = 10, WarningPercent = 80, Version = 1, IsEnabled = true
                });
                await store.Add(new SlaPolicyAction
                {
                    ActionId = Guid.NewGuid(), PolicyId = ids.Policy, TargetRoleId = staffRole.RoleId,
                    ActionType = "CREATE_OPERATIONAL_TASK", ExecutionMode = "AUTOMATIC", IsEnabled = true,
                    ConditionJson = """{"alert_level":"CRITICAL"}""",
                    ActionParametersJson = """{"title":"Open checkout","instructions":"Open one more counter","taskTypeCode":"TT-01"}"""
                });
                await store.Add(new Shift
                {
                    ShiftId = ids.Shift, SupermarketId = ids.Market, WeekStart = new(2026, 11, 9),
                    Name = $"D7-{ids.Token}", StartTime = new(8, 0), EndTime = new(16, 0)
                });
                for (var index = 0; index < staff.Length; index++)
                {
                    var userId = staff[index];
                    var assignmentId = Guid.NewGuid();
                    await store.Add(new UserAccount
                    {
                        UserId = userId, RoleId = staffRole.RoleId, SpecialtyId = taskType.RequiredSpecialtyId,
                        Email = $"d7-staff-{index}-{ids.Token}@test.local", PasswordHash = "fixture-only",
                        FullName = $"D7 Staff {index}", Code = UserAccount.CodeFor(userId)
                    });
                    await store.Add(new ShiftAssignment
                    {
                        ShiftAssignmentId = assignmentId, UserId = userId, ShiftId = ids.Shift,
                        WorkDate = new(2026, 11, 10), CheckInAt = clock.UtcNow.AddHours(-1)
                    });
                    await store.Add(new StaffZoneAssignment
                    {
                        StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignmentId,
                        ZoneId = ids.Zones[index].Id, AssignedAt = clock.UtcNow.AddHours(-1)
                    });
                }
                return 0;
            });

            Assert.True(await schema.Ready(default));
            var openEvent = Event(ids, Guid.NewGuid(), clock.UtcNow);
            await store.Transaction(async () =>
            {
                await store.Add(openEvent);
                await dispatch.Raised(openEvent, null, default);
                return 0;
            });
            var openTask = Assert.Single(await store.List<WorkTask>(task => task.EventId == openEvent.EventId));
            var originalDeadlines = (openTask.ResponseDeadlineAt, openTask.ResolutionDeadlineAt, openTask.SlaSnapshotJson);

            var scopes = new[]
            {
                TaskDispatchScope.SameZone, TaskDispatchScope.AdjacentSameFloor,
                TaskDispatchScope.RemainingSameFloor, TaskDispatchScope.AdjacentFloor,
                TaskDispatchScope.OtherFloor
            };
            for (var wave = 0; wave < scopes.Length; wave++)
            {
                var pending = Assert.Single(await store.List<TaskOffer>(offer => offer.TaskId == openTask.TaskId && offer.Status == TaskOfferStatus.Pending));
                Assert.Equal((wave + 1, scopes[wave], staff[wave]), (pending.WaveNumber, pending.DispatchScope, pending.StaffUserId));
                if (wave < scopes.Length - 1)
                {
                    clock.UtcNow = clock.UtcNow.AddSeconds(11);
                    Assert.Equal(1, await dispatch.Tick(default));
                    var expired = Assert.Single(await store.List<TaskOffer>(offer => offer.OfferId == pending.OfferId));
                    Assert.Equal((TaskOfferStatus.Expired, "NO_RESPONSE"), (expired.Status, expired.DeclineReasonCode));
                }
            }

            clock.UtcNow = clock.UtcNow.AddSeconds(1);
            openEvent.Status = "CLEARED";
            openEvent.ClearedAt = openEvent.LastObservedAt = clock.UtcNow;
            openEvent.UpdatedAt = clock.UtcNow;
            openEvent.MetricValue = 1;
            await store.Transaction(async () =>
            {
                await store.Update(openEvent);
                await dispatch.Cleared(openEvent, default);
                return 0;
            });
            openTask = Assert.Single(await store.List<WorkTask>(task => task.TaskId == openTask.TaskId));
            Assert.Equal(WorkTaskStatus.Cancelled, openTask.Status);
            Assert.Equal(originalDeadlines, (openTask.ResponseDeadlineAt, openTask.ResolutionDeadlineAt, openTask.SlaSnapshotJson));
            var withdrawn = Assert.Single(await store.List<TaskOffer>(offer => offer.TaskId == openTask.TaskId && offer.Status == TaskOfferStatus.Cancelled));
            Assert.Equal("EVENT_CLEARED", withdrawn.DeclineReasonCode);
            Assert.Single(await store.List<Notification>(notification => notification.TaskId == openTask.TaskId && notification.NotificationType == TaskNotifier.OfferWithdrawn));

            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var acceptedEvent = Event(ids, Guid.NewGuid(), clock.UtcNow);
            await store.Transaction(async () =>
            {
                await store.Add(acceptedEvent);
                await dispatch.Raised(acceptedEvent, null, default);
                return 0;
            });
            var acceptedTask = Assert.Single(await store.List<WorkTask>(task => task.EventId == acceptedEvent.EventId));
            var acceptedDeadlines = (acceptedTask.ResponseDeadlineAt, acceptedTask.ResolutionDeadlineAt, acceptedTask.SlaSnapshotJson);
            var acceptedOffer = Assert.Single(await store.List<TaskOffer>(offer => offer.TaskId == acceptedTask.TaskId && offer.Status == TaskOfferStatus.Pending));
            var staffTasks = new StaffTasks(store, new Actor(acceptedOffer.StaffUserId), clock, calendar, roster, dispatch, views, schema);
            await staffTasks.Respond(acceptedOffer.OfferId, new(TaskResponse.HandleLater, clock.UtcNow.AddMinutes(5), null), default);

            acceptedEvent.Status = "CLEARED";
            acceptedEvent.ClearedAt = acceptedEvent.LastObservedAt = clock.UtcNow.AddSeconds(1);
            acceptedEvent.UpdatedAt = acceptedEvent.ClearedAt.Value;
            acceptedEvent.MetricValue = 1;
            clock.UtcNow = acceptedEvent.ClearedAt.Value;
            await store.Transaction(async () =>
            {
                await store.Update(acceptedEvent);
                await dispatch.Cleared(acceptedEvent, default);
                return 0;
            });
            acceptedTask = Assert.Single(await store.List<WorkTask>(task => task.TaskId == acceptedTask.TaskId));
            Assert.Equal((WorkTaskStatus.Accepted, acceptedOffer.StaffUserId), (acceptedTask.Status, acceptedTask.CurrentAssigneeUserId));
            Assert.Null(acceptedTask.VerifiedAt);
            Assert.Equal(acceptedDeadlines, (acceptedTask.ResponseDeadlineAt, acceptedTask.ResolutionDeadlineAt, acceptedTask.SlaSnapshotJson));
            Assert.Single(await store.List<Notification>(notification => notification.TaskId == acceptedTask.TaskId && notification.NotificationType == TaskNotifier.EventCleared));
        }
        finally
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
        }
    }

    private static OperationalEvent Event(FixtureIds ids, Guid eventId, DateTime now) => new()
    {
        EventId = eventId, CameraId = ids.Camera, ZoneId = ids.SourceZone, RuleId = ids.Rule,
        EventTypeId = ids.EventType, MetricValue = 7, Priority = "CRITICAL", DetectedAt = now,
        LastObservedAt = now, CreatedAt = now, UpdatedAt = now, Status = "ACTIVE",
        MetadataJson = """{"measurementMode":"QUEUE_LENGTH","thresholdUnit":"PEOPLE","warningThreshold":3,"latestMetricValue":7}"""
    };

    private sealed record Actor(Guid UserId, string Role = "STAFF") : ICurrentUser;
    private sealed class TestClock : IClock { public DateTime UtcNow { get; set; } }

    private sealed class FixtureIds
    {
        public string Token { get; } = Guid.NewGuid().ToString("N")[..8];
        public Guid Admin { get; } = Guid.NewGuid();
        public Guid Market { get; } = Guid.NewGuid();
        public Guid SourceFloor { get; } = Guid.NewGuid();
        public Guid NearFloor { get; } = Guid.NewGuid();
        public Guid FarFloor { get; } = Guid.NewGuid();
        public Guid SourceZone { get; } = Guid.NewGuid();
        public Guid AdjacentZone { get; } = Guid.NewGuid();
        public Guid RemainingZone { get; } = Guid.NewGuid();
        public Guid NearZone { get; } = Guid.NewGuid();
        public Guid FarZone { get; } = Guid.NewGuid();
        public Guid Camera { get; } = Guid.NewGuid();
        public Guid EventType { get; } = Guid.NewGuid();
        public Guid Configuration { get; } = Guid.NewGuid();
        public Guid Rule { get; } = Guid.NewGuid();
        public Guid Policy { get; } = Guid.NewGuid();
        public Guid Shift { get; } = Guid.NewGuid();
        public (Guid Id, int Number)[] Floors => [(SourceFloor, 2), (NearFloor, 1), (FarFloor, 5)];
        public (Guid Id, Guid FloorId, string Code)[] Zones =>
        [
            (SourceZone, SourceFloor, $"D7-SRC-{Token}"),
            (AdjacentZone, SourceFloor, $"D7-ADJ-{Token}"),
            (RemainingZone, SourceFloor, $"D7-REM-{Token}"),
            (NearZone, NearFloor, $"D7-NEAR-{Token}"),
            (FarZone, FarFloor, $"D7-FAR-{Token}")
        ];
    }

    // Application transactions flush into the fixture's outer transaction. The test then rolls
    // everything back, while every read/write still uses the production EF mapper and SQL Server.
    private sealed class RollbackStore(ISetupStore inner, Db.AppDbContext db) : ISetupStore
    {
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new() => inner.Find<T>(id, ct);
        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new() => inner.List(filter, ct);
        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() => inner.Add(entity, ct);
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => inner.Update(entity, ct);
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => inner.Remove(entity, ct);
        public async Task<T> Transaction<T>(Func<Task<T>> work, CancellationToken ct = default)
        {
            var result = await work();
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            return result;
        }
    }
}
