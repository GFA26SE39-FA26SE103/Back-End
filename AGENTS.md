# AGENTS.md — FA26SE103 Backend

Project: AI-Powered Smart Supermarket Operations Monitoring System, GFA26SE39.
Assigned increment 03/10/2026: MF-02 ROI count/queue → OperationalEvent + live feed; see PROJECT_CONTEXT sections 39–41. The implementation stores OperationalEvents as episodes keyed by Camera + Zone + OperationalEventType while unresolved: repeated observations update the episode, escalation preserves the row, sustained recovery clears it, and a later breach opens a new episode. `MonitoringRule.outcome_type` keeps OperationalEvent and Incident persistence as separate branches; an OperationalEvent threshold breach does not create an Incident. A scoped MF-02 subset now implements automatic operational Task offers and Staff responses on the V6 scaffold; evidence verification, StaffReport and SWAP/COVER remain target work unless verified in code.
Business/Mainflow baseline: **18/10/2026 synchronization** over the 02/10/2026 Mainflow baseline. Current implementation focus remains **MF-01 — Setup & System Configuration**, plus the explicitly documented MF-02 subset.
Approved workflow delta (05/10/2026): Staff shift-swap requests and deferred task-response options are part of the project baseline/design, but are not assumed implemented unless verified in the repository.

## Required context

Before implementation, read:

1. This repository-root `AGENTS.md`.
2. [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) **in full** once per working session. It contains the complete team implementation guide, MF-01..MF-04, business rules, architecture, open decisions, and links to Report 1, Report 2, BR, and ERD sources. This short entry point does not replace that guide.
3. [README.md](README.md) for setup, current implementation, and outstanding work.
4. [docs/API.md](docs/API.md), [docs/openapi.json](docs/openapi.json), relevant source/tests, and applicable instruction files for the capability being changed.
5. [docs/VALIDATION.md](docs/VALIDATION.md) for historical verification evidence; do not treat its dated results as validation of later changes.

Keep the complete guide versioned inside this repository so a backend-only clone has the context. Do not depend on a sibling `AGENT` folder or machine-specific paths to load instructions.

Follow the full guide's source-of-truth precedence: latest explicit team/mentor decision, latest agreed BR clarification, Report 1, Report 2, approved ERD/database for persistence, then Report 3/Figma/implementation details. Inspect relevant code before deciding what is already implemented. Report unresolved document/code mismatches instead of silently inventing behavior.

## Current scope: MF-01

Implement and verify this setup flow:

```text
Admin login
→ Floor / Zone setup (single default store; no store setup step)
→ Register & configure camera
→ Test connection & preview
→ Map camera to Zone(s), including camera-frame ROI
→ Configure monitoring rules
→ Activate monitoring
→ Continuous camera / stream health checks
```

Health exceptions lead to Admin investigation/restoration, retesting, and resumed health checks. Test/preview is Admin-triggered; backend adapters perform the operation. Connection enablement remains a validated implementation precondition even if it is not a separate Mainflow diagram box.

Account administration supports MF-01 but is not a required diagram step. Later Mainflows remain design context; implement their incident, dispatch, task, shift, or analytics workflows only when assigned. Live cameras are the operational source; recorded video is for testing/evaluation/fallback demonstration. Do not introduce synthetic DEMO camera connections; the current backend rejects new DEMO source configurations.

## Domain rules to preserve

- One supermarket branch, as a single default `Supermarket` record from seed data. Do not build store create/edit/select flows; Admin setup starts at Floor. `Supermarket → Floor`; both `Zone` and `Camera` belong to `Floor`.
- Camera ↔ Zone is N:M through `CameraZoneMapping`. Reject mappings across floors.
- `Zone.map_polygon` is on the floor map; `CameraZoneMapping.roi_polygon` is on a camera frame. They are separate coordinate spaces, not an automatic projection.
- Persist spatial coordinates normalized to `[0,1]`, with valid polygon geometry and at least three points. Normalization handles scaling, not arbitrary crop/perspective changes.
- New connections start disabled; enabling must enforce current connection validation. Configuration changes invalidate old test results.
- Camera operational status, camera health status, connection enablement, and monitoring activation are separate concepts.
- `CameraHealthEvent` is system health, not an operational `Incident`. `OperationalEvent` and `Incident` are also distinct.
- OperationalEvent response is `OperationalEvent → ResponsePolicy → ResponsePolicyAction → notification/recommendation/operational Task`. Incident is an independent MonitoringRule outcome or confirmed-report path; a threshold breach or CRITICAL priority alone does not convert an Event into an Incident.
- `StaffReport` is a separate confirmed staff-submission path; routine reports must not be fabricated into OperationalEvents.
- `Task` has exactly one origin type: `OPERATIONAL_EVENT`, `INCIDENT`, `STAFF_REPORT`, or `MANUAL`, with exclusive nullable source FKs.
- Task acceptance must be atomic first-valid-acceptance-wins; eligibility uses checked-in/on-shift state, assigned zone, role, and availability.
- Model confidence filters detections; it does not decide incident routing. No cross-camera identity matching, face recognition, or customer identity storage.
- Only Admin creates accounts; protect the last active Admin. User passwords are hashed; recoverable camera credentials are encrypted and never returned/logged as secrets.
- For task offers, preserve the distinction between accepting responsibility, starting work, deferred start, active work, and overdue/escalated work. Staff responses are Handle Now, Accept and Handle Later, or Cannot Handle; `Cannot Handle` is not evidence rejection.
- Shift swaps follow Staff A request → eligible Staff B accept/decline → Manager approval. Candidate eligibility checks availability, overlap, zone coverage, workload, and working-hour constraints. Do not implement speculative optimization.

