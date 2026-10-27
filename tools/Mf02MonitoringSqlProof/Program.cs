using System.Data;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence;
using D = Supermarket.Domain;
using Db = Supermarket.Infrastructure.Persistence.Scaffolded;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var requireLive = args.Contains("--require-live", StringComparer.OrdinalIgnoreCase);
var readinessOnly = args.Contains("--readiness-only", StringComparer.OrdinalIgnoreCase);
var settingsPath = Environment.GetEnvironmentVariable("MF02_LOCAL_SETTINGS")
    ?? Path.Combine(root, "src/Supermarket.Api/appsettings.Local.json");
Require(File.Exists(settingsPath), $"Local settings file exists ({settingsPath}).");

using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
var connectionString = settings.RootElement.GetProperty("ConnectionStrings").GetProperty("SqlServer").GetString();
Require(!string.IsNullOrWhiteSpace(connectionString), "SQL connection string is configured.");

var options = new DbContextOptionsBuilder<Db.AppDbContext>().UseSqlServer(connectionString).Options;
await using var db = new Db.AppDbContext(options);
db.Database.SetCommandTimeout(20);
var clock = new Clock();
var store = new RollbackStore(new EfSetupStore(db, clock), db);
var snapshots = new MonitoringSnapshotReader(store);
var queries = new EfMonitoringIncidentQueries(db);
await queries.EnsureSchema(default);

var saved = await snapshots.ReadAll(default);
var readiness = saved.Select(snapshot => new
{
    snapshot.CameraId,
    snapshot.Connection.SourceType,
    snapshot.Connection.Protocol,
    snapshot.Connection.IsEnabled,
    snapshot.Connection.LastTestResult,
    snapshot.IssueCode,
    Zones = snapshot.Zones.Select(zone => new { zone.ZoneId, zone.IssueCode }).ToArray()
}).ToArray();
if (readinessOnly)
{
    Console.WriteLine(JsonSerializer.Serialize(new { checkedAtUtc = clock.UtcNow, readiness },
        new JsonSerializerOptions { WriteIndented = true }));
    return;
}

var candidates = saved
    .Where(snapshot => snapshot.Zones.Any()
        && snapshot.Connection.LastTestResult == "SUCCESS")
    .OrderBy(snapshot => snapshot.Connection.SourceType == "LIVE" ? 0 : 1)
    .ToArray();
Require(candidates.Length > 0, "A mapped camera with a saved successful connection test exists.");
var template = candidates[0];
if (requireLive)
{
    if (template.Connection.SourceType != "LIVE")
        throw new InvalidOperationException("FAILED: --require-live was set, but only a RECORDED fallback is currently ready.");
    Console.WriteLine("PASS: A LIVE camera candidate is available for the required acceptance path.");
}
var templateZone = template.Zones.First();
var longQueueType = await db.OperationalEventTypes.AsNoTracking()
    .SingleAsync(eventType => eventType.Code == "LONG_QUEUE" && eventType.Status == "ACTIVE");
var camera = await db.Cameras.AsNoTracking().SingleAsync(row => row.CameraId == template.CameraId);
var adminId = await db.UserAccounts.AsNoTracking()
    .Where(user => user.Status == "ACTIVE" && user.Role.Name == "ADMIN")
    .Select(user => user.UserId)
    .FirstAsync();

var suffix = Guid.NewGuid().ToString("N")[..10];
var zoneId = Guid.NewGuid();
var configId = Guid.NewGuid();
var ruleId = Guid.NewGuid();
var eventId = Guid.Empty;
var incidentCountBefore = await db.Incidents.CountAsync();
var proof = new Dictionary<string, object?>
{
    ["checkedAtUtc"] = clock.UtcNow,
    ["database"] = db.Database.GetDbConnection().Database,
    ["selectedSource"] = template.Connection.SourceType,
    ["sourceFallbackUsed"] = template.Connection.SourceType != "LIVE",
    ["templateCameraId"] = template.CameraId,
    ["templateZoneId"] = templateZone.ZoneId,
    ["savedIssueCode"] = template.IssueCode,
    ["contract"] = new
    {
        eventCode = "LONG_QUEUE", measurementMode = "QUEUE_LENGTH", unit = "PEOPLE",
        confidenceThreshold = .5m, warningThreshold = 3m, criticalThreshold = 5m,
        sustainSec = 5, recoverySec = 5, cooldownSec = 30
    },
    ["measurementSource"] = "synthetic aggregate observations through production snapshot reader, evaluator, writer, EF store and SQL",
    ["transaction"] = "SERIALIZABLE outer transaction; always rolled back"
};

