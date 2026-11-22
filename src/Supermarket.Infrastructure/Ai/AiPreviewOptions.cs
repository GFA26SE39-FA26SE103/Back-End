using Supermarket.Application;
using Supermarket.Domain;

namespace Supermarket.Infrastructure.Ai;

public sealed class AiPreviewOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8090";
    public string? RecordedVideoBaseUrl { get; set; }
    public string? InternalServiceKey { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
    public int MaxMonitoringResponseBytes { get; set; } = 4 * 1024 * 1024;
    public string Model { get; set; } = "../yolo26s.pt";
    public string Tracker { get; set; } = "bytetrack.yaml";
    public int[] Classes { get; set; } = [0];
    public decimal Confidence { get; set; } = .50m;
    public string Device { get; set; } = "cuda:0";
    public bool Half { get; set; } = true;

    public static bool IsHttpsOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host.Length > 0 && uri.AbsolutePath == "/"
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public string SourceUrl(CameraConnection connection)
    {
        if (connection.SourceType != "RECORDED" || string.IsNullOrWhiteSpace(RecordedVideoBaseUrl))
            return connection.StreamUri;
        var assetId = RecordedVideoPlayback.AssetId(connection.StreamUri)
            ?? throw new Supermarket.Application.ApplicationException("AI_RECORDED_VIDEO_INVALID", "The camera has no uploaded video.", 422);
        return new Uri(new Uri(RecordedVideoBaseUrl), RecordedVideoPlayback.ContentPath(connection.CameraId, assetId)).AbsoluteUri;
    }
}

