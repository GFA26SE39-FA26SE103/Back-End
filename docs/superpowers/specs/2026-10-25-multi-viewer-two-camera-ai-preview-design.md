# Multi-viewer and Two-camera AI Preview Design

**Date:** 2026-10-25  
**Status:** Approved by the team on 2026-10-25  
**Scope:** React frontend, ASP.NET Core backend AI-preview proxy, Python AI service runtime  

## Intent

Allow several authenticated users to watch the annotated preview of the same camera without one viewer stopping the preview for everyone else. Extend the AI runtime from one to at most two concurrently processed cameras on the current single RTX 4060 host.

The design keeps tracking camera-local and transient. It does not identify people, correlate tracks between cameras, deduplicate people across cameras, or persist track identities.

## Current-state findings

- The Python service owns one `_Session` per `camera_id` and reuses the running worker when the same camera is started again.
- `frame_condition.notify_all()` and the latest-frame buffer already allow multiple requests to read the same camera output.
- Every React `AnnotatedPreview` cleanup currently calls the camera-wide Stop endpoint.
- The public and internal Stop contracts identify only the camera, not the viewer. Consequently any viewer can stop the shared worker.
- Runtime logs confirmed this failure mode: two Start requests for the same camera returned 200, a subsequent camera-wide Delete returned 200, then another viewer's next-frame request returned 409.
- `SessionManager.start()` currently rejects a different camera whenever another camera worker is active.
- `FrameTracker` creates a separate Ultralytics `YOLO` instance and persistent ByteTrack context per camera session.
- Uvicorn intentionally runs with one worker, so the in-memory session registry has one owner process.

## Goals

1. One AI processing pipeline per active camera, shared by any number of authorized viewers.
2. A viewer leaving, changing mode, navigating away, or losing its browser connection must not stop other viewers or backend-owned monitoring.
3. Support up to two concurrently active camera pipelines, controlled by deployment configuration.
4. Keep each camera's reader, YOLO model, ByteTrack state, latest frame, sequence and errors isolated.
5. Treat a person observed by Camera 1 and Camera 2 as unrelated camera-local tracks.
6. Recover resources when viewers disappear without sending a clean Stop request.
7. Preserve the backend as the only Web-facing integration boundary; browsers never call the Python service directly.

## Non-goals

- Cross-camera re-identification, face recognition, global person IDs or cross-camera deduplication.
- Persisting raw detections, ByteTrack state or track identity.
- Combining counts from overlapping cameras or choosing the measurement source for a multi-camera Zone.
- Supporting more than two active camera pipelines in this change.
- Adding database tables, columns or ERD changes.
- Replacing the existing RTSP/HTTP/HLS readers or changing business Event/Incident rules.
- Guaranteeing a fixed production FPS before a hardware benchmark establishes an achievable target.

## Anti-goals

- Do not simply remove the capacity guard and allow unbounded GPU sessions.
- Do not use a shared `YOLO.track(persist=True)` context across cameras; that could mix tracker state.
- Do not make frontend component presence the owner of backend monitoring.
- Do not solve viewer cleanup by issuing a camera-wide Stop.
- Do not store lease tokens, frames or tracks in SQL Server.

## Approaches considered

### A. Independent camera pipelines plus viewer leases — selected

Each active camera owns one reader and one `FrameTracker`. Viewers receive independent leases attached to that camera session. Capacity counts camera workers, not viewer leases.

This matches the current code, keeps ByteTrack state isolated and limits the change to runtime/API lifecycle semantics. The cost is one YOLO model instance per active camera and therefore higher VRAM use.

### B. Shared detector scheduler plus one tracker per camera

Load one detection model, schedule or batch frames from cameras, then feed detections into separate ByteTrack instances. This is more resource-efficient but requires separating Ultralytics detection and tracking responsibilities and substantially refactoring the current tracker. It is not justified for the agreed two-camera ceiling.

### C. Remove explicit Stop and rely only on the existing idle timeout

This is the smallest patch, but it cannot accurately represent viewers, holds capacity after the last viewer leaves and leaves lifecycle races unresolved. It is rejected as a temporary symptom fix.

