using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Xunit;
using DomainAction = Supermarket.Domain.ResponsePolicyAction;

namespace Supermarket.Tests;

public sealed class TaskPersistenceMappingTests
{
    // Add/Find use the actual EF mapper and tracked rows without connecting to SQL.
    // MemoryStore-based dispatch tests cannot catch a domain/scaffold mismatch.
    [Fact]
    public async System.Threading.Tasks.Task AutomaticTaskActionRoundTripsThroughTheV6Mapper()
    {
        using var db = Context();
        var clock = new Clock();
        var store = new EfSetupStore(db, clock);
        var action = new DomainAction
        {
            ActionId = Guid.NewGuid(), PolicyId = Guid.NewGuid(), TargetRoleId = Guid.NewGuid(),
            ActionType = "CREATE_OPERATIONAL_TASK", ExecutionMode = "AUTOMATIC", IsEnabled = true,
            ConditionJson = "{}", ActionParametersJson = "{\"title\":\"Clear the queue\"}"
        };

        await store.Add(action);
        var row = Assert.Single(db.ResponsePolicyActions.Local);
        Assert.Equal(action.ActionParametersJson, row.ActionParametersJson);
        var loaded = Assert.IsType<DomainAction>(await store.Find<DomainAction>(action.ActionId));
        Assert.Equal(action.PolicyId, loaded.PolicyId);
        Assert.Equal(action.TargetRoleId, loaded.TargetRoleId);
        Assert.Equal(("CREATE_OPERATIONAL_TASK", "AUTOMATIC", true), (loaded.ActionType, loaded.ExecutionMode, loaded.IsEnabled));
        Assert.Equal(clock.UtcNow, loaded.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
    }

    [Fact]
    public async System.Threading.Tasks.Task EventTaskAndOfferRoundTripThroughTheV6Mapper()
    {
        using var db = Context();
        var store = new EfSetupStore(db, new Clock());
        var task = new WorkTask
        {
            TaskId = Guid.NewGuid(), ZoneId = Guid.NewGuid(), EventId = Guid.NewGuid(),
            Title = "Clear the queue", OriginType = TaskOrigin.OperationalEvent
        };
        var offer = new Supermarket.Domain.TaskAssignmentHistory
        {
            TaskAssignmentId = Guid.NewGuid(), TaskId = task.TaskId, StaffUserId = Guid.NewGuid(),
            AssignedAt = new Clock().UtcNow
        };

        await store.Add(task);
        await store.Add(offer);
        var loadedTask = Assert.IsType<WorkTask>(await store.Find<WorkTask>(task.TaskId));
        var loadedOffer = Assert.IsType<Supermarket.Domain.TaskAssignmentHistory>(await store.Find<Supermarket.Domain.TaskAssignmentHistory>(offer.TaskAssignmentId));
        Assert.Equal(task.EventId, loadedTask.EventId);
        Assert.Equal(TaskOrigin.OperationalEvent, loadedTask.OriginType);
        Assert.Equal(WorkTaskStatus.Offered, loadedTask.Status);
        Assert.Equal((task.TaskId, offer.StaffUserId, OfferStatus.Offered), (loadedOffer.TaskId, loadedOffer.StaffUserId, loadedOffer.Status));
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=localhost;Database=mapper-only;Integrated Security=true;TrustServerCertificate=true").Options);
    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 25, 5, 0, 0, DateTimeKind.Utc);
    }
}
