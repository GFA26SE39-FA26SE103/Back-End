using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

public sealed class CheckoutReviewTests
{
    [Fact]
    public async Task CheckoutOnlyReviewUsesLanePreviewAndCanActivateWithoutGenericRoi()
    {
        var f = new CheckoutFixture();
        f.H.Configuration.Status = "DRAFT";
        f.Mapping.StaffRoi = CheckoutFixture.OtherPolygon; // Independent polygons, not a generic zone ROI.
        var review = await f.Setup().Review(f.H.Zone.ZoneId, default);
        Assert.True(review.CanActivate);
        Assert.Empty(review.Issues);
        var camera = Assert.Single(review.Cameras);
        Assert.True(camera.Ready);
        Assert.Equal(new Point(0, 0), camera.RoiPolygon[0]);
        Assert.Equal("ACTIVE", (await f.Setup().Activate(f.H.Zone.ZoneId, true, default)).Status);
    }

    [Fact]
    public async Task ReviewExplainsContractDisabledInsteadOfClaimingOldAiSupportsCheckout()
    {
        var f = new CheckoutFixture();
        f.Options.AiContractEnabled = false;
        var review = await f.Setup().Review(f.H.Zone.ZoneId, default);
        Assert.False(review.CanActivate);
        Assert.Contains(review.Issues, i => i.Code == "CHECKOUT_AI_CONTRACT_DISABLED");
        Assert.DoesNotContain(review.Issues, i => i.Code == "RULE_RUNTIME_UNSUPPORTED");
        Assert.Equal("MONITORING_NOT_READY", (await Assert.ThrowsAsync<AppError>(() => f.Setup().Activate(f.H.Zone.ZoneId, true, default))).Code);
    }

    [Theory]
    [InlineData("zone")]
    [InlineData("floor")]
    [InlineData("camera")]
    [InlineData("test")]
    [InlineData("connection")]
    [InlineData("lane")]
    [InlineData("staff")]
    [InlineData("queue-source")]
    [InlineData("two-queue-sources")]
    public async Task ReviewRevalidatesEveryCheckoutSource(string reason)
    {
        var f = new CheckoutFixture();
        f.MakeInvalid(reason);
        var review = await f.Setup().Review(f.H.Zone.ZoneId, default);
        Assert.False(review.CanActivate);
        Assert.NotEmpty(review.Issues);
        Assert.Equal("MONITORING_NOT_READY", (await Assert.ThrowsAsync<AppError>(() => f.Setup().Activate(f.H.Zone.ZoneId, true, default))).Code);
    }

    [Fact]
    public async Task ReviewRejectsMissingCountersAndMissingMappings()
    {
        var f = new CheckoutFixture();
        f.Mapping.IsEnabled = false;
        Assert.False((await f.Setup().Review(f.H.Zone.ZoneId, default)).CanActivate);
        f.Counter.IsEnabled = false;
        Assert.False((await f.Setup().Review(f.H.Zone.ZoneId, default)).CanActivate);
    }

    [Fact]
    public async Task MixedReviewStillRequiresExactlyOneGenericCamera()
    {
        var f = new CheckoutFixture();
        f.H.AddRule();
        Assert.False((await f.Setup().Review(f.H.Zone.ZoneId, default)).CanActivate);
        f.Store.Values.Add(f.GenericMapping);
        f.AddSource(queueSource: false);
        var ready = await f.Setup().Review(f.H.Zone.ZoneId, default);
        Assert.True(ready.CanActivate);
        Assert.DoesNotContain(ready.Issues, i => i.Code == "MEASUREMENT_SOURCE_AMBIGUOUS");
    }

    [Fact]
    public async Task GenericReviewNeverQueriesCheckoutSchema()
    {
        var f = new CheckoutFixture();
        f.Rule.Enabled = false;
        f.H.AddRule();
        f.Store.Values.Add(f.GenericMapping);
        f.Store.RejectCheckoutReads = true;
        Assert.True((await f.Setup().Review(f.H.Zone.ZoneId, default)).CanActivate);
        Assert.Equal(0, f.Store.CheckoutReads);
        Assert.Equal(0, f.Schema.Calls);
    }

    [Fact]
    public async Task MissingSchemaIsAReviewIssueWithoutQueryingAbsentTables()
    {
        var f = new CheckoutFixture();
        f.Schema.IsReady = false;
        f.Store.RejectCheckoutReads = true;
        var review = await f.Setup().Review(f.H.Zone.ZoneId, default);
        Assert.False(review.CanActivate);
        Assert.Contains(review.Issues, i => i.Code == "CHECKOUT_SCHEMA_NOT_READY");
        Assert.Equal(0, f.Store.CheckoutReads);
    }

    [Fact]
    public async Task InvalidParametersAreReportedWithoutPretendingTheSourcesSupplyThem()
    {
        var f = new CheckoutFixture();
        f.Rule.ParametersJson = "{}";
        var review = await f.Setup().Review(f.H.Zone.ZoneId, default);
        Assert.False(review.CanActivate);
        Assert.Contains(review.Issues, i => i.Code == "INVALID_CHECKOUT_PARAMETERS");
    }

