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

Not verified in this workspace: real CCTV/RTSP connectivity, continuous browser streaming, AI pipeline integration, React integration, Docker image build, GitHub Actions execution or VPS deployment. Pending MonitoringRule/IncidentType ERD work remains outside this increment's persistence layer.
