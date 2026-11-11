# Manager analytics API — v1 (2026-11-06)

Implemented scope: WF-05 / FE-09–10 read-only recorded-data analytics and on-demand operational report export. This increment does not implement recommendations (FE-11), live camera access or SLA escalation workers.

## Routes and access

All routes require an authenticated **MANAGER**. ADMIN/OPERATOR/STAFF receive 403; unauthenticated requests receive 401. Responses use `Cache-Control: no-store`. Existing live-camera and Operator mutation permissions are unchanged.

| Method / route | Response |
| --- | --- |
| `GET /api/manager/analytics/overview` | `{period, summary}` — totals and aggregate KPIs |
| `GET /api/manager/analytics/trends` | `{period, days: [{date, summary}]}` — daily creation/detection cohorts, oldest first |
| `GET /api/manager/analytics/zones` | `{period, zones: [{zoneId, zoneName, summary}]}` — configured zones, including zones with no matching data |
| `GET /api/manager/analytics/export` | Download the same selected period as `csv`, `xlsx`, or `pdf` |

Every route accepts the same optional query parameters:

| Parameter | Definition |
| --- | --- |
| `from` | Local calendar date, `yyyy-MM-dd`, inclusive. Defaults to six days before `to`. |
| `to` | Local calendar date, inclusive. Defaults to today in the configured store timezone. Future dates are rejected. |
| `zoneId` | Existing Zone UUID. Omit for all zones; empty UUID is invalid. Inactive zones remain queryable for history. |

`/export` additionally accepts `format=csv|xlsx|pdf` (default `csv`). It performs one analytics read so its summary, daily rows and Zone rows share one `generatedAt` boundary. The file includes the local date range, configured timezone, Zone scope, generation timestamp, KPI summary, daily activity, Zone breakdown and data notes. CSV is UTF-8 with BOM; XLSX contains Summary, Daily, Zones and Notes worksheets; PDF is a paginated landscape report. Spreadsheet text is protected against formula injection.

Exports currently contain the recorded OperationalEvent, Incident and Task analytics available to these endpoints. They explicitly note that camera-health and AI-measurement time series are unavailable instead of fabricating or silently omitting that context. No stream credential, protected connection metadata or personal Staff data is exported. Each successful generation and renderer failure creates an `EXPORT_ANALYTICS_REPORT` AuditLog containing only format, dates, Zone, timezone, generation time and result status.

The range must contain 1–93 calendar days, beginning in year 2000 or later. Dates use the existing `Store:TimeZone` configuration (default `Asia/Ho_Chi_Minh`). Midnight boundaries are converted to UTC individually, including DST where configured. Query metadata exposes `from`, `to`, `timeZone`, `fromUtc`, `toUtcExclusive`, `generatedAt` (UTC) and `zoneId`.

Example:

```http
GET /api/manager/analytics/overview?from=2026-11-01&to=2026-11-06
Authorization: Bearer <manager-token>
```

For Vietnam, the above interval is `[2026-10-31T17:00:00Z, 2026-11-06T17:00:00Z)`. Rows dated after `generatedAt` are excluded even if the end-of-day boundary is later.

## Cohort semantics — required UI labels

- Events enter the selected period by `DetectedAt`; Incidents and Tasks by `CreatedAt`. Count each persisted entity once, not observations, offers, retry waves or assignments.
- Status and Event priority are the values stored when queried. The API does **not** reconstruct their values at the end of a past day. UI labels should say “Events detected in period”, “Tasks created in period”, “Current status” and “Latest critical”, rather than implying historical active-at-midnight counts or escalation counts.
- A Task created before the period is excluded even if it was verified during the period. A Task created inside the period can contribute its later acceptance/verification through `generatedAt`. Trends group those results by the Task's creation day, not completion day.
- Endpoints independently read current data. `generatedAt` bounds timestamps and SLA evaluation; it is not a temporal SQL snapshot or a guarantee of transactionally identical data across separate requests.
- Successful empty cohorts have count zero and duration/rate `null`, not invented measurements. SQL/timeout failures produce 503, never successful zeros or partial source totals.
- Recorded business data does not prove camera uptime or monitoring coverage. No people-footfall, queue measurement time series, heatmap, unique customer count or revenue KPI is inferred from Event counts.

## Summary schema and KPI definitions

Each `summary` contains `events`, `incidents` and `tasks`:

