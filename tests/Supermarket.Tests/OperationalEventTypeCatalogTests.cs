using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

public sealed class OperationalEventTypeCatalogTests
{
    [Fact]
    public async Task AdminCanCreateListUpdateAndDeactivateType()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var catalog = new OperationalEventTypeCatalog(store, new MonitoringSetupTests.User("ADMIN"));
        var created = await catalog.Create(new("custom_event", "Custom", null, "PEOPLE_COUNT", "INACTIVE"), default);
        Assert.Equal("CUSTOM_EVENT", created.Code);
        Assert.Equal(created, Assert.Single(await catalog.List(default)));

        var updated = await catalog.Update(created.EventTypeId, new("Renamed", "Details", "PEOPLE_COUNT", "INACTIVE"), default);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("Details", updated.Description);
        await catalog.Delete(created.EventTypeId, default);
        Assert.Equal("INACTIVE", Assert.Single(await catalog.List(default)).Status);
    }

    [Fact]
    public async Task RejectsDuplicateCodeAndUnsafeDeactivation()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var catalog = new OperationalEventTypeCatalog(store, new MonitoringSetupTests.User("ADMIN"));
        var created = await catalog.Create(new("LONG_QUEUE", "Queue", null, "QUEUE_LENGTH", "ACTIVE"), default);
        Assert.Equal("DUPLICATE", (await Assert.ThrowsAsync<AppError>(() => catalog.Create(new("long_queue", "Again", null, "QUEUE_LENGTH"), default))).Code);
        store.Values.Add(new MonitoringRule { EventTypeId = created.EventTypeId, Enabled = true });
        Assert.Equal("OPERATIONAL_EVENT_TYPE_IN_USE", (await Assert.ThrowsAsync<DomainException>(() => catalog.Delete(created.EventTypeId, default))).Code);
        Assert.Equal("ACTIVE", Assert.Single(await catalog.List(default)).Status);
    }

    [Fact]
    public async Task RejectsUnsupportedActiveTypeAndNonAdminWrites()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var catalog = new OperationalEventTypeCatalog(store, new MonitoringSetupTests.User("ADMIN"));
        Assert.Equal("RULE_RUNTIME_UNSUPPORTED", (await Assert.ThrowsAsync<DomainException>(() => catalog.Create(new("CUSTOM", "Custom", null, "PEOPLE_COUNT", "ACTIVE"), default))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => new OperationalEventTypeCatalog(store, new MonitoringSetupTests.User("OPERATOR")).Create(new("CUSTOM", "Custom", null, "PEOPLE_COUNT"), default))).Code);
    }
}
