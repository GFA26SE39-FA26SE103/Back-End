using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class AfterReviewContractTests
{
    [Fact]
    public void PublicViewsExposeOperationalPriorityTaskSlaAndOfferAuditFields()
    {
        Assert.NotNull(typeof(MonitoringRuleRuntimeView).GetProperty("OperationalEventPriority"));
        Assert.NotNull(typeof(StaffTaskView).GetProperty("SlaPriorityCode"));
        Assert.NotNull(typeof(StaffTaskView).GetProperty("ResolutionDeadlineAt"));
        Assert.NotNull(typeof(TaskOfferView).GetProperty("StaffUserId"));
        Assert.NotNull(typeof(TaskOfferView).GetProperty("ExpiresAt"));
    }

    [Fact]
    public async Task IncidentCatalogIncludesStaffReportedTypes()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var id = Guid.NewGuid();
        store.Values.Add(new IncidentType { IncidentTypeId = id, Code = "CLEANLINESS_ISSUE", Name = "Cleanliness issue", RequiresBeforePhoto = true, Status = "ACTIVE" });

        var rows = await new MonitoringSetup(store, new Current(Guid.NewGuid(), "ADMIN")).IncidentTypes(default);

        var row = Assert.Single(rows);
        Assert.Equal(id, row.IncidentTypeId);
        Assert.Equal("STAFF_REPORTED", row.SourceType);
        Assert.True(row.RequiresBeforePhoto);
    }

    [Fact]
    public async Task IncidentSlaPolicyAdminCreatesAndVersionsOnePolicyPerType()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var type = new IncidentType { IncidentTypeId = Guid.NewGuid(), Code = "LONG_QUEUE", Name = "Long queue", Status = "ACTIVE" };
        store.Values.Add(type);
        var service = new IncidentSlaPolicies(store, new Current(Guid.NewGuid(), "ADMIN"));

        var created = await service.Save(type.IncidentTypeId,
            new IncidentSlaPolicySaveRequest(null, true, "HIGH", 120, 900, 60, 80), default);
        var updated = await service.Save(type.IncidentTypeId,
            new IncidentSlaPolicySaveRequest(created.Version, false, "CRITICAL", 60, 600, 30, 75), default);

        Assert.Equal(1, created.Version);
        Assert.Equal(2, updated.Version);
        Assert.False(updated.IsEnabled);
        Assert.Single(await service.List(default));
    }

    private sealed record Current(Guid UserId, string Role) : ICurrentUser;
}