## Runtime model

```text
Camera 1 Session
  - CameraReader 1
  - YOLO model 1
  - ByteTrack context 1
  - latest annotated JPEG 1
  - viewer lease A
  - viewer lease B

Camera 2 Session
  - CameraReader 2
  - YOLO model 2
  - ByteTrack context 2
  - latest annotated JPEG 2
  - viewer lease C
```

Track IDs are meaningful only inside their camera/session context:

```text
Camera 1 / Track 15 != Camera 2 / Track 3
```

Both values are transient runtime state and are discarded when their camera session restarts.

## Viewer lease semantics

### Lease creation

- Starting preview creates a new opaque viewer lease UUID.
- If the camera pipeline is already active, the lease attaches to it without opening another stream or loading another tracker.
- If the camera pipeline is absent and capacity is available, Start creates the pipeline and the lease atomically under the camera/capacity locks.
- Repeated Start must not change the source, confidence or tracker of an already running session.

### Lease renewal

- Status and next-frame requests include the viewer lease ID and renew only that lease.
- A lease has no business identity meaning and is not a customer/staff tracking identifier.
- Expired leases are pruned using the configured session idle timeout.

### Lease release

- Stop releases only the supplied viewer lease.
- Releasing an unknown or already-released lease is idempotent.
- A preview-owned pipeline stops after its final active lease is released or expires.
- If another viewer lease remains, the worker and stream continue unchanged.
- If the session is monitoring-owned, viewer release never stops or resets the monitoring worker. The backend monitoring owner remains authoritative.

### Failure recovery

- A browser crash or lost network connection is handled by lease expiry; no clean Stop is required.
- A camera/reader failure changes only that camera session to reconnecting/error.
- An error in Camera 1 must not cancel Camera 2.
- Restarting an errored camera creates a fresh session and fresh camera-local tracker state.

## API contract direction

The browser continues to call only authenticated backend routes. The backend remains stateless with respect to lease ownership and forwards the opaque lease to the internal AI service.

### Public backend contract

- `POST /api/cameras/{cameraId}/ai-preview/start`
  - returns the existing status plus `viewerLeaseId`.
- `GET /api/cameras/{cameraId}/ai-preview/status?viewerLeaseId={id}`
  - validates/renews that viewer lease and returns camera session status.
- `GET /api/cameras/{cameraId}/ai-preview/frame/next?...&viewerLeaseId={id}`
  - validates/renews that lease and returns the next shared annotated frame.
- `POST /api/cameras/{cameraId}/ai-preview/stop`
  - accepts `{ "viewerLeaseId": "..." }` and releases only that lease.

The existing JWT role rules remain unchanged. The lease does not replace authentication or authorization.

### Internal AI contract

Use viewer-specific internal operations rather than camera-wide public deletion:

- attach/create viewer lease for a camera;
- renew/read status and frame using the lease;
- release one lease;
- retain separate owner-only monitoring start/measurements/stop operations.

The implementation plan may preserve route names where doing so remains clear and testable, but a viewer request must never call the camera-wide `_stop_session()` operation.

### Error behavior

- `AI_VIEWER_LEASE_REQUIRED`: viewer route omitted its lease.
- `AI_VIEWER_LEASE_EXPIRED`: lease is unknown or expired; frontend may perform a fresh Start.
- `AI_SESSION_CAPACITY`: two other camera pipelines already occupy capacity.
- Existing sanitized camera/source/service errors remain unchanged.

No error response exposes RTSP URLs, credentials, internal service keys or other viewers' lease IDs.

## Two-camera capacity and configuration

Add an AI service setting:

```env
AI_MAX_ACTIVE_CAMERA_SESSIONS=2
```

Rules:

- The value is bounded to `1..2`; deployment sets it to `2` for the current host and may use `1` for rollback.
- Capacity counts active camera worker tasks, regardless of viewer count.
- Starting another viewer for an already active camera never consumes another capacity slot.
- Preview-owned and monitoring-owned camera workers both consume one slot.
- A third distinct camera receives `AI_SESSION_CAPACITY` without affecting the two running cameras.
- A stopped/completed worker does not consume a slot; a retained final recorded frame is not an active GPU worker.

