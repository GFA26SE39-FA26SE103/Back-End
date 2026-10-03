using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;
public sealed class MonitoringSnapshotReaderTests
{
    [Fact]
    public async Task ActiveSnapshotIsVersionedButHealthUpdatesDoNotRestartTracking()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule(); h.Configuration.Status="ACTIVE";
        var reader=new MonitoringSnapshotReader(h.Store);
        var snapshot=Assert.Single(await reader.ReadAll(default));
        Assert.Equal(h.Camera.CameraId,snapshot.CameraId); Assert.Single(snapshot.Zones);
        Assert.True(await reader.IsCurrent(snapshot,h.Zone.ZoneId,default));
        h.Camera.UpdatedAt=DateTime.UtcNow; h.Camera.LastSeenAt=DateTime.UtcNow; h.Camera.HealthStatus="ONLINE";
        Assert.Equal(snapshot.Fingerprint,Assert.Single(await reader.ReadAll(default)).Fingerprint);
        h.Configuration.UpdatedAt=h.Configuration.UpdatedAt.AddMilliseconds(1);
        Assert.False(await reader.IsCurrent(snapshot,h.Zone.ZoneId,default));
        h.Configuration.Status="INACTIVE";
        Assert.Empty(await reader.ReadAll(default));
    }
    [Fact]
    public async Task LegacyDensityActiveReturnsBlockedRuntimeInsteadOfPretendingToRun()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule("OVERCROWDING_CONGESTION","CROWD_DENSITY","PEOPLE_PER_M2"); h.Configuration.Status="ACTIVE";
        var snapshot=Assert.Single(await new MonitoringSnapshotReader(h.Store).ReadAll(default));
        Assert.Equal("RULE_RUNTIME_UNSUPPORTED",snapshot.IssueCode);
    }
}
