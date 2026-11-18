# Floor Plan Upload and Camera Placement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an Admin upload a floor plan, view it in Store Layout, place a newly created camera as a draggable/rotatable camera sprite, and persist the camera position and direction through the existing MF-01 backend model.

**Architecture:** Use local filesystem storage behind a replaceable application storage abstraction; do not add S3 or database columns in this increment. Store the floor-map URL and dimensions in the existing `Floor` fields, and store camera position/direction in the existing `Camera.MapX`, `Camera.MapY`, and `Camera.MapRotationDeg` fields. The frontend renders one normalized coordinate surface for the floor plan and camera sprite, then calls the existing Admin APIs for persistence.

**Tech Stack:** ASP.NET Core .NET 10, EF Core database-first SQL Server persistence, local filesystem storage for development, React 19, TypeScript, Vite, Vitest, CSS Modules.

---

## Scope and invariants

- Admin-only upload and camera placement.
- No ERD/schema change and no code-first migration.
- No S3 dependency in this increment.
- Floor plan assets are stored outside `wwwroot` and served through an authenticated API endpoint.
- Store only a generated asset reference in the existing `Floor.MapAssetUrl`; never persist a client path or original filename as the storage path.
- Accept PNG, JPEG, and PDF floor plans. Raster images render directly; PDFs render through a PDF.js canvas so the camera overlay uses the same measured page surface.
- Reject unsupported MIME types, mismatched file signatures, empty files, and files above the configured floor-plan limit (`FloorPlan:MaxBytes`, default 20 MB).
- Camera coordinates are normalized `[0,1]`; screen pixels are never persisted.
- Camera rotation is persisted in degrees with a normalized UI range `[0,360)`.
- Existing camera lifecycle remains unchanged: create camera → configure connection → test → enable.
- The frontend must stop treating a local mock camera as persisted after this slice is integrated.

## Current persistence facts

- `Floor` already has `MapAssetUrl`, `MapWidth`, and `MapHeight`.
- `Camera` already has `MapX`, `MapY`, and `MapRotationDeg`.
- `POST /api/floors/{floorId}/cameras` persists a camera row.
- `PUT /api/cameras/{cameraId}/connection` persists the separate 1:1 connection row, disables it initially, clears old test state, and encrypts the supplied password.
- `POST /api/cameras/{cameraId}/connection/test` persists test results.
- `POST /api/cameras/{cameraId}/connection/enable` persists the enabled state after validation.

## Files to touch

### Backend

- Modify `src/Supermarket.Api/Controllers/StoreController.cs` — add Admin floor-map upload and authenticated map retrieval endpoints.
- Modify `src/Supermarket.Api/Program.cs` — register local floor-plan storage and bind `FloorPlan:MaxBytes`.
- Modify `src/Supermarket.Application/Abstractions.cs` — define the storage boundary.
- Modify `src/Supermarket.Application/Contracts.cs` — add upload/result records without exposing storage internals.
- Modify `src/Supermarket.Application/StoreSetup.cs` — add the upload use case, validate the floor, persist the existing map fields, and coordinate replacement cleanup.
- Create `src/Supermarket.Infrastructure/FloorPlans/LocalFloorPlanStorage.cs` — write/read/replace assets under the configured `.local/floor-plans` directory.
- Modify `src/Supermarket.Api/appsettings.Development.json` and `src/Supermarket.Api/appsettings.Production.json` only if the storage settings need explicit documented defaults.
- Modify `tests/Supermarket.Tests/ApiFlowTests.cs` or create `tests/Supermarket.Tests/FloorPlanTests.cs` — upload, retrieval, replacement, authorization, and validation coverage.
- Modify `tests/Supermarket.Tests/DomainTests.cs` only if a new pure validation rule is introduced.

### Frontend

- Create `src/api/floors.ts` — supermarket/floor/zone reads and floor-map upload/download helpers.
- Create `src/api/floors.test.ts` — multipart upload/download helper tests.
- Modify `src/api/cameras.ts` — add camera position fields to `CameraRecord` and an `updateCamera` helper using the existing camera PATCH contract.
- Create `src/api/cameras.test.ts` — camera placement PATCH contract tests.
- Create `src/components/floorPlanGeometry.ts` — normalized coordinate conversion, clamping, rotation normalization, and hit-area calculations.
- Create `src/components/FloorPlanSurface.tsx` — measured map/PDF surface and camera sprite interaction boundary.
- Create `src/components/FloorPlanSurface.module.css` — responsive surface, camera body, muzzle, FOV wedge, selection, and rotation handle styles.
- Modify `src/pages/StoreLayout.tsx` — load real floor/camera data, upload/replace the floor plan, render the surface, and save placement changes.
- Modify `src/pages/StoreLayout.module.css` only for page-level layout changes not contained by the new component.
- Create `src/components/floorPlanGeometry.test.ts` — deterministic coordinate and rotation tests.
- Create `src/components/FloorPlanSurface.test.tsx` — drag, rotate, keyboard movement, selection, and save-callback tests.
- Modify or create `src/pages/StoreLayout.test.tsx` — API-backed loading and upload/placement integration tests.

