using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

public sealed class IncidentTypeCatalogDeleteTests
{
    [Fact]
    public async Task DeleteDeactivatesTypeWithoutRemovingIt()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var catalog = new IncidentTypeCatalog(store, new MonitoringSetupTests.User("ADMIN"));
        var created = await catalog.Create(new("SPILL", "Spill", null, true), default);

        await catalog.Delete(created.IncidentTypeId, default);
        await catalog.Delete(created.IncidentTypeId, default);

        var stored = Assert.Single(store.Values.OfType<IncidentType>());
        Assert.Equal(created.IncidentTypeId, stored.IncidentTypeId);
        Assert.Equal("INACTIVE", stored.Status);
    }

    [Fact]
    public async Task DeleteRequiresAdminAndExistingType()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() =>
            new IncidentTypeCatalog(store, new MonitoringSetupTests.User("OPERATOR")).Delete(Guid.NewGuid(), default))).Code);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() =>
            new IncidentTypeCatalog(store, new MonitoringSetupTests.User("ADMIN")).Delete(Guid.NewGuid(), default))).Code);
    }
}
