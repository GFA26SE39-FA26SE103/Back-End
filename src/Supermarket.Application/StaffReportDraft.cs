namespace Supermarket.Application;

public sealed record StaffReportDraftImage(string ContentType, string Base64Data);
public sealed record StaffReportDraftAudio(byte[] Content);
public sealed record StaffReportDraftRequest(string Note, StaffReportDraftImage[] Images, StaffReportIncidentTypeView[] IncidentTypes)
{
    public StaffReportDraftAudio? Voice { get; init; }
    public string? Transcript { get; init; }
    public StaffReportTaskTypeView[] TaskTypes { get; init; } = [];
    public StaffReportZoneView[] ZoneCandidates { get; init; } = [];
}
public sealed record StaffReportDraftView(string Title, string Description, string LocationDescription,
    string? ReportCategory, string? IncidentTypeCode, bool NeedsReview)
{
    public string[] SuggestedTaskTypeCodes { get; init; } = [];
    public Guid? SuggestedZoneId { get; init; }
    /// <summary>Uncalibrated model self-assessment; never drives dispatch or confirmation.</summary>
    public string? ConfidenceLevel { get; init; }
    public string? Evidence { get; init; }
    public string? Transcript { get; init; }
}

/// <summary>AI suggestions are untrusted and never persist a report or trigger dispatch.</summary>
public interface IStaffReportDraftClient
{
    Task<string> Transcribe(StaffReportDraftAudio voice, CancellationToken ct);
    Task<StaffReportDraftView> Analyze(StaffReportDraftRequest request, CancellationToken ct);
}
