# Multi-viewer and Two-camera AI Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let multiple authenticated viewers share one annotated pipeline per camera and let the current AI host process at most two independent cameras concurrently, without cross-camera tracking or persisted track identity.

**Architecture:** The Python service remains the runtime owner of one reader, YOLO instance, ByteTrack context and latest-frame buffer per active camera. Each mounted preview receives an opaque viewer lease; ASP.NET validates business access and transparently proxies that lease, while React owns and releases only its component's lease. Capacity counts active camera workers, not viewers, and remains process-local under the existing single Uvicorn worker.

**Tech Stack:** Python 3.11, FastAPI, Pydantic, pytest/pytest-asyncio, PyTorch/Ultralytics/OpenCV; ASP.NET Core .NET 10, xUnit; React 19, TypeScript 6, Vite/Vitest; Docker Compose, NVIDIA CUDA, Tailscale.

**Spec:** `docs/superpowers/specs/2026-10-25-multi-viewer-two-camera-ai-preview-design.md`

## Global Constraints

- Scope ceiling is exactly two concurrently active camera workers: `AI_MAX_ACTIVE_CAMERA_SESSIONS` is bounded to `1..2` and defaults to `2` for this host.
- One camera has one shared processing worker regardless of viewer count; a viewer lease never owns YOLO, ByteTrack, monitoring, or a camera stream.
- Each camera worker has its own reader, YOLO model and ByteTrack context; never share `YOLO.track(persist=True)` state across cameras.
- Track IDs are camera/session-local, transient, not identities, not cross-camera correlated, and never persisted.
- No database, ERD, Incident, OperationalEvent, multi-camera counting or designated-source-camera change is allowed.
- Monitoring ownership remains authoritative; a viewer cannot stop, reconfigure or keep a monitoring worker alive.
- Keep exactly one Uvicorn worker while capacity and leases are in process memory.
- Do not expose RTSP URLs, camera credentials, internal service keys, or viewer lease IDs in error messages or routine URL query logs.
- Public ADMIN/OPERATOR authorization remains unchanged; a lease supplements JWT authorization and never replaces it.
- Preserve the existing uncommitted Docker/setup files in `setup/ai-service`; do not overwrite local secrets in `.env.docker`.

## Review Focus

- A stale lease from an earlier camera `sessionId` must return `AI_VIEWER_LEASE_EXPIRED`, never attach to the replacement session; Task 1 pins this in manager tests.
- Concurrent final-release and new-attach must leave exactly one live worker or one stopped session, never an orphan or capacity overrun; Task 1 pins the race.
- A Draft-confidence Start that conflicts with an active preview configuration must return `AI_PREVIEW_CONFIGURATION_CONFLICT` without restarting the shared worker; Tasks 1 and 4 pin this.
- SQL says monitoring is active while the Python monitoring worker has not started: no preview worker may be created; Task 4 pins `AI_MONITORING_START_PENDING` and Task 5 pins bounded retry behavior.
- Completed/error sessions have no worker but can retain leases/frames temporarily; expiry/release must reclaim them without consuming camera-worker capacity; Task 1 pins cleanup and capacity accounting.

---

### Task 1: Python per-viewer lease registry and two-camera capacity

