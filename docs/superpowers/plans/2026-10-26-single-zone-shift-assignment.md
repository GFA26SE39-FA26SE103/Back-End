# Single-zone Shift Assignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Manager shift assignments select and persist exactly one active Zone per Staff member.

**Architecture:** Keep the existing `zoneIds` request field to avoid a breaking API change, but constrain it to exactly one distinct non-empty ID in the Application layer. The React form models one `zoneId` and converts it to a one-item `zoneIds` array at the API boundary.

**Tech Stack:** React 19, TypeScript 6, Vitest/Testing Library, ASP.NET Core .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-26-single-zone-shift-assignment-design.md`

## Global Constraints

- Work inline on the currently checked-out FE and BE branches; do not create a branch, worktree, or sub-agent.
- Do not change the SQL schema, scaffolded persistence classes, ERD, or unrelated workflows.
- Keep request compatibility through `zoneIds`; valid create/update requests contain exactly one distinct non-empty Zone ID.
- Preserve `ZONE_REQUIRED` for an empty selection and return HTTP 422 `MULTIPLE_ZONES_NOT_ALLOWED` for multiple selections.
- Do not commit or push unless the user explicitly requests it.

## Review Focus

- A request with two different Zone IDs must fail before any assignment or Zone row is changed; covered by Task 1 tests.
- Repeated copies of the same Zone ID normalize to one selection and remain valid; covered by Task 1 tests.
- Editing an assignment must replace its old Zone rather than leave two active Zone rows; covered by Task 1 tests.
- A schedule containing legacy multi-Zone data must remain readable; the edit form uses the first returned Zone and a save normalizes it to one; covered by Task 2 tests.
- With multiple active Zones, a new assignment must not silently choose one; the Manager must choose from the dropdown; covered by Task 2 tests.

---

### Task 1: Enforce one Zone in backend scheduling

**Files:**
- Modify: `tests/Supermarket.Tests/ShiftPlanningTests.cs`
- Modify: `src/Supermarket.Application/ShiftPlanning.cs`
- Modify: `src/Supermarket.Application/ShiftPlanningContracts.cs`
- Modify: `docs/API.md`

**Interfaces:**
- Consumes: `AssignShiftRequest(Guid UserId, Guid ShiftId, DateOnly WorkDate, Guid[]? ZoneIds)` and `UpdateAssignmentRequest(Guid? UserId, Guid[]? ZoneIds)`.
- Produces: existing request shapes with cardinality validation: zero → `ZONE_REQUIRED`; more than one distinct non-empty ID → `MULTIPLE_ZONES_NOT_ALLOWED` (422); exactly one → accepted.

- [ ] **Step 1: Write failing backend tests**

Add focused assertions to `AssignmentRespectsRoleOverlapLeaveWeeklyLimitAndStartedShifts` for create requests with two different Zones and duplicated copies of one Zone. Update `ReassigningAPublishedShiftNotifiesBothStaffAndZonesCanBeReplaced` to assert that a two-Zone update fails and a subsequent one-Zone update replaces Checkout with Produce.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Supermarket.Tests/Supermarket.Tests.csproj --filter FullyQualifiedName~ShiftPlanningTests`

Expected: FAIL because multiple distinct Zones are currently accepted.

- [ ] **Step 3: Implement exact-one cardinality validation**

Change `ZoneList(Guid[]? zoneIds)` in `ShiftPlanning.cs` to keep normalization, retain the zero-item rule, and throw `ApplicationException("MULTIPLE_ZONES_NOT_ALLOWED", "Assign exactly one Zone to each Staff shift.", 422)` when the normalized length exceeds one. Adjust singular/plural XML comments and notification copy in `ShiftPlanning.cs` and `ShiftPlanningContracts.cs` where they describe replacing Zones.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run: `dotnet test tests/Supermarket.Tests/Supermarket.Tests.csproj --filter FullyQualifiedName~ShiftPlanningTests`

Expected: all `ShiftPlanningTests` pass.

- [ ] **Step 5: Update API documentation**

In `docs/API.md`, document create as `{..., zoneIds:[zoneId]}` and update as `{userId?, zoneIds?:[zoneId]}`, state exactly one active Zone is required, and list `MULTIPLE_ZONES_NOT_ALLOWED` as a 422 response.

### Task 2: Replace Zone checkboxes with a single dropdown

**Files:**
- Modify: `Frontend/Front-End/src/pages/manager/manager.test.tsx`
- Modify: `Frontend/Front-End/src/pages/manager/Schedule.tsx`

**Interfaces:**
- Consumes: active `StoreZone[]` and existing `assignShift` / `updateAssignment` functions that accept `zoneIds?: string[]`.
- Produces: one controlled `zoneId: string`; submission maps it to `zoneIds: [zoneId]`.

- [ ] **Step 1: Write failing frontend tests**

Change the assignment test fixture to expose two active Zones. Assert that the dialog has a combobox named `Zone`, has a disabled `Choose a zone` placeholder, has no Zone checkboxes, requires an explicit selection, and submits one selected ID. Add an edit-case assertion that legacy multi-Zone response data selects the first Zone and saving sends only that one ID.

- [ ] **Step 2: Run the focused frontend test and verify RED**

Run: `npm test -- --run src/pages/manager/manager.test.tsx`

Expected: FAIL because the form currently exposes Zone checkboxes and allows multiple selections.

- [ ] **Step 3: Implement the controlled Zone dropdown**

In `AssignmentDialog`, replace `zoneIds: string[]` state with `zoneId: string`. Initialize from `assignment?.zones[0]?.zoneId`, otherwise preselect only when exactly one active Zone exists. Render a labeled native `<select>` with placeholder and Zone options, use singular validation copy, and send `zoneIds: [zoneId]` for create/update.

- [ ] **Step 4: Run the focused frontend test and verify GREEN**

Run: `npm test -- --run src/pages/manager/manager.test.tsx`

Expected: all Manager console tests pass.

### Task 3: Full verification and review

**Files:**
- Review all files changed in Tasks 1–2.

**Interfaces:**
- Consumes: completed backend enforcement and frontend dropdown.
- Produces: verified FE/BE changes with no schema, branch, worktree, or unrelated modifications.

- [ ] **Step 1: Run the frontend build**

Run from `Frontend/Front-End`: `npm run build`.

Expected: exit code 0. The focused Manager test from Task 2 is the requested regression-test scope; skip the full suite and lint to optimize execution time.

- [ ] **Step 2: Run the backend Release build**

Run from `Backend/Back-End`: `dotnet build Supermarket.sln -c Release --no-restore`.

Expected: exit code 0. The focused `ShiftPlanningTests` from Task 1 are the requested regression-test scope; skip the full suite to optimize execution time.

- [ ] **Step 3: Review repository state and diffs**

Run `git status --short --branch`, `git diff --check`, and inspect `git diff` in each repository. Confirm FE remains on `main`, BE remains on `feature/mf02-staff-tasks`, and no worktree/schema/scaffolded files changed.

- [ ] **Step 4: Report evidence**

Summarize changed files, exact validation behavior, executed commands/results, and any pre-existing or environment-related failures without committing or pushing.
