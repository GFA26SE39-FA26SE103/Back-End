using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;

[Collection("SqlApi")]
public sealed class ConcurrencyTests(SqlApiFixture fixture)
{
    [Fact]
    public async Task DoesNotApplyTestResultToChangedConnection()
    {
        var probe = new PausedProbe();
        await using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<ICameraStream>(); s.AddSingleton<ICameraStream>(probe); }));
        using var client = factory.CreateClient();
        var login = (await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(SqlApiFixture.AdminEmail, SqlApiFixture.AdminPassword))).Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var stores = (await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/supermarkets"))!;
        Guid storeId;
        if (stores.Length == 0)
            storeId = (await (await client.PostAsJsonAsync("/api/supermarkets", new StoreRequest("STORE", "Demo", null))).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("supermarketId").GetGuid();
        else
            storeId = stores[0].GetProperty("supermarketId").GetGuid();
        var floor = (await (await client.PostAsJsonAsync($"/api/supermarkets/{storeId}/floors", new FloorRequest(Random.Shared.Next(1000000, 2000000), "Concurrency", null, null, null))).Content.ReadFromJsonAsync<Floor>())!;
        var camera = (await (await client.PostAsJsonAsync($"/api/floors/{floor.FloorId}/cameras", new CameraRequest(Guid.NewGuid().ToString("N"), "Concurrency", null, null, null, DateTime.UtcNow, DateTime.UtcNow.AddYears(1), null, null, null, "ACTIVE"))).Content.ReadFromJsonAsync<Camera>())!;
        var route = $"/api/cameras/{camera.CameraId}/connection";
        (await client.PutAsJsonAsync(route, new ConnectionRequest("DEMO", "HTTP", "demo://camera/first"))).EnsureSuccessStatusCode();
        var testing = client.PostAsync(route + "/test", null);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            (await client.PutAsJsonAsync(route, new ConnectionRequest("DEMO", "HTTP", "demo://camera/second"))).EnsureSuccessStatusCode();
        }
        finally { probe.Release.TrySetResult(); }
        var response = await testing;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("CONNECTION_CHANGED", await response.Content.ReadAsStringAsync());
        var current = (await client.GetFromJsonAsync<ConnectionView>(route))!;
        Assert.Null(current.LastTestResult);
        Assert.False(current.IsEnabled);
    }
    [Fact]
    public async Task ConcurrentAdminDisablePreservesAnActiveAdmin()
    {
        var isolated = new SqlApiFixture();
        await isolated.InitializeAsync();
        try
        {
            using var first = isolated.Factory.CreateClient();
            var original = (await (await first.PostAsJsonAsync("/api/auth/login", new LoginRequest(SqlApiFixture.AdminEmail, SqlApiFixture.AdminPassword))).Content.ReadFromJsonAsync<LoginResponse>())!;
            first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", original.AccessToken);
            var roles = (await first.GetFromJsonAsync<Role[]>("/api/roles"))!;
            var secondUser = (await (await first.PostAsJsonAsync("/api/users", new CreateUserRequest("admin2@test.example", "Second-admin-123!", "Admin 2", roles.Single(r => r.Name == "ADMIN").RoleId))).Content.ReadFromJsonAsync<UserView>())!;
            using var second = isolated.Factory.CreateClient();
            var secondLogin = (await (await second.PostAsJsonAsync("/api/auth/login", new LoginRequest(secondUser.Email, "Second-admin-123!"))).Content.ReadFromJsonAsync<LoginResponse>())!;
            second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondLogin.AccessToken);
            var results = await Task.WhenAll(first.PostAsync($"/api/users/{original.User.UserId}/disable", null), second.PostAsync($"/api/users/{secondUser.UserId}/disable", null));
            Assert.Single(results, r => r.IsSuccessStatusCode);
            Assert.Contains(results, r => r.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);
            await using var db = new Microsoft.Data.SqlClient.SqlConnection(isolated.ConnectionString);
            await db.OpenAsync();
            await using var query = new Microsoft.Data.SqlClient.SqlCommand("SELECT COUNT(*) FROM dbo.UserAccount u JOIN dbo.Role r ON u.role_id=r.role_id WHERE r.name='ADMIN' AND u.status='ACTIVE'", db);
            Assert.Equal(1, (int)(await query.ExecuteScalarAsync())!);
        }
        finally { await isolated.DisposeAsync(); }
    }
    private sealed class PausedProbe : ICameraStream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ProbeResult> Test(CameraConnection connection, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new(true, "FRAME_RECEIVED");
        }
        public Task<PreviewFrame> Preview(CameraConnection connection, CancellationToken ct) => throw new NotSupportedException();
    }
}
