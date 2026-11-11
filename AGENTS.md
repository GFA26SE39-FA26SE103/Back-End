# AGENTS.md — FA26SE103 Backend

> Updated planning baseline: **2026-11-11**. Target repository: `GFA26SE39-FA26SE103/Back-End`, normally `dev`. This document guides coding agents; **it does not assert that planned features are implemented**. Read current branch/source and dated evidence before editing.

## Read first and establish scope

1. Read this file and any workspace-root `AGENTS.md` that applies.
2. Read [README.md](README.md), relevant current source/tests and [docs/API.md](docs/API.md).
3. Review [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) for historical decisions; **newest dated sections supersede older snapshots**.
4. Use [docs/VALIDATION.md](docs/VALIDATION.md) as **dated evidence only**, not proof of the current commit or environment.
5. If changing persistence, inspect the **canonical Database repository SQL scripts**, approved ERD and the target environment before editing.
6. For deployments, follow [docs/DEPLOY_DEV.md](docs/DEPLOY_DEV.md).

Decision priority: latest explicit approved user/team decision and applicable BM-04 commitment → official current business requirements → approved database/schema → current source/code contracts → historical notes. If these conflict, report the discrepancy and ask for a decision rather than silently inventing a rule. BM-04 mainly commits scope, test coverage and outcomes; it does not mandate a particular checkout table layout.

**Do not equate:** approved design, feature branch, merged `dev` implementation, deployed SQL schema, successful automated tests, and end-to-end staging acceptance.

## Current workflow names and boundaries

- **WF-01 Configure and Activate Store Monitoring**.
- **WF-02 Detect and Route Operational Issues**.
- **WF-03 Execute and Verify Operational Tasks**.
- **WF-04 Plan Staff Shifts and Resolve Coverage Changes**.
- **WF-05 Review Operational Performance and Generate Reports**.

Avoid obsolete four-mainflow labels as the current business workflow model; older docs may retain them as implementation history.

### Operational monitoring versus Incident qualification

- Valid AI measurements are evaluated through `MonitoringRule` and create/update **OperationalEvents**, not Incidents directly.
- Event alert level (`WARNING`/`CRITICAL`) is **not** SLA response priority (`CRITICAL`/`HIGH`/`MEDIUM`/`LOW`). Never map these by matching strings.
- Event episodes update/clear under the configured lifecycle; no silent Event-to-Incident conversion.
- Current post-cutover response policy persistence is **`SLAPolicy` and `SLAPolicyAction`**. Legacy `/response-policies` endpoints may remain as compatibility facades; **do not recreate the removed legacy response-policy tables**.
- WARNING may notify without Task creation; CRITICAL may create an operational Task only when the approved enabled policy/action applies. No arbitrary fallback if missing, ambiguous or invalid.
- Camera/service health events remain technical health records, not automatically operational Incidents.
- Staff Reports require Staff confirmation and appropriate report type/duplicate/qualification handling before Incident/Task actions. AI draft suggestions do not create a persisted Incident or Task by themselves.

### Dispatch, SLA, task verification

- Select exactly one valid approved SLA policy for the applicable EventType/IncidentType/standalone TaskType path; capture policy identity/version, priority, deadlines and provenance where required.
- SLA response clock: Task creation to first valid acceptance; resolution clock: Task creation to Verified. Deadlines use continuous wall-clock time; retries, reassignment and shift changes do not reset them.
- Eligibility considers specialty, published active shift, availability/check-in, zone and workload. Ring progression: same Zone → adjacent same-Floor Zones → remaining same-Floor Zones → adjacent Floors → other Floors → Operator exception. Follow current implementation and policy; no magic global retry count.
- `Handle Now`, `Handle Later`, `Cannot Handle` remain distinct. First valid offer acceptance wins atomically; competing offers are cancelled; expired offers are not declines.
- Task origin is exactly one of `OPERATIONAL_EVENT`, `INCIDENT`, `STAFF_REPORT`, `MANUAL`, respecting exclusive origin FKs.
- For Incident corrective Tasks, valid After Photo **or** Completion Note can satisfy the submission requirement subject to the precise current task contract; Before Photo is optional. Do not weaken task-type-specific mandatory checks.
- Mandatory MANUAL verification includes snapshotted CRITICAL SLA response priority and configured safety task types. Do not substitute an OperationalEvent CRITICAL alert level.
- MANUAL rejection #1 returns to work; #2 requires an eligible replacement/reassignment path without SLA reset, with Operator handling when no eligible replacement is available.
- Close an Incident only after its mandatory Tasks are verified and blockers are cleared. Do not close merely because an Event cleared.

