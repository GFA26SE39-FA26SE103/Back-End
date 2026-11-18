using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

/// <summary>L3: Operator Incident board with closure and mitigation milestones.</summary>
public sealed class IncidentBoardTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OpenAndEndedIncidentsShowTypeZoneMandatoryTaskProgressAndMilestones()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var type = new IncidentType { IncidentTypeId = Guid.NewGuid(), Code = "SPILL", Name = "Spill" };
        var zone = new Zone { ZoneId = Guid.NewGuid(), Name = "Entrance" };
        var staff = new UserAccount { UserId = Guid.NewGuid(), FullName = "Linh", Email = "linh@test", Status = "ACTIVE" };
        var isolate = new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-12", Name = "Isolate Hazardous Area" };
        var cleanType = new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-06", Name = "Clean Liquid Spill" };
        var open = new Incident { IncidentId = Guid.NewGuid(), IncidentTypeId = type.IncidentTypeId, ZoneId = zone.ZoneId, Title = "Spill at entrance", Status = "IN_PROGRESS",
            SourceType = "STAFF_REPORTED", CreatedAt = Now, MitigatedAt = Now.AddMinutes(5),
            SlaSnapshotJson = """{"priorityCode":"HIGH","warningPercent":80}""" };
        var closed = new Incident { IncidentId = Guid.NewGuid(), IncidentTypeId = type.IncidentTypeId, ZoneId = zone.ZoneId, Title = "Old spill", Status = "CLOSED",
            StatusReason = IncidentClosure.AllMandatoryVerified, CreatedAt = Now.AddHours(-2), ClosedAt = Now.AddHours(-1) };
        store.Values.AddRange([type, zone, staff, isolate, cleanType, open, closed,
            new WorkTask { TaskId = Guid.NewGuid(), IncidentId = open.IncidentId, Title = "Isolate spill", Status = WorkTaskStatus.Verified, IsMandatory = true, CreatedAt = Now, CurrentAssigneeUserId = staff.UserId, TaskTypeId = isolate.TaskTypeId },
            new WorkTask { TaskId = Guid.NewGuid(), IncidentId = open.IncidentId, Title = "Dry floor", Status = WorkTaskStatus.InProgress, IsMandatory = true, CreatedAt = Now.AddMinutes(1), TaskTypeId = cleanType.TaskTypeId }]);
        var incidents = new IncidentActions(store, new MonitoringSetupTests.User("OPERATOR"), new StaffTaskTests.Clock { UtcNow = Now });

        var row = Assert.Single(await incidents.List(null, default));
        Assert.Equal(open.IncidentId, row.IncidentId);
        Assert.Equal(("SPILL", "Entrance", "HIGH", true), (row.IncidentTypeCode, row.ZoneName, row.SlaPriorityCode, row.IsOpen));
        Assert.Equal((2, 1), (row.MandatoryTasks, row.VerifiedMandatoryTasks));
        Assert.Equal(Now.AddMinutes(5), row.MitigatedAt);
        Assert.Equal("Linh", row.Tasks[0].AssigneeName);
        Assert.Equal(("TT-12", true), (row.Tasks[0].TaskTypeCode, row.Tasks[0].IsMitigation));
        Assert.Equal(("TT-06", false), (row.Tasks[1].TaskTypeCode, row.Tasks[1].IsMitigation));

        var ended = Assert.Single(await incidents.List("ended", default));
        Assert.Equal((closed.IncidentId, "ALL_MANDATORY_TASKS_VERIFIED", false), (ended.IncidentId, ended.StatusReason, ended.IsOpen));
        Assert.Equal(2, (await incidents.List("all", default)).Length);
        Assert.Equal(open.Title, (await incidents.Get(open.IncidentId, default)).Title);
        await Assert.ThrowsAsync<DomainException>(() => incidents.List("closed-ish", default));
        await Assert.ThrowsAsync<AppError>(() => new IncidentActions(store, new MonitoringSetupTests.User("STAFF"), new StaffTaskTests.Clock()).List(null, default));
    }
}
