namespace Supermarket.Domain;

public abstract class Entity;
public abstract class TrackedEntity : Entity
{
    public DateTime CreatedAt
    {
        get; set;
    }
    public DateTime UpdatedAt
    {
        get; set;
    }
}
public sealed class Role : Entity
{
    public Guid RoleId
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public string? Description
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
}
public sealed class UserAccount : TrackedEntity
{
    public Guid UserId
    {
        get; set;
    }
    public Guid RoleId
    {
        get; set;
    }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Status { get; set; } = "ACTIVE";
}
public sealed class Supermarket : TrackedEntity
{
    public Guid SupermarketId
    {
        get; set;
    }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address
    {
        get; set;
    }
    public string Status { get; set; } = "ACTIVE";
}
public sealed class Floor : TrackedEntity
{
    public Guid FloorId
    {
        get; set;
    }
    public Guid SupermarketId
    {
        get; set;
    }
    public int FloorNumber
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public string? MapAssetUrl
    {
        get; set;
    }
    public int? MapWidth
    {
        get; set;
    }
    public int? MapHeight
    {
        get; set;
    }
}
public sealed class Zone : TrackedEntity
{
    public Guid ZoneId
    {
        get; set;
    }
    public Guid FloorId
    {
        get; set;
    }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ZoneType
    {
        get; set;
    }
    public string? ColorHex
    {
        get; set;
    }
    public decimal? AreaM2
    {
        get; set;
    }
    public string MapPolygon { get; set; } = "[]";
    public string Status { get; set; } = "ACTIVE";
}
public sealed class Camera : TrackedEntity
{
    public Guid CameraId
    {
        get; set;
    }
    public Guid FloorId
    {
        get; set;
    }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Manufacturer
    {
        get; set;
    }
    public string? Model
    {
        get; set;
    }
    public string? SerialNumber
    {
        get; set;
    }
    public DateTime? InstalledAt
    {
        get; set;
    }
    public DateTime? WarrantyExpiresAt
    {
        get; set;
    }
    public decimal? MapX
    {
        get; set;
    }
    public decimal? MapY
    {
        get; set;
    }
    public decimal? MapRotationDeg
    {
        get; set;
    }
    public string Status { get; set; } = "INACTIVE";
    public string HealthStatus { get; set; } = "UNKNOWN";
    public DateTime? LastSeenAt
    {
        get; set;
    }
}
public sealed class CameraConnection : TrackedEntity
{
    public Guid ConnectionId
    {
        get; set;
    }
    public Guid CameraId
    {
        get; set;
    }
    public string SourceType { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string StreamUri { get; set; } = "";
    public string? SnapshotUri
    {
        get; set;
    }
    public string? Username
    {
        get; set;
    }
    public string? CredentialSecretRef
    {
        get; set;
    }
    public bool IsEnabled
    {
        get; set;
    }
    public DateTime? LastTestedAt
    {
        get; set;
    }
    public string? LastTestResult
    {
        get; set;
    }
    public string? LastTestMessage
    {
        get; set;
    }
}
public sealed class CameraZoneMapping : TrackedEntity
{
    public Guid CameraZoneId
    {
        get; set;
    }
    public Guid CameraId
    {
        get; set;
    }
    public Guid ZoneId
    {
        get; set;
    }
    public string RoiPolygon { get; set; } = "[]";
    public string Status { get; set; } = "ACTIVE";
}
public sealed class MonitoringConfiguration : TrackedEntity
{
    public Guid ConfigId
    {
        get; set;
    }
    public Guid ZoneId
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public decimal ConfidenceThreshold { get; set; } = .5m;
    public string Status { get; set; } = "DRAFT";
    public Guid CreatedByUserId
    {
        get; set;
    }
}
public sealed class CameraHealthEvent : Entity
{
    public Guid HealthEventId
    {
        get; set;
    }
    public Guid CameraId
    {
        get; set;
    }
    public string EventType { get; set; } = "";
    public string Status { get; set; } = "OPEN";
    public DateTime DetectedAt
    {
        get; set;
    }
    public Guid? InvestigatedByUserId
    {
        get; set;
    }
    public DateTime? InvestigatedAt
    {
        get; set;
    }
    public DateTime? ResolvedAt
    {
        get; set;
    }
    public string? ResolutionNote
    {
        get; set;
    }
}
