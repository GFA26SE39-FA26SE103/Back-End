# FA26SE103 — Smart Supermarket Operations Backend

Backend for the **AI-Powered Smart Supermarket Operations Monitoring System** (Group GFA26SE39, Capstone FA26SE103).

> **Documentation baseline: 2026-11-11, `dev` branch.** This is a developer entry point, **not a guarantee that every planned workflow is implemented or deployed**. For exact endpoints, schema prerequisites, validation evidence and deployments, consult the linked repository documents. Evidence is valid for the specific date/commit/environment recorded.

## What this backend does

The ASP.NET Core backend coordinates supermarket structure, camera/monitoring configuration, operational observations, independently qualified incidents, tasks and dispatch, staff/workforce operations, notifications, and analytics. A separate Python AI service supplies video detections and measurements; the backend owns business rules and persistence.

The project's five functional workflows are:

1. **WF-01 Configure and Activate Store Monitoring** — stores, floors, zones, cameras, ROI, monitoring and operational policies.
2. **WF-02 Detect and Route Operational Issues** — valid camera measurements → OperationalEvents → applicable response; separately, Staff-confirmed reports may qualify as Incidents or routine requests.
3. **WF-03 Execute and Verify Operational Tasks** — task offers, assignment, execution, evidence, verification, rework and closure.
4. **WF-04 Plan Staff Shifts and Resolve Coverage Changes** — schedules, staffing constraints, SWAP/COVER and approval.
5. **WF-05 Review Operational Performance and Generate Reports** — dashboards, analytics, reporting and advisory recommendations.

These are **business scope/workflow names**, not claims that all steps are complete in the current `dev` branch.

## Repository layout

```text
src/
  Supermarket.Api/             HTTP endpoints, JWT/RBAC, DTO validation, DI, ProblemDetails
  Supermarket.Application/     Use cases, orchestration, contracts and abstractions
  Supermarket.Domain/          Domain models, validation and invariants
  Supermarket.Infrastructure/  SQL Server/EF persistence, adapters, workers, media/secrets

tests/Supermarket.Tests/       Automated tests
scripts/                       Local development utilities
 docs/                         API, implementation context, validation and deployment runbooks
```

Architectural boundaries: controllers remain thin; do not call `DbContext` directly in controllers; domain logic must not depend on ASP.NET/EF; scaffolded persistence entities are not public API DTOs. SQL Server is authoritative; Redis is transient where used.

## Technologies

- ASP.NET Core **.NET 10**, C#, Entity Framework Core, Microsoft SQL Server.
- JWT authentication and role-based authorization (ADMIN, OPERATOR, MANAGER, STAFF as applicable).
- Docker/Compose and GitHub Actions for the Backend development deployment.
- Camera/stream integration, Cloudinary media workflows, Firebase push and Python AI-service integration as described in their specific codepaths and docs.
- Tests include a non-SQL suite and separately controlled SQL-integration/rollback checks.

Check source and environment configuration before assuming any optional integration is enabled.

## Get started (existing development database)

**Prerequisites:** .NET SDK 10, access to the team's approved development SQL Server, and any external tools required by the feature being exercised (for example FFmpeg for camera/video flows). Keep credentials out of Git.

1. Clone this repository and check out the branch being used for the task (usually `dev`).
2. Create **`src/Supermarket.Api/appsettings.Local.json`** from `appsettings.Local.example.json` only if no local file exists.
3. Supply the `FA26SE103_Dev` SQL connection settings, a locally generated JWT signing key and any feature-specific secrets. Never commit the local file or secrets.
4. Run from the repository root:

```powershell
if (!(Test-Path src/Supermarket.Api/appsettings.Local.json)) {
    Copy-Item src/Supermarket.Api/appsettings.Local.example.json src/Supermarket.Api/appsettings.Local.json
}

# Generate a signing-key value and place it in the local configuration.
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))

dotnet restore --configfile NuGet.Config
.\scripts\Start-Local.ps1
```

