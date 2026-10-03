using Supermarket.Domain;

namespace Supermarket.Application;

public interface IRecordedVideoStorage
{
    Task<string> Save(Stream content, string filename, long length, CancellationToken ct);
    void Delete(string uri);
}

public sealed record RecordedVideoView(Guid CameraId, string SourceType = "RECORDED", string Protocol = "FILE");

public sealed class RecordedVideoUpload(ISetupStore store, ICurrentUser current, IRecordedVideoStorage files,
    CameraSetup cameras, IAiPreviewClient ai)
{
    public const long MaxBytes = 200L * 1024 * 1024;

    public async Task<RecordedVideoView> Upload(Guid cameraId, Stream content, string filename, long length, CancellationToken ct)
    {
        UseCase.Admin(current);
        var camera = UseCase.Found(await store.Find<Camera>(cameraId, ct));
        if (camera.Status != "ACTIVE")
            throw new ApplicationException("CAMERA_NOT_ACTIVE", "Activate the camera before uploading video.");
        var mappings = await store.List<CameraZoneMapping>(m => m.CameraId == cameraId && m.Status == "ACTIVE", ct);
        foreach (var mapping in mappings)
            if ((await store.List<MonitoringConfiguration>(m => m.ZoneId == mapping.ZoneId && m.Status == "ACTIVE", ct)).Count > 0)
                throw new ApplicationException("MONITORING_ACTIVE", "Deactivate monitoring before replacing the camera source.");

        // Stop the old session so an idempotent start cannot continue processing its previous source.
        try { await ai.Stop(cameraId, ct); }
        catch (ApplicationException error) when (error.Code == "AI_SERVICE_UNAVAILABLE") { }

        var uri = await files.Save(content, filename, length, ct);
        try
        {
            await cameras.Configure(cameraId, new ConnectionRequest("RECORDED", "FILE", uri), ct);
        }
        catch
        {
            files.Delete(uri);
            throw;
        }
        return new(cameraId);
    }
}