### Workforce, analytics, recommendations

- SWAP/COVER: Staff request → peer response → Manager decision. Recheck specialty, shift, availability, overlap, weekly-hour cap and check-in rules at the relevant decision points.
- Both participants must satisfy the current same non-null primary specialty requirement for eligible transfers; do not silently bypass it.
- Keep OperationalEvent and Incident analytics separate. Missing observations are unavailable, **never zero**.
- Recommendations are advisory; accepting/viewing them does not directly modify shifts, policies or Tasks.

## Backend architecture

```text
src/Supermarket.Api/             Controllers, authentication, validation and DI
src/Supermarket.Application/     Application use cases, orchestration and contracts
src/Supermarket.Domain/          Domain models and business invariants
src/Supermarket.Infrastructure/  EF persistence, adapters, secrets and workers
tests/Supermarket.Tests/        Domain, application, API and integration checks
```

- Controllers are thin; never use `DbContext` directly from API controllers.
- Domain remains independent of ASP.NET/EF.
- Do not expose scaffolded EF entities as HTTP DTOs.
- SQL Server is persistent source of truth; Redis is not a second authoritative database.
- Python AI provides detections/measurements; backend owns operational policy, lifecycle and security.
- Use ProblemDetails and typed domain/application errors; do not expose stack traces, connection strings, credentials or raw internal SQL.

## Database-first: mandatory

**Do not use EF code-first migrations** for this repository's team schema changes.

Approved design/ERD → reviewed **versioned SQL script in Database repository** → approved environment preflight/backup/application → regenerate EF scaffold → inspect generated diff → update custom mappings/contracts/use cases/tests/docs.

- No business logic inside generated scaffold files; don't hand-edit them to simulate a deployed schema.
- Do not infer the database's applied version from docs or `dev` alone. Distinguish ERD V7 from later V9–V15 SQL versions and actual per-environment state.
- Preserve independent OperationalEvent/Incident/StaffReport provenance and exclusive Task origin references.
- Coordinate all new tables/FKs/constraints with the Database repo and other services.
- Never apply SQL to a shared or production database without an explicit instruction identifying the target and an agreed safety plan.

## Checkout counters / checkout-specific ROI — planned, not implemented

Status: design/integration plan only; do not assume tables, APIs or runtime metrics exist. Reconcile this section with current Report 3, the approved Database schema and the Task plan before changing schema.

