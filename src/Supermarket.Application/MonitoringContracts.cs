using System.ComponentModel.DataAnnotations;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed record MonitoringRuleRequest(Guid IncidentTypeId, decimal WarningThreshold, decimal CriticalThreshold,
    [Required, MaxLength(30)] string ThresholdUnit, int SustainSec = 30, int CooldownSec = 300, int ClearDurationSec = 60, bool Enabled = true,
    [MaxLength(16000)] string? ParametersJson = null,
    // Dual threshold: hold the level until the value drops below these (null = same as the enter threshold).
    decimal? WarningExitThreshold = null, decimal? CriticalExitThreshold = null);
public sealed record MonitoringActivationRequest(DateTime ExpectedUpdatedAt);
public sealed record MonitoringDeleteRequest(Guid ConfigId, DateTime ExpectedUpdatedAt);
public sealed record MonitoringRuleView(Guid RuleId, Guid IncidentTypeId, string IncidentCode, string IncidentName,
    decimal WarningThreshold, decimal CriticalThreshold, string ThresholdUnit, int SustainSec, int CooldownSec,
    int ClearDurationSec, bool Enabled, string? ParametersJson, decimal? WarningExitThreshold = null, decimal? CriticalExitThreshold = null);
public sealed record MonitoringConfigurationView(Guid ConfigId, Guid ZoneId, string Name, decimal ConfidenceThreshold,
    string Status, Guid CreatedByUserId, DateTime CreatedAt, DateTime UpdatedAt, MonitoringRuleView[] Rules);
public sealed record MonitoringMeasurementOptionView(string Mode, string Unit, bool Supported, string? Reason, decimal? DefaultWarning=null, decimal? DefaultCritical=null);
public sealed record IncidentTypeView(Guid IncidentTypeId, string Code, string Name, string? Description, string SourceType,
    string? MeasurementType, string Status, bool Supported, string? ThresholdUnit, decimal? DefaultWarningThreshold,
    decimal? DefaultCriticalThreshold, string? UnsupportedReason, MonitoringMeasurementOptionView[]? MeasurementOptions=null,
    bool RequiresBeforePhoto=false);
public sealed record MonitoringIssue(string Code, string Message);
public sealed record MonitoringCameraView(Guid CameraId, string Code, string Name, string Status, string MappingStatus,
    Point[] RoiPolygon, string? SourceType, string? Protocol, bool IsEnabled, string? LastTestResult, DateTime? LastTestedAt,
    bool Ready, MonitoringIssue[] Issues);
public sealed record MonitoringReviewView(MonitoringConfigurationView Configuration, ZoneView Zone,
    MonitoringCameraView[] Cameras, MonitoringIssue[] Issues, string[] Warnings, bool CanActivate);
