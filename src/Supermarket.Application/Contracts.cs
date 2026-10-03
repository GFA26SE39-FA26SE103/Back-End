using System.ComponentModel.DataAnnotations;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed record LoginRequest([Required, EmailAddress, MaxLength(255)] string Email, [Required, MaxLength(128)] string Password);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserView User);
public sealed record UserView(Guid UserId, string Email, string FullName, Guid RoleId, string Role, string Status);
public sealed record CreateUserRequest([Required, EmailAddress, MaxLength(255)] string Email, [Required, MinLength(12), MaxLength(128)] string Password, [Required, MaxLength(150)] string FullName, Guid RoleId);
public sealed record UpdateUserRequest([Required, MaxLength(150)] string FullName, Guid RoleId);
public sealed record StoreRequest([Required, MaxLength(50)] string Code, [Required, MaxLength(150)] string Name, [MaxLength(500)] string? Address, string Status = "ACTIVE");
public sealed record FloorRequest(int FloorNumber, [Required, MaxLength(100)] string Name, [MaxLength(1000)] string? MapAssetUrl, int? MapWidth, int? MapHeight);
public sealed record ZoneRequest([Required, MaxLength(50)] string Code, [Required, MaxLength(100)] string Name, [MaxLength(50)] string? ZoneType, [Required] Point[] MapPolygon, string Status = "ACTIVE", [MaxLength(7)] string? ColorHex = null, decimal? AreaM2 = null);
public sealed record CameraRequest([Required, MaxLength(50)] string Code, [Required, MaxLength(100)] string Name, [MaxLength(100)] string? Manufacturer, [MaxLength(100)] string? Model, [MaxLength(150)] string? SerialNumber, DateTime InstalledAt, DateTime WarrantyExpiresAt, decimal? MapX, decimal? MapY, decimal? MapRotationDeg, string Status = "INACTIVE");
public sealed record ConnectionRequest([Required] string SourceType, [Required] string Protocol, [Required, MaxLength(1000)] string StreamUri, [MaxLength(1000)] string? SnapshotUri = null, [MaxLength(150)] string? Username = null, [MaxLength(128)] string? Password = null);
public sealed record ConnectionView(Guid ConnectionId, Guid CameraId, string SourceType, string Protocol, string StreamUri, string? SnapshotUri, bool HasCredentials, bool IsEnabled, DateTime? LastTestedAt, string? LastTestResult, string? LastTestMessage, DateTime UpdatedAt);
public sealed record MappingRequest([Required] Point[] RoiPolygon, string Status = "ACTIVE");
public sealed record MappingView(Guid CameraZoneId, Guid CameraId, Guid ZoneId, Point[] RoiPolygon, string Status);
public sealed record ZoneView(Guid ZoneId, Guid FloorId, string Code, string Name, string? ZoneType, Point[] MapPolygon, string? ColorHex, decimal? AreaM2, string Status, DateTime UpdatedAt);
public sealed record MonitoringRequest([Required, MaxLength(100)] string Name, decimal ConfidenceThreshold = .5m);
public sealed record ResolveRequest([Required, MaxLength(1000)] string ResolutionNote);
// Legacy contract only. ERD v3 freezes the schema; persistence/endpoints still need SQL synchronization.
public sealed record MonitoringRuleContract(Guid IncidentTypeId, decimal WarningThreshold, decimal CriticalThreshold, string Unit, int SustainSeconds, int CooldownSeconds, bool Enabled);
