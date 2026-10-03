using Microsoft.Extensions.DependencyInjection;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;

[Collection("SqlApi")]
[Trait("Category", "SqlIntegration")]
public sealed class MonitoringRuntimeSchemaApiFlowTests(SqlApiFixture fixture)
{
    [Fact]
    public async Task RuntimeEntitiesRoundTripAndPreserveUtc()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISetupStore>();
        var zone = new Zone { ZoneId=Guid.NewGuid(), Code="RT-"+Guid.NewGuid().ToString("N")[..8], Name="Runtime test", MapPolygon="[{\"x\":0,\"y\":0},{\"x\":1,\"y\":0},{\"x\":0,\"y\":1}]" };
        var camera = new Camera { CameraId=Guid.NewGuid(), Code="RT-"+Guid.NewGuid().ToString("N")[..8], Name="Runtime test" };
        var type = (await store.List<IncidentType>(t=>t.Code=="LONG_QUEUE")).Single();
        var incident = new Incident { IncidentId=Guid.NewGuid(), ZoneId=zone.ZoneId, IncidentTypeId=type.IncidentTypeId, TriggerCameraId=camera.CameraId, Title="Runtime queue" };
        var observed = new OperationalEvent { EventId=Guid.NewGuid(), CameraId=camera.CameraId, ZoneId=zone.ZoneId, IncidentId=incident.IncidentId,
            EventType="PEOPLE_COUNT", MetricValue=2, DetectedAt=DateTime.UtcNow, CreatedAt=DateTime.UtcNow,
            MetadataJson="{\"measurementMode\":\"PEOPLE_COUNT\"}", Status="INCIDENT_LINKED" };
        await store.Transaction(async()=> {
            var supermarket=(await store.List<Supermarket.Domain.Supermarket>()).FirstOrDefault();
            if(supermarket is null) { supermarket=new() { SupermarketId=Guid.NewGuid(), Code="TEST", Name="Runtime fixture" }; await store.Add(supermarket); }
            var floor=new Floor { FloorId=Guid.NewGuid(), SupermarketId=supermarket.SupermarketId, FloorNumber=1, Name="Runtime fixture" };
            await store.Add(floor); zone.FloorId=camera.FloorId=floor.FloorId;
            await store.Add(zone); await store.Add(camera); await store.Add(incident); await store.Add(observed); return true;
        });
        var saved=(await store.Find<Incident>(incident.IncidentId))!;
        Assert.Equal("DETECTED",saved.Status); Assert.Equal("AI_DETECTED",saved.SourceType);
        Assert.Null(saved.ReportedByUserId); Assert.Equal(DateTimeKind.Utc,saved.CreatedAt.Kind);
        var savedEvent=(await store.Find<OperationalEvent>(observed.EventId))!;
        Assert.Equal(2,savedEvent.MetricValue); Assert.Equal(observed.MetadataJson,savedEvent.MetadataJson);
        Assert.Equal(DateTimeKind.Utc,savedEvent.DetectedAt.Kind);
    }
}
