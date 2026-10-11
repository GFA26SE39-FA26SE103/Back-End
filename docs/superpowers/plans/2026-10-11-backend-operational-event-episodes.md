# Backend Operational Event Episodes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace one-`OperationalEvent`-per-measurement persistence with reliable operational-event episodes keyed by camera, zone, and operational event type.

**Architecture:** Keep the current Camera → AI zone measurement → rule evaluator pipeline. Extend rule evaluation with recovery hysteresis, then use the existing serializable `ISetupStore` transaction boundary to open, update, escalate, or clear one active row for `(CameraId, ZoneId, EventTypeId)`. Keep existing incident handling as a compatibility boundary in this change; ResponsePolicy-based incident qualification is a separate implementation because it changes the business escalation flow.

**Tech Stack:** .NET, C#, EF Core, SQL Server, BackgroundService monitoring worker, xUnit.

---

## Current-state findings

- The live V2 database is already migrated and enforces one active event per `(camera_id, zone_id, event_type_id)`.
- Existing 852 `OperationalEvent` rows are preserved as closed historical rows.
- Backend EF models are stale: `OperationalEvent` does not expose the new episode columns, and `MonitoringRule` does not expose `event_type_id`.
- `MonitoringIncidentWriter.EventIdentity` currently includes session and sequence, so every accepted measurement creates another event row.
- `MonitoringCameraCoordinator` already evaluates each configured zone independently, and `AiZoneMeasurement` already carries `ZoneId`.
- `MonitoringRuleEvaluator` supports breach sustain and duplicate/continuity checks, but has no recovery hysteresis or explicit clear transition.

## Locked behavior

- Episode key: `CameraId + ZoneId + OperationalEventTypeId`.
- Same camera and event type in two zones produces two independent episodes.
- Different cameras monitoring the same zone/event type may each have an independent episode.
- Same key with repeated measurements updates one active row.
- Warning → Critical updates the same row; priority does not downgrade during the episode.
- Sustained recovery clears the row; a later sustained breach creates a new row.
- Missing/unavailable measurements never create, update, or clear an operational event and are never interpreted as zero.
- V1 combines multiple ROIs mapped to the same camera/zone/event type into one zone-level episode.
- No cross-camera takeover or fallback is added.
- No legacy event rows are deleted or merged.

## Task 1: Synchronize the EF model with Database V2

**Files:**
- Modify `src/Supermarket.Infrastructure/Persistence/Scaffolded/OperationalEvent.cs`
- Modify `src/Supermarket.Infrastructure/Persistence/Scaffolded/MonitoringRule.cs`
- Create `src/Supermarket.Infrastructure/Persistence/Scaffolded/OperationalEventType.cs`
- Modify `src/Supermarket.Infrastructure/Persistence/Scaffolded/AppDbContext.cs`
- Modify `src/Supermarket.Infrastructure/Persistence/AppDbContext.Extensions.cs`
- Test `tests/Supermarket.Tests/MonitoringRuntimeSchemaApiFlowTests.cs`

- [ ] Add `EventTypeId`, `Priority`, `LastObservedAt`, `ClearedAt`, and `UpdatedAt` to the operational-event model while retaining the legacy `EventType` column.
- [ ] Add `EventTypeId` and its relationship to the monitoring-rule model while retaining `IncidentTypeId` for compatibility.
- [ ] Add the minimal `OperationalEventType` entity and relationships required by monitoring; do not scaffold unrelated V2 tables in this change.
- [ ] Map the filtered unique active-episode index exactly as `(CameraId, ZoneId, EventTypeId) WHERE ClearedAt IS NULL`.
- [ ] Add a schema integration test that reads and writes all required episode columns against SQL Server.
- [ ] Keep the monitoring worker disabled until this task passes, because the old model cannot safely insert into the migrated table.

## Task 2: Add recovery hysteresis to rule evaluation

**Files:**
- Modify `src/Supermarket.Domain/MonitoringEvaluation.cs`
- Modify `src/Supermarket.Application/MonitoringRuntimeContracts.cs`
- Modify `src/Supermarket.Application/MonitoringSnapshotReader.cs`
- Test `tests/Supermarket.Tests/MonitoringPolicyTests.cs`

- [ ] Extend `RuleEvaluationState` with recovery timing state.
- [ ] Extend `RuleEvaluationResult` with an explicit clear signal; do not infer clearing merely from `EligibleSeverity == null`.
- [ ] Start recovery only when a valid measurement is below the warning threshold.
- [ ] Reset recovery progress if the metric breaches again, continuity changes, configuration changes, or sequence ordering breaks.
- [ ] Use an optional `recoverySec` value from existing rule parameters; default it to `SustainSec` so no additional schema migration is required.
- [ ] Keep existing duplicate and out-of-order observation rejection.
- [ ] Test breach sustain, recovery sustain, interrupted recovery, Warning → Critical escalation, duplicates, and continuity breaks.

## Task 3: Apply episode changes through the existing transaction boundary

**Files:**
- Modify `src/Supermarket.Application/MonitoringIncidentWriter.cs`
- Test `tests/Supermarket.Tests/MonitoringIncidentWriterTests.cs`