The Docker deployment keeps one Uvicorn worker. Multiple Uvicorn workers would create independent in-memory registries and invalidate capacity/lease correctness unless a separate coordinator were introduced, which is outside this design.

## Frontend behavior

- `AnnotatedPreview` stores the returned viewer lease in a ref scoped to that mounted preview instance.
- All status/frame/stop calls for that component use the same lease.
- Cleanup waits for an in-flight Start, then releases only the resulting lease.
- Switching camera or switching back to Live Feed releases that component's lease but never issues a camera-wide stop.
- The direct camera-wide stop currently issued by the Cameras page during mode switching is replaced with release of that mounted component's lease.
- When a lease expires, the component performs one controlled reattach instead of repeatedly creating leases.
- Two browser windows displaying the same camera receive the same camera `sessionId` but different `viewerLeaseId` values.

## Backend behavior

- Application/API contracts carry `viewerLeaseId` without persisting it.
- Backend validation rejects malformed lease UUIDs at the API boundary.
- `AiPreviewClient` forwards lease operations and continues to attach the internal service key.
- Monitoring ownership remains independent: public preview viewers can attach to monitoring output but cannot stop, restart or reconfigure the monitoring owner.
- Keep a separate Admin/internal force-stop operation for source replacement and other controlled maintenance. Ordinary viewer cleanup must never reach it.
- API documentation/OpenAPI are updated only after the final contract is implemented.

## AI service behavior

- `_Session` stores viewer leases and their last-renewed monotonic timestamps.
- Capacity becomes configurable and counts active tasks across distinct cameras.
- Per-camera locks serialize attach/release/restart operations; the capacity lock prevents two simultaneous starts from exceeding the limit.
- Each camera retains a separate `FrameTracker`, preserving camera-local ByteTrack state.
- Frame publication continues to use the current latest-JPEG buffer and `Condition.notify_all()`, allowing all waiting viewers to receive the same sequence independently.
- Idle decisions for preview sessions use active viewer leases rather than one global `last_access` timestamp.
- Monitoring sessions continue to use their owner lease and do not depend on viewer leases for liveness.
- Viewer leases belong to one camera session generation. A fresh session ID invalidates all leases from the prior generation; clients perform one controlled reattach.
- A lifecycle sweeper or equivalent bounded expiry mechanism cleans leases for preview, completed, error and monitoring sessions even when no further request arrives.

## Concurrency invariants

1. At most one worker exists for a given camera ID.
2. At most `AI_MAX_ACTIVE_CAMERA_SESSIONS` distinct camera workers are active.
3. Attaching/releasing a viewer is atomic relative to stopping that camera worker.
4. A camera worker can stop for zero viewers only after confirming no lease remains under the camera lock.
5. Viewer operations cannot mutate another camera's leases or tracker.
6. Monitoring ownership cannot be released by a viewer lease.
7. Capacity is released exactly once when a worker ends.
8. Lease validation/renewal happens under the camera lock, but the lock is released before the bounded next-frame wait.

## Verification strategy

### Python unit/concurrency tests

- Two viewers attach to one camera; one reader/tracker/worker is created.
- Releasing Viewer A leaves Viewer B live and able to receive newer frames.
- Releasing/expiring the final viewer stops and closes the preview-owned reader.
- Abandoned leases expire and release capacity.
- Two different cameras run simultaneously with two readers and two tracker instances.
- Camera 1 and Camera 2 can produce the same numeric track ID without shared state.
- Failure/stop/reconnect of Camera 1 does not change Camera 2.
- A third camera receives `AI_SESSION_CAPACITY`.
- Viewer release cannot stop a monitoring-owned session.
- Concurrent attach/release/start does not orphan a worker or exceed capacity.

### Backend tests

