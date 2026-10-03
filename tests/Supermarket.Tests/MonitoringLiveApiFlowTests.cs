using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;
public sealed partial class ApiFlowTests
{
    [Fact]
    public async Task MonitoringLiveReturnsNotActivatedRatherThanFakeMetricsAndEnforcesRbac()
    {
        using var client=await Admin(); var camera=await Camera(client,await Floor(client));
        var url=$"/api/cameras/{camera.CameraId}";
        var runtime=await Read<MonitoringCameraRuntimeView>(await client.GetAsync(url+"/monitoring-runtime"));
        Assert.Equal("NO_ACTIVE_CONFIGURATION",runtime.Reason); Assert.Null(runtime.SourceElapsedMs);
        Assert.Empty((await Read<IncidentFeedView>(await client.GetAsync(url+"/incidents"))).Items);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(url+"/incidents?limit=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(url+$"/incidents?afterIncidentId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/cameras/{Guid.NewGuid()}/monitoring-runtime")).StatusCode);
        using var anonymous=fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(url+"/monitoring-runtime")).StatusCode);
        var roles=(await client.GetFromJsonAsync<Role[]>("/api/roles"))!;
        foreach(var role in new[] {"OPERATOR","STAFF","MANAGER"})
        {
            var email=$"runtime-{Guid.NewGuid():N}@test.example";
            await Read<UserView>(await client.PostAsJsonAsync("/api/users",new CreateUserRequest(email,"Password-testing-123!",role,roles.Single(r=>r.Name==role).RoleId)));
            var login=await Read<LoginResponse>(await anonymous.PostAsJsonAsync("/api/auth/login",new LoginRequest(email,"Password-testing-123!")));
            anonymous.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.AccessToken);
            Assert.Equal(role=="OPERATOR"?HttpStatusCode.OK:HttpStatusCode.Forbidden,(await anonymous.GetAsync(url+"/monitoring-runtime")).StatusCode);
            Assert.Equal(role=="OPERATOR"?HttpStatusCode.OK:HttpStatusCode.Forbidden,(await anonymous.GetAsync(url+"/incidents")).StatusCode);
        }
    }

    [Fact]
    public async Task MissingRuntimeSchemaIs503NotEmptyFeedOrSqlDetail()
    {
        var isolated=new SqlApiFixture();
        try {
            await isolated.InitializeAsync(); using var client=await isolated.AdminClient();
            var floor=await Floor(client); var camera=await Camera(client,floor); var zone=await Zone(client,floor);
            var queue=(await client.GetFromJsonAsync<IncidentTypeView[]>("/api/incident-types"))!.Single(t=>t.Code=="LONG_QUEUE");
            var route=$"/api/zones/{zone.ZoneId}/monitoring";
            var draft=await Read<MonitoringConfigurationView>(await client.PutAsJsonAsync(route,new MonitoringRequest("Before schema missing",Rules:[new(queue.IncidentTypeId,1,2,"PEOPLE")])));
            await using(var connection=new SqlConnection(isolated.ConnectionString)) {
                await connection.OpenAsync();
                // This fixture belongs exclusively to this test, never shared Dev.
                await using var command=new SqlCommand("DROP TABLE dbo.OperationalEvent; DROP TABLE dbo.Incident;",connection);
                await command.ExecuteNonQueryAsync();
            }
            foreach(var endpoint in new[] {"monitoring-runtime","incidents"}) {
                var response=await client.GetAsync($"/api/cameras/{camera.CameraId}/{endpoint}");
                Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
                var body=await response.Content.ReadAsStringAsync(); Assert.Contains("MONITORING_SCHEMA_NOT_READY",body); Assert.DoesNotContain("SqlException",body);
            }
            var removal=await client.PutAsJsonAsync(route,new MonitoringRequest("Remove rule",Rules:[],ExpectedUpdatedAt:draft.UpdatedAt));
            Assert.Equal(HttpStatusCode.ServiceUnavailable,removal.StatusCode);
            Assert.Contains("MONITORING_SCHEMA_NOT_READY",await removal.Content.ReadAsStringAsync());
        } finally { await isolated.DisposeAsync(); }
    }
}
