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
using EventRow = Supermarket.Infrastructure.Persistence.Scaffolded.OperationalEvent;

namespace Supermarket.Tests;

public sealed class OperationalEventHistoryTests
{
    private static readonly Guid ZoneId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : IClock { public DateTime UtcNow => Now; }
    private sealed class Queries : IOperationalEventQueries
    {
        public OperationalEventWindow? Window { get; private set; }
        public bool Fail { get; set; }
        public Task<OperationalEventPage> List(OperationalEventWindow window, string timeZone, CancellationToken ct)
        {
            Window = window;
            if (Fail) throw new AppError("EVENT_DATA_UNAVAILABLE", "Operational event data is temporarily unavailable. Retry later.", 503);
            return Task.FromResult(new OperationalEventPage([View()], 1, window.Page, window.PageSize, window.GeneratedAt, timeZone));
        }
        public Task<OperationalEventDetail?> Get(Guid id, CancellationToken ct) => Task.FromResult(id == EventId
            ? new OperationalEventDetail(View(), [new(Guid.NewGuid(), "Assist queue", "IN_PROGRESS", "CRITICAL", "Staff A", Now)], false) : null);
    }
    private static OperationalEventView View() => new(EventId, Guid.NewGuid(), "LONG_QUEUE", "Long queue", ZoneId, "Checkout", Guid.NewGuid(),
        "CAM-01", "Checkout camera", "WARNING", "CLEARED", 4, "QUEUE_LENGTH", Now.AddHours(-1), Now, Now, Now, 1);
    private static MonitoringSetupTests.MemoryStore Store() => new() { Values = { new Zone { ZoneId = ZoneId, Name = "Checkout", Status = "INACTIVE" } } };
    private static OperationalEventHistory Service(Queries queries, string role = "OPERATOR") => new(queries, Store(), new MonitoringSetupTests.User(role), new Clock(), new(StoreCalendar.Zone(null)));