**Files:**
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/app/session_manager.py`
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/app/schemas.py`
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/app/settings.py`
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/tests/test_session_manager.py`
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/tests/test_monitoring_sessions.py`

**Interfaces:**
- Consumes: existing `StartSessionRequest`, `SessionStatusResponse`, per-camera `_lock_for(camera_id)`, `_capacity_lock`, and monitoring-owner lifecycle.
- Produces: `SessionStatusResponse.viewer_lease_id: UUID | None`; `ViewerLeaseError`; `SessionConfigurationConflictError`; `SessionManager.start(...)`; `attach_viewer(...)`; `status(camera_id, viewer_lease_id)`; `frame(camera_id, viewer_lease_id)`; `next_frame(camera_id, viewer_lease_id, after_sequence, after_session_id)`; `release_viewer(camera_id, viewer_lease_id)`; `force_stop(camera_id)`; `max_active_camera_sessions` constructor option.

- [ ] **Step 1: Replace the single-capacity test with failing lease/capacity tests**

  Add tests named `test_two_viewers_share_one_camera_worker_and_have_distinct_leases`, `test_releasing_one_viewer_keeps_the_other_live`, `test_two_distinct_cameras_run_with_isolated_trackers`, `test_same_numeric_track_id_on_two_cameras_remains_camera_local`, `test_preview_and_monitoring_workers_share_same_two_camera_capacity`, and `test_third_camera_is_rejected_without_stopping_the_first_two`. Assert one reader/tracker for two same-camera viewers, two readers/trackers for two cameras, distinct lease UUIDs, advancing frames on surviving sessions, unrelated equal-number fake track IDs, and `AI_SESSION_CAPACITY` only for Camera 3.

- [ ] **Step 2: Run the focused manager tests and verify the new tests fail**

  Run from `C:/FPT University/CAPSTONE/setup/ai-service`:
  `..\.venv\Scripts\python.exe -m pytest tests/test_session_manager.py -q`

  Expected: failures because leases and configurable two-camera capacity do not exist yet.

- [ ] **Step 3: Add failing lifecycle and isolation tests**

  Add `test_final_release_stops_preview_owned_worker`, `test_abandoned_final_lease_expires_and_releases_capacity`, `test_completed_or_error_session_lease_is_pruned_by_sweeper`, `test_old_generation_lease_cannot_access_restarted_session`, `test_unknown_release_is_idempotent`, `test_configuration_mismatch_does_not_restart_shared_worker`, `test_failure_of_camera_one_does_not_change_camera_two`, and `test_concurrent_final_release_and_attach_leaves_no_orphan_worker`.

  In `test_monitoring_sessions.py`, add `test_viewer_release_never_stops_monitoring_owner` and `test_monitoring_replacement_invalidates_preview_generation_leases`.

- [ ] **Step 4: Implement lease and capacity state in the manager**

  Add `_Session.viewer_leases: dict[UUID, float]`, `_Session.preview_request: StartSessionRequest | None`, and a manager-owned sweeper task. Implement the produced interfaces with these exact rules: validate/renew under the camera lock; release the lock before the one-second frame wait; count only non-done worker tasks; allow at most `max_active_camera_sessions`; clear leases whenever a fresh `session_id` is created; stop a preview-owned worker only after its final lease is absent; never let viewer release stop monitoring.

  Compare active preview requests using source type/URI, username, secret value, model, tracker, classes, confidence, device and half, but never log or return those values. Raise `AI_PREVIEW_CONFIGURATION_CONFLICT` on mismatch.

- [ ] **Step 5: Add settings validation and wire manager defaults**

  Add `Settings.max_active_camera_sessions: int = Field(default=2, ge=1, le=2)`. Pass it to `SessionManager`; keep Uvicorn worker count unchanged.

- [ ] **Step 6: Run Python manager and monitoring tests**

  Run:
  `..\.venv\Scripts\python.exe -m pytest tests/test_session_manager.py tests/test_monitoring_sessions.py -q`

  Expected: all selected tests pass, including camera-local tracker isolation and lease expiry.

- [ ] **Step 7: Commit the Python runtime unit**

  From `setup/ai-service`, stage only the five files in this task and commit as `feat: add viewer leases and two-camera capacity`.

---

### Task 2: Python internal HTTP lease contract

**Files:**
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/app/main.py`
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/tests/test_api.py`
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/tests/test_monitoring_api.py`

**Interfaces:**
- Consumes: Task 1 manager methods and `SessionStatusResponse.viewer_lease_id`.
- Produces: header `X-AI-Viewer-Lease`; viewer Start, status, frame, next-frame and release routes; `POST /sessions/{camera_id}/viewers/attach`; separate `DELETE /sessions/{camera_id}/force`; stable errors `AI_VIEWER_LEASE_REQUIRED`, `AI_VIEWER_LEASE_EXPIRED`, and `AI_PREVIEW_CONFIGURATION_CONFLICT`.