- `CheckoutCounter` is a logical lane identity tied to a Checkout Zone (`zone_id`, `code`, `name`, `is_enabled`). Floor-plan geometry is not in the current K9 data-model scope; add it only after separate ERD/Database approval.
- `CameraCheckoutMapping` associates a camera and counter and stores `lane_roi`, `staff_roi`, `is_enabled`, plus an explicit queue-measurement-source selection. A counter may have multiple camera mappings, but exactly one enabled mapping is selected as its queue-count source. Camera and counter must be on the same Floor. Keep generic `CameraZoneMapping` behavior and API unchanged.
- Lane ROI and Staff ROI are independent polygons in normalized camera-frame coordinates. Both are required for an enabled checkout mapping; do not require adjacency, overlap or containment.
- Aggregate staff-presence state by `counterId`: `PRESENT` after at least one enabled, healthy, fresh and sufficiently observable mapping confirms presence for about 15 seconds; `ABSENT` only after every enabled mapping is healthy/fresh/sufficiently observable and confirms absence for about 15 seconds; otherwise `UNKNOWN`/`UNAVAILABLE`. Unknown time does not count toward either persistence period. When valid sources disagree, a persisted `PRESENT` wins and disagreement is observable for diagnosis. Validate both 15-second values with footage.
- Queue count comes only from the selected queue-source mapping for each counter; never sum overlapping camera detections or use cross-camera ReID. The existing “standing at least five seconds” queue definition is provisional and must be validated against footage. Do not automatically switch queue sources when the selected source fails.
- `Observed Staffed Checkout Ratio` describes observed staff presence, not an active transaction or POS-level utilization. For OE-04, define demand `Q` from valid queue measurements and staffed supply `S` as distinct counters in `PRESENT`; configure the queue minimum, demand-to-staffed-capacity threshold and persistence through MonitoringRule, not hardcoded constants. `Q > 0` with `S = 0` indicates pressure only when coverage is sufficient; otherwise report unavailable, never zero.
- OE-01 Long Queue (queue demand alone) and OE-04 Checkout Capacity Issue (demand relative to staffed supply) may coexist. Check cross-event Task deduplication/response before enabling automatic Task creation (Task plan K9.8); do not silently merge events or create duplicate Tasks.
- Waiting time is a separate metric requiring validated queue-entry and service-start signals/tracking. A lane polygon alone does not establish waiting time.

## Spatial, security and runtime invariants
## Spatial, security and runtime invariants

- Supermarket → Floors → Zones; Cameras belong to Floors, and camera/zone mappings must not cross Floors.
- No face recognition, cross-camera person ReID or persistent customer identity.
- Only authorized admins change configuration and accounts; preserve last-active-Admin protections.
- Hash passwords with approved hasher; store camera credentials as encrypted recoverable secrets or protected references, never plain text in source/logs.
- Do not assume waiting-time/checkout-capacity model accuracy, camera availability or operational actions from an unvalidated demo.
- Missing, stale or unreliable measurements must not create spurious Events, qualify Incidents, produce successful AUTO verification or drive recommendations.

## Implementation and verification protocol

**Before edits**

1. Confirm repository, branch, current SHA and scope; preserve unrelated changes.
2. Read relevant contracts/controllers/use cases/entities/adapters/tests and any Database scripts involved.
3. Separate *required behavior*, *current implementation*, *known gap*, and *proposed change* in the task plan.
4. Flag schema/API/official-document contradictions rather than asserting one stale document as truth.

**After behavior edits**

1. Add focused domain/application/API regression tests.
2. Run targeted tests, then relevant non-SQL suite; run SQL integration only with approved isolation or rollback.
3. Update API/OpenAPI and affected integration docs; note cross-repository changes needed (Frontend, Mobile, AI Service, Database).
4. Report exact commands/outcomes, commit/branch, changed files, unrun tests and remaining gaps. Do not copy old pass counts into current results.

```powershell
dotnet restore --configfile NuGet.Config
dotnet test --no-restore --filter "Category!=SqlIntegration"
dotnet publish src/Supermarket.Api -c Release --no-restore
```

For docs-only changes: inspect links/context and use `git diff --check`; avoid claiming runtime validation.

## Completion gates and honest reporting

Do not claim full Review 2/end-to-end completion until evidence covers StaffReport qualification and routine flow, approved SLA policy matching and escalation, multi-ring dispatch and operator fallbacks, AUTO/MANUAL verification/rework, mandatory Incident closure, analytics/export and unavailable-metric handling, and cross-role staging scenarios. Check current BM-04/Report 5 commitments for exact minimum tests and thresholds; do not fabricate them here.

The newest dated `docs/VALIDATION.md` entries are useful history, but a passed test on an earlier branch or schema is not proof for later changes.

## Git and agent conduct

- Respect the user's branch instructions; do not create worktrees/branches unless asked.
- Never commit secrets, real camera URLs/passwords, unredacted media, local appsettings, key rings, generated build/test data or production dumps.
- Request explicit approval before destructive changes, shared DB writes, deploys or irreversible migration steps.
- Make narrow, reviewable commits and preserve unrelated files.
- Report what was **actually changed**, what was **only planned**, and what remains unverified.
