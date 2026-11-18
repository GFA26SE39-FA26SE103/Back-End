using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed partial class ApiFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalyticsExportCommitsAuditRows(bool rendererFails)
    {
        using var admin = await Admin();
        var roles = await Read<Role[]>(await admin.GetAsync("/api/roles"));
        var managerRole = roles.Single(role => role.Name == "MANAGER");
        var email = $"analytics-{Guid.NewGuid():N}@mf01.test";
        const string password = "Analytics-test-123!";
        var manager = await Read<UserView>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest(email, password, "Analytics test Manager", managerRole.RoleId)));
        var floor = await Floor(admin);
        Point[] rectangle = [new(.1m, .1m), new(.8m, .1m), new(.8m, .8m), new(.1m, .8m)];
        var zone = await Read<ZoneView>(await admin.PostAsJsonAsync($"/api/floors/{floor}/zones",
            new ZoneRequest(Guid.NewGuid().ToString("N"), "Analytics test Zone", "CHECKOUT", rectangle)));

        await using var api = fixture.Factory.WithWebHostBuilder(builder =>
        {
            if (rendererFails)
                builder.ConfigureTestServices(services => services.Replace(
                    ServiceDescriptor.Singleton<IAnalyticsReportRenderer>(new FailingAnalyticsRenderer())));
        });
        using var client = api.CreateClient();
        var login = await Read<LoginResponse>(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var to = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var from = to.AddDays(-1);
        var formats = new[] { "csv", "xlsx", "pdf" };

        foreach (var format in formats)
        {
            using var response = await client.GetAsync($"/api/manager/analytics/export?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&zoneId={zone.ZoneId}&format={format}");
            Assert.Equal(rendererFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, response.StatusCode);
            if (rendererFails)
                Assert.Contains("REPORT_GENERATION_FAILED", await response.Content.ReadAsStringAsync());
            else
                Assert.EndsWith($".{format}", response.Content.Headers.ContentDisposition?.FileNameStar);
        }

        await using var scope = api.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISetupStore>();
        var auditRows = await store.List<AuditLog>(row => row.UserId == manager.UserId && row.Action == "EXPORT_ANALYTICS_REPORT");
        Assert.Equal(formats.Length, auditRows.Count);
        var auditedFormats = auditRows.Select(row =>
        {
            Assert.Equal(rendererFails ? "FAILED" : "SUCCESS", row.ResultStatus);
            Assert.Equal(rendererFails ? "REPORT_GENERATION_FAILED" : null, row.Reason);
            using var metadata = JsonDocument.Parse(row.NewValuesJson!);
            Assert.Equal(zone.ZoneId, metadata.RootElement.GetProperty("zoneId").GetGuid());
            Assert.Equal(from.ToString("yyyy-MM-dd"), metadata.RootElement.GetProperty("from").GetString());
            Assert.Equal(to.ToString("yyyy-MM-dd"), metadata.RootElement.GetProperty("to").GetString());
            return metadata.RootElement.GetProperty("format").GetString();
        });
        Assert.Equal(formats.Order(), auditedFormats.Order());
    }

    private sealed class FailingAnalyticsRenderer : IAnalyticsReportRenderer
    {
        public AnalyticsExportFile Render(AnalyticsReportData report, string format)
            => throw new InvalidOperationException("Test renderer failure.");
    }
}