The documented local API base is **`http://localhost:5080/api`** and Swagger is **`http://localhost:5080/swagger`** when enabled. Authenticate through `POST /api/auth/login`, then use the token for authorized routes.

For an empty approved development database, consult the repository's bootstrap instructions before running `Start-Local.ps1 -BootstrapAdmin`. Do **not** bootstrap against an existing team database without checking its accounts and approval. The backend does not create or migrate the database on startup.

### Local configuration and secrets

- `appsettings.json` and environment-specific files provide non-secret defaults.
- `appsettings.Local.json` is machine-specific, ignored by Git and not intended for production publishing.
- Production/staging secrets should be supplied via environment/secrets management, not committed files.
- Camera credentials are protected with ASP.NET Data Protection. Any two instances reading existing encrypted camera credentials must have access to the appropriate shared **development** key ring; a JWT key is not a substitute.
- Avoid exposing camera passwords in connection URLs, logs, screenshots or API responses.
- Staff Report AI capabilities, if configured, require separate provider keys. AI suggestions are drafts; Staff confirmation is required for persistence and qualification.

See [deployment and secrets runbook](docs/DEPLOY_DEV.md) for environment-specific setup.

## Current implementation areas and caveats

This is a **status-oriented summary based on repository documentation checked on 2026-11-11**, not fresh runtime acceptance evidence. Always examine the linked source and dated test results before marking an item done.

| Area | Repository evidence / current boundary |
| --- | --- |
| Store, floor, zone and camera configuration | CRUD, floor plans, camera setup, camera/zone mappings and ROI contracts are documented and represented in application source. |
| Monitoring and OperationalEvents | Monitoring rules, event episodes and event history/drill-down have implementation/validation entries. Health and invalid readings are distinct from valid operational observations. |
| SLA and operational response | November 6 cutover uses `SLAPolicy`/`SLAPolicyAction` rather than the older physical response-policy tables. WARNING can notify; CRITICAL can trigger configured Task action. Never conflate event alert level with SLA response priority. |
| Task offers and dispatch | Dated evidence covers specialty-aware five-ring dispatch, offer handling, assignment, rejection/reassignment and evidence/verification increments. Full cross-role staging acceptance remains a separate gate. |
| Staff reports and Incident qualification | `PROJECT_CONTEXT.md` documents a November 9–10 StaffReport/AI increment and V12–V15 schema work. Check what actually landed on `dev` versus feature branches and which database received each script. |
| Shifts and SWAP/COVER | Published schedules, eligibility and same-specialty transfer constraints have documented API and test increments. |
| Manager analytics | Read-only overview/cohort/zone KPIs are documented. Dashboard UI, exports, full recommendations and performance acceptance are not established merely by these endpoints. |
| Waiting Time / Checkout Capacity | Supported as project goals, **not automatically reliable runtime measurements**. Validate against footage and represent missing/invalid measurements as unavailable. |

**Never infer implementation or production readiness solely because an entity, route description, SQL migration, or historical test result exists.**

## Checkout counter design — planned, not yet implemented

The current Task-plan decision introduces two tables in the canonical Database repository. This describes approved direction, not existing schema/API/runtime behavior.

- `CheckoutCounter`: logical counter/lane identity tied to a Checkout Zone (`zone_id`, `code`, `name`, `is_enabled`). Floor-plan geometry is not part of the current K9 schema scope unless separately approved in the ERD.
- `CameraCheckoutMapping`: Camera↔Counter mapping with `lane_roi`, `staff_roi`, `is_enabled` and an explicit queue-measurement-source selection. Multiple cameras may observe a counter; exactly one enabled mapping is selected to count that counter’s queue. Camera and counter must be on the same Floor. Keep generic `CameraZoneMapping` unchanged.
- Both ROIs are independent normalized polygons in camera-frame coordinates and required for an enabled mapping. No adjacency/containment rule. No additional ROI/history table or POS integration is currently planned.