- [ ] **Step 1: Write failing FastAPI contract tests**

  Update the fake manager to accept lease parameters. Test that Start returns `viewer_lease_id`; status/frame/frame-next require `X-AI-Viewer-Lease`; `DELETE /sessions/{camera_id}/viewer` releases only that lease; `POST /sessions/{camera_id}/viewers/attach` attaches only to an existing worker; and `DELETE /sessions/{camera_id}/force` calls `force_stop`.

- [ ] **Step 2: Add failing error/sanitization tests**

  Assert omitted header on read routes gives 422 with `AI_VIEWER_LEASE_REQUIRED`, stale/unknown lease on read routes gives 409 with `AI_VIEWER_LEASE_EXPIRED`, releasing an unknown/already-released lease remains 200/idempotent, config mismatch gives 409 with `AI_PREVIEW_CONFIGURATION_CONFLICT`, and none of the responses contain stream URLs, credentials, service keys, or any other lease.

- [ ] **Step 3: Run API tests and verify failure**

  Run:
  `..\.venv\Scripts\python.exe -m pytest tests/test_api.py tests/test_monitoring_api.py -q`

  Expected: failures on missing routes, header parameters and error handlers.

- [ ] **Step 4: Implement the internal HTTP contract**

  Use `Header(alias="X-AI-Viewer-Lease")` for status/frame/next/release. Keep `/sessions/{camera_id}/start` as the create-or-attach operation. Add the explicit attach-existing route for monitoring-owned backend flow. Replace the old camera-wide viewer delete with lease release and reserve `/force` for controlled backend maintenance only. Mark the non-sequenced frame route deprecated in OpenAPI while enforcing the same lease.

- [ ] **Step 5: Run all Python tests**

  Run:
  `..\.venv\Scripts\python.exe -m pytest -q`

  Expected: full suite passes; monitoring owner routes continue to use `X-AI-Monitoring-Owner` independently.

- [ ] **Step 6: Commit the Python API unit**

  Commit the three task files as `feat: expose viewer-scoped AI preview contract`.

---

### Task 3: ASP.NET internal client and typed lease contracts

