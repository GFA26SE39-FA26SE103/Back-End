using System.Collections.Concurrent;
using Supermarket.Domain;

namespace Supermarket.Application;

public static class CameraHealthEventTypes
{
    public const string StreamUnavailable = "STREAM_UNAVAILABLE";
    public const string ViewBlocked = "CAMERA_VIEW_BLOCKED";
    public const string ViewBlurred = "CAMERA_VIEW_BLURRED";
    public const string ViewFrozen = "CAMERA_VIEW_FROZEN";
    public const string FrameInvalid = "CAMERA_FRAME_INVALID";

    public static readonly string[] Visual = [ViewBlocked, ViewBlurred, ViewFrozen, FrameInvalid];
    public static bool IsVisual(string value) => Visual.Contains(value, StringComparer.Ordinal);
}

public sealed record CameraHealthView(
    Guid CameraId,
    string ConnectionStatus,
    string ProcessingAvailability,
    string MonitoringReadiness,
    string[] ActiveHealthIssues,
    DateTime? LastSeenAt,
    DateTime? ObservedAt);

public sealed record CameraHealthTransition(string[] OpenIssues, string[] ResolvedIssues, CameraHealthView View);

/// <summary>
/// Process-local current-state projection for health hysteresis and MF-02 fail-closed readiness checks.
/// CameraHealthEvent remains the persistent audit/history record.
/// </summary>
public sealed class CameraHealthRuntimeState
{
    private sealed class IssueStreak { public int Bad; public int Good; }
    private sealed class CameraState
    {
        public object Sync { get; } = new();
        public HashSet<string> Active { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IssueStreak> Streaks { get; } = [];
        public bool? ProcessingAvailable;
        public DateTime? ObservedAt;
        public bool Initialized;
    }

    private readonly ConcurrentDictionary<Guid, CameraState> states = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gates = new();
    private readonly int badObservationsToOpen;
    private readonly int goodObservationsToResolve;

    public CameraHealthRuntimeState(int badObservationsToOpen = 3, int goodObservationsToResolve = 2)
    {
        if (badObservationsToOpen is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(badObservationsToOpen));
        if (goodObservationsToResolve is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(goodObservationsToResolve));
        this.badObservationsToOpen = badObservationsToOpen;
        this.goodObservationsToResolve = goodObservationsToResolve;
    }

    public async Task<T> Serialize<T>(Guid cameraId, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = gates.GetOrAdd(cameraId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }

    public CameraHealthTransition Observe(Camera camera, ProbeResult probe, IEnumerable<string> persistedIssues, DateTime observedAt)
    {
        var persisted = persistedIssues.ToHashSet(StringComparer.Ordinal);
        var state = states.GetOrAdd(camera.CameraId, static _ => new CameraState());
        lock (state.Sync)
        {
            state.Active.UnionWith(persisted);
            state.Initialized = true;
            state.ObservedAt = observedAt;
            state.ProcessingAvailable = probe.Success ? probe.ProcessingAvailable : null;

            if (!probe.Success)
            {
                state.Active.Add(CameraHealthEventTypes.StreamUnavailable);
            }
            else
            {
                state.Active.Remove(CameraHealthEventTypes.StreamUnavailable);
                if (probe.ProcessingAvailable == true)
                {
                    var observed = (probe.VisualIssues ?? []).ToHashSet(StringComparer.Ordinal);
                    foreach (var issue in CameraHealthEventTypes.Visual)
                    {
                        if (!state.Streaks.TryGetValue(issue, out var streak))
                            state.Streaks[issue] = streak = new IssueStreak();
                        if (observed.Contains(issue))
                        {
                            streak.Bad = Math.Min(badObservationsToOpen, streak.Bad + 1);
                            streak.Good = 0;
                            if (streak.Bad >= badObservationsToOpen)
                                state.Active.Add(issue);
                        }
                        else
                        {
                            streak.Bad = 0;
                            streak.Good = Math.Min(goodObservationsToResolve, streak.Good + 1);
                            if (streak.Good >= goodObservationsToResolve)
                                state.Active.Remove(issue);
                        }
                    }
                }
            }

            var open = state.Active.Except(persisted, StringComparer.Ordinal).Order().ToArray();
            var resolved = persisted.Except(state.Active, StringComparer.Ordinal).Order().ToArray();
            return new(open, resolved, Build(camera, state.Active, state.ProcessingAvailable, state.ObservedAt, operational: true));
        }
    }

    public CameraHealthView Snapshot(Camera camera, IEnumerable<string> persistedIssues, bool operational)
    {
        var persisted = persistedIssues.ToHashSet(StringComparer.Ordinal);
        if (!states.TryGetValue(camera.CameraId, out var state))
            return Build(camera, persisted, null, null, operational);
        lock (state.Sync)
        {
            var active = state.Active.Union(persisted, StringComparer.Ordinal).ToArray();
            return Build(camera, active, state.ProcessingAvailable, state.ObservedAt, operational);
        }
    }

    public bool IsRecovered(Guid cameraId, string eventType, string connectionStatus)
    {
        if (eventType == CameraHealthEventTypes.StreamUnavailable)
            return connectionStatus == "ONLINE";
        if (!CameraHealthEventTypes.IsVisual(eventType) || !states.TryGetValue(cameraId, out var state))
            return false;
        lock (state.Sync)
            return state.Initialized && state.ProcessingAvailable == true && !state.Active.Contains(eventType);
    }

    private static CameraHealthView Build(Camera camera, IEnumerable<string> issues, bool? processingAvailable, DateTime? observedAt, bool operational)
    {
        var active = issues.Distinct(StringComparer.Ordinal).Order().ToArray();
        var processing = processingAvailable switch { true => "AVAILABLE", false => "UNAVAILABLE", null => "UNKNOWN" };
        var readiness = operational && camera.Status == "ACTIVE" && camera.HealthStatus == "ONLINE" && processingAvailable == true && active.Length == 0 ? "READY" : "NOT_READY";
        return new(camera.CameraId, camera.HealthStatus, processing, readiness, active, camera.LastSeenAt, observedAt);
    }
}
