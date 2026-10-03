namespace Supermarket.Domain;

public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
public readonly record struct Point(decimal X, decimal Y);
public static class Rules
{
    public static void Require(bool condition, string code, string message)
    {
        if (!condition)
            throw new DomainException(code, message);
    }
    public static string Text(string? value, int max, string field)
    {
        Require(!string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max, "INVALID_FIELD", $"{field} is required and must not exceed {max} characters.");
        return value!.Trim();
    }
    public static void Optional(string? value, int max, string field) => Require(value is null || value.Length <= max, "INVALID_FIELD", $"{field} must not exceed {max} characters.");
    public static void Status(string value, params string[] allowed) => Require(allowed.Contains(value), "INVALID_STATUS", "The requested status is not supported.");
    public static void Coordinate(decimal? value) => Require(value is null or >= 0 and <= 1, "INVALID_COORDINATE", "Coordinates must be normalized to [0,1].");
    public static void MapSize(int? width, int? height) => Require((width is null && height is null) || (width > 0 && height > 0), "INVALID_MAP_SIZE", "Map width and height must both be absent or positive.");
    public static void ZoneColor(string? value) => Require(value is null || value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit), "INVALID_COLOR", "Zone color must use #RRGGBB format.");
    public static void ZoneArea(decimal? value) => Require(value is null || value is > 0 and <= 9999999999.99m && decimal.Round(value.Value, 2) == value.Value, "INVALID_AREA", "Zone area must be positive, use at most two decimal places, and fit decimal(12,2).");
    public static void Polygon(IReadOnlyList<Point>? points)
    {
        Require(points is { Count: >= 3 and <= 1000 }, "INVALID_POLYGON", "A polygon requires 3 to 1000 points.");
        foreach (var p in points!)
        {
            Coordinate(p.X);
            Coordinate(p.Y);
        }
        Require(points.Distinct().Count() == points.Count, "INVALID_POLYGON", "Polygon vertices must be distinct; omit the repeated closing vertex.");
        decimal area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
            for (int j = i + 1; j < points.Count; j++)
            {
                if (j == i + 1 || (i == 0 && j == points.Count - 1))
                    continue;
                Require(!Intersects(a, b, points[j], points[(j + 1) % points.Count]), "INVALID_POLYGON", "Polygon edges must not intersect.");
            }
        }
        Require(Math.Abs(area) > .000000000001m, "INVALID_POLYGON", "Polygon must have nonzero area.");
    }
    private static decimal Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static bool On(Point a, Point b, Point c) => Cross(a, b, c) == 0 && c.X >= Math.Min(a.X, b.X) && c.X <= Math.Max(a.X, b.X) && c.Y >= Math.Min(a.Y, b.Y) && c.Y <= Math.Max(a.Y, b.Y);
    private static bool Intersects(Point a, Point b, Point c, Point d) => (Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0) || On(a, b, c) || On(a, b, d) || On(c, d, a) || On(c, d, b);
    public static void SameFloor(Camera camera, Zone zone) => Require(camera.FloorId == zone.FloorId, "CROSS_FLOOR_MAPPING", "Camera and zone must belong to the same floor.");
    public static void Camera(Camera camera)
    {
        Coordinate(camera.MapX);
        Coordinate(camera.MapY);
        Require((camera.MapX is null) == (camera.MapY is null), "INVALID_POSITION", "Map x and y must be provided together.");
        Require(camera.MapRotationDeg is null or >= 0 and < 360, "INVALID_ROTATION", "Rotation must be in [0,360).");
        Status(camera.Status, "ACTIVE", "INACTIVE", "DISABLED");
        Require(camera.InstalledAt != null && camera.WarrantyExpiresAt >= camera.InstalledAt, "INVALID_WARRANTY", "Installation and warranty dates are required; warranty cannot end before installation.");
    }
    public static void Connection(CameraConnection connection)
    {
        Status(connection.SourceType, "LIVE", "RECORDED", "DEMO");
        Status(connection.Protocol, "RTSP", "HTTP", "HLS", "WEBRTC", "FILE");
        var uri = SafeUri(connection.StreamUri);
        Require(connection.SourceType switch
        {
            "DEMO" => connection.Protocol == "HTTP" && uri.Scheme == "demo" && uri.Host == "camera",
            "RECORDED" => connection.Protocol == "FILE" && uri.Scheme == "file",
            _ => connection.Protocol switch { "RTSP" => uri.Scheme == "rtsp", "HTTP" or "HLS" or "WEBRTC" => uri.Scheme is "http" or "https", _ => false }
        }, "INVALID_SOURCE", "Source type, protocol and URI must agree.");
        if (connection.SnapshotUri is not null)
            Require(SafeUri(connection.SnapshotUri).Scheme is "http" or "https", "INVALID_SOURCE", "Snapshot URI must use HTTP(S).");
    }
    public static Uri SafeUri(string value)
    {
        Require(Uri.TryCreate(value, UriKind.Absolute, out var uri) && value.Length <= 1000, "INVALID_URI", "An absolute URI of at most 1000 characters is required.");
        Require(string.IsNullOrEmpty(uri!.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment), "URI_CONTAINS_SECRET", "Credentials, query strings and fragments are not allowed in camera URIs; use separate credential fields.");
        return uri;
    }
    public static void Enable(Camera camera, CameraConnection connection) => Require(camera.Status == "ACTIVE" && connection.LastTestResult == "SUCCESS" && connection.LastTestedAt is not null, "CONNECTION_NOT_READY", "An active camera and successful test of its current connection are required.");
    public static void Confidence(decimal value) => Require(value is >= 0 and <= 1, "INVALID_CONFIDENCE", "Model confidence must be in [0,1].");
    public static void ProtectAdmin(bool wasActiveAdmin, bool remainsActiveAdmin, int activeAdmins) => Require(!wasActiveAdmin || remainsActiveAdmin || activeAdmins > 1, "LAST_ACTIVE_ADMIN", "The last active Admin cannot be disabled or demoted.");
    public static void Investigate(CameraHealthEvent health) => Require(health.Status == "OPEN", "INVALID_TRANSITION", "Only an open event can be investigated.");
    public static void Resolve(CameraHealthEvent health, Camera camera) => Require(health.Status is "OPEN" or "INVESTIGATING" && camera.HealthStatus == "ONLINE", "INVALID_TRANSITION", "Only an unresolved event for a restored online camera can be resolved.");
}
