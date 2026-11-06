# AGENTS.md — FA26SE103 Backend

> **Revision:** 06/11/2026 — synchronized with the workspace Review 2 baseline.
> **Repository:** ASP.NET Core .NET 10 / EF Core / SQL Server backend.
> **Purpose:** backend-specific instructions; the workspace-root `AGENTS.md` remains the cross-project business baseline.

## Required context

Before implementation, read:

1. this file;
2. the workspace/root `AGENTS.md` when available;
3. `docs/PROJECT_CONTEXT.md` for repository history and schema lineage;
4. `README.md`, `docs/API.md`, `docs/openapi.json` and the relevant source/tests;
5. `docs/VALIDATION.md` only as dated evidence, never as proof for later commits;
6. BM04, Post-Review 2 and Reports 1–5 linked from the workspace guide.

Precedence is: latest explicit mentor/team decision → BM04 → Post-Review 2 → current Reports 1–5 → approved database/ERD for persistence → UI/design/implementation details.

## Current business baseline

The product has five workflows:

1. Store, Monitoring & Operational Policy Configuration.
2. AI Monitoring, Event/Incident & Dispatch.
3. Task Execution, Evidence & Closure.
4. Shifts, Availability & SWAP/COVER.
5. Dashboard, Analytics, Reporting & Recommendations.

Do not use the superseded four-Mainflow model or assume the backend is MF-01-only.

### Monitoring outcomes

- Detection confidence is an input filter only.
- A MonitoringRule produces exactly one configured outcome.
- OperationalEvent and Incident are independent persistence/lifecycle branches.
- OperationalEvent → ResponsePolicy → optional notification/recommendation/Task.
- An OperationalEvent never silently converts into an Incident and does not always create a Task.
- StaffReport requires real extraction/media/duplicate/confirmation handling before Incident creation.
- CameraHealthEvent is technical health, not an operational Incident.

### Dispatch and SLA

- Task SLA priorities are P1–P4, not LOW/MEDIUM/HIGH/CRITICAL.
- Select SLA by exact Problem Type + Priority, then approved global-priority fallback; missing/ambiguous policy is an explicit exception.
- Snapshot policy id/version/priority, response/resolution deadlines and provenance immutably.
- Response clock is Task creation → first valid acceptance; Resolution clock is Task creation → Verified.
- Warning at 80%, breach at 100%; expansion/rework/reassignment never resets deadlines.
- P1 is continuous 24/7 with immediate Operator + Manager notification; P2–P4 use approved operating hours.
- Eligible Staff satisfy specialty, published active Shift, check-in/availability and workload.
- Dispatch rings: Same Zone → adjacent same-Floor Zones → remaining same-Floor Zones → adjacent Floors → other Floors → Operator exception.
- Offer concurrently inside one ring; advance when the ring is exhausted; do not impose a fixed global retry count.
- Handle Now, Handle Later and Cannot Handle are explicit responses. Cannot Handle requires BUSY/UNAVAILABLE/NOT_QUALIFIED/OTHER.
- First valid acceptance wins atomically; other offers become CANCELLED; unanswered offers become EXPIRED.

### Task execution and closure

- Staff starts only an assigned Accepted Task.
- Corrective Incident Task submission requires After Photo OR Completion Note; Before Photo is optional in Review 2.
- TT-01..TT-05 may verify from Staff completion + valid metrics without a photo.
- P1 and TT-07/TT-12–TT-15 are MANUAL; other configured Tasks may AUTO verify only when their conditions pass.
- Missing AUTO metrics produces an exception.
- MANUAL reject #1 returns the Task to In Progress for the same assignee.
- MANUAL reject #2 reassigns to a different eligible same-specialty Staff member without resetting SLA.
- Only Exposed Electrical Wire creates mandatory TT-12 + TT-13.
- Incident auto-closes only when every mandatory Task is Verified and no blocker remains.

### Workforce and decision support

- SWAP/COVER is approved scope: Staff A request → Staff B response → Manager validation/decision.
- Enforce availability, overlap, specialty, 48-hour weekly maximum and started/checked-in immutability.
- Analytics keep OperationalEvent and Incident categories separate.
- Recommendations are advisory records; acceptance never directly changes shifts, tasks, policies or monitoring configuration.

## Architecture

