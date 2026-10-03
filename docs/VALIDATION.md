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

## Cloudinary floor-plan storage validation 2026-10-03

Added a selectable Cloudinary adapter using signed HTTPS Upload API calls (SHA-256 per Cloudinary documentation), with authenticated assets, private original-file downloads and asynchronous exact-asset cleanup. The default remains Local until keys are supplied and Provider is changed. Existing local references remain readable through the routing adapter. No schema/scaffold change, SQL script or shared database access was required.

- Release solution build: **PASS**, 0 warnings/errors. Backend non-SQL selection (`Category!=SqlIntegration`) with coverage: **234 passed, 0 failed, 0 skipped**. **58 focused storage/Operator tests passed**, including 35 new Cloudinary cases. Process-only EventLog logging override used as above.
- Controlled HttpMessageHandler tests verify signed multipart/form fields, no exposed secret, authenticated type, immutable asset references, image/raw resource types, original PNG/JPEG/PDF bytes and MIME, pre-upload size/extension/signature/incomplete/truncated-header rejection, malformed/transformed upstream response, sanitized auth/network/timeout errors, old-map preservation, DB-failure cleanup, exact prior deletion without touching unrelated assets, cleanup-failure warning without failing a committed upload, fresh-adapter read, local fallback, invalid/cross-floor/external references, cloud mismatch, missing asset and fail-fast startup configuration. WebApplicationFactory tests exercise the production DI and real POST/GET controllers for ADMIN/OPERATOR/MANAGER/STAFF/anonymous and no-store delivery; store and cloud transport are fake.
- Existing local persistence-failure test now additionally proves the prior map can still be read after rollback; spatial configuration is not changed by storage upload.
- `dotnet publish src/Supermarket.Api -c Release --no-restore`: **PASS**.
- Frontend floor-plan HTTP timeout compatibility: **29 focused tests across 3 files passed** (floor API, client and Store Layout), production build and lint **PASS**. Upload timeout is 300 seconds and map download 150 seconds to cover the supported remote operation timeout range; a fake-clock test checks a 31-second upload remains eligible and explicit download cancellation still aborts/cleans timers. Existing Vite bundle-size advisory remains. No UI/DTO/API route or authentication behavior changed.
- Prepared the ignored `appsettings.Local.json` Cloudinary section without printing credentials or changing existing DB/JWT settings; tracked base/example configuration contains empty credentials and Provider Local. Verified Git ignores the real local file. API and setup instructions were updated; OpenAPI route/DTO schemas are unchanged.

No real Cloudinary credential was supplied, so live upload/download/delete, account limits/PDF permissions and actual cross-machine/restart acceptance remain unverified. No shared local map was uploaded/deleted, no SQL integration was executed and no VPS was accessed. Previously saved local images require their local files until explicitly replaced with the same originals under the Cloudinary provider. Cleanup/network failures can leave unreferenced assets; warnings/manual storage inspection are documented, with no automatic bulk deletion or startup migration.

## Cloudinary Media Library folder correction 2026-10-03

After the user supplied credentials locally and reported a floor-plan image appearing in Cloudinary Home, source inspection found that uploads only prefixed `public_id` with the configured Folder. Cloudinary dynamic folders require a separate `asset_folder` parameter. Uploads now send the configured value in both locations; existing asset IDs/downloads are unchanged and no remote asset was moved.

- Release build performed by focused tests: **PASS**. Cloudinary/floor-plan/Operator selection: **59 passed, 0 failed, 0 skipped**, including 36 Cloudinary cases. PNG/JPEG/PDF contract checks now assert `asset_folder`; an additional case verifies a custom Folder value and the signed request. `git diff --check`: **PASS**.
- README and API storage notes explain new uploads, existing Home assets and folder moves. No frontend/API schema/SQL change. Live acceptance of the newly added folder parameter remains unverified; no real credentials were printed or used by these tests.

## MF-02 ROI count / queue / incident runtime validation 2026-10-03

