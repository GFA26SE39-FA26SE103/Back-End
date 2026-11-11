using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public static class AutoVerificationOutcome
{
    public const string NotAuto = "NOT_AUTO", Waiting = "WAITING", Verified = "VERIFIED", ManualFallback = "MANUAL_FALLBACK";
}

/// <summary>Fail-safe AUTO verification for metric-backed Operational Event tasks.</summary>
public sealed class TaskVerification(ISetupStore store, IClock clock, TaskNotifier notifier)
{
    public const string AutoVerified = "TASK_AUTO_VERIFIED", VerificationException = "TASK_VERIFICATION_EXCEPTION";

    public async Task<string> TryAuto(WorkTask task, CancellationToken ct)
    {
        if (task.VerificationMode != TaskVerificationMode.Auto || task.Status != WorkTaskStatus.Submitted)
            return AutoVerificationOutcome.NotAuto;
        if (task.EventId is not { } eventId || await store.Find<OperationalEvent>(eventId, ct) is not { } ev)
            return await Fallback(task, "The source Operational Event or its metric is unavailable.", ct);
        if (!MetricContract(ev, out var observed, out var warning))
            return await Fallback(task, "The metric contract is missing or inconsistent.", ct);
        if (ev.ClearedAt is null) return AutoVerificationOutcome.Waiting;
        if (ev.Status != "CLEARED" || ev.LastObservedAt < ev.ClearedAt.Value || observed >= warning)
            return await Fallback(task, "The recovery metric is missing, stale or inconsistent.", ct);

        var pending = await store.List<TaskEvidence>(e => e.TaskId == task.TaskId && e.ReviewStatus == TaskEvidenceReview.Pending, ct);
        var submission = pending.Where(e => e.EvidenceType == TaskEvidenceType.CompletionNote).OrderByDescending(e => e.CreatedAt).FirstOrDefault();
        if (submission is null)
            return await Fallback(task, "The completion submission required for automatic verification is missing.", ct);
        foreach (var row in pending)
        {
            row.ReviewStatus = TaskEvidenceReview.Verified;
            await store.Update(row, ct);
        }
        var now = clock.UtcNow;
        task.Status = WorkTaskStatus.Verified; task.VerifiedAt = now; task.VerifiedByUserId = null;
        await store.Update(task, ct);
        foreach (var assignment in await store.List<TaskAssignmentHistory>(a => a.TaskId == task.TaskId
            && a.Status == OfferStatus.Accepted && a.EndedAt == null, ct))
        {
            assignment.EndedAt = now;
            await store.Update(assignment, ct);
        }
        await notifier.Send(AutoVerified, "Task automatically verified",
            $"{task.Title}: the monitored condition recovered and the completion data was valid.", task,
            [submission.SubmittedByUserId], "PERFORMER", ct, ev.CameraId);
        return AutoVerificationOutcome.Verified;
    }

    private async Task<string> Fallback(WorkTask task, string reason, CancellationToken ct)
    {
        task.VerificationMode = TaskVerificationMode.Manual;
        task.VerificationMethod = TaskVerificationMethod.MetricException;
        await store.Update(task, ct);
        await notifier.Send(VerificationException, "Task needs manual verification", $"{task.Title}: {reason}", task,
            await notifier.Operators(ct), "OPERATOR", ct);
        return AutoVerificationOutcome.ManualFallback;
    }

    private static bool MetricContract(OperationalEvent ev, out decimal observed, out decimal warning)
    {
        observed = warning = default;
        if (string.IsNullOrWhiteSpace(ev.MetadataJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(ev.MetadataJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("measurementMode", out var mode) && mode.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(mode.GetString())
                && root.TryGetProperty("thresholdUnit", out var unit) && unit.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(unit.GetString())
                && root.TryGetProperty("metric", out var metric) && metric.TryGetDecimal(out observed)
                && root.TryGetProperty("warningThreshold", out var threshold) && threshold.TryGetDecimal(out warning)
                && observed == ev.MetricValue;
        }
        catch (JsonException) { return false; }
    }
}
