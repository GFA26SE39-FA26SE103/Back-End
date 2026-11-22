using Supermarket.Domain;

namespace Supermarket.Application;

public interface IRecordedVideoTicket
{
    string Issue(Guid cameraId, Guid assetId);
    bool IsValid(string? ticket, Guid cameraId, Guid assetId);
}

public sealed class RecordedVideoPlayback(ISetupStore store, IRecordedVideoStorage files, IRecordedVideoTicket tickets)
{
    public static string ContentPath(Guid cameraId, Guid assetId) => $"/api/cameras/{cameraId}/recorded-video/content/{assetId:N}";

    public static Guid? AssetId(string streamUri) =>
        Uri.TryCreate(streamUri, UriKind.Absolute, out var uri) && uri.IsFile && string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && string.Equals(Path.GetExtension(uri.LocalPath), ".mp4", StringComparison.OrdinalIgnoreCase)
        && Guid.TryParseExact(Path.GetFileNameWithoutExtension(uri.LocalPath), "N", out var id) ? id : null;

    public CameraPlaybackView Prepare(CameraConnection connection)
    {
        var id = connection.CameraId;
        var assetId = AssetId(connection.StreamUri) ?? throw NotFound();
        return new(id, "MP4", $"{ContentPath(id, assetId)}?ticket={Uri.EscapeDataString(tickets.Issue(id, assetId))}");
    }

    public async Task<Stream> Open(Guid cameraId, Guid assetId, string? ticket, bool aiAuthorized, CancellationToken ct)
    {
        if (!aiAuthorized && !tickets.IsValid(ticket, cameraId, assetId))
            throw NotFound();

        var camera = await store.Find<Camera>(cameraId, ct);
        var connection = (await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault();
        if (camera?.Status != "ACTIVE" || connection is not
            { IsEnabled: true, LastTestResult: "SUCCESS", LastTestedAt: not null, SourceType: "RECORDED", Protocol: "FILE" }
            || AssetId(connection.StreamUri) != assetId)
            throw NotFound();

        return files.Open(connection.StreamUri) ?? throw NotFound();
    }

    private static ApplicationException NotFound() => new("RECORDED_VIDEO_UNAVAILABLE", "The recorded video is unavailable.", 404);
}
