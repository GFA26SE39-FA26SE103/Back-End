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