await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
{
    try
    {
        var fixtureCamera = await db.Cameras.FindAsync(template.CameraId);
        var fixtureConnection = await db.CameraConnections.SingleAsync(row => row.CameraId == template.CameraId);
        Require(fixtureConnection.LastTestResult == "SUCCESS" && fixtureConnection.LastTestedAt is not null,
            "The selected camera connection has a saved successful connection test.");
        fixtureCamera!.Status = "ACTIVE";
        fixtureConnection.IsEnabled = true;

        db.Zones.Add(new Db.Zone
        {
            ZoneId = zoneId, FloorId = camera.FloorId, Code = "MF02-" + suffix, Name = "MF-02 SQL proof " + suffix,
            MapPolygon = "[{\"x\":0.1,\"y\":0.1},{\"x\":0.2,\"y\":0.1},{\"x\":0.2,\"y\":0.2}]",
            Status = "ACTIVE", MinStaff = 0, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow
        });
        db.MonitoringConfigurations.Add(new Db.MonitoringConfiguration
        {
            ConfigId = configId, ZoneId = zoneId, Name = "MF-02 SQL proof " + suffix,
            ConfidenceThreshold = .5m, Status = "ACTIVE", CreatedByUserId = adminId,
            CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow
        });
        db.MonitoringRules.Add(new Db.MonitoringRule
        {
            RuleId = ruleId, ConfigId = configId, EventTypeId = longQueueType.EventTypeId,
            OutcomeType = "OPERATIONAL_EVENT", Enabled = true,
            WarningThreshold = 3m, CriticalThreshold = 5m, ThresholdUnit = "PEOPLE",
            SustainSec = 5, CooldownSec = 30,
            ParametersJson = "{\"measurementMode\":\"QUEUE_LENGTH\",\"recoverySec\":5}",
            CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow
        });
        db.CameraZoneMappings.Add(new Db.CameraZoneMapping
        {
            CameraZoneId = Guid.NewGuid(), CameraId = template.CameraId, ZoneId = zoneId,
            RoiPolygon = JsonSerializer.Serialize(templateZone.Roi, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = "ACTIVE", CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var readySnapshot = (await snapshots.ReadAll(default)).Single(snapshot => snapshot.CameraId == template.CameraId);
        Require(readySnapshot.IssueCode is null, "Fixture monitoring snapshot is ready.");
        var zone = readySnapshot.Zones.Single(row => row.ZoneId == zoneId);
        var rule = zone.Rules.Single();
        var writer = new MonitoringIncidentWriter(store, snapshots, queries, clock);
        var sessionId = Guid.NewGuid();
        var continuityId = Guid.NewGuid();
        var state = new D.RuleEvaluationState();
        long sequence = 0;

        async Task<IncidentWriteResult> Observe(int count, long elapsedMs)
        {
            var measurement = new AiZoneMeasurement(zoneId, configId, zone.ConfigVersion, count, count);
            var batch = new AiMeasurementBatch(1, sessionId, continuityId, ++sequence,
                clock.UtcNow.AddMilliseconds(elapsedMs), elapsedMs, readySnapshot.Fingerprint, [measurement]);
            var evaluation = D.MonitoringRuleEvaluator.Evaluate(rule.Settings(), state,
                new(sessionId, continuityId, sequence, elapsedMs, count, readySnapshot.Fingerprint, false));
            state = evaluation.NextState;
            return await writer.Apply(new(readySnapshot, zone, rule, batch, measurement, evaluation, true), default);
        }

        var warningCount = (int)decimal.Ceiling(rule.WarningThreshold);
        await Observe(warningCount, 0);
        var warning = await Observe(warningCount, rule.SustainSec * 1000L + 1);
        Require(warning.Applied && warning.OperationalEventId is not null, "Sustained warning opens an OperationalEvent.");
        eventId = warning.OperationalEventId!.Value;

        var criticalCount = (int)decimal.Ceiling(rule.CriticalThreshold);
        var criticalStart = rule.SustainSec * 1000L + 1001;
        await Observe(criticalCount, criticalStart);
        await Observe(criticalCount, criticalStart + rule.SustainSec * 1000L + 1);
        var escalated = await db.OperationalEvents.AsNoTracking().SingleAsync(row => row.EventId == eventId);
        Require(escalated.Priority == "CRITICAL", "The same OperationalEvent escalates to CRITICAL.");

        var recoveryStart = criticalStart + rule.SustainSec * 1000L + 1001;
        await Observe(0, recoveryStart);
        await Observe(0, recoveryStart + (rule.RecoverySec ?? rule.SustainSec) * 1000L + 1);
        var cleared = await db.OperationalEvents.AsNoTracking().SingleAsync(row => row.EventId == eventId);
        Require(cleared.Status == "CLEARED" && cleared.ClearedAt is not null,
            "Sustained valid recovery clears the OperationalEvent.");
        Require(await db.Incidents.CountAsync() == incidentCountBefore,
            "An OPERATIONAL_EVENT rule never creates an Incident.");

        proof["fixtureZoneId"] = zoneId;
        proof["fixtureConfigId"] = configId;
        proof["fixtureRuleId"] = ruleId;
        proof["fixtureEventId"] = eventId;
        proof["finalEvent"] = new { cleared.Status, cleared.Priority, cleared.ClearedAt };
    }
    finally
    {
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
    }
}

Require(!await db.Zones.AnyAsync(row => row.ZoneId == zoneId)
    && !await db.MonitoringConfigurations.AnyAsync(row => row.ConfigId == configId)
    && !await db.OperationalEvents.AnyAsync(row => row.EventId == eventId),
    "Rollback removed every fixture row.");
proof["rollbackVerified"] = true;
proof["passed"] = true;
Console.WriteLine(JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}

sealed class Clock : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

sealed class RollbackStore(ISetupStore inner, Db.AppDbContext db) : ISetupStore
{
    public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : D.Entity, new() => inner.Find<T>(id, ct);
    public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : D.Entity, new() => inner.List(filter, ct);
    public Task Add<T>(T value, CancellationToken ct = default) where T : D.Entity, new() => inner.Add(value, ct);
    public Task Update<T>(T value, CancellationToken ct = default) where T : D.Entity, new() => inner.Update(value, ct);
    public Task Remove<T>(T value, CancellationToken ct = default) where T : D.Entity, new() => inner.Remove(value, ct);
    public async Task<T> Transaction<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        var result = await work();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return result;
    }
}
