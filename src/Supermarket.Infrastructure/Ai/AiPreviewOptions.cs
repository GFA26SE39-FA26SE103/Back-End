namespace Supermarket.Infrastructure.Ai;

public sealed class AiPreviewOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8090";
    public string? InternalServiceKey { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
    public string Model { get; set; } = "../yolo26n.pt";
    public string Tracker { get; set; } = "bytetrack.yaml";
    public int[] Classes { get; set; } = [0];
    public decimal Confidence { get; set; } = .50m;
    public string Device { get; set; } = "cuda:0";
    public bool Half { get; set; } = true;
}

