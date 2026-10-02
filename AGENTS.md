# AGENTS.md — FA26SE103 Backend

Project: AI-Powered Smart Supermarket Operations Monitoring System, GFA26SE39.
Business/Mainflow baseline: **02/10/2026**. Current implementation focus: **MF-01 — Setup & System Configuration**.

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
→ Store / Floor / Zone setup
→ Register & configure camera
→ Test connection & preview
→ Map camera to Zone(s), including camera-frame ROI
→ Configure monitoring rules
→ Activate monitoring
→ Continuous camera / stream health checks
```

Health exceptions lead to Admin investigation/restoration, retesting, and resumed health checks. Test/preview is Admin-triggered; backend adapters perform the operation. Connection enablement remains a validated implementation precondition even if it is not a separate Mainflow diagram box.

Account administration supports MF-01 but is not a required diagram step. Later Mainflows remain design context; implement their incident, dispatch, task, shift, or analytics workflows only when assigned. Live cameras are the operational source; recorded/demo video is for testing/evaluation/fallback demonstration.

## Domain rules to preserve

- One supermarket branch. `Supermarket → Floor`; both `Zone` and `Camera` belong to `Floor`.
- Camera ↔ Zone is N:M through `CameraZoneMapping`. Reject mappings across floors.
- `Zone.map_polygon` is on the floor map; `CameraZoneMapping.roi_polygon` is on a camera frame. They are separate coordinate spaces, not an automatic projection.
- Persist spatial coordinates normalized to `[0,1]`, with valid polygon geometry and at least three points. Normalization handles scaling, not arbitrary crop/perspective changes.
- New connections start disabled; enabling must enforce current connection validation. Configuration changes invalidate old test results.
- Camera operational status, camera health status, connection enablement, and monitoring activation are separate concepts.
- `CameraHealthEvent` is system health, not an operational `Incident`. `OperationalEvent` and `Incident` are also distinct.
- Model confidence filters detections; it does not decide incident routing. No cross-camera identity matching, face recognition, or customer identity storage.
- Only Admin creates accounts; protect the last active Admin. User passwords are hashed; recoverable camera credentials are encrypted and never returned/logged as secrets.

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

The full guide still marks final `MonitoringRule`, `IncidentType`, `Zone.area_m2`, measurement-source selection, checkout-counter and manager-on-duty representation as open/pending. Coordinate before freezing their physical schema. Legacy DTOs/comments do not establish schema approval. Do not edit team-owned ERD/schema unless assigned.

Current scaffolding covers the ten stable setup entities. SQL source is maintained outside this backend repository; see README for the approved schema path/override and integration-test prerequisites. A missing schema file does not justify generating one from C# models.

Monitoring activation currently validates setup readiness and updates configuration status. AI preview does not establish that continuous rule evaluation or incident generation is implemented. Keep requirements, existing code, and verified behavior clearly distinguished.

## Workflow and verification

- Inspect branch and working tree before editing; preserve unrelated user changes.
- Use feature/fix/docs branches from `dev`; continue an appropriate existing task branch. Do not implement directly on `dev` or `main` or bypass PR review.
- Keep changes focused on the assigned capability. No speculative tables, dependencies, ERP/POS integration, or expanded AI scope.
- Update affected API documentation and Report/BR references when behavior changes.
- Run focused tests for changed behavior, then required build/integration checks. Do not call SQL integration verified if its prerequisites are missing or CI skipped it.
- Never commit secrets, local appsettings, camera passwords, Data Protection keys, videos, caches, or build output. See README for local setup and key-ring handling.
- Ask before destructive/irreversible actions. Report changed files, verification evidence, and remaining gaps.

From this repository root, the existing commands are:

```powershell
dotnet restore --configfile NuGet.Config
dotnet test --filter FullyQualifiedName~Supermarket.Tests.DomainTests --settings coverage.runsettings
dotnet test --settings coverage.runsettings --collect:'XPlat Code Coverage' --results-directory TestResults
dotnet publish src/Supermarket.Api -c Release
```

The full test command requires an approved SQL schema and isolated SQL Server test databases; follow README before running it. For documentation-only changes, check links, context consistency, and `git diff --check`; runtime tests are unnecessary unless behavior changed.

## Maintaining shared context

Maintain `docs/PROJECT_CONTEXT.md` as the complete repo-owned team guide. Update it with approved BR/Mainflow/ERD decisions and record the baseline date. Keep this root file short and synchronize its critical rules when the guide changes. Keep current implementation/gaps in README and verification evidence dated in `docs/VALIDATION.md`.

All required local context links must resolve inside a standalone backend clone. External reports/BR/ERD remain authoritative sources linked from the full guide; do not pretend a local guide automatically follows their future revisions.
