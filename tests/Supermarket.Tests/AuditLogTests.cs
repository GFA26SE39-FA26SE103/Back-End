using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
using Task = System.Threading.Tasks.Task;
using AuditRow = Supermarket.Infrastructure.Persistence.Scaffolded.AuditLog;
using UserRow = Supermarket.Infrastructure.Persistence.Scaffolded.UserAccount;

namespace Supermarket.Tests;

/// <summary>FE-12 §3.13.3: Admin audit-log inspection is read-only, filtered and paged on the server.</summary>
public sealed class AuditLogTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid IncidentId = Guid.NewGuid();
    private sealed class Clock : IClock { public DateTime UtcNow => Now; }
    private sealed class Queries : IAuditLogQueries
    {
        public AuditLogWindow? Window { get; private set; }
        public bool Fail { get; set; }
        public Task<AuditLogPage> List(AuditLogWindow window, string timeZone, CancellationToken ct)
        {
            if (Fail) throw new AppError("AUDIT_DATA_UNAVAILABLE", "unavailable", 503);
            Window = window;
            return Task.FromResult(new AuditLogPage([Row()], 1, window.Page, window.PageSize, window.GeneratedAt, timeZone));
        }
        public Task<AuditLogView?> Get(Guid auditLogId, CancellationToken ct) => Task.FromResult<AuditLogView?>(auditLogId == Row().AuditLogId ? Row() : null);
        public Task<AuditLogFacets> Facets(CancellationToken ct)
            => Task.FromResult(new AuditLogFacets(["DISMISS_INCIDENT"], ["Incident"], ["SUCCESS"], [new AuditLogActor(Guid.Empty, "Huy", "OPERATOR")]));
        internal static AuditLogView Row() => new(Guid.Parse("11111111-1111-1111-1111-111111111111"), Now, null, "Huy", "huy@test", "OPERATOR",
            "DISMISS_INCIDENT", "Incident", IncidentId, "Duplicate", "SUCCESS", null, """{"status":"IN_PROGRESS"}""", """{"status":"DISMISSED"}""");
    }
    private static AuditLogs Service(Queries queries, string role = "ADMIN") => new(queries, new MonitoringSetupTests.User(role), new Clock(), new(StoreCalendar.Zone(null)));

    [Fact]
    public async Task AdminQueryIsNormalizedToStoreDaysAndRejectsInvalidInput()
    {
        var queries = new Queries();
        var page = await Service(queries).List(new(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12), Action: " dismiss_incident ",
            EntityType: " Incident ", Result: "success", Search: "  dup  ", Page: 2, PageSize: 20), default);
        Assert.Equal(1, page.TotalCount);
        var window = queries.Window!;
        Assert.Equal(new DateTime(2026, 11, 9, 17, 0, 0, DateTimeKind.Utc), window.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc), window.ToUtcExclusive);
        Assert.Equal(("DISMISS_INCIDENT", "Incident", "SUCCESS", "dup", 2, 20), (window.Action, window.EntityType, window.Result, window.Search, window.Page, window.PageSize));

        foreach (var invalid in new AuditLogQuery[] { new(PageSize: 101), new(Page: 0), new(From: new DateOnly(2026, 11, 12), To: new DateOnly(2026, 11, 11)),
                     new(To: new DateOnly(2026, 11, 13)), new(UserId: Guid.Empty) })
            Assert.Equal("INVALID_AUDIT_QUERY", (await Assert.ThrowsAsync<AppError>(() => Service(queries).List(invalid, default))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => Service(queries, "OPERATOR").List(new(), default))).Code);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => Service(queries).Get(Guid.NewGuid(), default))).Code);
    }

    [Fact]
    public void FilterAppliesEveryCriterionBeforePaging()
    {
        var actor = new UserRow { UserId = Guid.NewGuid(), FullName = "Huy Nguyen", Email = "huy@test" };
        AuditRow Row(DateTime at, string action = "DISMISS_INCIDENT", string type = "Incident", Guid? entity = null, string? result = "SUCCESS", string? reason = "Duplicate report", UserRow? user = null)
            => new() { AuditLogId = Guid.NewGuid(), CreatedAt = at, Action = action, EntityType = type, EntityId = entity ?? IncidentId, ResultStatus = result,
                Reason = reason, UserId = user?.UserId, User = user };
        var start = Now.AddDays(-1);
        var rows = new[]
        {
            Row(start, user: actor), Row(start.AddTicks(-1), user: actor), Row(Now.AddTicks(1), user: actor),
            Row(start, action: "MITIGATE_INCIDENT", user: actor), Row(start, type: "SystemSetting", user: actor), Row(start, entity: Guid.NewGuid(), user: actor),
            Row(start, result: "FAILED", user: actor), Row(start, reason: "Peak hour", user: actor), Row(start)
        }.AsQueryable();
        var window = new AuditLogWindow(start, null, actor.UserId, "DISMISS_INCIDENT", "Incident", IncidentId, "SUCCESS", "Duplicate", Now, 1, 50);
        Assert.Single(EfAuditLogQueries.Filter(rows, window).ToList());
        Assert.Equal(2, EfAuditLogQueries.Filter(rows, window with { Search = null }).Count());
        Assert.Equal(2, EfAuditLogQueries.Filter(rows, window with { Search = "Huy" }).Count());
        Assert.Equal(9, EfAuditLogQueries.Filter(rows, new AuditLogWindow(null, null, null, null, null, null, null, null, Now.AddDays(1), 1, 50)).Count());
    }

    [Fact]
    public void PageTranslatesToNewestFirstBoundedSql()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=localhost;Database=TranslationOnly;Integrated Security=True").Options);
        var sql = new EfAuditLogQueries(db).Page(new(null, null, null, null, null, null, null, "x", Now, 3, 25)).ToQueryString();
        Assert.Contains("ORDER BY", sql); Assert.Contains("DESC", sql); Assert.Contains("OFFSET", sql); Assert.Contains("FETCH NEXT", sql);
        Assert.DoesNotContain("PasswordHash", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("OPERATOR", HttpStatusCode.Forbidden)]
    [InlineData("MANAGER", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.OK)]
    public async Task HttpReadsAreAdminOnlyAndNotCached(string? role, HttpStatusCode expected)
    {
        await using var root = new SetupOverviewTests.Factory(new MonitoringSetupTests.MemoryStore());
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IAuditLogQueries>(); services.AddSingleton<IAuditLogQueries, Queries>(); }));
        using var client = factory.CreateClient(); if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        foreach (var path in new[] { "/api/admin/audit-logs?action=DISMISS_INCIDENT", "/api/admin/audit-logs/facets", $"/api/admin/audit-logs/{Queries.Row().AuditLogId}" })
        {
            var response = await client.GetAsync(path); Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.OK) Assert.True(response.Headers.CacheControl?.NoStore);
        }
        if (expected == HttpStatusCode.OK)
        {
            var page = (await client.GetFromJsonAsync<AuditLogPage>("/api/admin/audit-logs"))!;
            Assert.Equal("DISMISSED", System.Text.Json.JsonDocument.Parse(Assert.Single(page.Items).NewValuesJson!).RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task UnavailableRepositoryIsAnErrorAndNotAnEmptyLog()
    {
        await using var root = new SetupOverviewTests.Factory(new MonitoringSetupTests.MemoryStore());
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IAuditLogQueries>(); services.AddSingleton<IAuditLogQueries>(new Queries { Fail = true }); }));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/admin/audit-logs")).StatusCode);
    }
}