**Files:**
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/src/Supermarket.Application/AiPreviewContracts.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/src/Supermarket.Application/AiPreviewAbstractions.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/src/Supermarket.Infrastructure/Ai/AiPreviewClient.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/tests/Supermarket.Tests/AiPreviewApiTests.cs`

**Interfaces:**
- Consumes: Task 2 internal routes and `X-AI-Viewer-Lease`.
- Produces: `AiPreviewStatusView.ViewerLeaseId`; `IAiPreviewClient.Start`; `AttachViewer`; lease-aware `Status`, `Frame`, `NextFrame`, `ReleaseViewer`; maintenance-only `ForceStop`.

- [ ] **Step 1: Write failing client contract tests**

  Assert Start deserializes `viewer_lease_id`; all read calls and release send exactly the supplied `X-AI-Viewer-Lease`; release targets `/sessions/{cameraId}/viewer`; `AttachViewer` targets `/sessions/{cameraId}/viewers/attach`; and `ForceStop` alone targets `/sessions/{cameraId}/force`.

- [ ] **Step 2: Write failing stable-error tests**

  Extend the allowlist/mapping assertions for `AI_VIEWER_LEASE_REQUIRED`, `AI_VIEWER_LEASE_EXPIRED`, `AI_PREVIEW_CONFIGURATION_CONFLICT`, and `AI_MONITORING_START_PENDING`. Assert messages remain sanitized.

- [ ] **Step 3: Run focused .NET tests and verify failure**

  Run from `C:/FPT University/CAPSTONE/Backend/Back-End`:
  `dotnet test tests/Supermarket.Tests/Supermarket.Tests.csproj --filter FullyQualifiedName~AiPreviewApiTests`

  Expected: compile/test failures until interface signatures and DTOs carry the lease.

- [ ] **Step 4: Implement the typed client contract**

  Use signatures:
  `Task<AiPreviewStatusView> AttachViewer(Guid cameraId, CancellationToken ct)`;
  `Task<AiPreviewStatusView> Status(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)`;
  `Task<PreviewFrame> Frame(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)`;
  `Task<SequencedPreviewFrame?> NextFrame(Guid cameraId, Guid viewerLeaseId, long afterSequence, Guid? afterSessionId, CancellationToken ct)`;
  `Task<AiPreviewStatusView> ReleaseViewer(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)`;
  `Task<AiPreviewStatusView> ForceStop(Guid cameraId, CancellationToken ct)`.

  Continue adding `X-AI-Service-Key` independently on every internal request.

- [ ] **Step 5: Run focused tests**

  Run the Step 3 command. Expected: all `AiPreviewApiTests` client-contract cases pass.

- [ ] **Step 6: Commit the backend transport unit**

  Commit the four task files as `feat: proxy AI viewer leases`.

---

### Task 4: ASP.NET application/API lifecycle and monitoring authority

**Files:**
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/src/Supermarket.Application/AiPreview.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/src/Supermarket.Api/Controllers/AiPreviewController.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/src/Supermarket.Application/RecordedVideoUpload.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/tests/Supermarket.Tests/AiPreviewApplicationTests.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/tests/Supermarket.Tests/AiPreviewApiTests.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/tests/Supermarket.Tests/MonitoringPreviewOwnershipTests.cs`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/tests/Supermarket.Tests/RecordedVideoTests.cs`

**Interfaces:**
- Consumes: Task 3 `IAiPreviewClient` lease methods.
- Produces: public header `X-AI-Viewer-Lease` for status/frame reads; `ReleaseAiPreviewRequest(Guid ViewerLeaseId)` body for stop; monitoring-pending error; one public lease per successful Start.

- [ ] **Step 1: Write failing application lifecycle tests**

  Replace camera-wide idempotent Start/Stop expectations with: two Starts return distinct leases; releasing Lease A leaves Lease B usable; malformed/unknown public leases are not substituted; a Draft configuration conflict is propagated without ForceStop; and ordinary viewer cleanup never calls ForceStop.

- [ ] **Step 2: Write failing monitoring-transition tests**

  Assert active SQL monitoring plus a live Python monitoring worker uses `AttachViewer`; active SQL monitoring plus no worker returns `AI_MONITORING_START_PENDING` and never starts preview; monitoring session replacement causes old lease expiry; and viewer release does not stop monitoring.

- [ ] **Step 3: Write failing controller and maintenance tests**

  Assert status/frame/next read `X-AI-Viewer-Lease`, stop requires JSON `{ viewerLeaseId }`, ADMIN/OPERATOR authorization remains, and recorded-video replacement uses `ForceStop` rather than viewer release.

- [ ] **Step 4: Run the focused backend tests and verify failure**

  Run:
  `dotnet test tests/Supermarket.Tests/Supermarket.Tests.csproj --filter "FullyQualifiedName~AiPreview|FullyQualifiedName~MonitoringPreviewOwnership|FullyQualifiedName~RecordedVideo"`

  Expected: compile/test failures until the public application contract is lease-aware.

- [ ] **Step 5: Implement application and controller behavior**

  Make normal Start call the create-or-attach client method. When monitoring is authoritative, call `AttachViewer` only; translate `AI_PREVIEW_NOT_RUNNING` to `AI_MONITORING_START_PENDING` without creating a preview worker. Remove the current Draft path that calls camera-wide Stop; let the AI service reject mismatched active config. Route all viewer reads/releases with the supplied lease. Keep ForceStop private to controlled maintenance use cases such as recorded-source replacement.

- [ ] **Step 6: Run focused and non-SQL backend suites**

  Run the Step 4 command, then:
  `dotnet test tests/Supermarket.Tests/Supermarket.Tests.csproj --filter "Category!=SqlServer"`

  Expected: all selected tests pass with no database schema changes.

- [ ] **Step 7: Commit the backend lifecycle unit**

  Commit the seven task files as `feat: scope AI preview lifecycle to each viewer`.

---

### Task 5: React lease ownership and controlled reattach

**Files:**
- Modify: `C:/FPT University/CAPSTONE/Frontend/Front-End/src/api/cameras.ts`
- Modify: `C:/FPT University/CAPSTONE/Frontend/Front-End/src/api/cameras.test.ts`
- Modify: `C:/FPT University/CAPSTONE/Frontend/Front-End/src/components/AnnotatedPreview.tsx`
- Modify: `C:/FPT University/CAPSTONE/Frontend/Front-End/src/components/AnnotatedPreview.test.tsx`
- Modify: `C:/FPT University/CAPSTONE/Frontend/Front-End/src/pages/Cameras.tsx`
- Modify: `C:/FPT University/CAPSTONE/Frontend/Front-End/src/pages/Cameras.test.tsx`

**Interfaces:**
- Consumes: Task 4 public contract and `AiPreviewStatus.viewerLeaseId`.
- Produces: lease-aware API functions and one component-local lease ref per mounted `AnnotatedPreview`.

- [ ] **Step 1: Write failing API tests**

  Assert `getAiPreviewStatus`, legacy frame and next-frame send `X-AI-Viewer-Lease`; `stopAiPreview(cameraId, viewerLeaseId)` sends JSON; and Start requires a non-null `viewerLeaseId` in its successful response.

- [ ] **Step 2: Write failing component isolation tests**

  Add tests that mount two previews for the same camera, return Lease A and Lease B, and assert cleanup of A releases only A while B continues polling with B. Assert camera switching and Live Feed switching release the mounted component lease exactly once and never issue a camera-wide stop.

- [ ] **Step 3: Write failing recovery tests**

  Assert one `AI_VIEWER_LEASE_EXPIRED` transitions into a single reattach state machine, replaces the lease, resets `sessionId/frameSequence`, and resumes frames. Assert `AI_MONITORING_START_PENDING` retries Start with bounded delay without creating a lease or requiring reload. Assert repeated unrelated failures still stop after the existing retry limit.

- [ ] **Step 4: Run focused frontend tests and verify failure**

  Run from `C:/FPT University/CAPSTONE/Frontend/Front-End`:
  `npm test -- --run src/api/cameras.test.ts src/components/AnnotatedPreview.test.tsx src/pages/Cameras.test.tsx`

  Expected: failures because calls are camera-only and `Cameras.tsx` still invokes a global stop.

- [ ] **Step 5: Implement lease-aware API functions**

  Change signatures to `getAiPreviewStatus(cameraId, viewerLeaseId, signal?)`, `getAiPreviewFrame(cameraId, viewerLeaseId, signal?, afterSequence?, afterSessionId?)`, and `stopAiPreview(cameraId, viewerLeaseId)`. Keep lease values in headers/body, never URL query strings.

- [ ] **Step 6: Implement component-local lifecycle**

  Store the Start result lease in a ref scoped to the effect generation. Cleanup waits for an in-flight Start and releases only that generation's lease. Remove the direct stop import/call from `Cameras.tsx`; component unmount/disable owns release. Permit one lease-expiry reattach per lease generation, with a new allowance only after a successful fresh lease is established.

- [ ] **Step 7: Run frontend verification**

  Run:
  `npm test -- --run`

  Then run:
  `npm run lint`

  Then run:
  `npm run build`

  Expected: tests, lint and production build all pass.

- [ ] **Step 8: Commit the frontend unit**

  Commit the six task files as `feat: isolate AI preview viewer leases`.

---

### Task 6: Contract documentation and deployment configuration

**Files:**
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/compose.ai.yml`
- Modify: `C:/FPT University/CAPSTONE/setup/ai-service/README.md`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/docs/API.md`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/docs/openapi.json`
- Modify: `C:/FPT University/CAPSTONE/Backend/Back-End/README.md`
- Modify: `C:/FPT University/CAPSTONE/Frontend/Front-End/README.md`