- Start returns a lease and passes it through without exposing internal secrets.
- Status/frame/stop require and forward the correct lease.
- One viewer Stop does not call a camera-wide internal stop.
- Lease errors and capacity errors map to stable sanitized ProblemDetails.
- Existing ADMIN/OPERATOR authorization remains intact.

### Frontend tests

- Every mounted preview uses its own lease.
- Cleanup releases exactly its own lease after an in-flight Start completes.
- Two preview components for the same camera do not share or overwrite lease IDs.
- Expired lease triggers one controlled reattach.
- Switching modes/cameras cannot terminate another component's preview.

### Hardware/integration acceptance

- Run two distinct live or controlled camera sources concurrently on the RTX 4060 Docker host.
- Confirm both frame sequences advance and both annotated previews remain responsive.
- Record GPU memory, GPU utilization, effective per-camera FPS and end-to-end frame age; do not invent a fixed FPS pass threshold before this measurement.
- Verify no CUDA out-of-memory, container restart or starvation over a bounded soak test.
- Open two browsers on Camera 1 and another browser on Camera 2; close them in different orders and confirm unaffected viewers continue.
- Attempt Camera 3 and confirm a clean capacity response while Cameras 1 and 2 continue.

## Deployment and compatibility

This is a coordinated AI service + backend + frontend contract change. Deploying only one layer can retain the old global-stop behavior or leave clients without a lease.

Recommended rollout:

1. Build and verify all three repositories against the new contract.
2. Deploy AI service and backend in one maintenance window.
3. Deploy the matching frontend immediately after.
4. Smoke-test one-camera/multi-viewer behavior before enabling the configured two-camera capacity.
5. Run the two-camera hardware test and retain the capacity at `2` only if the host remains stable.

Rollback restores the previous single-camera contract and `AI_MAX_ACTIVE_CAMERA_SESSIONS=1`; it must not require a database rollback.

## Documentation impact

- Update backend `docs/API.md` and `docs/openapi.json` for viewer lease fields and errors.
- Update backend/AI/frontend READMEs to distinguish viewers from active camera GPU sessions.
- Add dated verification evidence without rewriting historical results.
- Flag Report 4 package/sequence diagrams if they currently imply one viewer owns the camera worker.
- No Report 1/2/3 scope addition is required: this implements multi-view viewing and two independent camera-local pipelines without cross-camera identity.

## Open implementation detail

The implementation plan must choose exact internal route names while preserving the semantics above. Internally, prefer a header for the lease UUID so routine proxy/access logs do not record it. It must not weaken JWT authorization or expose the AI service directly to browsers.

## Approved implementation decisions

### Conflicting preview configurations

Current normal preview and Admin Draft-confidence preview can request different confidence/tracker configuration for the same camera. The selected multi-viewer design cannot silently satisfy both with one pipeline.

**Recommended:** attach only when the requested runtime configuration matches the active pipeline. Reject a conflicting Start with `AI_PREVIEW_CONFIGURATION_CONFLICT`. Do not let an Admin Draft preview silently restart a pipeline being watched by other viewers. The Admin must stop/deactivate the conflicting runtime deliberately before starting the requested Draft configuration.

### Monitoring ownership transitions

**Recommended:** monitoring remains authoritative.

- If SQL configuration is ACTIVE but its monitoring worker is not yet running, viewer Start reports a stable monitoring-start-pending state and does not create a competing preview worker.
- When monitoring starts while a preview worker exists, it may replace that worker as the authoritative pipeline. The session ID changes, old viewer leases expire, and mounted clients perform one controlled reattach to the monitoring output.
- When monitoring stops, its session ends and viewer leases for that generation expire. A still-mounted viewer may perform one controlled Start to create a new preview-owned pipeline; no monitoring tracker state is carried over.
- These transitions may show a brief reconnecting state but require no manual page reload and never correlate/persist tracks across the new session.

### Legacy non-sequenced frame endpoint

**Recommended:** keep `GET .../frame` temporarily for compatibility, mark it deprecated, and require a valid viewer lease exactly like `frame/next`. It must not remain as a lease-free path. New frontend code continues to use `frame/next`.
