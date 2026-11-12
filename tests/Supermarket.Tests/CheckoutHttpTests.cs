using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutHttpTests
{
    [Fact]
    public async Task AdminCanCreateConfigureReadDisableAndDeleteACounterWithoutChangingGenericRoi()
    {
        await using var w=new World(); w.Client.DefaultRequestHeaders.Add("X-Test-Role","ADMIN");
        var empty=await w.Client.GetFromJsonAsync<CheckoutCounterView[]>($"/api/zones/{w.Zone.ZoneId}/checkout-counters");
        Assert.Empty(empty!);
        var create=await w.Client.PostAsJsonAsync($"/api/zones/{w.Zone.ZoneId}/checkout-counters",new CheckoutCounterRequest("C01","Lane one"));
        Assert.Equal(HttpStatusCode.Created,create.StatusCode);
        var counter=(await create.Content.ReadFromJsonAsync<CheckoutCounterView>())!;
        Assert.False(counter.IsEnabled);
        Assert.Equal($"/api/checkout-counters/{counter.CheckoutCounterId}",create.Headers.Location!.OriginalString);
        Point[] roi=[new(0,0),new(1,0),new(0,1)];
        var mappingResponse=await w.Client.PutAsJsonAsync($"/api/checkout-counters/{counter.CheckoutCounterId}/camera-mappings/{w.Camera.CameraId}",
            new CheckoutMappingRequest(roi,roi,true,true,ServiceRoi:roi));
        Assert.Equal(HttpStatusCode.OK,mappingResponse.StatusCode);
        var mapping=(await mappingResponse.Content.ReadFromJsonAsync<CheckoutMappingView>())!;
        var enabledResponse=await w.Client.PatchAsJsonAsync($"/api/checkout-counters/{counter.CheckoutCounterId}",
            new CheckoutCounterRequest(counter.Code,counter.Name,true,counter.UpdatedAt));
        Assert.Equal(HttpStatusCode.OK,enabledResponse.StatusCode);
        var enabled=(await enabledResponse.Content.ReadFromJsonAsync<CheckoutCounterView>())!;
        Assert.True(enabled.IsEnabled);
        Assert.Single((await w.Client.GetFromJsonAsync<CheckoutMappingView[]>($"/api/cameras/{w.Camera.CameraId}/checkout-mappings"))!);
        Assert.Single((await w.Client.GetFromJsonAsync<CheckoutMappingView[]>($"/api/checkout-counters/{counter.CheckoutCounterId}/camera-mappings"))!);
        Assert.Equal(counter.CheckoutCounterId,(await w.Client.GetFromJsonAsync<CheckoutCounterView>($"/api/checkout-counters/{counter.CheckoutCounterId}"))!.CheckoutCounterId);
        var removeMapping=$"/api/checkout-counters/{counter.CheckoutCounterId}/camera-mappings/{w.Camera.CameraId}?expectedUpdatedAt={Uri.EscapeDataString(mapping.UpdatedAt.ToString("O"))}";
        Assert.Equal(HttpStatusCode.Conflict,(await w.Client.DeleteAsync(removeMapping)).StatusCode);
        var disabledResponse=await w.Client.PatchAsJsonAsync($"/api/checkout-counters/{counter.CheckoutCounterId}",
            new CheckoutCounterRequest(counter.Code,counter.Name,false,enabled.UpdatedAt));
        var disabled=(await disabledResponse.Content.ReadFromJsonAsync<CheckoutCounterView>())!;
        Assert.Equal(HttpStatusCode.NoContent,(await w.Client.DeleteAsync(removeMapping)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await w.Client.DeleteAsync($"/api/checkout-counters/{counter.CheckoutCounterId}?expectedUpdatedAt={Uri.EscapeDataString(disabled.UpdatedAt.ToString("O"))}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await w.Client.GetAsync($"/api/checkout-counters/{counter.CheckoutCounterId}")).StatusCode);
        Assert.Equal("original-generic-roi",Assert.Single(w.Store.Values.OfType<CameraZoneMapping>()).RoiPolygon);
    }

    [Theory,InlineData(null,401),InlineData("STAFF",403),InlineData("OPERATOR",403),InlineData("MANAGER",403)]
    public async Task OnlyAdminCanWriteCheckoutConfiguration(string? role,int expected)
    {
        await using var w=new World(); if(role is not null) w.Client.DefaultRequestHeaders.Add("X-Test-Role",role);
        var response=await w.Client.PostAsJsonAsync($"/api/zones/{w.Zone.ZoneId}/checkout-counters",new CheckoutCounterRequest("C01","Checkout"));
        Assert.Equal((HttpStatusCode)expected,response.StatusCode);
        Assert.Empty(w.Store.Values.OfType<CheckoutCounter>());
    }

    [Fact]
    public async Task MissingSchemaReturnsSanitized503WithoutAnyWrites()
    {
        await using var w=new World(false); w.Client.DefaultRequestHeaders.Add("X-Test-Role","ADMIN");
        var response=await w.Client.PostAsJsonAsync($"/api/zones/{w.Zone.ZoneId}/checkout-counters",new CheckoutCounterRequest("C01","Checkout"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.Contains("CHECKOUT_SCHEMA_NOT_READY",await response.Content.ReadAsStringAsync());
        Assert.Empty(w.Store.Values.OfType<CheckoutCounter>());
    }

    [Fact]
    public async Task OpenApiPublishesCheckoutRoutesAndVersionedPolygonContract()
    {
        await using var w=new World();
        var json=await w.Client.GetStringAsync("/swagger/v1/swagger.json");
        using var document=JsonDocument.Parse(json); var paths=document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/zones/{zoneId}/checkout-counters",out _));
        Assert.True(paths.TryGetProperty("/api/checkout-counters/{counterId}/camera-mappings/{cameraId}",out _));
        Assert.True(paths.TryGetProperty("/api/zones/{zoneId}/checkout-runtime",out _));
        var properties=document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CheckoutMappingRequest").GetProperty("properties");
        Assert.True(properties.TryGetProperty("laneRoi",out _)); Assert.True(properties.TryGetProperty("staffRoi",out _));
        Assert.True(properties.TryGetProperty("expectedUpdatedAt",out _));
        Assert.DoesNotContain("credentialSecretRef",properties.GetRawText());
        // Export only on an explicit developer invocation; ordinary tests never write docs/artifacts.
        var output=Environment.GetEnvironmentVariable("CHECKOUT_OPENAPI_OUTPUT");
        if(!string.IsNullOrEmpty(output)) await File.WriteAllTextAsync(output,json);
    }

    private sealed class World:IAsyncDisposable
    {
        public MonitoringSetupTests.MemoryStore Store {get;}=new();
        public Zone Zone {get;}=new(){ZoneId=Guid.NewGuid(),FloorId=Guid.NewGuid(),ZoneType="CHECKOUT",Name="Checkout",Status="ACTIVE"};
        public Camera Camera {get;}
        public HttpClient Client {get;}
        private readonly SetupOverviewTests.Factory root;
        private readonly WebApplicationFactory<Program> factory;
        public World(bool schemaReady=true)
        {
            Camera=new(){CameraId=Guid.NewGuid(),FloorId=Zone.FloorId,Status="ACTIVE"};
            Store.Values.AddRange([Zone,Camera,new Floor{FloorId=Zone.FloorId},
                new CameraConnection{ConnectionId=Guid.NewGuid(),CameraId=Camera.CameraId,SourceType="LIVE",Protocol="HTTP",StreamUri="http://camera.test/video",IsEnabled=true,LastTestResult="SUCCESS",LastTestedAt=DateTime.UtcNow},
                new CameraZoneMapping{CameraZoneId=Guid.NewGuid(),CameraId=Camera.CameraId,ZoneId=Zone.ZoneId,RoiPolygon="original-generic-roi"}]);
            root=new(Store);
            factory=root.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
                ["Swagger:Enabled"]="true",["Monitoring:Enabled"]="false",["TaskOffers:Enabled"]="false",["ShiftSwaps:Enabled"]="false",
                ["SlaMonitor:Enabled"]="false",["Notifications:Push:Enabled"]="false"})).ConfigureTestServices(s=>{
                    s.RemoveAll<ICheckoutSchema>();s.AddSingleton<ICheckoutSchema>(new Schema(schemaReady));
                    s.RemoveAll<ICheckoutConfigurationQueries>();s.AddSingleton<ICheckoutConfigurationQueries>(new Queries(Store));
                }));
            Client=factory.CreateClient();
        }
        public async ValueTask DisposeAsync(){Client.Dispose();await factory.DisposeAsync();await root.DisposeAsync();}
    }
    private sealed class Schema(bool ready):ICheckoutSchema{public Task<bool> Ready(CancellationToken ct)=>Task.FromResult(ready);}
    private sealed class Queries(MonitoringSetupTests.MemoryStore store):ICheckoutConfigurationQueries
    {
        public Task<List<CheckoutCounter>> CountersByZone(Guid id,int limit,CancellationToken ct)=>Task.FromResult(store.Values.OfType<CheckoutCounter>().Where(c=>c.ZoneId==id).Take(limit+1).ToList());
        public Task<List<CameraCheckoutMapping>> MappingsByCamera(Guid id,int limit,CancellationToken ct)=>Task.FromResult(store.Values.OfType<CameraCheckoutMapping>().Where(m=>m.CameraId==id).Take(limit+1).ToList());
        public Task<List<CameraCheckoutMapping>> MappingsByCounter(Guid id,int limit,CancellationToken ct)=>Task.FromResult(store.Values.OfType<CameraCheckoutMapping>().Where(m=>m.CheckoutCounterId==id).Take(limit+1).ToList());
    }
}
