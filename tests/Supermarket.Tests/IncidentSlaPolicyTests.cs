using System.Text.Json;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

public sealed class IncidentSlaPolicyTests
{
    [Fact]
    public void V7IncidentContractRemovesIncidentSeverityButPreservesIndependentPriorities()
    {
        Assert.Null(typeof(Incident).GetProperty("Severity"));
        Assert.Null(typeof(IncidentType).GetProperty("DefaultSeverity"));
        Assert.NotNull(typeof(OperationalEvent).GetProperty("Priority"));
        Assert.NotNull(typeof(Notification).GetProperty("Severity"));
    }

    [Fact]
    public async Task AssignsTheEnabledIncidentTypePolicyAndSnapshotsWallClockDeadlines()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var clock = new Clock();
        var incidentTypeId = Guid.NewGuid();
        var policy = Policy(incidentTypeId, "CRITICAL", responseSeconds: 60, resolutionSeconds: 900);
        store.Values.Add(policy);
        var incident = new Incident { IncidentId = Guid.NewGuid(), IncidentTypeId = incidentTypeId };

        await new IncidentSlaPolicyService(store, clock).Assign(incident, default);

        Assert.Equal(policy.PolicyId, incident.SlaPolicyId);
        Assert.Equal(clock.UtcNow.AddSeconds(900), incident.ResolutionDeadlineAt);
        var snapshot = JsonSerializer.Deserialize<IncidentSlaSnapshot>(incident.SlaSnapshotJson!, IncidentSlaPolicyService.JsonOptions);
        Assert.NotNull(snapshot);
        Assert.Equal("CRITICAL", snapshot.PriorityCode);
        Assert.Equal(clock.UtcNow.AddSeconds(60), snapshot.ResponseDeadlineAt);
        Assert.Equal(clock.UtcNow.AddSeconds(900), snapshot.ResolutionDeadlineAt);
        Assert.Equal(policy.Version, snapshot.PolicyVersion);
    }

    [Fact]
    public async Task MissingOrAmbiguousIncidentTypePolicyFailsClosed()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var clock = new Clock();
        var incidentTypeId = Guid.NewGuid();
        var incident = new Incident { IncidentId = Guid.NewGuid(), IncidentTypeId = incidentTypeId };
        var service = new IncidentSlaPolicyService(store, clock);

        var missing = await Assert.ThrowsAsync<AppError>(() => service.Assign(incident, default));
        Assert.Equal("INCIDENT_SLA_POLICY_MISSING", missing.Code);

        store.Values.Add(Policy(incidentTypeId, "HIGH", 180, 1800));
        store.Values.Add(Policy(incidentTypeId, "CRITICAL", 60, 900));
        var ambiguous = await Assert.ThrowsAsync<AppError>(() => service.Assign(incident, default));
        Assert.Equal("INCIDENT_SLA_POLICY_AMBIGUOUS", ambiguous.Code);
    }

    private static SlaPolicy Policy(Guid incidentTypeId, string priority, int responseSeconds, int resolutionSeconds) => new()
    {
        PolicyId = Guid.NewGuid(),
        IsEnabled = true,
        Version = 2,
        ProblemDomain = "INCIDENT",
        IncidentTypeId = incidentTypeId,
        PriorityCode = priority,
        ResponseTargetSec = responseSeconds,
        ResolutionTargetSec = resolutionSeconds,
        OfferTimeoutSec = 30,
        WarningPercent = 80
    };

    private sealed class Clock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 11, 5, 9, 30, 0, DateTimeKind.Utc);
    }
}