Post-integration verification (2026-10-04): BE Release **279 passed, 0 failed, 0 skipped** including fixture-owned SQL integration tests; the complementary non-SQL category gate passed **259/259** with an intentionally unavailable SQL connection. FE **169/169** across 22 files, build/lint and BE publish passed. Conflicts were resolved keeping Cloudinary/floor editing/setup overview/config deletion alongside monitoring runtime/feed. Review and overview both reject ambiguous multi-camera measurements; deletion now detaches optional rule references while retaining historical events and snapshots (SQL regression verified). Empty rule saves follow the team's newer RULES_REQUIRED policy. The initial restricted-host non-SQL run failed on Windows EventLog permissions and passed when rerun outside the sandbox. No shared Dev DB mutation or new browser/GPU acceptance was performed during integration. Code was committed/pushed on the existing BE/FE branches; these local documentation changes remain uncommitted. AI-Service/Database were not pushed in this operation.

Assigned increment: explicit temporary PEOPLE_COUNT for overcrowding (no m² estimate), QUEUE_LENGTH with observed continuous ROI dwell ≥5s, then independent warning/critical sustain, backend-owned monitoring, aggregate OperationalEvent persistence and one open DETECTED Incident per zone/type. DETECTED means not dispatched. This is the user's approved increment, not a claim that the team's BR/ERD documents were revised. Density, waiting-time/checkout runtime, dispatch/tasks, product closure endpoints, media evidence and analytics remain unimplemented.

- `dotnet restore --configfile NuGet.Config`: **PASS**.
- `dotnet test -c Release --no-restore --settings coverage.runsettings --collect:'XPlat Code Coverage' --results-directory TestResults`: **200 passed, 0 failed, 0 skipped**. Tests use fixture-owned local SQL Express databases, apply Database baseline + migration01 + migration02, and clean up those fixtures. Shared Dev was not migrated or mutated.
- `dotnet publish src/Supermarket.Api -c Release --no-restore -o .local/publish`: **PASS**.
- Both Database migration runners: **PASS**, including preserved upgrade/rerun history, nested caller transaction, schema/JSON/FK/lifecycle/unique-open guards, wrong target, partial/missing/altered CHECK/index rejection and DDL rollback. EF scaffold was generated from an isolated migrated SQL fixture (14 tables), not manually edited.
- Real-SQL API/production coordinator tests cover Draft → Review → Activate, no area required for explicit count, WARNING → CRITICAL without a second open incident, no auto-close/downgrade, viewer start/stop independence, EOF once, restart/post-closure cooldown, deactivate/queued-observation race, concurrent serializable writers/idempotent retries, 1000 tied-history rows with bounded SQL keyset pagination, and two configuration versions on one camera without stop/recorded rewind. The hosted timer is disabled in these tests: controlled production `Tick` verifies processing, not scheduler cadence under load. Closure is fixture SQL only, not a product endpoint.
- Worker regressions cover partial-frame retry checkpoints, source/credential identity release before restart, ordered low observations between highs, and all-frame evaluation with SQL writes restricted to source-second samples or newly sustained severity. Last observed incident ID is retained between samples. Fixture DateTime parameters explicitly use datetime2(3); a deterministic .002→.003 legacy datetime rounding reproduction prevented a false cooldown-boundary result.
- Generated Swagger contract: **43 paths, 40 schemas**; typed runtime/feed, additive catalog options/preview purpose, RBAC and readiness503 covered by a real-SQL test host. `docs/openapi.json` is exported from that compiled host. It is not an export from the user's running API or shared DB.
- FE `npm run test -- --run`: **148 passed across 21 files**. `npm run build` and `npm run lint`: **PASS**, no lint warnings; existing Vite >500kB chunk advisory remains. Tests cover explicit count conversion/no silent density reuse, team overview/detail/edit preservation, non-overlapping polling, camera-switch cancellation, separate endpoint errors/stale data, runtime/DEMO/DETECTED display, owned preview startup/EOF/Draft conflict, pending-stop guidance and existing ROI/minimap behavior.
- AI full pytest using existing native venv and a unique writable `--basetemp` (`-p no:cacheprovider`): **52 passed, 1 skipped**. The skipped IP Webcam test requires `AI_TEST_STREAM_URL`; it is not GPU validation. One existing Starlette/httpx dependency deprecation warning remains.
- Native AI smoke on RTX4060 Laptop GPU used existing YOLO26n + ByteTrack and an existing uploaded video, read-only and bounded to its first 8s: **240 frames / 7967ms source time**, two zone confidence contexts (.5/.7), maximum counts1/1, final JPEG118745 bytes, COMPLETED and no EOF replay. Queue remained0 in this sample; positive 5s dwell is covered by deterministic tests, not claimed observed in this native sample. Installed Ultralytics emitted a `half` deprecation advisory; no dependency upgrade was made.
- `git diff --check` in BE/FE/AI/Database: **PASS**; no conflict markers. Current branches preserved; no stage, commit or push.

