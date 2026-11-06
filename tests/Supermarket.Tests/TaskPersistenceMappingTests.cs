using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Xunit;
using DomainAction = Supermarket.Domain.SlaPolicyAction;

namespace Supermarket.Tests;

public sealed class TaskPersistenceMappingTests
{
    [Fact]
    public void RuntimeEfModelHasNoLegacyResponsePolicyTables()
    {
        using var db = Context();
        var tables = db.Model.GetEntityTypes().Select(entity => entity.GetTableName()).ToArray();
        Assert.DoesNotContain("ResponsePolicy", tables);
        Assert.DoesNotContain("ResponsePolicyAction", tables);
        Assert.Contains("SLAPolicy", tables);
        Assert.Contains("SLAPolicyAction", tables);
    }

    // Add/Find use the actual EF mapper and tracked rows without connecting to SQL.
    // MemoryStore-based dispatch tests cannot catch a domain/scaffold mismatch.
    [Fact]
    public async System.Threading.Tasks.Task AutomaticSlaTaskActionRoundTripsThroughTheV7Mapper()
    {
        using var db = Context();
        var clock = new Clock();
        var store = new EfSetupStore(db, clock);
        var action = new DomainAction
        {
            ActionId = Guid.NewGuid(), PolicyId = Guid.NewGuid(), TargetRoleId = Guid.NewGuid(),
            ActionType = "CREATE_OPERATIONAL_TASK", ExecutionMode = "AUTOMATIC", IsEnabled = true,
            ConditionJson = """{"alert_level":"CRITICAL"}""",
            ActionParametersJson = """{"title":"Clear the queue","taskTypeCode":"TT-01"}"""
        };

        await store.Add(action);
        var row = Assert.IsType<SlapolicyAction>(Assert.Single(db.SlapolicyActions.Local));
        Assert.Equal(action.ActionId, row.ActionId);
        Assert.Equal(action.PolicyId, row.PolicyId);
        Assert.Equal(action.TargetRoleId, row.TargetRoleId);
        Assert.Equal(action.ConditionJson, row.ConditionJson);
        Assert.Equal(action.ActionParametersJson, row.ActionParametersJson);
        var loaded = Assert.IsType<DomainAction>(await store.Find<DomainAction>(action.ActionId));
        Assert.Equal(action.ActionId, loaded.ActionId);
        Assert.Equal(action.PolicyId, loaded.PolicyId);
        Assert.Equal(action.TargetRoleId, loaded.TargetRoleId);
        Assert.Equal(action.ConditionJson, loaded.ConditionJson);
        Assert.Equal(action.ActionParametersJson, loaded.ActionParametersJson);
        Assert.Equal(("CREATE_OPERATIONAL_TASK", "AUTOMATIC", true), (loaded.ActionType, loaded.ExecutionMode, loaded.IsEnabled));
        Assert.Equal(clock.UtcNow, loaded.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
        Assert.Equal(clock.UtcNow, loaded.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, loaded.UpdatedAt.Kind);
    }

    [Fact]
    public async System.Threading.Tasks.Task EventTaskAndMultipleOffersRoundTripThroughTheV7Mapper()
    {
        using var db = Context();
        var store = new EfSetupStore(db, new Clock());
        var taskTypeId = Guid.NewGuid();
        var task = new WorkTask
        {
            TaskId = Guid.NewGuid(), ZoneId = Guid.NewGuid(), EventId = Guid.NewGuid(),
            Title = "Clear the queue", OriginType = TaskOrigin.OperationalEvent, TaskTypeId = taskTypeId
        };
        var cycle = Guid.NewGuid();
        var offer = new Supermarket.Domain.TaskOffer
        {
            OfferId = Guid.NewGuid(), TaskId = task.TaskId, StaffUserId = Guid.NewGuid(), DispatchCycleId = cycle,
            WaveNumber = 1, DispatchScope = TaskDispatchScope.SameZone, Status = TaskOfferStatus.Pending,
            OfferedAt = new Clock().UtcNow, ExpiresAt = new Clock().UtcNow.AddSeconds(30)
        };
        var other = new Supermarket.Domain.TaskOffer
        {
            OfferId = Guid.NewGuid(), TaskId = task.TaskId, StaffUserId = Guid.NewGuid(), DispatchCycleId = cycle,
            WaveNumber = 1, DispatchScope = TaskDispatchScope.SameZone, Status = TaskOfferStatus.Pending,
            OfferedAt = offer.OfferedAt, ExpiresAt = offer.ExpiresAt
        };

        await store.Add(task);
        await store.Add(offer);
        await store.Add(other);
        var loadedTask = Assert.IsType<WorkTask>(await store.Find<WorkTask>(task.TaskId));
        var loadedOffer = Assert.IsType<Supermarket.Domain.TaskOffer>(await store.Find<Supermarket.Domain.TaskOffer>(offer.OfferId));
        Assert.Equal(task.EventId, loadedTask.EventId);
        Assert.Equal(taskTypeId, loadedTask.TaskTypeId);
        Assert.Equal(TaskOrigin.OperationalEvent, loadedTask.OriginType);
        Assert.Equal(WorkTaskStatus.Offered, loadedTask.Status);
        Assert.Equal((task.TaskId, offer.StaffUserId, TaskOfferStatus.Pending), (loadedOffer.TaskId, loadedOffer.StaffUserId, loadedOffer.Status));
        Assert.Equal(2, db.TaskOffers.Local.Count);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=localhost;Database=mapper-only;Integrated Security=true;TrustServerCertificate=true").Options);
    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 25, 5, 0, 0, DateTimeKind.Utc);
    }
}
