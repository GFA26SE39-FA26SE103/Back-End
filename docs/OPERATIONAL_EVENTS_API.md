# Operational Event history — 2026-11-06

Read-only API for ADMIN, OPERATOR and MANAGER; STAFF is forbidden. Responses disable caching. No event creation, manual clear, Incident conversion or schema changes are introduced.

| Route | Result |
| --- | --- |
| `GET /api/operational-events` | `{items,totalCount,page,pageSize,generatedAt,timeZone}` |
| `GET /api/operational-events/zones` | `[{zoneId,name,status}]`, including inactive historical zones |
| `GET /api/operational-events/{eventId}` | `{event,tasks,hasMoreTasks}` with up to 100 recent linked tasks |

List filters: `status=ALL|ACTIVE|CLEARED`, `priority=ALL|WARNING|CRITICAL`, optional `zoneId`, `from`, `to`, `page`, `pageSize`. Defaults are **All statuses/priorities/zones/history**, page 1, size 50. Status/priority values are case-insensitive. Page must be 1–10000; size 1–100. Unknown/empty Zone IDs are rejected; inactive Zones are allowed.

Dates are inclusive store-local calendar dates from year 2000 through today; blank dates leave that bound open. Filtering uses `DetectedAt`, with individually converted UTC midnight bounds, and excludes future detections. Invalid filters return 400 `INVALID_EVENT_QUERY`; unknown Zone/Event returns 404. Database/timeout failures return sanitized 503 `EVENT_DATA_UNAVAILABLE`, not successful empty data.

List count and paging execute in SQL with stable `DetectedAt DESC, EventId DESC` ordering. Concurrent writes can change counts/pages between reads; this is not a historical snapshot. Priority/status are latest persisted values. Public views include source code, event type, Zone/camera labels, latest metric/measurement type, detection/observation/clear/update times and linked Task count. Private metadata, stream URLs and credentials are not returned. Timestamps serialize as UTC. Clearing an Event does not verify its Task.

Verification: **16 focused Event history tests**, full non-SQL backend suite **526 passed**, backend build passed. SQL translation/filter composition verified without live SQL execution. Frontend focused Event/Task/Analytics suite: **21 passed**; frontend build passed. Edge verified All/status filtering, clear, event-to-task links and All tasks using mocked responses. No staging SQL, deployment or end-to-end acceptance is claimed.