**Multi-camera staff presence (approved planning rule):** aggregate by `counterId`. `PRESENT` after at least one enabled, healthy, fresh and sufficiently observable mapping confirms staff presence for about 15 seconds. `ABSENT` only after all enabled mappings are healthy/fresh/sufficiently observable and confirm absence for about 15 seconds. Otherwise return `UNKNOWN`/`UNAVAILABLE`; unknown time does not count. Persisted PRESENT wins a source disagreement, which should remain diagnosable. Validate both 15-second persistence values with footage. This is staff-presence aggregation, not cross-camera person ReID or automatic queue-source failover.

Queue demand is measured only from the selected mapping per counter; do not sum overlapping detections across cameras. The existing standing-≥5-second queue definition is provisional until validated. The initial metric is **Observed Staffed Checkout Ratio**, not active transaction utilization. OE-04 should compare valid queue demand `Q` with distinct observed staffed counters `S` using configurable MonitoringRule thresholds/persistence; incomplete coverage yields unavailable, not zero. OE-01 Long Queue and OE-04 Checkout Capacity Issue may coexist; check their cross-event Task deduplication before enabling automatic Task creation (Task plan K9.8). Waiting time needs separately validated queue-entry/service-start signals.

## Database-first workflow
## Database-first workflow

**The Backend repository is database-first. Do not use code-first EF migrations for team schema changes.**

1. Agree on the ERD/contract and coordinate with the **Database repository** owner.
2. Create and review a versioned SQL change in the canonical Database repository, with preflight/rollback and target environment identified.
3. Apply only to the approved database after the required review/backup/checkpoint.
4. Re-scaffold generated EF persistence models from that schema.
5. Review differences, update domain/application contracts, controllers/adapters and tests.
6. Update `docs/API.md`, OpenAPI, and any business/design documentation affected by the actual implementation.

**Schema version nuance:** the project design baseline references ERD V7, while later Database scripts V9–V15 are discussed in dated repository notes. These are **different concepts** (ERD version, migration/script version and deployed DB version). Inspect the Database repository and the target SQL database before claiming any particular script is applied.

## API and verification

- Complete route details, permissions, payloads and errors: [docs/API.md](docs/API.md); generated/reference specification: [docs/openapi.json](docs/openapi.json).
- Dated test/SQL evidence and limitations: [docs/VALIDATION.md](docs/VALIDATION.md).
- Historical decisions, known gaps and superseded documentation: [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md).
- Development deployment: [docs/DEPLOY_DEV.md](docs/DEPLOY_DEV.md).
- Coding-agent rules: [AGENTS.md](AGENTS.md).

```powershell
dotnet restore --configfile NuGet.Config
dotnet test --no-restore --filter "Category!=SqlIntegration"
dotnet publish src/Supermarket.Api -c Release --no-restore
```

Run SQL-integration tests only against an approved isolated or rollback-safe environment. Record the exact command, commit, DB/script state, and result; do not reuse historical pass counts as current proof.

## Development deployment

The repo documents a `dev`-branch pipeline that tests, builds/pushes a container image and deploys through a controlled GHCR/Tailscale/SSH flow. The documented development endpoint is `https://supermarket-api-dev.kitsuracloud.com`, but availability and deployed revision must be checked live. Pipeline deployment **does not** implicitly migrate SQL, configure DNS/secrets, deploy frontend/AI services or validate every business workflow.

Follow [docs/DEPLOY_DEV.md](docs/DEPLOY_DEV.md) before changing infrastructure or credentials.

## Safety and scope boundaries

- `OperationalEvent` and `Incident` are separate branches. Camera rules produce/update OperationalEvents under the current approved baseline; confirmed Staff reports can qualify as Incidents.
- No face recognition, cross-camera person ReID or persistent customer identity.
- Do not treat missing/expired camera measurements as zero, as successful AUTO verification, or as a trigger for operational actions.
- Advisory recommendations do not automatically change schedules, policies, Tasks or monitoring configuration.
- Do not assume POS/ERP, inventory or revenue data integrations exist.

For new work, start with **[AGENTS.md](AGENTS.md)**, then inspect the current branch, relevant source/tests, the newest project decisions, and the canonical Database scripts.
