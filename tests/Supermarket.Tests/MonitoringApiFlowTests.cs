using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Supermarket.Infrastructure.Ai;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;

public sealed partial class ApiFlowTests
{
    [Fact]
    public async Task MonitoringRulesPersistReviewActivateAndProtectActiveConfiguration()
    {
        var ai = new ConfidenceHandler();
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICameraStream>();
            services.AddSingleton<ICameraStream>(new SuccessfulStream());
            services.RemoveAll<IAiPreviewClient>();
            services.AddScoped<IAiPreviewClient>(provider => new AiPreviewClient(new HttpClient(ai) { BaseAddress = new Uri("http://ai.example.test") }, Options.Create(new AiPreviewOptions { Confidence = .5m }), provider.GetRequiredService<ICredentialProtector>()));
        }));
        using var client = factory.CreateClient();
        var login = await Read<LoginResponse>(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(SqlApiFixture.AdminEmail, SqlApiFixture.AdminPassword)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var floor = await Floor(client);
        var camera = await Camera(client, floor);
        var zone = await Zone(client, floor);
        var catalog = (await client.GetFromJsonAsync<IncidentTypeView[]>("/api/incident-types"))!;
        Assert.Equal(4, catalog.Length);
        Assert.All(catalog, t => Assert.Equal("AI_DETECTED", t.SourceType));
        var queue = catalog.Single(t => t.Code == "LONG_QUEUE");
        var checkout = catalog.Single(t => t.Code == "CHECKOUT_CAPACITY_ISSUE");
        Assert.False(checkout.Supported);
        var input = new[] {
            new MonitoringRuleRequest(queue.IncidentTypeId, 3, 5, "PEOPLE", 0, 0),
            new MonitoringRuleRequest(checkout.IncidentTypeId, 0, 1, "PENDING", Enabled: false)
        };
        var route = $"/api/zones/{zone.ZoneId}/monitoring";
        var emptyCreate = await client.PutAsJsonAsync(route, new MonitoringRequest("Missing incidents", Rules: []));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, emptyCreate.StatusCode);
        Assert.Contains("RULES_REQUIRED", await emptyCreate.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        var draft = await Read<MonitoringConfigurationView>(await client.PutAsJsonAsync(route, new MonitoringRequest("Real SQL draft", .7123m, input)));
        Assert.Equal("DRAFT", draft.Status);
        var loaded = (await client.GetFromJsonAsync<MonitoringConfigurationView>(route))!;
        Assert.Equal(.7123m, loaded.ConfidenceThreshold);
        Assert.False(loaded.Rules.Single(r => r.IncidentTypeId == checkout.IncidentTypeId).Enabled);
        Assert.Equal(0, loaded.Rules.Single(r => r.IncidentTypeId == queue.IncidentTypeId).CooldownSec);
        var emptyUpdate = await client.PutAsJsonAsync(route, new MonitoringRequest("Remove every incident", Rules: [], ExpectedUpdatedAt: loaded.UpdatedAt));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, emptyUpdate.StatusCode);
        var afterRejectedSave = (await client.GetFromJsonAsync<MonitoringConfigurationView>(route))!;
        Assert.Equal(loaded.Name, afterRejectedSave.Name);
        Assert.Equal(loaded.UpdatedAt, afterRejectedSave.UpdatedAt);
        Assert.Equal(loaded.Rules.Length, afterRejectedSave.Rules.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(route, new MonitoringRequest("Stale", Rules: input, ExpectedUpdatedAt: draft.UpdatedAt.AddSeconds(-1)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(route, new MonitoringRequest("Invalid", Rules: [input[0], input[0]], ExpectedUpdatedAt: draft.UpdatedAt))).StatusCode);
        Assert.Equal("Real SQL draft", (await client.GetFromJsonAsync<MonitoringConfigurationView>(route))!.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route + "/activate", new MonitoringActivationRequest(draft.UpdatedAt))).StatusCode);
        var connection = $"/api/cameras/{camera.CameraId}/connection";
        await Read<ConnectionView>(await client.PutAsJsonAsync(connection, new ConnectionRequest("LIVE", "HTTP", "http://camera.example.test/video", Username: "private-user", Password: "private-camera-password")));
        await Read<ConnectionView>(await client.PostAsync(connection + "/test", null));
        await Read<ConnectionView>(await client.PostAsync(connection + "/enable", null));
        var mapping = $"/api/cameras/{camera.CameraId}/zones/{zone.ZoneId}";
        await Read<MappingView>(await client.PutAsJsonAsync(mapping, new MappingRequest(Triangle)));
        var reviewResponse = await client.GetAsync(route + "/review");
        var reviewText = await reviewResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-user", reviewText);
        Assert.DoesNotContain("private-camera-password", reviewText);
        Assert.DoesNotContain("http://camera", reviewText);
        var review = await Read<MonitoringReviewView>(reviewResponse);
        Assert.True(review.CanActivate);
        Assert.True(Assert.Single(review.Cameras).Ready);
        await Read<AiPreviewStatusView>(await client.PostAsync($"/api/cameras/{camera.CameraId}/ai-preview/start?zoneId={zone.ZoneId}", null));
        Assert.Equal(.7123m, ai.Confidence);
        Assert.Equal("DELETE", ai.Methods.First());
        // Readiness must be checked again, not trusted from an earlier review.
        await Read<ConnectionView>(await client.PostAsync(connection + "/disable", null));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route + "/activate", new MonitoringActivationRequest(review.Configuration.UpdatedAt))).StatusCode);
        await Read<ConnectionView>(await client.PostAsync(connection + "/enable", null));
        var active = await Read<MonitoringConfigurationView>(await client.PostAsJsonAsync(route + "/activate", new MonitoringActivationRequest(draft.UpdatedAt)));
        Assert.Equal("ACTIVE", active.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(route, new MonitoringRequest("Must not overwrite", Rules: input, ExpectedUpdatedAt: active.UpdatedAt))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(mapping)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(mapping, new MappingRequest([new(0,0),new(.5m,0),new(0,.5m)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"/api/zones/{zone.ZoneId}", new ZoneRequest(zone.Code, zone.Name, zone.ZoneType, Triangle, AreaM2: 20))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(connection, new ConnectionRequest("LIVE", "HTTP", "http://changed.example.test/video"))).StatusCode);
        var inactive = await Read<MonitoringConfigurationView>(await client.PostAsJsonAsync(route + "/deactivate", new MonitoringActivationRequest(active.UpdatedAt)));
        var replaced = await Read<MonitoringConfigurationView>(await client.PutAsJsonAsync(route, new MonitoringRequest("Only queue", 0, [input[0] with { WarningThreshold = 4, CriticalThreshold = 7 }], inactive.UpdatedAt)));
        Assert.Single(replaced.Rules);
        var reloaded = (await client.GetFromJsonAsync<MonitoringConfigurationView>(route))!;
        Assert.Single(reloaded.Rules);
        Assert.Equal(0, reloaded.ConfidenceThreshold);
        Assert.Equal(4, reloaded.Rules[0].WarningThreshold);
        Assert.Equal(loaded.Rules.Single(r => r.IncidentTypeId == queue.IncidentTypeId).RuleId, reloaded.Rules[0].RuleId);
    }

    [Fact]
    public async Task MonitoringRejectsMalformedBodiesInvalidRulesAndUnauthorizedRoles()
    {
        using var client = await Admin();
        var zone = await Zone(client, await Floor(client));
        var route = $"/api/zones/{zone.ZoneId}/monitoring";
        var type = (await client.GetFromJsonAsync<IncidentTypeView[]>("/api/incident-types"))!.Single(t => t.Code == "LONG_QUEUE");
        var rule = new MonitoringRuleRequest(type.IncidentTypeId, 3, 5, "PEOPLE");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(route, new { name = "Missing rules" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync(route + "/activate", null)).StatusCode);
        foreach (var invalid in new[] { rule with { CriticalThreshold = 3 }, rule with { ThresholdUnit = "MINUTES" }, rule with { SustainSec = -1 }, rule with { ParametersJson = "{invalid" }, rule with { WarningThreshold = 3.5m } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(route, new MonitoringRequest("Invalid", Rules: [invalid]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/incident-types")).StatusCode);
        var roles = (await client.GetFromJsonAsync<Role[]>("/api/roles"))!;
        var role = roles.Single(r => r.Name == "OPERATOR");
        var email = $"operator-monitor-{Guid.NewGuid():N}@test.example";
        await Read<UserView>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, "Operator-password-123!", "Operator", role.RoleId)));
        var login = await Read<LoginResponse>(await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Operator-password-123!")));
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        foreach (var endpoint in new[] { "/api/incident-types", route, route + "/review" }) Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PutAsJsonAsync(route, new MonitoringRequest("No", Rules: [rule]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync(route + "/activate", new MonitoringActivationRequest(DateTime.UtcNow))).StatusCode);
    }
    private sealed class SuccessfulStream : ICameraStream
    {
        public Task<ProbeResult> Test(CameraConnection connection, CancellationToken ct) => Task.FromResult(new ProbeResult(true, "FRAME_RECEIVED"));
        public Task<PreviewFrame> Preview(CameraConnection connection, CancellationToken ct) => Task.FromResult(new PreviewFrame([1], "image/jpeg"));
    }
    private sealed class ConfidenceHandler : HttpMessageHandler
    {
        public decimal? Confidence;
        public List<string> Methods = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/health") return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "UP" }) };
            Methods.Add(request.Method.Method);
            if (request.Content is not null)
            {
                using var body = System.Text.Json.JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                Confidence = body.RootElement.GetProperty("confidence").GetDecimal();
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { camera_id = Guid.Parse(path.Split('/')[2]), state = request.Method == HttpMethod.Delete ? "STOPPED" : "LIVE", updated_at = DateTime.UtcNow, frame_sequence = 0 }) };
        }
    }
}