```text
src/Supermarket.Api             HTTP, auth/RBAC, DTO validation, ProblemDetails, DI
src/Supermarket.Application     Use cases, orchestration, abstractions, transactions
src/Supermarket.Domain          Models, invariants, geometry and state rules
src/Supermarket.Infrastructure  EF persistence, adapters, secrets, workers, AI/push integration
tests/Supermarket.Tests         Domain, application, infrastructure and API tests
```

- Controllers stay thin and never use `DbContext` directly.
- Domain must not depend on ASP.NET or EF.
- Do not expose scaffolded entities as DTOs.
- SQL Server is authoritative; Redis is transient only.
- Python AI supplies detections/tracks/measurements; backend owns business workflow.
- Use ProblemDetails and never expose stack traces, SQL details or secrets.

## Database-first rules

Approved ERD/schema → reviewed SQL script → apply to the intended database → re-scaffold → review generated diff → update custom mappings/use cases/contracts/tests.

- Generated files under `src/Supermarket.Infrastructure/Persistence/Scaffolded` contain persistence shape only.
- Do not put business rules in generated files or create code-first migrations.
- Do not infer a completed feature from an existing table/entity.
- Current schema lineage is the approved post-cleanup database plus the V7 weekly Work Shift extension; verify the actual deployed DB version before relying on a column/table.
- Preserve exclusive Task origin FKs and the separation of OperationalEvent, Incident, StaffReport and CameraHealthEvent.
- Database/ERD changes require explicit assignment and coordination with the Database repository.

## Spatial, security and secret invariants

- Supermarket → Floor; Zone and Camera both belong to Floor.
- Camera ↔ Zone is N:M; reject cross-Floor mappings.
- Floor polygon and camera ROI are different normalized `[0,1]` coordinate spaces.
- No cross-camera ReID, face recognition or customer identity persistence.
- Only Admin creates accounts; protect the last active Admin.
- Hash user passwords with a proven password hasher.
- Camera credentials are recoverable encrypted secrets/references, never one-way hashes and never embedded in logged URLs.

## Runtime availability

- Count/queue aggregates currently have the strongest runtime evidence.
- Waiting Time and Checkout Capacity remain experimental/unavailable until footage-based validation passes.
- Physical density requires valid `Zone.area_m2`; heatmap remains camera-view accumulation.
- Missing/stale metrics must not trigger OperationalEvents, Incidents, AUTO verification or recommendations.

## Verification workflow

Before editing:

1. inspect branch/status and preserve unrelated changes;
2. inspect the relevant use case, contracts, schema and tests;
3. compare current behavior with the workspace Review 2 baseline;
4. report unresolved schema/document conflicts instead of inventing behavior.

For behavior changes:

- write focused domain/application/API tests;
- run the focused test first, then the non-SQL suite;
- run SQL integration only against an approved isolated database/schema;
- update `docs/API.md`/OpenAPI and affected business documentation;
- report exact commands/results and any unrun prerequisite-dependent checks.

Common commands:

```powershell
dotnet restore --configfile NuGet.Config
dotnet test --no-restore --filter "Category!=SqlIntegration"
dotnet publish src/Supermarket.Api -c Release --no-restore
```

Documentation-only changes require link/context review and `git diff --check`; runtime tests are unnecessary unless behavior changes.

## Current Review 2 audit gates

Do not claim completion until code and tests prove:

- [ ] real StaffReport API and Incident qualification;
- [ ] Zone/Floor ring expansion without fixed attempts;
- [ ] P1–P4 SLA policy selection, operating calendar, warning/breach and escalation;
- [ ] AUTO/MANUAL verification and evidence-or-note semantics;
- [ ] second-rejection automatic eligible reassignment;
- [ ] mandatory-Task Incident auto-close with blocker guard;
- [ ] truthful waiting-time/checkout-capacity availability;
- [ ] live dashboard/analytics/export/recommendation APIs;
- [ ] cross-role staging workflows linked to Report 5 evidence.

## Git and agent behavior

- Follow the user's explicit current-branch, integration and worktree instructions.
- Do not create a worktree unless requested.
- Never commit secrets, local appsettings, key rings, videos, caches, build output or generated local data.
- Ask before destructive or irreversible actions.
- Keep commits capability-focused and preserve unrelated user files.
- Report changed files, verification evidence, document/code mismatches and remaining gaps.
