# Checkout Backend contract

Implementation branch: `feature/checkout-counter-backend`. V17 was applied to `FA26SE103_Dev` and scaffolded on 2026-11-11. This does not assert another environment, deployed Backend, Frontend or AI-Service has been updated.

## Configuration routes

All paths below have prefix `/api`. Reads allow ADMIN, OPERATOR and MANAGER; mutations require ADMIN. STAFF cannot access these routes. HTTP and application layers both check authorization.

| Method | Path | Result |
| --- | --- | --- |
| GET | `/zones/{zoneId}/checkout-counters` | Scoped counter views |
| POST | `/zones/{zoneId}/checkout-counters` | 201 with counter and Location |
| GET | `/checkout-counters/{counterId}` | Counter view |
| PATCH | `/checkout-counters/{counterId}` | Updated counter view |
| DELETE | `/checkout-counters/{counterId}?expectedUpdatedAt={UTC}` | 204; disabled counter/mappings only |
| GET | `/cameras/{cameraId}/checkout-mappings` | Scoped mapping views |
| GET | `/checkout-counters/{counterId}/camera-mappings` | Scoped mapping views |
| PUT | `/checkout-counters/{counterId}/camera-mappings/{cameraId}` | Create/update mapping view |
| DELETE | `/checkout-counters/{counterId}/camera-mappings/{cameraId}?expectedUpdatedAt={UTC}` | 204 |

Counter request: `code`, `name`, `isEnabled` (default false), `expectedUpdatedAt` (absent for create; required for existing counter). PATCH is a full editing DTO, not JSON Patch.

Mapping request: `laneRoi`, `staffRoi`, `isEnabled`, `isQueueSource`, `expectedUpdatedAt`. Both polygon arrays are required even when disabled; each contains 3–1000 distinct normalized `{x,y}` vertices and must be non-degenerate/non-self-intersecting. Do not repeat the closing vertex. No adjacency, overlap or containment relationship is required between the two ROIs. Mapping IDs and camera/counter associations are returned by the API, not chosen by the client.

Views return `createdAt`/`updatedAt`; counter views use `checkoutCounterId`, and mapping views use `mappingId`, `cameraId`, `checkoutCounterId`. No camera credentials or secret reference is returned. Send the last saved `updatedAt` back for updates/deletes. A stale/missing version returns 409 `CHECKOUT_CONFIGURATION_CHANGED`.

## Required editing sequence

1. Create a disabled counter in an existing `zoneType=CHECKOUT` Zone.
2. Save complete Lane/Staff ROI mappings. Camera and counter Zone must have the same Floor, thus the same supermarket.
3. Enable at least one mapping and select exactly one enabled mapping as queue source. Enabling mappings requires an active camera with a tested, enabled connection; monitoring review separately validates source/ROI compatibility.
4. Enable the counter with its current version. An enabled counter must always retain exactly one enabled queue source. Multiple enabled staff-observation mappings are allowed.
5. Save/review/activate the existing Zone monitoring configuration when the AI deployment is ready.

Deactivate Zone monitoring before editing counter/mapping configuration or replacing an involved camera source. To switch queue sources, deactivate monitoring and disable the counter, demote the old source, promote the new source, re-enable the counter, then review/activate. No automatic fallback or hidden source change occurs. Temporary runtime health failures do not disable saved configuration.

Changing a Zone away from CHECKOUT is rejected while its counters exist. Generic `CameraZoneMapping` and its API remain separate and unchanged. Floor-map coordinates are not camera-frame ROI coordinates.

## Limits and failures

`Checkout:Configuration` controls scoped capacity: default 128 counters/Zone, 16 mappings/counter and 128 mappings/camera (supported ceilings match these limits). Read queries fetch only limit + 1 rows; overflow rejects rather than silently hiding part of the configuration.