| Field | Calculation |
| --- | --- |
| `events.total` | Number of Event episodes detected in period. |
| `events.active` / `cleared` | Current status `ACTIVE` / `CLEARED` of those episodes. |
| `events.latestCritical` | Those episodes whose currently stored Event priority is `CRITICAL`; independent of SLA priority. |
| `events.clearTime` | Average `ClearedAt − DetectedAt` for `CLEARED` episodes with valid, non-future clear timestamps. |
| `incidents.recorded` | All Incident records created in period, including dismissed records for reconciliation. |
| `incidents.total` | Recorded minus `DISMISSED`, preserving the approved dismissed-Incident KPI exclusion. |
| `incidents.open` / `closed` / `dismissed` | Open excludes `CLOSED` and `DISMISSED`; closed and dismissed count their respective current statuses. |
| `incidents.resolutionTime` | Average `ClosedAt − CreatedAt` for `CLOSED` records with valid, non-future close timestamps; dismissed records excluded. |
| `tasks.total` | All Tasks created in period, including cancelled Tasks. |
| `tasks.byStatus` | Counts for OFFERED, ACCEPTED, IN_PROGRESS, SUBMITTED, VERIFIED, CANCELLED. All six keys are present; unrecognized persisted statuses remain additional keys rather than disappearing. |
| `tasks.responseTime` | Average `AcceptedAt − CreatedAt`, excluding cancelled Tasks, missing timestamps, negative durations and future acceptance. Manual assignment can populate AcceptedAt according to the existing workflow. |
| `tasks.completionTime` | Average `VerifiedAt − CreatedAt` for currently VERIFIED Tasks with valid, non-future verification. Submission alone is not completion. |
| `tasks.responseSla` | Evaluation against each Task's persisted `ResponseDeadlineAt`, using AcceptedAt. |
| `tasks.resolutionSla` | Evaluation against persisted `ResolutionDeadlineAt`, using VerifiedAt for currently VERIFIED Tasks. |

Duration objects are `{samples, averageSeconds}`. Durations use SQL second precision; averages are sample-weighted across SQL groups and never averages of averages. Zero samples give `averageSeconds: null`.

SLA objects are `{evaluated, met, breached, pending, unavailable, metPercent}`:

- Cancelled Tasks are excluded from all SLA counts.
- Missing deadlines, deadlines before Task creation, invalid/future completion timestamps, missing AcceptedAt on a non-OFFERED Task, or missing VerifiedAt on a VERIFIED Task are unavailable. No deadline is reconstructed from the current policy or Event priority.
- A valid completed response/resolution is met when its timestamp is **at or before** the persisted deadline; otherwise breached.
- An incomplete response/resolution is breached when the deadline is strictly before `generatedAt`; otherwise pending. Exactly at the deadline remains pending until exceeded.
- `evaluated = met + breached`; `metPercent = 100 × met / evaluated`. Pending/unavailable/cancelled Tasks are outside that denominator. No evaluated Tasks gives `metPercent: null`.
- These are reporting calculations only; they do not persist breaches or send escalation notifications.

Example empty summary:

```json
{
  "events": {"total": 0, "active": 0, "cleared": 0, "latestCritical": 0, "clearTime": {"samples": 0, "averageSeconds": null}},
  "incidents": {"recorded": 0, "total": 0, "open": 0, "closed": 0, "dismissed": 0, "resolutionTime": {"samples": 0, "averageSeconds": null}},
  "tasks": {
    "total": 0,
    "byStatus": {"OFFERED": 0, "ACCEPTED": 0, "IN_PROGRESS": 0, "SUBMITTED": 0, "VERIFIED": 0, "CANCELLED": 0},
    "responseTime": {"samples": 0, "averageSeconds": null},
    "completionTime": {"samples": 0, "averageSeconds": null},
    "responseSla": {"evaluated": 0, "met": 0, "breached": 0, "pending": 0, "unavailable": 0, "metPercent": null},
    "resolutionSla": {"evaluated": 0, "met": 0, "breached": 0, "pending": 0, "unavailable": 0, "metPercent": null}
  }
}
```

## Errors

Errors follow the existing ProblemDetails contract (`code`, sanitized `detail`, `requestId`).

| Status / code | Meaning |
| --- | --- |
| 400 `INVALID_ANALYTICS_QUERY` | Reversed/future/too-wide date range, date before 2000 or empty Zone UUID. |
| 400 `INVALID_REPORT_FORMAT` | Export format is not `csv`, `xlsx`, or `pdf`. |
| 400 model binding validation | Malformed date or UUID. |
| 401 / 403 | Missing authentication / role is not MANAGER. |
| 404 `NOT_FOUND` | Requested Zone does not exist. |
| 503 `ANALYTICS_UNAVAILABLE` | Database query or timeout failed. No partial successful result is returned. |
| 503 `REPORT_GENERATION_FAILED` | The selected report could not be generated completely. |

## Implementation and next increment

Persistence uses three sequential, SQL-side grouped queries (Events/Incidents/Tasks) per request. Raw Task lists are not downloaded or limited by the Operator board's 200-row cap. Daily grouping uses a balanced CASE tree bounded to 93 days, compatible with SQL Server's ten-level CASE nesting limit. No schema, seed, generated scaffold or shared data is changed.

Next: connect the Manager dashboard export action to this route, validate generated files against actual dev records, and measure representative query/generation latency for NFR-02. FE-11 recommendations remain separate work with persisted supporting data and explicit Manager decisions.