## Architecture and database changes

Use the existing ASP.NET Core .NET 10 / EF Core / SQL Server architecture:

```text
src/Supermarket.Api             HTTP, auth/RBAC, DTO validation, ProblemDetails, DI
src/Supermarket.Application     Use cases, orchestration, abstractions, transactions
src/Supermarket.Domain          Models, invariants, geometry and state rules
src/Supermarket.Infrastructure  EF persistence, adapters, secrets, camera health, AI client
tests/Supermarket.Tests         Domain and integration tests
```

Keep controllers thin. Domain must not depend on ASP.NET or EF. Do not expose scaffolded entities as API DTOs. The backend owns authorization, business workflow and SQL persistence; the Python AI service supplies detections/tracks/measurements. Redis is transient support, not authoritative configuration storage.

Database-first: approved ERD/schema → approved SQL Server change → re-scaffold → review generated changes → update mappings/use cases/contracts/tests. Generated files under `src/Supermarket.Infrastructure/Persistence/Scaffolded` must not be manually edited for business behavior. Keep custom EF extensions outside that directory. Do not introduce code-first migrations or a competing schema.

The target physical persistence baseline is approved `FA26SE103_ERD_v6.drawio` / the 18/10/2026 synchronized project context. Database V5 is the non-destructive V6 expansion; Database V6 Cleanup is the guarded final cutover after all older writers are stopped and the V6-aware backend is deployed. The migration branch scaffold is generated from the exact 37-table post-cleanup schema: MonitoringRule has one nullable target per outcome, OperationalEvent has no Incident/string-type link, and IncidentType has no source/measurement columns. Workflow availability must still be verified in application code; a scaffolded table is not proof that its use case is implemented. Legacy DTOs/comments do not establish schema approval. Do not edit team-owned ERD/schema unless assigned.

SQL source and approved migrations are in the separate Database repository. Apply the migration chain through `FA26SE103_Database_V5.sql`; run `FA26SE103_Database_V6_Cleanup.sql` only at coordinated cutover after a current backup, preflight, stopped older writers, and matching backend deployment are confirmed. Do not infer schema from an old entity count. A missing legacy SQL-test bootstrap file does not justify generating SQL from C# models or creating a new one-off SQL script solely to make a test fixture run.

Monitoring activation validates readiness and requests backend-owned runtime monitoring (count/queue only). Preview is view-only while monitoring owns the camera. Keep requirements, existing code, and verified behavior distinct; this increment does not implement dispatch or task verification.

## Workflow and verification

- Inspect branch and working tree before editing; preserve unrelated user changes.
- Use feature/fix/docs branches from `dev`; continue an appropriate existing task branch. Do not implement directly on `dev` or `main` or bypass PR review.
- Keep changes focused on the assigned capability. No speculative tables, dependencies, ERP/POS integration, or expanded AI scope.
- Update affected API documentation and Report/BR references when behavior changes.
- Run focused tests for changed behavior and the non-SQL build/test suite. For the current delivery phase, a disposable-database SQL integration suite is optional and is not a completion gate when its legacy bootstrap scripts are absent. Do not create a separate SQL file per test or reconstruct obsolete bootstrap migrations just for that suite. For database-affecting work, validate the canonical Database V5/V6 scripts with SQL parsing plus focused live-schema/data checks; report SQL integration as not run when its prerequisites are unavailable.
- Never commit secrets, local appsettings, camera passwords, Data Protection keys, videos, caches, or build output. See README for local setup and key-ring handling.
- Ask before destructive/irreversible actions. Report changed files, verification evidence, and remaining gaps.

From this repository root, the existing commands are:

```powershell
dotnet restore --configfile NuGet.Config
dotnet test --filter FullyQualifiedName~Supermarket.Tests.DomainTests --settings coverage.runsettings
dotnet test --settings coverage.runsettings --collect:'XPlat Code Coverage' --results-directory TestResults
dotnet publish src/Supermarket.Api -c Release
```

The full test command may require approved SQL bootstrap files and isolated SQL Server test databases. It is optional in the current phase when those prerequisites are absent; do not add test-only SQL files to satisfy it. For documentation-only changes, check links, context consistency, and `git diff --check`; runtime tests are unnecessary unless behavior changed.

## Maintaining shared context

Maintain `docs/PROJECT_CONTEXT.md` as the complete repo-owned team guide. Update it with approved BR/Mainflow/ERD decisions and record the baseline date. Keep this root file short and synchronize its critical rules when the guide changes. Keep current implementation/gaps in README and verification evidence dated in `docs/VALIDATION.md`.

All required local context links must resolve inside a standalone backend clone. External reports/BR/ERD remain authoritative sources linked from the full guide; do not pretend a local guide automatically follows their future revisions.
