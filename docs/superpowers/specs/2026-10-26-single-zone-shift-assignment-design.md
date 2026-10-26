# Single-zone shift assignment

## Intent

Align Manager scheduling with the rule that one Staff member works in exactly one Zone for a shift. Replace the multi-select checkbox list with a single dropdown and prevent non-UI clients from saving multiple Zones.

## Goals

- Manager chooses one active Zone from a dropdown when creating or editing an assignment.
- The existing API remains wire-compatible by continuing to use `zoneIds`, containing exactly one Zone ID.
- Backend rejects missing Zones and requests containing more than one distinct non-empty Zone ID.
- Existing single-Zone assignments continue to display and edit normally.

## Non-goals

- No database or ERD change.
- No branch or worktree creation.
- No redesign of coverage, shift swaps, task routing, or Zone administration.
- No automatic cleanup of historical assignments that already contain multiple active Zones.

## Approaches considered

1. UI-only dropdown: smallest change, but other clients could still violate the business rule.
2. Dropdown plus backend cardinality validation: selected because it preserves the current API shape while enforcing the rule authoritatively.
3. Replace `zoneIds` with singular `zoneId`: clearer contract but a breaking change across web/mobile/docs with unnecessary migration risk.

## Design

### Frontend

- Replace the Zone checkbox fieldset in `Schedule.tsx` with a labeled native `select`.
- Store one selected `zoneId`; preselect the only available Zone for a new assignment and the assignment's first Zone when editing.
- Keep the placeholder unselected when multiple Zones exist until the Manager chooses one.
- Submit `zoneIds: [zoneId]` and keep the existing empty-selection error behavior with singular wording.
- Update the Manager UI test to verify dropdown behavior and the one-item payload.

### Backend

- Keep `AssignShiftRequest.ZoneIds` and `UpdateAssignmentRequest.ZoneIds` for compatibility.
- Normalize empty GUIDs and duplicates as today, then require exactly one distinct Zone.
- Preserve `ZONE_REQUIRED` for zero Zones and add `MULTIPLE_ZONES_NOT_ALLOWED` with HTTP 422 for more than one Zone.
- Update scheduling tests so create and update reject multiple Zones and a valid update replaces the previous Zone.
- Update API documentation to state that `zoneIds` must contain exactly one item.

## Verification

- Observe the new FE regression test fail before implementation, then pass.
- Observe the new BE regression test fail before implementation, then pass.
- Run the full FE test/build checks and the backend non-SQL test suite/build checks available in the repositories.
- Review both diffs and confirm no schema, branch, or worktree changes were made.
