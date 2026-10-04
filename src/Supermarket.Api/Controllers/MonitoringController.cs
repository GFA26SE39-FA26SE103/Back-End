using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
using Supermarket.Domain;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/zones/{zoneId:guid}/monitoring")]
public sealed class MonitoringController(MonitoringSetup setup) : ControllerBase
{
    [HttpGet] public Task<MonitoringConfigurationView> Get(Guid zoneId, CancellationToken ct) => setup.Get(zoneId, ct);
    [HttpPut] public Task<MonitoringConfigurationView> Save(Guid zoneId, MonitoringRequest r, CancellationToken ct) => setup.Save(zoneId, r, ct);
    [HttpDelete, ProducesResponseType(StatusCodes.Status204NoContent)] public async Task<IActionResult> Delete(Guid zoneId, [FromBody] MonitoringDeleteRequest request, CancellationToken ct)
    {
        await setup.Delete(zoneId, request, ct);
        return NoContent();
    }
    [HttpGet("review")] public Task<MonitoringReviewView> Review(Guid zoneId, CancellationToken ct) => setup.Review(zoneId, ct);
    [HttpPost("activate")] public Task<MonitoringConfigurationView> Activate(Guid zoneId, MonitoringActivationRequest request, CancellationToken ct) => setup.Activate(zoneId, true, request.ExpectedUpdatedAt, ct);
    [HttpPost("deactivate")] public Task<MonitoringConfigurationView> Deactivate(Guid zoneId, MonitoringActivationRequest request, CancellationToken ct) => setup.Activate(zoneId, false, request.ExpectedUpdatedAt, ct);
}
[ApiController, Authorize(Roles = "ADMIN"), Route("api/camera-health-events")]
public sealed class HealthController(CameraHealth health) : ControllerBase
{
    [HttpGet] public Task<List<CameraHealthEvent>> List([FromQuery] Guid? cameraId, [FromQuery] string? status, CancellationToken ct) => health.Events(cameraId, status, ct);
    [HttpGet("/api/cameras/{id:guid}/health")] public Task<CameraHealthView> Get(Guid id, CancellationToken ct) => health.Get(id, ct);
    [HttpPost("/api/cameras/{id:guid}/health/check")] public Task<CameraHealthView> Check(Guid id, CancellationToken ct) => health.Check(id, ct);
    [HttpPost("{id:guid}/investigate")] public Task<CameraHealthEvent> Investigate(Guid id, CancellationToken ct) => health.Investigate(id, ct);
    [HttpPost("{id:guid}/resolve")] public Task<CameraHealthEvent> Resolve(Guid id, ResolveRequest r, CancellationToken ct) => health.Resolve(id, r, ct);
}
