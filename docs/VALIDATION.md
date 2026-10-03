# MF-01 validation 2026-10-01

Verified locally with .NET SDK 10.0.300, runtime 10.0.8 and SQL Server Express. Build and Release publish succeeded with 0 warnings and 0 errors.

`dotnet test --no-restore --settings coverage.runsettings --collect:'XPlat Code Coverage' --results-directory TestResults`: **56 passed, 0 failed, 0 skipped**. SQL integration tests read the shared `Project/DB/FA26SE103_Database_V0.1.sql`, create isolated databases and remove them after execution. Backend does not maintain a separate SQL schema copy. Tests do not use EF InMemory or SQLite.

Coverage excludes scaffolded persistence code. Current measured line coverage after source formatting: Domain **98.67%** (business rules **97.14%**), Application **88.43%**, API **84.28%**, Infrastructure **60.29%**. Infrastructure coverage is lower because live FFmpeg decoding and background-worker timer paths require external video/runtime validation. These figures are not a claim of live-camera acceptance coverage.

Verified behavior:

- Normalized geometry, polygon validity, same-floor mapping, warranty/position validation.
- Login, role permissions, revoked account token rejection, last-active-Admin protection and concurrent disable attempts.
- Store/floor/zone persistence, duplicate rejection, map dimensions and mutation timestamps.
- Connection default disabled, test/preview/enable/reload, credential encryption and sanitized responses, test reset after configuration changes.
- A stale asynchronous test cannot overwrite a newer connection configuration.
- Camera-zone mapping is idempotent and rejects cross-floor requests.
- Monitoring readiness checks and activation.
- Demo camera ONLINE → disconnect → one OPEN event → INVESTIGATING → restore → RESOLVED.
- OpenAPI generation documents setup endpoints without exposing credential/password-hash fields.

Local initialization was run successfully and created `FA26SE103_MF01_Local`. Admin bootstrap remains available through `Start-Local.ps1 -BootstrapAdmin`; no shared/default production password is committed.

The local API was started on port 5080, liveness returned UP, SQL readiness returned READY, and the Swagger contract was exported to `docs/openapi.json`. The smoke-test process was then stopped so the first Admin can be bootstrapped on the user's next start.

Not verified in this workspace: real CCTV/RTSP connectivity, continuous browser streaming, AI pipeline integration, React integration, Docker image build, GitHub Actions execution or VPS deployment. This dated evidence predates the ERD v3 documentation baseline; MonitoringRule/IncidentType persistence remains outside this MF-01 increment even though ERD v3 now defines their physical structures.

## Floor-plan upload and camera placement validation 2026-10-03

Verified after implementing authenticated floor-plan storage and the real Store Layout placement flow:

- `dotnet build Supermarket.sln --no-restore`: **PASS**, 0 warnings, 0 errors.
- Focused backend floor-plan tests: **11 passed**. Coverage includes ADMIN/STAFF/anonymous authorization, binary retrieval and Content-Type, empty/oversized/unsupported/signature-mismatched rejection, serialized concurrent replacement, replacement cleanup, and preservation of the previous stored file after persistence failure.
- Backend Domain + floor-plan test selection: **59 passed**.
- Frontend `npm run build`: **PASS**. Vite emitted only the existing bundle-size advisory; type-check and production bundle completed.
- Frontend `npm run lint`: **PASS**, no warnings.
- Frontend `npm run test -- --run`: **37 passed across 9 files**. Coverage includes multipart/bearer contract, authenticated Blob download, complete camera PATCH, normalized geometry, drag/rotate/keyboard interaction, API-backed Store Layout loading, replacement upload, successful save, and save-failure dirty-state retention.
- Runtime Development Swagger export: **PASS** (`GET /swagger/v1/swagger.json` returned 200); `docs/openapi.json` was regenerated from the running controller contract.

The complete SQL integration suite was not rerun because the approved shared SQL schema file is not present in this checkout. No live browser-to-database acceptance, real PDF visual inspection, object-storage deployment, S3/MinIO integration, or concurrent multi-process storage validation is claimed here.

## Zone editor contract validation 2026-10-03

- `Zone.color_hex` and `Zone.area_m2` were added additively to `FA26SE103_Dev`; the table contained no Zone rows at migration time.
- EF scaffold regenerated from the updated database and maps `color_hex` as `nvarchar(7) NULL` and `area_m2` as `decimal(12,2) NULL`.
- Release solution build: **PASS**, 0 warnings and 0 errors.
- Backend tests not requiring the unavailable shared SQL file: **100 passed**. The new SQL integration test compiles and covers color/area round-trip plus invalid values, but could not execute without the shared schema file.
- Development Swagger export: **PASS**; `docs/openapi.json` was regenerated from the running Release API contract.
- Frontend build and lint: **PASS**. Frontend tests: **44 passed across 10 files**.

