using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;

namespace Supermarket.Infrastructure.FloorPlans;

public sealed class FloorPlanStorage(LocalFloorPlanStorage local, CloudinaryFloorPlanStorage cloud,
    IOptions<FloorPlanOptions> options, ILogger<FloorPlanStorage> logger) : IFloorPlanStorage
{
    public Task<StoredFloorPlan> Save(Guid floorId, Stream content, string filename, string contentType, long length, CancellationToken ct)
        => Selected.Save(floorId, content, filename, contentType, length, ct);
    public Task<FloorPlanFile?> Open(Guid floorId, string token, CancellationToken ct)
        => Storage(token).Open(floorId, token, ct);
    public async Task Delete(Guid floorId, string token, CancellationToken ct)
    {
        try { await Storage(token).Delete(floorId, token, ct); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException
            or OperationCanceledException or Supermarket.Application.ApplicationException)
        {
            // Never log upstream bodies, credentials or signed URLs. The committed map remains authoritative.
            logger.LogWarning("Floor-plan cleanup failed for floor {FloorId}. Inspect storage for an unreferenced asset.", floorId);
        }
    }
    private IFloorPlanStorage Selected => options.Value.Provider.Equals("Cloudinary", StringComparison.OrdinalIgnoreCase) ? cloud : local;
    private IFloorPlanStorage Storage(string token) => CloudinaryFloorPlanStorage.IsCloudinary(token) ? cloud : local;
}
