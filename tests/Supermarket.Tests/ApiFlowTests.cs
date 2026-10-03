using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;

[Collection("SqlApi")]
public sealed partial class ApiFlowTests(SqlApiFixture fixture)
{
    private static Point[] Triangle => [new(.1m, .1m), new(.8m, .1m), new(.1m, .8m)];
    private async Task<HttpClient> Admin()
    {
        var client = fixture.Factory.CreateClient();
        var result = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(SqlApiFixture.AdminEmail, SqlApiFixture.AdminPassword));
        result.EnsureSuccessStatusCode();
        var login = (await result.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private async Task<Guid> Floor(HttpClient client)
    {
        var stores = await client.GetFromJsonAsync<JsonElement[]>("/api/supermarkets");
        Guid store;
        if (stores!.Length == 0)
            store = (await Read<JsonElement>(await client.PostAsJsonAsync("/api/supermarkets", new StoreRequest("STORE", "Demo supermarket", null)))).GetProperty("supermarketId").GetGuid();
        else
            store = stores[0].GetProperty("supermarketId").GetGuid();
        return (await Read<Floor>(await client.PostAsJsonAsync($"/api/supermarkets/{store}/floors", new FloorRequest(Random.Shared.Next(1, 1000000), "Floor", "https://assets.example.test/map.png", 960, 540)))).FloorId;
    }
    private async Task<Camera> Camera(HttpClient client, Guid floor) => await Read<Camera>(await client.PostAsJsonAsync($"/api/floors/{floor}/cameras", new CameraRequest(Guid.NewGuid().ToString("N"), "Camera", null, null, null, DateTime.UtcNow, DateTime.UtcNow.AddYears(2), .3m, .4m, 45, "ACTIVE")));
    private async Task<ZoneView> Zone(HttpClient client, Guid floor) => await Read<ZoneView>(await client.PostAsJsonAsync($"/api/floors/{floor}/zones", new ZoneRequest(Guid.NewGuid().ToString("N"), "Checkout", "CHECKOUT", Triangle)));
    [Fact]
    public async Task StoreFloorZonePersistsAndValidates()
    {
        using var client = await Admin();
        var floor = await Floor(client);
        var zone = await Zone(client, floor);
        var reload = (await client.GetFromJsonAsync<ZoneView>($"/api/zones/{zone.ZoneId}"))!;
        Assert.Equal(Triangle, reload.MapPolygon);
        var duplicate = await client.PostAsJsonAsync($"/api/floors/{floor}/zones", new ZoneRequest(zone.Code, "Duplicate", null, Triangle));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var invalid = await client.PatchAsJsonAsync($"/api/floors/{floor}", new FloorRequest(10, "Floor", null, 100, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var updated = await Read<ZoneView>(await client.PatchAsJsonAsync($"/api/zones/{zone.ZoneId}", new ZoneRequest(zone.Code, "Updated", zone.ZoneType, Triangle)));
        Assert.True(updated.UpdatedAt > reload.UpdatedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/supermarkets", new StoreRequest("SECOND", "Second", null))).StatusCode);
    }
    [Fact]
    public async Task ZoneColorAndAreaPersistAndValidate()
    {
        using var client = await Admin();
        var floor = await Floor(client);
        var created = await Read<ZoneView>(await client.PostAsJsonAsync($"/api/floors/{floor}/zones", new ZoneRequest(
            Guid.NewGuid().ToString("N"), "Produce", "SALES", Triangle, ColorHex: "#22C55E", AreaM2: 125.50m)));

        Assert.Equal("#22C55E", created.ColorHex);
        Assert.Equal(125.50m, created.AreaM2);
        var reload = (await client.GetFromJsonAsync<ZoneView>($"/api/zones/{created.ZoneId}"))!;
        Assert.Equal("#22C55E", reload.ColorHex);
        Assert.Equal(125.50m, reload.AreaM2);

        var invalidColor = await client.PostAsJsonAsync($"/api/floors/{floor}/zones", new ZoneRequest(
            Guid.NewGuid().ToString("N"), "Invalid color", null, Triangle, ColorHex: "green"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidColor.StatusCode);
        var invalidArea = await client.PostAsJsonAsync($"/api/floors/{floor}/zones", new ZoneRequest(
            Guid.NewGuid().ToString("N"), "Invalid area", null, Triangle, AreaM2: 0));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidArea.StatusCode);
    }
    [Fact]
    public async Task ConnectionTestPreviewEnableReloadAndReset()
    {
        using var client = await Admin();
        var camera = await Camera(client, await Floor(client));
        var route = $"/api/cameras/{camera.CameraId}/connection";
        var configured = await Read<ConnectionView>(await client.PutAsJsonAsync(route, new ConnectionRequest("DEMO", "HTTP", "demo://camera/main", Username: "camera-user", Password: "Camera-secret-123")));
        Assert.False(configured.IsEnabled);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync(route + "/enable", null)).StatusCode);
        Assert.Equal("SUCCESS", (await Read<ConnectionView>(await client.PostAsync(route + "/test", null))).LastTestResult);
        var preview = await client.GetAsync($"/api/cameras/{camera.CameraId}/preview");
        Assert.Equal("image/svg+xml", preview.Content.Headers.ContentType!.MediaType);
        Assert.True((await Read<ConnectionView>(await client.PostAsync(route + "/enable", null))).IsEnabled);
        var reloaded = await client.GetFromJsonAsync<ConnectionView>(route);
        Assert.True(reloaded!.IsEnabled);
        var body = await client.GetStringAsync(route);
        Assert.DoesNotContain("Camera-secret-123", body);
        Assert.DoesNotContain("credentialSecretRef", body);
        Assert.DoesNotContain("camera-user", body);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT credential_secret_ref FROM dbo.CameraConnection WHERE camera_id=@id", connection);
            command.Parameters.AddWithValue("@id", camera.CameraId);
            var encrypted = (string)(await command.ExecuteScalarAsync())!;
            Assert.DoesNotContain("Camera-secret-123", encrypted);
        }
        var reset = await Read<ConnectionView>(await client.PutAsJsonAsync(route, new ConnectionRequest("DEMO", "HTTP", "demo://camera/changed")));
        Assert.False(reset.IsEnabled);
        Assert.Null(reset.LastTestResult);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync(route + "/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(route, new ConnectionRequest("LIVE", "RTSP", "rtsp://admin:secret@camera/main"))).StatusCode);
    }
    [Fact]
    public async Task MappingIsIdempotentAndRejectsCrossFloor()
    {
        using var client = await Admin();
        var floor = await Floor(client);
        var camera = await Camera(client, floor);
        var zone = await Zone(client, floor);
        var other = await Zone(client, await Floor(client));
        var route = $"/api/cameras/{camera.CameraId}/zones";
        var first = await Read<MappingView>(await client.PutAsJsonAsync(route + $"/{zone.ZoneId}", new MappingRequest(Triangle)));
        var second = await Read<MappingView>(await client.PutAsJsonAsync(route + $"/{zone.ZoneId}", new MappingRequest(Triangle)));
        Assert.Equal(first.CameraZoneId, second.CameraZoneId);
        var reload = await client.GetFromJsonAsync<MappingView[]>(route);
        Assert.Single(reload!);
        Assert.Equal(Triangle, reload![0].RoiPolygon);
        var invalid = await client.PutAsJsonAsync(route + $"/{other.ZoneId}", new MappingRequest(Triangle));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Contains("CROSS_FLOOR_MAPPING", await invalid.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task DemoCameraFailureRecoveryAndMonitoringSourceRejection()
    {
        using var client = await Admin();
        var floor = await Floor(client);
        var camera = await Camera(client, floor);
        var zone = await Zone(client, floor);
        var monitoring = $"/api/zones/{zone.ZoneId}/monitoring";
        var connection = $"/api/cameras/{camera.CameraId}/connection";
        var incidentTypes = (await client.GetFromJsonAsync<IncidentTypeView[]>("/api/incident-types"))!;
        var queueType = incidentTypes.Single(t => t.Code == "LONG_QUEUE");
        var draft = await Read<MonitoringConfigurationView>(await client.PutAsJsonAsync(monitoring, new MonitoringRequest("Queue monitoring",
            Rules: [new MonitoringRuleRequest(queueType.IncidentTypeId, 3, 5, "PEOPLE", 30, 300)])));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(monitoring + "/activate", new MonitoringActivationRequest(draft.UpdatedAt))).StatusCode);
        await Read<ConnectionView>(await client.PutAsJsonAsync(connection, new ConnectionRequest("DEMO", "HTTP", "demo://camera/main")));
        await Read<ConnectionView>(await client.PostAsync(connection + "/test", null));
        await Read<ConnectionView>(await client.PostAsync(connection + "/enable", null));
        await Read<MappingView>(await client.PutAsJsonAsync($"/api/cameras/{camera.CameraId}/zones/{zone.ZoneId}", new MappingRequest(Triangle)));
        var review = (await client.GetFromJsonAsync<MonitoringReviewView>(monitoring + "/review"))!;
        Assert.False(review.CanActivate);
        Assert.Contains(review.Cameras.Single().Issues, i => i.Code == "AI_SOURCE_UNSUPPORTED");
        var check = $"/api/cameras/{camera.CameraId}/health/check";
        Assert.Equal("ONLINE", (await Read<Camera>(await client.PostAsync(check, null))).HealthStatus);
        Assert.True((await client.PostAsync($"/api/demo/cameras/{camera.CameraId}/state?online=false", null)).IsSuccessStatusCode);
        Assert.Equal("OFFLINE", (await Read<Camera>(await client.PostAsync(check, null))).HealthStatus);
        await Read<Camera>(await client.PostAsync(check, null));
        var events = (await client.GetFromJsonAsync<CameraHealthEvent[]>($"/api/camera-health-events?cameraId={camera.CameraId}"))!;
        var health = Assert.Single(events);
        Assert.Equal("OPEN", health.Status);
        Assert.Equal("INVESTIGATING", (await Read<CameraHealthEvent>(await client.PostAsync($"/api/camera-health-events/{health.HealthEventId}/investigate", null))).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/camera-health-events/{health.HealthEventId}/resolve", new ResolveRequest("premature"))).StatusCode);
        await client.PostAsync($"/api/demo/cameras/{camera.CameraId}/state?online=true", null);
        await Read<Camera>(await client.PostAsync(check, null));
        var resolved = await Read<CameraHealthEvent>(await client.PostAsJsonAsync($"/api/camera-health-events/{health.HealthEventId}/resolve", new ResolveRequest("Cable restored")));
        Assert.Equal("RESOLVED", resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.NotNull(resolved.InvestigatedByUserId);
        Assert.True((await client.DeleteAsync($"/api/cameras/{camera.CameraId}/zones/{zone.ZoneId}")).IsSuccessStatusCode);
    }
    [Fact]
    public async Task AuthenticationRbacLastAdminAndTokenRevocation()
    {
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(SqlApiFixture.AdminEmail, "wrong"))).StatusCode);
        using var admin = await Admin();
        var me = (await admin.GetFromJsonAsync<UserView>("/api/auth/me"))!;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsync($"/api/users/{me.UserId}/disable", null)).StatusCode);
        var roles = (await admin.GetFromJsonAsync<Role[]>("/api/roles"))!;
        var staffRole = roles.Single(r => r.Name == "STAFF");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PatchAsJsonAsync($"/api/users/{me.UserId}", new UpdateUserRequest(me.FullName, staffRole.RoleId))).StatusCode);
        var email = $"staff-{Guid.NewGuid():N}@test.example";
        var staff = await Read<UserView>(await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(email, "Staff-password-123!", "Staff", staffRole.RoleId)));
        var login = await Read<LoginResponse>(await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Staff-password-123!")));
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("/api/users")).StatusCode);
        await Read<UserView>(await admin.PostAsync($"/api/users/{staff.UserId}/disable", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task OpenApiDocumentsAllSetupCapabilities()
    {
        using var client = fixture.Factory.CreateClient();
        var spec = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/cameras/{id}/connection/test", spec);
        Assert.Contains("/api/zones/{zoneId}/monitoring/activate", spec);
        Assert.Contains("/api/camera-health-events", spec);
        Assert.DoesNotContain("CredentialSecretRef", spec);
        Assert.DoesNotContain("PasswordHash", spec);
    }
}