## Monitoring configuration / review / activation validation 2026-10-03

Approved scope: MF-01 per-zone Draft rule persistence and readiness-validated activation; existing team ROI UI is reused. Database source is the separate Database repository's baseline plus approved `migrations/20261003_01_monitoring_rules_erd_v3.sql`. EF was re-scaffolded from the migrated Dev schema (12 tables); generated files were not hand-edited.

- Full backend suite: **168 passed, 0 failed, 0 skipped**, .NET SDK 10.0.302 / SQL Server Express. Tests create fixture-owned `FA26SE103_MF01_Test_<guid>` databases, apply the approved SQL baseline/migration with the guard retargeted to that temporary catalog, and remove the test database. No shared Dev configuration/rule data was mutated by tests.
- Real SQL API coverage: ADMIN JWT, AI-only catalog, Draft save/reload, rule replacement/removal and retained IDs, numeric confidence/threshold round trip, explicit zero timing/confidence, disabled rule persisted despite SQL defaults, stale-version conflict, malformed/domain/RBAC rejection, active-configuration/source/mapping/ROI/area protection, sanitized review, readiness rechecked after a source becomes disabled, deactivate→new Draft.
- Domain/application coverage: supported measurement/unit defaults per PROJECT_CONTEXT §§24–26, precision/range/integer counts, sustain/cooldown, JSON parameters, disabled Checkout Draft, missing rules/ROI/source/mapping/zone area, role restrictions, saved zone-confidence preview and stop-before-start restart.
- Frontend: **118 tests passed across 18 files**, production build and lint **PASS** (no lint warnings; existing Vite bundle-size advisory). New tests exercise actual React components with controlled HTTP responses: saved numeric payload/bearer, review-before-activate/version body, blockers, failed-save input preservation, unknown-config load failure, active/deactivate lifecycle, dirty-review invalidation, saved zone-confidence annotated preview and existing ROI editor navigation.
- Release publish **PASS**. Runtime Development Swagger export **PASS**, 41 paths including catalog/review and zoneId preview query; `docs/openapi.json` regenerated from the current compiled controller contract. Temporary documentation API ran on 5087 with bootstrap and health worker disabled, then only that owned process was stopped. The user's existing Debug API was not stopped.

Not claimed: real browser-to-shared-DB mutation acceptance, live YOLO/GPU confidence comparison, continuous rule evaluation, ROI measurements, OperationalEvents/Incidents, cooldown/dedup/dispatch, multi-camera count aggregation, checkout composite implementation, GitHub Actions execution or deployment. AI-service source confirms the supplied confidence is passed to YOLO tracking; controlled HTTP tests verify the BE payload, not GPU results. MF-01 ACTIVE is configuration state, not evidence of MF-02 incident processing.

## CI test-category split validation 2026-10-03

The previous `verify` filter excluded only names containing `ApiFlowTests`. Both `ConcurrencyTests` still loaded `SqlApiFixture`, causing the standalone runner to fail before test execution when the approved SQL schema was unavailable. `ApiFlowTests` (including its partial monitoring tests) and `ConcurrencyTests` now carry `Category=SqlIntegration`. The workflow selects complementary categories: `Category!=SqlIntegration` in `verify`, `Category=SqlIntegration` in the existing conditional SQL job.

- Release build and non-SQL test selection with coverage: **159 passed, 0 failed, 0 skipped**. On this restricted Windows host, the first run failed in 14 HTTP tests because the optional Windows EventLog provider could not write to `.NET Runtime`. Rerunning with process-only `Logging__EventLog__LogLevel__Default=None` passed; the environment value was restored afterward. No application or workflow logging configuration was changed.
- SQL-category discovery with `--list-tests`: **11 tests selected**, including both concurrency tests and both monitoring API-flow tests. Discovery does not initialize fixtures or execute SQL tests; no shared database was accessed.
- `dotnet publish src/Supermarket.Api -c Release --no-restore`: **PASS**.
- `git diff --check`: **PASS**.

SQL integration execution and a new GitHub Actions run are not claimed. The SQL job retains its existing schema-repository prerequisite; the CI fix does not change the database schema, SQL source, or runtime business behavior.

## MF-01 setup dashboard validation 2026-10-03

Added ADMIN-only `GET /api/setup/overview` for the single default store, saved floor/zone/configuration prerequisites and camera health. Readiness is shared with Monitoring Review/Activate. Missing configurations remain absent; the overview does not create records, probe sources or activate monitoring. Camera health, lifecycle, enabled connections and configuration activation remain independent.

