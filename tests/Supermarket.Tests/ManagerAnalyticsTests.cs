using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
using Task = System.Threading.Tasks.Task;
using Zone = Supermarket.Domain.Zone;

namespace Supermarket.Tests;

public sealed class ManagerAnalyticsTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 5, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ZoneId = Guid.NewGuid();
    private sealed class Clock : IClock { public DateTime UtcNow => Now; }
    private sealed class Queries(params AnalyticsBucket[] rows) : IAnalyticsQueries
    {
        public AnalyticsWindow? Window { get; private set; }
        public bool Fail { get; set; }
        public Task<List<AnalyticsBucket>> Read(AnalyticsWindow window, CancellationToken ct)
        {
            Window = window;
            if (Fail) throw new AppError("ANALYTICS_UNAVAILABLE", "Analytics data is temporarily unavailable. Retry later.", 503);
            return Task.FromResult(rows.ToList());
        }
    }
    private static MonitoringSetupTests.MemoryStore Store() => new() { Values = { new Zone { ZoneId = ZoneId, Name = "Checkout" } } };
    private static ManagerAnalytics Service(Queries queries, string role = "MANAGER", StoreCalendar? calendar = null)
        => new(queries, Store(), new MonitoringSetupTests.User(role), new Clock(), calendar ?? new(StoreCalendar.Zone(null)));
    private static AnalyticsBucket Bucket(string domain, string status, int count = 1, int day = 0, string? priority = null,
        int responseSamples = 0, double responseSeconds = 0, int resolutionSamples = 0, double resolutionSeconds = 0,
        string responseSla = "EXCLUDED", string resolutionSla = "EXCLUDED")
        => new(domain, day, ZoneId, status, priority, count, responseSamples, responseSeconds, resolutionSamples, resolutionSeconds, responseSla, resolutionSla);

    [Fact]
    public async Task DefaultWindowUsesSevenLocalDaysAndUtcBoundaries()
    {
        var queries = new Queries();
        var result = await Service(queries).Overview(new(), default);
        Assert.Equal(new DateOnly(2026, 10, 31), result.Period.From);
        Assert.Equal(new DateOnly(2026, 11, 6), result.Period.To);
        Assert.Equal(new DateTime(2026, 10, 30, 17, 0, 0, DateTimeKind.Utc), queries.Window!.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 6, 17, 0, 0, DateTimeKind.Utc), queries.Window.ToUtcExclusive);
        Assert.Equal(Now, result.Period.GeneratedAt);
    }

    [Theory]
    [InlineData("2026-11-07", "2026-11-07")]
    [InlineData("2026-11-06", "2026-11-05")]
    [InlineData("2026-07-01", "2026-11-06")]
    [InlineData("0001-01-01", "0001-01-01")]
    public async Task InvalidRangesFailBeforeQuerying(string from, string to)
    {
        var queries = new Queries();
        var error = await Assert.ThrowsAsync<AppError>(() => Service(queries).Overview(new(DateOnly.Parse(from), DateOnly.Parse(to)), default));
        Assert.Equal(400, error.Status);
        Assert.Null(queries.Window);
    }

    [Fact]
    public async Task MissingZoneIs404AndEmptyZoneIs400()
    {
        var service = Service(new());
        Assert.Equal(404, (await Assert.ThrowsAsync<AppError>(() => service.Overview(new(ZoneId: Guid.NewGuid()), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<AppError>(() => service.Overview(new(ZoneId: Guid.Empty), default))).Status);
    }

    [Fact]
    public async Task EmptyPeriodReturnsZerosButDoesNotInventDurationsOrSlaRates()
    {
        var result = await Service(new()).Overview(new(), default);
        Assert.Equal(0, result.Summary.Events.Total);
        Assert.Equal(0, result.Summary.Tasks.Total);
        Assert.Null(result.Summary.Tasks.ResponseTime.AverageSeconds);
        Assert.Null(result.Summary.Tasks.ResolutionSla.MetPercent);
        Assert.Equal(0, result.Summary.Tasks.ByStatus["VERIFIED"]);
    }

    [Fact]
    public async Task AggregatesWeightedDurationsAndExplicitSlaDenominators()
    {
        var queries = new Queries(
            Bucket("EVENT", "ACTIVE", 2, priority: "CRITICAL"), Bucket("EVENT", "CLEARED", resolutionSamples: 1, resolutionSeconds: 300),
            Bucket("INCIDENT", "DISMISSED", 4), Bucket("INCIDENT", "CLOSED", resolutionSamples: 1, resolutionSeconds: 600),
            Bucket("TASK", "VERIFIED", 3, responseSamples: 3, responseSeconds: 90, resolutionSamples: 3, resolutionSeconds: 900, responseSla: "MET", resolutionSla: "MET"),
            Bucket("TASK", "IN_PROGRESS", responseSamples: 1, responseSeconds: 90, responseSla: "BREACHED", resolutionSla: "PENDING"),
            Bucket("TASK", "OFFERED", 2, responseSla: "UNAVAILABLE", resolutionSla: "UNAVAILABLE"), Bucket("TASK", "CANCELLED"));
        var result = (await Service(queries).Overview(new(), default)).Summary;
        Assert.Equal(3, result.Events.Total);
        Assert.Equal(2, result.Events.LatestCritical);
        Assert.Equal(4, result.Incidents.Dismissed);
        Assert.Equal(5, result.Incidents.Recorded);
        Assert.Equal(1, result.Incidents.Total); // Dismissed records never inflate incident KPIs.
        Assert.Equal(600, result.Incidents.ResolutionTime.AverageSeconds);
        Assert.Equal(7, result.Tasks.Total);
        Assert.Equal(45, result.Tasks.ResponseTime.AverageSeconds); // 180 seconds / four samples, not average of bucket averages.
        Assert.Equal(300, result.Tasks.CompletionTime.AverageSeconds);
        Assert.Equal(new AnalyticsSla(4, 3, 1, 0, 2, 75), result.Tasks.ResponseSla);
        Assert.Equal(new AnalyticsSla(3, 3, 0, 1, 2, 100), result.Tasks.ResolutionSla);
    }

    [Fact]
    public async Task TrendsFillMissingDaysAndZoneViewIncludesEmptyConfiguredZones()
    {
        var service = Service(new(Bucket("EVENT", "ACTIVE", 2, day: 1)));
        var trends = await service.Trends(new(new(2026, 11, 4), new(2026, 11, 6), ZoneId), default);
        Assert.Equal(new[] { 0, 2, 0 }, trends.Days.Select(day => day.Summary.Events.Total));
        var zones = await Service(new()).Zones(new(), default);
        Assert.Equal("Checkout", Assert.Single(zones.Zones).ZoneName);
        Assert.Equal(0, zones.Zones[0].Summary.Tasks.Total);
    }

    [Fact]
    public async Task CalendarDayBucketsRespectDstChanges()
    {
        var queries = new Queries();
        var calendar = new StoreCalendar(StoreCalendar.Zone("America/New_York"));
        await Service(queries, calendar: calendar).Overview(new(new(2026, 11, 1), new(2026, 11, 2)), default);
        Assert.Equal(TimeSpan.FromHours(25), queries.Window!.DayStartsUtc[1] - queries.Window.DayStartsUtc[0]);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    [InlineData("OPERATOR", HttpStatusCode.Forbidden)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("MANAGER", HttpStatusCode.OK)]
    public async Task HttpRoutesEnforceManagerRoleAndDisableCaching(string? role, HttpStatusCode expected)
    {
        var queries = new Queries();
        await using var baseFactory = new SetupOverviewTests.Factory(Store());
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAnalyticsQueries>(); services.AddSingleton<IAnalyticsQueries>(queries);
            services.RemoveAll<IClock>(); services.AddSingleton<IClock, Clock>();
        }));
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        foreach (var path in new[] { "overview", "trends", "zones" })
        {
            var response = await client.GetAsync($"/api/manager/analytics/{path}?from=2026-11-05&to=2026-11-06&zoneId={ZoneId}");
            Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.OK) Assert.True(response.Headers.CacheControl?.NoStore);
        }
        if (expected == HttpStatusCode.OK) Assert.Equal(ZoneId, queries.Window!.ZoneId);
    }

    [Fact]
    public async Task UnavailableDataRemainsAnHttpErrorRatherThanSuccessfulZeros()
    {
        await using var baseFactory = new SetupOverviewTests.Factory(Store());
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAnalyticsQueries>(); services.AddSingleton<IAnalyticsQueries>(new Queries { Fail = true });
        }));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Role", "MANAGER");
        var response = await client.GetAsync("/api/manager/analytics/overview");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("ANALYTICS_UNAVAILABLE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AllAggregationsTranslateToGroupedSqlWithoutTheTaskFeedCap()
    {
        var queries = new Queries(); await Service(queries).Overview(new(), default);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=AnalyticsTranslationOnly;Integrated Security=True;TrustServerCertificate=True").Options);
        var repository = new EfAnalyticsQueries(db);
        foreach (var sql in new[] { repository.Events(queries.Window!).ToQueryString(), repository.Incidents(queries.Window!).ToQueryString(), repository.Tasks(queries.Window!).ToQueryString() })
        {
            Assert.Contains("GROUP BY", sql);
            Assert.Contains("SUM(", sql);
            Assert.Contains("WHERE", sql);
            Assert.Contains("CASE", sql);
            Assert.DoesNotContain("TOP(", sql);
        }
    }

    [Fact]
    public async Task MaximumRangeUsesSqlServerCompatibleCaseDepth()
    {
        var queries = new Queries();
        await Service(queries).Overview(new(new DateOnly(2026, 11, 6).AddDays(-92), new(2026, 11, 6)), default);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=AnalyticsTranslationOnly;Integrated Security=True;TrustServerCertificate=True").Options);
        var sql = new EfAnalyticsQueries(db).Tasks(queries.Window!).ToQueryString();
        var depth = 0; var maximum = 0;
        foreach (System.Text.RegularExpressions.Match token in System.Text.RegularExpressions.Regex.Matches(sql, @"\b(CASE|END)\b"))
        {
            depth += token.Value == "CASE" ? 1 : -1;
            maximum = Math.Max(maximum, depth);
        }
        Assert.InRange(maximum, 1, 10);
        Assert.Equal(0, depth);
    }

    [Fact]
    public async Task SwaggerDefinesAnalyticsQueryAndResponseContracts()
    {
        await using var baseFactory = new SetupOverviewTests.Factory(Store());
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        foreach (var path in new[] { "overview", "trends", "zones" })
        {
            var operation = document.RootElement.GetProperty("paths").GetProperty($"/api/manager/analytics/{path}").GetProperty("get");
            Assert.Equal(new[] { "From", "To", "ZoneId" }, operation.GetProperty("parameters").EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString()).ToArray());
            Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
        }
    }
}
