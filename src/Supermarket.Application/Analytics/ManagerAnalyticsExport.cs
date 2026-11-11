using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class ManagerAnalyticsExport(ManagerAnalytics analytics, IAnalyticsReportRenderer renderer,
    ISetupStore store, ICurrentUser current, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase) { "csv", "xlsx", "pdf" };

    public async Task<AnalyticsExportFile> Generate(AnalyticsQuery query, string format, CancellationToken ct)
    {
        UseCase.Manager(current);
        var normalized = format.Trim().ToLowerInvariant();
        if (!Formats.Contains(normalized))
            throw new ApplicationException("INVALID_REPORT_FORMAT", "Use csv, xlsx, or pdf report format.", 400);

        var report = await analytics.Report(query, ct);
        try
        {
            var file = renderer.Render(report, normalized);
            await Audit(report, normalized, "SUCCESS", null, ct);
            return file;
        }
        catch (Exception error) when (error is not ApplicationException)
        {
            await Audit(report, normalized, "FAILED", "REPORT_GENERATION_FAILED", ct);
            throw new ApplicationException("REPORT_GENERATION_FAILED", "The analytics report could not be generated completely.", 503);
        }
    }

    private Task Audit(AnalyticsReportData report, string format, string status, string? reason, CancellationToken ct)
        => store.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(), UserId = current.UserId, Action = "EXPORT_ANALYTICS_REPORT",
            EntityType = "AnalyticsReport", CreatedAt = clock.UtcNow, ResultStatus = status, Reason = reason,
            NewValuesJson = JsonSerializer.Serialize(new
            {
                format,
                from = report.Period.From,
                to = report.Period.To,
                report.Period.ZoneId,
                report.Period.TimeZone,
                report.Period.GeneratedAt
            }, Json)
        }, ct);
}