- 503 `CHECKOUT_SCHEMA_NOT_READY`: V17 tables/columns/required uniqueness/FKs are absent or unavailable.
- 409 `MONITORING_ACTIVE`: deactivate affected monitoring before changing configuration.
- 409 `CHECKOUT_QUEUE_SOURCE_REQUIRED` / `CHECKOUT_QUEUE_SOURCE_DUPLICATE`: invalid enabled source selection.
- 409 `CHECKOUT_CAMERA_NOT_READY`: active/tested/enabled source is required.
- 409 `CHECKOUT_CAPACITY_EXCEEDED`: scoped configuration limit exceeded.
- 422 `INVALID_POLYGON`, `CROSS_FLOOR_MAPPING`, `CHECKOUT_ZONE_REQUIRED`: invalid geometry/scope.

SQL uniqueness/range locking and optimistic timestamps protect concurrent writes; non-SQL tests are not proof of SQL concurrency acceptance.

## Runtime read and rule configuration

`GET /api/zones/{zoneId}/checkout-runtime` is read-only and no-store. State is `NOT_ACTIVE`, `UNAVAILABLE` or `READY`. Unknown/unavailable `queueCount`, `staffedCounterCount`, `queuePerStaffedCounter` and rule metrics are null, never fabricated zero. Counter presence is PRESENT/ABSENT/UNKNOWN. `noStaffedCapacity=true` is an explicit valid-measurement condition, not an Infinity/NaN ratio. The legacy `incidentId` field is not reused for event identity; checkout rule views expose `operationalEventId`.

AI observations remain camera-local. Backend confirms presence/absence for 15 seconds per source, then fuses by counter: any confirmed PRESENT wins; ABSENT requires all enabled sources confirmed absent; otherwise UNKNOWN. Health, missing/stale observations, gaps/session changes and config replacement invalidate the affected evidence. Only the selected queue source supplies each counter's queue count. A secondary source can contribute staff presence but cannot take over queue counting.

Checkout uses the existing Zone MonitoringRule API/catalog mapping to CHECKOUT_CAPACITY. Required settings:

- `thresholdUnit=PEOPLE_PER_STAFFED_COUNTER`;
- explicit positive `warningThreshold < criticalThreshold`, with up to four decimal places; no seeded/invented checkout defaults;
- `parametersJson` object with `minQueue` (integer >= 1) and `noStaffedAlertLevel` (`WARNING` or `CRITICAL`, an Event Alert Level, not SLA priority);
- normal configurable Sustain/ClearDuration/Cooldown.

Q is queue demand across configured independent physical lanes; S is distinct staffed counters. Below minQueue the condition is normal. For S > 0, rule input is Q/S rounded to four decimals. For S = 0 with demand meeting minQueue, the configured noStaffedAlertLevel drives the condition through the corresponding configured threshold, still respecting sustain. Insufficient coverage skips evaluation, clear and AUTO verification.

One Zone aggregate owns each rule episode. The camera whose valid observation first leads to a confirmed trigger is saved once as event CameraId; updates retain that origin and metadata lists contributing cameras/mappings and aggregate evidence. The existing active RuleId+ZoneId uniqueness is reused. Existing SLA/action/Task dispatch is reused without cross-event dedup changes: distinct OE-01/OE-04 can each create their configured Task.

## AI rollout gate

`Checkout:Monitoring:AiContractEnabled` defaults to false. CRUD works independently, but Review/Activate blocks checkout runtime until a compatible, validated [AI protocol v2](CHECKOUT_AI_PROTOCOL.md) deployment is explicitly enabled. A v1/invalid response fails closed; it is not interpreted as zero demand or no staff. Generic-only monitoring continues using protocol v1. This Backend change does not implement or deploy the AI-Service/Frontend.

The configured camera session is shared between generic and checkout observations; there is no detector/session per counter. Staff ROI occupancy is an observed-person proxy at the cashier position, not employee identity recognition or transaction detection. Queue counting reuses the existing track dwell logic (>=5 seconds inside ROI), not a new stationary-person classifier. Cross-camera person ReID and POS are not included. Validate physical queue coverage against footage; selecting one source per counter does not deduplicate people between different counters. Waiting Time remains separate and unsupported.
