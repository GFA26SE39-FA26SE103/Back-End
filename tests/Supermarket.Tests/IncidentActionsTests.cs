using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Api;
using Supermarket.Api.Controllers;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

public sealed class IncidentActionsTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void DismissEndpointIsOperatorOnlyAndUsesTheIncidentRoute()
    {
        var controller = typeof(IncidentsController);
        var authorization = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var route = Assert.Single(controller.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());
        var method = controller.GetMethod(nameof(IncidentsController.Dismiss))!;
        var post = Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());

        Assert.Equal(AccessRoles.Operator, authorization.Roles);
        Assert.Equal("api/incidents", route.Template);
        Assert.Equal("{incidentId:guid}/dismiss", post.Template);
    }

    [Fact]
    public async Task OperatorDismissesOpenIncidentWithTrimmedReasonAndAudit()
    {
        var world = new World("OPERATOR");

        var result = await world.Actions.Dismiss(world.Incident.IncidentId, new("  False alarm after inspection.  "), default);

        Assert.Equal("DISMISSED", result.Status);
        Assert.Equal("False alarm after inspection.", result.StatusReason);
        Assert.Equal(Now, result.ClosedAt);
        Assert.Equal("DISMISSED", world.Incident.Status);
        Assert.Equal(Now, world.Incident.ClosedAt);

        var audit = Assert.Single(world.Store.Values.OfType<AuditLog>());
        Assert.Equal("DISMISS_INCIDENT", audit.Action);
        Assert.Equal("Incident", audit.EntityType);
        Assert.Equal(world.Incident.IncidentId, audit.EntityId);
        Assert.Equal(world.User.UserId, audit.UserId);
        Assert.Equal("False alarm after inspection.", audit.Reason);
        Assert.Equal("SUCCESS", audit.ResultStatus);
        using var before = JsonDocument.Parse(audit.OldValuesJson!);
        using var after = JsonDocument.Parse(audit.NewValuesJson!);
        Assert.Equal("IN_HANDLING", before.RootElement.GetProperty("status").GetString());
        Assert.Equal("DISMISSED", after.RootElement.GetProperty("status").GetString());
        Assert.False(after.RootElement.TryGetProperty("description", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankDismissReasonIsRejectedWithoutMutation(string reason)
    {
        var world = new World("OPERATOR");

        var error = await Assert.ThrowsAsync<AppError>(() => world.Actions.Dismiss(world.Incident.IncidentId, new(reason), default));

        Assert.Equal("INCIDENT_DISMISS_REASON_REQUIRED", error.Code);
        Assert.Equal(422, error.Status);
        Assert.Equal("IN_HANDLING", world.Incident.Status);
        Assert.Null(world.Incident.ClosedAt);
        Assert.Empty(world.Store.Values.OfType<AuditLog>());
    }

    [Fact]
    public async Task OversizedDismissReasonIsRejectedWithoutMutation()
    {
        var world = new World("OPERATOR");

        var error = await Assert.ThrowsAsync<AppError>(() => world.Actions.Dismiss(
            world.Incident.IncidentId, new(new string('x', 2001)), default));

        Assert.Equal("INCIDENT_DISMISS_REASON_TOO_LONG", error.Code);
        Assert.Equal(422, error.Status);
        Assert.Equal("IN_HANDLING", world.Incident.Status);
        Assert.Empty(world.Store.Values.OfType<AuditLog>());
    }

    [Fact]
    public async Task MissingIncidentReturnsNotFoundWithoutAudit()
    {
        var world = new World("OPERATOR");

        var error = await Assert.ThrowsAsync<AppError>(() => world.Actions.Dismiss(Guid.NewGuid(), new("Not actionable."), default));

        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal(404, error.Status);
        Assert.Empty(world.Store.Values.OfType<AuditLog>());
    }

    [Theory]
    [InlineData("CLOSED")]
    [InlineData("DISMISSED")]
    [InlineData("CANCELLED")]
    public async Task EndedIncidentCannotBeDismissedAgain(string status)
    {
        var world = new World("OPERATOR");
        world.Incident.Status = status;
        world.Incident.ClosedAt = Now.AddMinutes(-5);

        var error = await Assert.ThrowsAsync<AppError>(() => world.Actions.Dismiss(world.Incident.IncidentId, new("Try again."), default));

        Assert.Equal("INCIDENT_ALREADY_ENDED", error.Code);
        Assert.Equal(409, error.Status);
        Assert.Equal(status, world.Incident.Status);
        Assert.Equal(Now.AddMinutes(-5), world.Incident.ClosedAt);
        Assert.Empty(world.Store.Values.OfType<AuditLog>());
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("MANAGER")]
    [InlineData("STAFF")]
    public async Task OnlyOperatorCanDismissIncident(string role)
    {
        var world = new World(role);

        var error = await Assert.ThrowsAsync<AppError>(() => world.Actions.Dismiss(world.Incident.IncidentId, new("Not actionable."), default));

        Assert.Equal("FORBIDDEN", error.Code);
        Assert.Equal(403, error.Status);
        Assert.Equal("IN_HANDLING", world.Incident.Status);
        Assert.Empty(world.Store.Values.OfType<AuditLog>());
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public Current User { get; }
        public Incident Incident { get; } = new()
        {
            IncidentId = Guid.NewGuid(), IncidentTypeId = Guid.NewGuid(), ZoneId = Guid.NewGuid(),
            SourceType = "STAFF_REPORT", Title = "Spill near entrance", Status = "IN_HANDLING",
            CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddMinutes(-10)
        };
        public IncidentActions Actions => new(Store, User, new Clock(Now));

        public World(string role)
        {
            User = new Current(Guid.NewGuid(), role);
            Store.Values.Add(Incident);
        }
    }

    private sealed record Current(Guid UserId, string Role) : ICurrentUser;
    private sealed record Clock(DateTime UtcNow) : IClock;
}
