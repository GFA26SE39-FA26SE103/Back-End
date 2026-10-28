namespace Supermarket.Domain;

/// <summary>Immutable administrative action history persisted in dbo.AuditLog.</summary>
public sealed class AuditLog : Entity
{
    public Guid AuditLogId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ResultStatus { get; set; }
}

/// <summary>One mobile installation registered for push delivery; raw tokens never leave the backend API.</summary>
public sealed class UserDevice : Entity
{
    public Guid DeviceId { get; set; }
    public Guid UserId { get; set; }
    public string FcmToken { get; set; } = "";
    public string Platform { get; set; } = "";
    public string? AppVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