Supplemental CI prerequisite verification: the old non-SQL name filter also selected `ConcurrencyTests`, which uses `SqlApiFixture`. It was corrected to `FullyQualifiedName!~ApiFlowTests&FullyQualifiedName!~ConcurrencyTests`. The corrected Release selection passed **180/180** with `MF01_TEST_SERVER=localhost\MF02_NO_SQL_UNIT_GATE` (intentionally unavailable), confirming no SQL fixture was required. The first sandbox attempt additionally failed on Windows EventLog/SQL integrated-auth permissions; these are execution-environment limitations, not a green test result. Full Release200/200 and the corrected filter were run with normal local permissions. GitHub Actions itself remains unverified.

Final deactivation race regression: real-SQL test RED STOPPED→GREEN STOPPING before worker cleanup, RED premature Activate200→GREEN409 MONITORING_STOP_PENDING, then STOPPED/owner cleared followed by fresh-session source-time0 replay. Existing same-source two-zone reconfiguration remains allowed. After this fix the complete BE Release suite **200/200**, local publish and FE **148/148/build/lint** were rerun successfully.

Not verified: one joint native AI + SQL + real browser acceptance session, real phone/CCTV monitoring, sustained multi-camera/load/long-history performance, GitHub Actions execution or deployment. The native smoke validates actual GPU inference separately; SQL tests use fake measurement transport and FE tests use controlled API responses. Runtime lease, in-memory buffer and progress are transient; restart resets continuity, while SQL incident/cooldown history remains authoritative. After reviewing/running migration02 on shared Dev, restart the upgraded services, deactivate legacy density configurations, explicitly choose count with new integer thresholds, Save → Review → Activate, then validate runtime/incident panels in the live page.

## YOLO26s and sequence-aware preview validation 2026-10-04

- Default model references in AI and BE changed to `../yolo26s.pt`; the official 20,422,725-byte checkpoint is present locally in `CAPSTONE/setup` and was loaded on the RTX 4060 Laptop GPU. No model weights or video were added to Git. Local AI `.env` and BE `appsettings.Local.json` do not override this model.
- A read-only 30-frame sample from an existing 854×480, 29.97 FPS MP4 measured full preview tracking + annotation + JPEG at 17.9 ms/frame for YOLO26n and 18.9 ms/frame for YOLO26s after five warm-up frames. Another 854×480, 30 FPS sample measured YOLO26s at 16.3 ms/frame. These are local processing measurements, not end-to-end browser FPS or accuracy scores.
- Native AI HTTP smoke with the real YOLO26s, ByteTrack and recorded MP4: `POST /sessions/{id}/start` returned STARTING, then `GET .../frame/next` returned JPEG200 with sequence1 and 106,249 bytes. The owned test session was stopped. No BE/SQL/browser was part of this smoke.
- AI pytest: **54 passed, 1 skipped** (IP Webcam opt-in stream absent); one pre-existing Starlette/httpx warning. BE tests excluding SQL integration: **261 passed**, 0 failed. BE Release publish: **PASS**. FE: **172 passed across 22 files**, lint and production build **PASS**; existing Vite chunk-size advisory remains.
- New tests cover waiting for a newer frame, no duplicate at EOF, session-ID reset, no stale frame after ERROR, AI 200/204 contracts, BE proxy headers/204, React sequence/no redraw, delayed Blob URL cleanup, recorded completion, ROI, abort, and a frame-request/session-restart race. `git diff --check` and checked-in OpenAPI JSON syntax were checked separately. Runtime Swagger export, shared SQL integration, full browser acceptance, real IP Webcam and sustained end-to-end FPS remain unverified in this increment.
