using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;

/// <summary>SLA warning and breach milestones for Tasks and Incidents (BR-38–BR-41).</summary>
public sealed class SlaMonitorTests
{
    private static readonly DateTime Created = new(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AnUnacceptedTaskWarnsOperatorsAtTheWarningPercentAndEscalatesToManagersAtTheDeadline()
    {
        var w = new World(); var op = w.User("OPERATOR"); var manager = w.User("MANAGER");
        // Response 10 min, resolution 60 min, warning at 50 %.
        var task = w.Task(response: 600, resolution: 3600, warningPercent: 50);

        w.Clock.UtcNow = Created.AddMinutes(4);
        Assert.Equal(0, await w.Monitor.Tick(default));

        w.Clock.UtcNow = Created.AddMinutes(5);
        Assert.Equal(1, await w.Monitor.Tick(default));
        var warning = Assert.Single(w.Notifications(SlaMonitor.ResponseWarning));
        Assert.Equal((task.TaskId, (Guid?)null), (warning.TaskId!.Value, warning.IncidentId));
        Assert.Equal([op], w.Recipients(warning));

        // A second check in the same window writes nothing new.
        Assert.Equal(0, await w.Monitor.Tick(default));

        w.Clock.UtcNow = Created.AddMinutes(10);
        Assert.Equal(1, await w.Monitor.Tick(default));
        Assert.Equal(new HashSet<Guid> { op, manager }, w.Recipients(Assert.Single(w.Notifications(SlaMonitor.ResponseBreach))).ToHashSet());
    }

    [Fact]
    public async Task AcceptanceStopsTheResponseClockButResolutionRunsUntilVerified()
    {
        var w = new World(); w.User("OPERATOR"); w.User("MANAGER");
        var task = w.Task(response: 600, resolution: 3600, warningPercent: 80);
        task.Status = WorkTaskStatus.InProgress; task.AcceptedAt = Created.AddMinutes(2);

        // Past both response milestones and the resolution warning (48 min), but before the resolution deadline.
        w.Clock.UtcNow = Created.AddMinutes(50);
        Assert.Equal(1, await w.Monitor.Tick(default));
        Assert.Empty(w.Notifications(SlaMonitor.ResponseWarning));
        Assert.Empty(w.Notifications(SlaMonitor.ResponseBreach));
        Assert.Single(w.Notifications(SlaMonitor.ResolutionWarning));

        task.Status = WorkTaskStatus.Verified;
        w.Clock.UtcNow = Created.AddHours(2);
        Assert.Equal(0, await w.Monitor.Tick(default));
        Assert.Empty(w.Notifications(SlaMonitor.ResolutionBreach));
    }

    [Fact]
    public async Task AFoundLateBreachStillRecordsItsWarningAndUsesTheDefaultPercentWithoutASnapshot()
    {
        var w = new World(); w.User("OPERATOR");
        var task = w.Task(response: null, resolution: 1000, warningPercent: null);
        w.Clock.UtcNow = Created.AddSeconds(799);
        Assert.Equal(0, await w.Monitor.Tick(default));
        w.Clock.UtcNow = Created.AddSeconds(800);
        Assert.Equal(1, await w.Monitor.Tick(default));

        var late = w.Task(response: 600, resolution: 3600, warningPercent: 80);
        w.Clock.UtcNow = Created.AddHours(3);
        await w.Monitor.Tick(default);
        Assert.Equal(2, w.Notifications(SlaMonitor.ResponseWarning).Count + w.Notifications(SlaMonitor.ResponseBreach).Count);
        Assert.Contains(w.Notifications(SlaMonitor.ResolutionBreach), n => n.TaskId == late.TaskId);
        Assert.Contains(w.Notifications(SlaMonitor.ResolutionBreach), n => n.TaskId == task.TaskId);
    }

    [Fact]
    public async Task AnOpenIncidentGetsResolutionMilestonesAndAClosedOneDoesNot()
    {
        var w = new World(); var op = w.User("OPERATOR");
        var open = w.Incident(resolution: 3600, status: "DETECTED");
        w.Incident(resolution: 3600, status: "DISMISSED");
        w.Clock.UtcNow = Created.AddHours(2);

        Assert.Equal(2, await w.Monitor.Tick(default));
        var breach = Assert.Single(w.Notifications(SlaMonitor.IncidentBreach));
        Assert.Equal((open.IncidentId, (Guid?)null, "HIGH"), (breach.IncidentId!.Value, breach.TaskId, breach.Severity));
        Assert.Single(w.Notifications(SlaMonitor.IncidentWarning));
        Assert.Equal(0, await w.Monitor.Tick(default));
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public StaffTaskTests.Clock Clock { get; } = new() { UtcNow = Created };
        public SlaMonitor Monitor { get; }

        public World()
        {
            foreach (var role in new[] { "OPERATOR", "MANAGER", "STAFF" }) Store.Values.Add(new Role { RoleId = Guid.NewGuid(), Name = role });
            Monitor = new SlaMonitor(Store, Clock, new TaskNotifier(Store, Clock), new ReadySchema());
        }

        public Guid User(string role)
        {
            var user = new UserAccount { UserId = Guid.NewGuid(), RoleId = Store.Values.OfType<Role>().Single(r => r.Name == role).RoleId, FullName = role, Email = $"{Guid.NewGuid():N}@test", Status = "ACTIVE" };
            Store.Values.Add(user);
            return user.UserId;
        }

        public WorkTask Task(int? response, int resolution, int? warningPercent)
        {
            var snapshot = warningPercent is { } p ? $$"""{"priorityCode":"HIGH","warningPercent":{{p}},"appliedAt":"{{Created:O}}"}""" : null;
            var task = new WorkTask { TaskId = Guid.NewGuid(), ZoneId = Guid.NewGuid(), Title = "Clean the spill", Priority = "HIGH", Status = WorkTaskStatus.Offered,
                OriginType = TaskOrigin.Incident, CreatedAt = Created, SlaSnapshotJson = snapshot,
                ResponseDeadlineAt = response is { } r ? Created.AddSeconds(r) : null, ResolutionDeadlineAt = Created.AddSeconds(resolution) };
            Store.Values.Add(task);
            return task;
        }

        public Incident Incident(int resolution, string status)
        {
            var incident = new Incident { IncidentId = Guid.NewGuid(), IncidentTypeId = Guid.NewGuid(), ZoneId = Guid.NewGuid(), Title = "Spill at entrance", Status = status,
                CreatedAt = Created, ResolutionDeadlineAt = Created.AddSeconds(resolution),
                SlaSnapshotJson = $$"""{"priorityCode":"HIGH","warningPercent":80,"appliedAt":"{{Created:O}}"}""" };
            Store.Values.Add(incident);
            return incident;
        }

        public List<Notification> Notifications(string type) => Store.Values.OfType<Notification>().Where(n => n.NotificationType == type).ToList();
        public Guid[] Recipients(Notification n) => Store.Values.OfType<NotificationRecipient>().Where(r => r.NotificationId == n.NotificationId).Select(r => r.UserId).ToArray();
    }

    private sealed class ReadySchema : ITaskSchema { public Task<bool> Ready(CancellationToken ct) => System.Threading.Tasks.Task.FromResult(true); }
}
