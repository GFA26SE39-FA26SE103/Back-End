using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;
public sealed partial class ApiFlowTests
{
    [Fact]
    public async Task RemovingRuleRetainsHistoricalEventAndSnapshot()
    {
        using var client=await Admin(); var floor=await Floor(client); var zone=await Zone(client,floor); var camera=await Camera(client,floor);
        var types=(await client.GetFromJsonAsync<IncidentTypeView[]>("/api/incident-types"))!;
        var queue=types.Single(t=>t.Code=="LONG_QUEUE"); var waiting=types.Single(t=>t.Code=="EXCESSIVE_WAITING_TIME");
        var rules=new[] { new MonitoringRuleRequest(queue.IncidentTypeId,1,2,"PEOPLE"), new MonitoringRuleRequest(waiting.IncidentTypeId,1,2,"MINUTES",Enabled:false) };
        var url=$"/api/zones/{zone.ZoneId}/monitoring";
        var saved=await Read<MonitoringConfigurationView>(await client.PutAsJsonAsync(url,new MonitoringRequest("History",Rules:rules)));
        var removed=saved.Rules.Single(r=>r.IncidentTypeId==waiting.IncidentTypeId);
        var evidence=new OperationalEvent { EventId=Guid.NewGuid(),CameraId=camera.CameraId,ZoneId=zone.ZoneId,RuleId=removed.RuleId,
            EventType="WAITING_TIME",MetricValue=2,DetectedAt=DateTime.UtcNow,CreatedAt=DateTime.UtcNow,MetadataJson="{\"warningThreshold\":1,\"configurationVersion\":\"old\"}" };
        using(var scope=fixture.Factory.Services.CreateScope()) {
            var store=scope.ServiceProvider.GetRequiredService<ISetupStore>();
            await store.Transaction(async()=> { await store.Add(evidence); return true; });
        }
        var changed=await Read<MonitoringConfigurationView>(await client.PutAsJsonAsync(url,new MonitoringRequest("After removal",Rules:[rules[0]],ExpectedUpdatedAt:saved.UpdatedAt)));
        Assert.Equal(saved.Rules.Single(r=>r.IncidentTypeId==queue.IncidentTypeId).RuleId,Assert.Single(changed.Rules).RuleId);
        using(var scope=fixture.Factory.Services.CreateScope()) {
            var row=(await scope.ServiceProvider.GetRequiredService<ISetupStore>().Find<OperationalEvent>(evidence.EventId))!;
            Assert.Null(row.RuleId); Assert.Equal(evidence.MetadataJson,row.MetadataJson);
        }
    }
}