**Interfaces:**
- Consumes: final routes, headers, DTOs and errors from Tasks 2–5.
- Produces: reproducible Docker setting and synchronized API/operator documentation.

- [ ] **Step 1: Document the final public/internal contracts**

  Update API/OpenAPI docs with `viewerLeaseId`, `X-AI-Viewer-Lease`, release body, `AI_VIEWER_LEASE_REQUIRED`, `AI_VIEWER_LEASE_EXPIRED`, `AI_PREVIEW_CONFIGURATION_CONFLICT`, `AI_MONITORING_START_PENDING`, deprecated lease-protected legacy frame endpoint, and unchanged ADMIN/OPERATOR roles.

- [ ] **Step 2: Document scope and runtime ownership**

  State explicitly in all relevant READMEs: many viewers share one camera worker; at most two cameras; each has independent YOLO+ByteTrack state; no cross-camera re-identification/deduplication/persistence; one Uvicorn worker required.

- [ ] **Step 3: Add non-secret Docker configuration**

  In `compose.ai.yml`, pass `AI_MAX_ACTIVE_CAMERA_SESSIONS=${AI_MAX_ACTIVE_CAMERA_SESSIONS:-2}` without copying any service key or camera credential into the file. Keep the current GPU reservation and worker command unchanged.

- [ ] **Step 4: Validate generated/static contracts**

  Run Python tests, the backend focused tests, frontend tests, and parse `docs/openapi.json` with the repository's existing validation approach. Expected: no drift between implemented route names and documented route names.