- [ ] Define an application-facing episode command containing the key, transition, metric, priority, source timestamp, source session/sequence, rule, configuration fingerprint, and bounded metadata.
- [ ] Implement indexed active lookup without loading event history into memory.
- [ ] For `OPEN`, insert only when no active key exists.
- [ ] For `UPDATE`/`ESCALATE`, update metric, maximum priority, `LastObservedAt`, metadata, and `UpdatedAt` on the existing row.
- [ ] For `CLEAR`, set `ClearedAt`, final metric, `LastObservedAt`, and `UpdatedAt`.
- [ ] Preserve `DetectedAt` as the episode's first sustained-breach time.
- [ ] Populate both `EventTypeId` and the legacy `EventType` code.
- [ ] Use a transaction plus the database unique index as the final concurrency guard. On a duplicate-key race, reload and update the winning active episode rather than returning a 500 or inserting another row.
- [ ] Bound metadata to the fields needed for evidence and idempotency; do not store frames or unbounded detector output.

## Task 4: Replace sample persistence with episode transitions

**Files:**
- Modify `src/Supermarket.Application/MonitoringIncidentWriter.cs`
- Modify `src/Supermarket.Infrastructure/Monitoring/MonitoringCameraCoordinator.cs`
- Modify `src/Supermarket.Api/Program.cs`
- Modify `tests/Supermarket.Tests/MonitoringIncidentWriterTests.cs`

- [ ] Remove session/sequence-derived event IDs as the operational-event identity.
- [ ] On sustained breach with no active episode, send `OPEN`.
- [ ] On subsequent valid breaches, send `UPDATE`; send `ESCALATE` only when priority increases.
- [ ] On sustained recovery, send `CLEAR`.
- [ ] Do not write an `OperationalEvent` merely because `ShouldSample` is true.
- [ ] Continue exposing current measurements from runtime memory; raw measurement-history persistence is not part of this episode change.
- [ ] Preserve stale-configuration checks before applying a transition.
- [ ] Return the active `OperationalEventId`, priority, and transition result to the coordinator.

## Task 5: Preserve zone isolation and unavailable-data behavior

**Files:**
- Modify `src/Supermarket.Infrastructure/Monitoring/MonitoringCameraCoordinator.cs`
- Modify `src/Supermarket.Application/MonitoringRuntimeContracts.cs`
- Test `tests/Supermarket.Tests/MonitoringRuntimeApiFlowTests.cs`

- [ ] Continue matching each AI measurement by `ZoneId + ConfigId + ConfigVersion`.
- [ ] Evaluate and persist each zone/rule pair independently.
- [ ] Skip rule evaluation and episode persistence when monitoring readiness is `NOT_READY`, the AI page is unavailable, or the expected zone measurement is absent.
- [ ] Do not synthesize zero counts for unavailable measurements.
- [ ] Ensure a camera-wide visual-health failure suppresses all zone measurements from that camera without clearing active episodes.
- [ ] Add an integration test where one camera reports simultaneous overcrowding in two zones and verify two active rows with different `ZoneId` values.

## Task 6: Keep incident compatibility contained

**Files:**
- Modify `src/Supermarket.Application/MonitoringIncidentWriter.cs`
- Modify `src/Supermarket.Application/MonitoringRuntimeContracts.cs`
- Test `tests/Supermarket.Tests/MonitoringRuntimeApiFlowTests.cs`

- [ ] Ensure repeated measurements within one episode cannot create repeated incidents.
- [ ] Link at most one current compatibility incident to an episode while the existing IncidentPolicy path remains in service.
- [ ] Do not treat this compatibility behavior as final ERD v4 compliance.
- [ ] Do not implement ResponsePolicy, ResponsePolicyAction, task creation, notification, or recommendation in this change; prepare a separate reviewed plan for that business-flow cutover.

## Task 7: Verification

- [ ] Run focused tests:

```powershell
dotnet test 'D:\Study\FA26\SEP490\Implement\Back-End\tests\Supermarket.Tests\Supermarket.Tests.csproj' --filter 'FullyQualifiedName~MonitoringEvaluationTests|FullyQualifiedName~MonitoringIncidentWriterTests|FullyQualifiedName~MonitoringWorkerTests|FullyQualifiedName~MonitoringRuntimeApiFlowTests|FullyQualifiedName~MonitoringRuntimeSchemaApiFlowTests'
```

- [ ] Run the full backend suite:

```powershell
dotnet test 'D:\Study\FA26\SEP490\Implement\Back-End\Supermarket.sln'
```

- [ ] Run `git diff --check`.
- [ ] Start the API with monitoring disabled and verify database readiness.
- [ ] Enable monitoring against a controlled source and verify:
  - repeated violating measurements update one row;
  - two violating zones create two rows;
  - escalation changes priority on the same row;
  - sustained recovery sets `ClearedAt`;
  - unavailable input creates no zero measurement and does not clear the episode.
- [ ] Query row counts before and after the controlled run and confirm the 852 legacy rows remain unchanged.

## Scalability guardrails

- Growth variable: cameras × mapped zones × enabled event types, not frames per second.
- Maximum active rows are bounded by configured camera/zone/event-type combinations.
- Active lookup must use the filtered unique index and never scan all historical events.
- No raw frames or detector boxes are persisted in operational-event metadata.
- Measurement-history aggregation into `ZoneMetric` requires a separate retention and bucket-size decision; do not move the previous per-frame row explosion into another table without that policy.