    [Fact]
    public async Task CheckoutActivationDoesNotTreatUnrelatedActiveGenericConfigAsCheckoutOwner()
    {
        var f = new CheckoutFixture();
        f.H.Configuration.Status = "DRAFT";
        // This camera is mapped to a checkout counter in another zone, but its active configuration has no checkout rule.
        var otherZone = new Zone { ZoneId = Guid.NewGuid(), FloorId = f.H.Zone.FloorId, Status = "ACTIVE", ZoneType = "CHECKOUT" };
        var otherConfig = new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = otherZone.ZoneId, Status = "ACTIVE" };
        var otherCounter = new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = otherZone.ZoneId, IsEnabled = true };
        f.Store.Values.AddRange([otherZone, otherConfig, otherCounter,
            new CameraCheckoutMapping { MappingId = Guid.NewGuid(), CameraId = f.H.Camera.CameraId,
                CheckoutCounterId = otherCounter.CheckoutCounterId, IsEnabled = true, IsQueueSource = true,
                LaneRoi = CheckoutFixture.Polygon, StaffRoi = CheckoutFixture.Polygon }]);
        Assert.Equal("MONITORING_STOP_PENDING", (await Assert.ThrowsAsync<AppError>(() =>
            f.Setup(new Owner()).Activate(f.H.Zone.ZoneId, true, default))).Code);
    }

    [Fact]
    public async Task GenericActivationRecognizesActiveCheckoutOwnerOnTheSameCamera()
    {
        var f = new CheckoutFixture();
        var generic = new MonitoringSetupTests.Harness();
        generic.AddRule();
        var genericMapping = generic.Store.Values.OfType<CameraZoneMapping>().Single();
        genericMapping.CameraId = f.H.Camera.CameraId;
        generic.Zone.FloorId = f.H.Zone.FloorId;
        f.Store.Values.AddRange(generic.Store.Values.Where(e => e is not Camera && e is not CameraConnection));
        Assert.Equal("ACTIVE", (await f.Setup(new Owner()).Activate(generic.Zone.ZoneId, true, default)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckoutCatalogHasExplicitUnitNoInventedDefaultsAndUsesContractFlag(bool enabled)
    {
        var f = new CheckoutFixture();
        f.Options.AiContractEnabled = enabled;
        var type = Assert.Single(await f.Setup().IncidentTypes(default));
        Assert.Equal("PEOPLE_PER_STAFFED_COUNTER", type.ThresholdUnit);
        Assert.Equal(enabled, type.Supported);
        Assert.Null(type.DefaultWarningThreshold);
        Assert.Null(type.DefaultCriticalThreshold);
        var option = Assert.Single(type.MeasurementOptions!);
        Assert.Equal("PEOPLE_PER_STAFFED_COUNTER", option.Unit);
        Assert.Equal(enabled, option.Supported);
        Assert.Null(option.DefaultWarning);
        Assert.Null(option.DefaultCritical);
    }

    [Fact]
    public async Task CheckoutCatalogDoesNotBecomeStaffReportedWhenEventCatalogIsMissing()
    {
        var f = new CheckoutFixture();
        f.Store.Values.RemoveAll(e => e is OperationalEventType);
        var type = Assert.Single(await f.Setup().IncidentTypes(default));
        Assert.Equal("AI_DETECTED", type.SourceType);
        Assert.Equal("PEOPLE_PER_STAFFED_COUNTER", type.ThresholdUnit);
        Assert.False(type.Supported);
        Assert.Null(type.DefaultWarningThreshold);
        Assert.Null(type.DefaultCriticalThreshold);
    }

    [Fact]
    public async Task ActivationRechecksCheckoutPairAfterSuccessfulReview()
    {
        var f = new CheckoutFixture();
        Assert.True((await f.Setup().Review(f.H.Zone.ZoneId, default)).CanActivate);
        f.Mapping.StaffRoi = "[]";
        Assert.Equal("MONITORING_NOT_READY", (await Assert.ThrowsAsync<AppError>(() =>
            f.Setup().Activate(f.H.Zone.ZoneId, true, default))).Code);
    }

    [Fact]
    public async Task CheckoutActivationHonorsStoppingOwnerWithoutGenericMapping()
    {
        var f = new CheckoutFixture();
        f.H.Configuration.Status = "DRAFT";
        Assert.Equal("MONITORING_STOP_PENDING", (await Assert.ThrowsAsync<AppError>(() =>
            f.Setup(new Owner()).Activate(f.H.Zone.ZoneId, true, default))).Code);
        f.H.Configuration.Status = "ACTIVE";
        Assert.Equal("ACTIVE", (await f.Setup(new Owner()).Activate(f.H.Zone.ZoneId, true, default)).Status);
    }

    private sealed class Owner : IMonitoringSessionOwnership { public bool IsMonitoringOwned(Guid cameraId) => true; }
}