- [ ] **Step 5: Commit documentation/configuration**

  Commit only non-secret tracked files as `docs: document multi-viewer two-camera preview`.

---

### Task 7: Coordinated local and hardware acceptance

**Files:**
- Create: `C:/FPT University/CAPSTONE/Backend/Back-End/docs/verification/2026-10-25-multi-viewer-two-camera-ai-preview.md`

**Interfaces:**
- Consumes: built AI image, deployed backend/frontend contract, two reachable camera sources, RTX 4060 and Tailscale connectivity.
- Produces: dated evidence for release/rollback; no product data or new business behavior.

- [ ] **Step 1: Rebuild and start the AI container**

  From `setup/ai-service`, run `docker compose -f compose.ai.yml build` and `docker compose -f compose.ai.yml up -d`. Confirm `docker compose -f compose.ai.yml ps` reports healthy and `/health` returns `ready`.

- [ ] **Step 2: Smoke-test one camera with two viewers**

  Open two authenticated browser sessions on the same camera. Record that they receive the same `sessionId`, distinct `viewerLeaseId` values, and advancing frames. Close Viewer A and prove Viewer B continues without a worker restart or sequence reset.

- [ ] **Step 3: Exercise two independent cameras**

  Start Camera 1 and Camera 2 together. Record distinct camera/session IDs, advancing frames on both, two camera-local tracker instances, and no shared/persisted track identity. A repeated numeric track ID across cameras is acceptable and must be documented as unrelated.

- [ ] **Step 4: Prove bounded capacity and fault isolation**

  Attempt Camera 3 and capture `AI_SESSION_CAPACITY` while Cameras 1 and 2 continue. Stop/fail Camera 1 and prove Camera 2 remains live. Restart Camera 1 and prove it receives a fresh session/tracker generation.

- [ ] **Step 5: Record hardware observations**

  During a bounded soak, record GPU memory, GPU utilization, effective FPS per camera, frame age, container restarts and CUDA OOM count. Do not invent a fixed FPS threshold; pass requires both previews remain usable with no OOM, restart or starvation.

- [ ] **Step 6: Verify monitoring/Draft transitions**

  Confirm viewer release never stops active monitoring, monitoring replacement makes old viewer leases expire and reattach, monitoring-start-pending does not create a competing preview, and conflicting Draft confidence returns `AI_PREVIEW_CONFIGURATION_CONFLICT` without interrupting viewers.

- [ ] **Step 7: Write verification evidence and rollback note**

  Save commands, timestamps, observed metrics and pass/fail outcomes. Document rollback as coordinated frontend/backend/AI rollback plus `AI_MAX_ACTIVE_CAMERA_SESSIONS=1`; no database rollback exists.

- [ ] **Step 8: Final cross-repository verification**

  Re-run all three automated suites and `git diff --check` in each repository. Expected: all pass, no secret file staged, and no ERD/database changes.

