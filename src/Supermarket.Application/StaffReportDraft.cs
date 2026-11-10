namespace Supermarket.Application;

public sealed record StaffReportDraftImage(string ContentType, string Base64Data);
public sealed record StaffReportDraftRequest(string Note, StaffReportDraftImage[] Images, StaffReportIncidentTypeView[] IncidentTypes);
public sealed record StaffReportDraftView(string Title, string Description, string LocationDescription,
    string? ReportCategory, string? IncidentTypeCode, bool NeedsReview);

/// <summary>AI suggestions are untrusted and never persist a report or trigger dispatch.</summary>
public interface IStaffReportDraftClient
{
    Task<StaffReportDraftView> Analyze(StaffReportDraftRequest request, CancellationToken ct);
}