- Backend Release non-SQL selection (`Category!=SqlIntegration`) with coverage: **178 passed, 0 failed, 0 skipped**. Process-only `Logging__EventLog__LogLevel__Default=None` avoids the restricted Windows host's optional EventLog sink. New application/HTTP tests cover missing seed/config, readiness parity with Review for valid/invalid/disabled/cross-floor/recorded/demo/density cases, N:M mappings, no saved-data mutations, ACTIVE with OFFLINE health, disabled ONLINE exclusion, sanitized responses, ADMIN/other-role/anonymous authorization and no-store headers. HTTP tests use an in-memory `ISetupStore`; they do not access SQL.
- `dotnet publish src/Supermarket.Api -c Release --no-restore`: **PASS**.
- Frontend: **143 tests passed across 19 files**; **27 focused dashboard/camera/layout tests passed** after the final selection-feedback change; production build and lint **PASS**. Only the existing Vite bundle-size advisory remains. Dashboard tests cover actual bearer HTTP contracts, saved status separation, filters/action URLs, empty setup, load/refresh/check failures, stale snapshot retention, explicit health probing and visible/hidden polling cleanup. Cameras and Store Layout tests cover requested camera/floor selection, missing targets and cross-floor target rejection.
- Playwright/Edge browser checks with synthetic API responses at **1920×1080 and 1280×720**: **PASS**. Checked filtering, missing requirements, explicit health check and snapshot reload, links opening the correct camera/floor/configuration, read-only AI Config detail, stale warning/retry, missing seed/empty setup and absence of horizontal page overflow. The only fixture write was the explicitly clicked health check; no shared DB data was accessed or mutated.
- Development Swagger export: **PASS**, **42 paths**, including the overview route and all new DTO schemas. The temporary Release documentation API used a dummy SQL connection with bootstrap/health worker disabled; only Swagger was requested. It was stopped after export; the existing user Debug API was not stopped.

SQL-backed overview execution, live camera/browser acceptance, event Investigation/Resolve forms, GitHub Actions execution and VPS deployment are not claimed. No schema/scaffold/DB script was changed or used to determine this contract. Dashboard readiness reflects the existing activation policy, rather than defining new activation requirements or MF-02 runtime behavior.

## Configuration deletion and floor editing validation 2026-10-03

Added ADMIN-only configuration DELETE with explicit config ID and saved timestamp checks. ACTIVE configs require deactivation; deleting DRAFT/INACTIVE removes their rules and config in the existing transaction, preserving spatial setup. AI Config now exposes the existing Review → Activate flow in a prominent detail banner. Store Layout adds Create/Edit floor dialogs; metadata editing uses a dedicated name/number PATCH that retains the latest stored map metadata.

- Backend Release non-SQL tests (`Category!=SqlIntegration`) with coverage: **199 passed, 0 failed, 0 skipped**. Process-only EventLog logging override used as above. **38 focused tests passed**, including 21 new cases for DRAFT/INACTIVE removal, spatial and other-config preservation, ACTIVE/stale/replaced config rejection, recreation, updated-map preservation, duplicate/name validation, application and HTTP ADMIN/other-role/anonymous checks, and the 204 response. HTTP tests substitute an in-memory store; no SQL fixture runs.
- Release build: **PASS**, 0 warnings/errors. `dotnet publish src/Supermarket.Api -c Release --no-restore`: **PASS**.
- Frontend: **152 tests passed across 19 files**, production build and lint **PASS**. Only the existing bundle-size advisory remains. Added coverage for delete confirmation/cancel/version/bearer/conflict/overview, active deletion restrictions, floor creation/editing/selection/map preservation, local validation, duplicate retry, discarded input/focus, missing seed and current-edit guards. Fixed a missing warning SVG that otherwise crashed the no-floor guidance.
- Playwright/Edge at **1920×1080 and 1280×720** using synthetic API responses: **PASS**. Verified review blockers, Activate/Deactivate, ACTIVE deletion blocked, delete confirmation/Escape/focus restoration/conflict/success, detail/overview updates, floor metadata edit, Create cancel/Escape, duplicate errors retaining input, successful creation selecting the new floor, upload availability, empty floors/missing seed and no horizontal page overflow. Screenshots were inspected. All intercepted writes used the synthetic session; no shared database or real backend mutation endpoint was called.
- Runtime Swagger export: **PASS**, **43 paths**, including floor details PATCH, monitoring DELETE with 204 and their DTOs. Preserved the existing required-rule annotation. The owned Release API on 5087 used a dummy SQL connection and disabled bootstrap/health worker; only Swagger was requested, then that owned API was stopped. The user's Debug API was not stopped.

SQL transaction/FK rollback execution, shared-DB acceptance, actual GPU/camera work, GitHub Actions and deployment were not exercised in this increment. No SQL script, database schema or generated scaffold was modified. Floor deletion and System Health/event lifecycle forms are outside this request.