### Documentation after implementation

- Modify `docs/API.md` and `docs/openapi.json` only after the final endpoint contract is implemented.
- Modify `README.md` with local floor-plan storage, supported formats, size limit, and the camera placement flow.
- Add dated evidence to `docs/VALIDATION.md`; do not overwrite historical results.

---

## Task 1: Freeze the floor-map and camera-placement contract

**Files:**
- Modify: `src/Supermarket.Api/Controllers/StoreController.cs`
- Modify: `src/Supermarket.Application/Contracts.cs`
- Modify: `src/api/floors.ts`
- Modify: `src/api/cameras.ts`

- [ ] **Step 1: Define the floor-map API contract.**

  Use these routes and shapes:

  ```text
  POST /api/floors/{floorId}/map
    multipart/form-data: file
    response: FloorMapView

  GET /api/floors/{floorId}/map
    response: binary floor-plan content with stored Content-Type

  FloorMapView:
    floorId
    mapUrl
    mapWidth
    mapHeight
    contentType
    updatedAt
  ```

  Both routes require `ADMIN`. The GET route must not expose the filesystem path.

- [ ] **Step 2: Record the existing camera placement contract.**

  Extend the frontend camera type with:

  ```ts
  mapX: number | null;
  mapY: number | null;
  mapRotationDeg: number | null;
  ```

  Add:

  ```ts
  export const updateCamera = (cameraId: string, body: CreateCameraRequest) =>
    apiFetch<CameraRecord>(`/api/cameras/${cameraId}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    });
  ```

  The implementation must preserve the camera's existing metadata when saving only placement fields; do not send a partial body to an endpoint that expects the full `CameraRequest`.

- [ ] **Step 3: Add contract tests before implementation.**

  Assert that the frontend calls the exact upload/download and camera PATCH routes, sends `multipart/form-data` without manually overriding its boundary, and retains the bearer token.

- [ ] **Step 4: Run the focused tests and confirm the new tests fail for the missing implementation.**

  Run from `D:\Study\FA26\SEP490\Implement\Front-End`:

  ```powershell
  npm run test -- --run src/api/floors.test.ts src/api/cameras.test.ts
  ```

  Expected: the new contract tests fail only because the helpers do not yet exist.

---

## Task 2: Implement local floor-plan storage and API

**Files:**
- Modify: `src/Supermarket.Application/Abstractions.cs`
- Modify: `src/Supermarket.Application/Contracts.cs`
- Modify: `src/Supermarket.Application/StoreSetup.cs`
- Create: `src/Supermarket.Infrastructure/FloorPlans/LocalFloorPlanStorage.cs`
- Modify: `src/Supermarket.Api/Controllers/StoreController.cs`
- Modify: `src/Supermarket.Api/Program.cs`
- Modify: `src/Supermarket.Api/appsettings.Development.json`
- Modify: `src/Supermarket.Api/appsettings.Production.json`
- Test: `tests/Supermarket.Tests/FloorPlanTests.cs`

- [ ] **Step 1: Define the storage interface.**

  Use an application-facing interface equivalent to:

  ```csharp
  public interface IFloorPlanStorage
  {
      Task<StoredFloorPlan> Save(Guid floorId, Stream content, string contentType, string extension, long length, CancellationToken ct);
      Task<StoredFloorPlanFile?> Open(Guid floorId, CancellationToken ct);
      Task Delete(Guid floorId, CancellationToken ct);
  }
  ```

  `StoredFloorPlan` contains only generated storage identity, content type, dimensions, and length. It must not contain a client-controlled filesystem path.

- [ ] **Step 2: Implement safe local storage.**

  Store files at `.local/floor-plans/{floorId}/{generated-name}.{extension}`. Write to a temporary file, flush and close it, then atomically replace the current file. Remove the previous file only after the database update succeeds. Clean up the temporary/new file when validation or persistence fails.

- [ ] **Step 3: Validate uploads before persistence.**

  Enforce:

  ```text
  ADMIN authorization
  non-empty file
  length <= FloorPlan:MaxBytes
  allowed content type: image/png, image/jpeg, application/pdf
  extension matches content type
  PNG/JPEG/PDF signature is valid
  dimensions are positive when available
  ```

  Generate the filename server-side. Never use the submitted filename as a path or URL.

- [ ] **Step 4: Add the upload use case.**

  Load the floor, write the new asset, update the existing `MapAssetUrl`, `MapWidth`, and `MapHeight`, and persist the floor through the existing transaction pattern. Keep the database URL as the authenticated API map URL, not a local disk path.

- [ ] **Step 5: Add the authenticated retrieval endpoint.**

  Stream the stored file with the recorded content type and `Cache-Control: no-store` while development storage is local. Do not serve the floor-plan directory through static files.

- [ ] **Step 6: Add backend tests.**

  Cover:

  ```text
  Admin upload succeeds and updates Floor.MapAssetUrl/size metadata.
  Non-Admin upload returns 403.
  Empty, oversized, unsupported, and signature-mismatched files are rejected.
  GET returns the stored bytes and Content-Type.
  Replacement serves the new file and removes the old file after commit.
  Failed persistence does not delete the previous valid file.
  ```

- [ ] **Step 7: Run backend verification.**

  ```powershell
  dotnet build Supermarket.sln --no-restore
  dotnet test tests/Supermarket.Tests/Supermarket.Tests.csproj --filter FullyQualifiedName~FloorPlan
  ```

  Run the full SQL suite only with the approved schema prerequisite available.

---

## Task 3: Implement normalized floor-plan geometry

**Files:**
- Create: `src/components/floorPlanGeometry.ts`
- Test: `src/components/floorPlanGeometry.test.ts`

- [ ] **Step 1: Add pure conversion functions.**

  Implement these signatures:

  ```ts
  export type NormalizedPosition = { x: number; y: number };

  export function clientToNormalized(
    rect: DOMRect,
    clientX: number,
    clientY: number,
  ): NormalizedPosition;

  export function normalizedToPixels(
    position: NormalizedPosition,
    width: number,
    height: number,
  ): { left: number; top: number };

  export function clampNormalized(position: NormalizedPosition): NormalizedPosition;

  export function normalizeRotation(degrees: number): number;
  ```

  `clientToNormalized` must clamp to `[0,1]`; `normalizeRotation` must return `[0,360)`.

- [ ] **Step 2: Test geometry boundaries.**

  Cover center, corners, pointer positions outside the surface, zero-size rejection, negative rotation, rotation greater than 360, and round-trip conversion within a small floating-point tolerance.

- [ ] **Step 3: Run the geometry tests.**

  ```powershell
  npm run test -- --run src/components/floorPlanGeometry.test.ts
  ```

  Expected: all geometry tests pass before the UI component is added.

---

## Task 4: Build the draggable/rotatable camera sprite

**Files:**
- Create: `src/components/FloorPlanSurface.tsx`
- Create: `src/components/FloorPlanSurface.module.css`
- Test: `src/components/FloorPlanSurface.test.tsx`

- [ ] **Step 1: Define the component contract.**

  Use a controlled component so persistence remains in `StoreLayout`:

  ```ts
  type CameraPlacement = {
    cameraId: string;
    code: string;
    mapX: number | null;
    mapY: number | null;
    mapRotationDeg: number | null;
    status: string;
  };

  type FloorPlanSurfaceProps = {
    mapUrl: string;
    mapContentType: string;
    cameras: CameraPlacement[];
    selectedCameraId: string | null;
    onSelectCamera: (cameraId: string) => void;
    onChangePlacement: (cameraId: string, placement: NormalizedPosition & { rotationDeg: number }) => void;
  };
  ```

- [ ] **Step 2: Render the floor map and sprite in one coordinate surface.**

  Use a relative wrapper and an absolutely positioned map layer. Render each camera as a rectangle with a clearly marked front/muzzle and translucent FOV wedge. Convert normalized coordinates to percentages so the overlay remains aligned when the map resizes.

- [ ] **Step 3: Implement pointer dragging.**

  On pointer down for the camera body, call `setPointerCapture`, compute normalized coordinates from the surface `DOMRect`, clamp them, and emit `onChangePlacement`. Release capture on pointer up/cancel. Do not drag the map or other cameras.

- [ ] **Step 4: Implement rotation.**

  Add a visible rotation handle for the selected camera. Compute the angle from the camera center to the pointer and normalize it to `[0,360)`. Add keyboard controls for rotation in fixed increments so the action is accessible without a pointer.

- [ ] **Step 5: Add accessible interaction states.**

  Give each sprite an accessible label containing camera code and position, use `aria-pressed`/selection state, expose keyboard movement with arrow keys, and show an unsaved indicator when the controlled placement differs from the last persisted value.

- [ ] **Step 6: Test the component.**

  Cover camera selection, pointer drag emitting normalized coordinates, rotation emitting degrees, clamping at map edges, keyboard movement, and no cross-camera movement. Use mocked `getBoundingClientRect` with a fixed surface rectangle.

- [ ] **Step 7: Run component tests.**

  ```powershell
  npm run test -- --run src/components/FloorPlanSurface.test.tsx
  ```

---

## Task 5: Replace Store Layout mocks with the real floor/camera flow

**Files:**
- Create: `src/api/floors.ts`
- Modify: `src/api/cameras.ts`
- Modify: `src/pages/StoreLayout.tsx`
- Modify: `src/pages/StoreLayout.module.css`
- Test: `src/pages/StoreLayout.test.tsx`

- [ ] **Step 1: Load real configuration data.**

  On page load, call the existing list APIs for supermarkets, floors, zones, and cameras. Keep loading, unauthorized, empty-store, and API-error states visible. Do not silently fall back to mock data after an API failure.

- [ ] **Step 2: Add floor-plan upload UI.**

  Replace the current no-op “Replace floor plan” and “Upload floor plan” buttons with a file input that sends `FormData` to `POST /api/floors/{floorId}/map`. Show upload progress, validation errors, success state, and refresh the floor-map URL after success.

- [ ] **Step 3: Load the map as an authenticated blob.**

  Fetch the map through `apiFetch` with `responseType: 'blob'`, create an object URL, revoke the previous object URL on replacement/unmount, and pass the URL/content type to `FloorPlanSurface`.

- [ ] **Step 4: Use the real camera creation flow.**

  After `POST /api/floors/{floorId}/cameras` succeeds, add the returned camera to the server-backed list, select it, and show its default sprite. Do not claim that a camera is saved when only React state changed.

- [ ] **Step 5: Persist placement explicitly.**

  Add a Save placement action. Merge the edited `mapX`, `mapY`, and `mapRotationDeg` into the full `CameraRequest`, call `PATCH /api/cameras/{cameraId}`, then replace the local record with the response. Keep the unsaved state until the API returns successfully.

- [ ] **Step 6: Keep connection initialization separate.**

  After camera creation, call the existing connection configuration/test/enable APIs in their current order. Placement saving must not implicitly enable a camera or start monitoring.

- [ ] **Step 7: Remove only the replaced mock behavior.**

  Delete the Store Layout dependency on `src/data/mock.ts`, `cameraPlacements`, and static placement state for cameras that now come from the backend. Preserve visual fixture geometry only where it represents the uploaded floor-map rendering or design placeholder.

- [ ] **Step 8: Test the page flow.**

  Cover:

  ```text
  floor and camera data load from API;
  floor-plan upload sends multipart FormData;
  uploaded map is rendered;
  new camera appears as a sprite;
  drag/rotate creates an unsaved state;
  Save placement sends the complete camera request;
  API failure keeps unsaved placement visible and shows an error;
  reloading uses persisted coordinates.
  ```

- [ ] **Step 9: Run frontend verification.**

  ```powershell
  npm run build
  npm run lint
  npm run test -- --run
  ```

  Resolve the existing `Response`/`Blob` test-environment failure before declaring the frontend slice green.

---

## Task 6: Documentation and end-to-end acceptance

**Files:**
- Modify: `docs/API.md`
- Modify: `docs/openapi.json`
- Modify: `README.md`
- Modify: `docs/VALIDATION.md`

- [ ] **Step 1: Document the final endpoint contract.**

  Document multipart field name, supported formats, size limit, Admin authorization, replacement behavior, authenticated retrieval, and camera placement fields. Regenerate or manually synchronize OpenAPI only after the implementation contract is final.

- [ ] **Step 2: Run the real MF-01 acceptance flow.**

  With SQL Server reachable and the local API running:

  ```text
  Admin login
  → create/open supermarket
  → create floor
  → upload floor plan
  → create camera
  → drag camera sprite into position
  → rotate muzzle/FOV to match the map
  → save placement
  → configure connection
  → test connection
  → enable connection
  → reload Store Layout
  → verify map and camera placement remain aligned
  ```

- [ ] **Step 3: Record dated evidence.**

  Record build/test results, upload validation, persistence after reload, and any unverified live-camera limitations in `docs/VALIDATION.md`. Do not reuse older validation results as evidence for this implementation.

## Risks and explicit non-goals

- Local storage is development-only and must remain behind `IFloorPlanStorage` so S3/MinIO can be added later without changing the API use case.
- PDF rendering adds frontend complexity; the map surface must use the rendered PDF page bounds, not the browser PDF toolbar viewport.
- No floor-plan version history, delete endpoint, object-storage lifecycle, task evidence upload, or incident media upload is included here.
- No changes to `MonitoringRule`, `IncidentType`, `Zone.area_m2`, `ZoneAdjacency`, or other ERD-v3 persistence are included.
- No automatic camera enablement or monitoring activation occurs after placement save.