    [Fact]
    public async Task DefaultsIncludeAllStatusesPrioritiesAndHistory()
    {
        var queries = new Queries(); var page = await Service(queries).List(new(), default);
        Assert.Equal("ALL", queries.Window!.Status); Assert.Equal("ALL", queries.Window.Priority);
        Assert.Null(queries.Window.FromUtc); Assert.Null(queries.Window.ToUtcExclusive);
        Assert.Equal(1, page.TotalCount); Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task CombinedFiltersUseStoreMidnightBoundsAndAllowInactiveZones()
    {
        var queries = new Queries();
        await Service(queries).List(new("cleared", "warning", ZoneId, new(2026, 11, 1), new(2026, 11, 6), 2, 20), default);
        Assert.Equal("CLEARED", queries.Window!.Status); Assert.Equal("WARNING", queries.Window.Priority);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), queries.Window.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 6, 17, 0, 0, DateTimeKind.Utc), queries.Window.ToUtcExclusive);
        Assert.Equal(2, queries.Window.Page); Assert.Equal(ZoneId, queries.Window.ZoneId);
        Assert.Equal("INACTIVE", Assert.Single(await Service(queries).Zones(default)).Status);
    }

    [Theory]
    [InlineData("INVALID", "ALL", 1, 50)]
    [InlineData("ALL", "HIGH", 1, 50)]
    [InlineData("ALL", "ALL", 0, 50)]
    [InlineData("ALL", "ALL", 1, 101)]
    public async Task InvalidFiltersFailBeforeReading(string status, string priority, int page, int size)
    {
        var queries = new Queries();
        Assert.Equal(400, (await Assert.ThrowsAsync<AppError>(() => Service(queries).List(new(status, priority, Page: page, PageSize: size), default))).Status);
        Assert.Null(queries.Window);
    }

    [Fact]
    public async Task RejectsFutureReversedEmptyAndUnknownZoneQueries()
    {
        var service = Service(new());
        foreach (var query in new[] { new OperationalEventQuery(From: new(2026, 11, 7)), new OperationalEventQuery(From: new(2026, 11, 6), To: new(2026, 11, 1)), new OperationalEventQuery(ZoneId: Guid.Empty) })
            Assert.Equal(400, (await Assert.ThrowsAsync<AppError>(() => service.List(query, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppError>(() => service.List(new(ZoneId: Guid.NewGuid()), default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppError>(() => service.Get(Guid.NewGuid(), default))).Status);
    }

    [Fact]
    public void PersistenceFiltersIncludeStartExcludeEndAndDoNotConfuseEventAndSlaPriority()
    {
        var start = Now.AddDays(-2); var end = Now.AddDays(-1);
        EventRow Row(DateTime at, Guid? zone = null, string priority = "CRITICAL", string status = "ACTIVE") => new() { EventId = Guid.NewGuid(), ZoneId = zone ?? ZoneId, DetectedAt = at, Priority = priority, Status = status };
        var rows = new[] { Row(start.AddTicks(-1)), Row(start), Row(end.AddTicks(-1)), Row(end), Row(start, Guid.NewGuid()), Row(start, priority: "WARNING"), Row(start, status: "CLEARED") }.AsQueryable();
        var filter = new OperationalEventWindow("ACTIVE", "CRITICAL", ZoneId, start, end, Now, 1, 20);
        Assert.Equal(2, EfOperationalEventQueries.Filter(rows, filter).Count());
        Assert.Equal(3, EfOperationalEventQueries.Filter(rows, filter with { Status = "ALL" }).Count());
        Assert.Equal(4, EfOperationalEventQueries.Filter(rows, filter with { Status = "ALL", Priority = "ALL" }).Count());
        Assert.Equal(5, EfOperationalEventQueries.Filter(rows, filter with { Status = "ALL", Priority = "ALL", ZoneId = null }).Count());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.OK)]
    [InlineData("OPERATOR", HttpStatusCode.OK)]
    [InlineData("MANAGER", HttpStatusCode.OK)]
    public async Task HttpReadsEnforceRolesAndReturnEventTaskLinks(string? role, HttpStatusCode expected)
    {
        await using var root = new SetupOverviewTests.Factory(Store());
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IOperationalEventQueries>(); services.AddSingleton<IOperationalEventQueries, Queries>(); }));
        using var client = factory.CreateClient(); if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        foreach (var path in new[] { "/api/operational-events?status=ALL&priority=ALL", "/api/operational-events/zones", $"/api/operational-events/{EventId}" })
        {
            var response = await client.GetAsync(path); Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.OK) Assert.True(response.Headers.CacheControl?.NoStore);
        }
        if (expected == HttpStatusCode.OK)
        {
            var detail = (await client.GetFromJsonAsync<OperationalEventDetail>($"/api/operational-events/{EventId}"))!;
            Assert.Equal("CLEARED", detail.Event.Status);
            Assert.Equal("IN_PROGRESS", Assert.Single(detail.Tasks).Status); // Clearing is independent of Task verification.
            Assert.StartsWith("EVT-", detail.Event.SourceCode);
        }
    }

    [Fact]
    public async Task UnavailableRepositoryIsAnErrorAndDoesNotReturnAnEmptyHistory()
    {
        await using var root = new SetupOverviewTests.Factory(Store());
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IOperationalEventQueries>(); services.AddSingleton<IOperationalEventQueries>(new Queries { Fail = true }); }));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Role", "OPERATOR");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/operational-events")).StatusCode);
    }

    [Fact]
    public void ListTranslatesToBoundedSqlAndDoesNotExposePrivateMetadata()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=localhost;Database=TranslationOnly;Integrated Security=True").Options);
        var sql = new EfOperationalEventQueries(db).Page(new("ALL", "ALL", null, null, null, Now, 2, 20)).ToQueryString();
        Assert.Contains("ORDER BY", sql); Assert.Contains("OFFSET", sql); Assert.Contains("FETCH NEXT", sql);
        Assert.DoesNotContain("MetadataJson", sql); Assert.DoesNotContain("credential", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwaggerDocumentsEventReadFiltersAndSafeDetailSchemas()
    {
        await using var root = new SetupOverviewTests.Factory(Store());
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/operational-events", out var list));
        Assert.True(paths.TryGetProperty("/api/operational-events/{eventId}", out _));
        Assert.True(paths.TryGetProperty("/api/operational-events/zones", out _));
        Assert.Contains(list.GetProperty("get").GetProperty("parameters").EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "Status");
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("OperationalEventView").GetRawText();
        Assert.DoesNotContain("metadataJson", schema); Assert.Contains("sourceCode", schema);
        if (Environment.GetEnvironmentVariable("EVENT_EXPORT_OPENAPI") == "1")
        {
            System.IO.Directory.CreateDirectory("TestResults");
            await System.IO.File.WriteAllTextAsync("TestResults/operational-events-openapi.json", json);
        }
    }
}
