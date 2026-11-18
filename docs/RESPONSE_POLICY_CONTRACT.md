# ResponsePolicy management contract — C handoff

Status: implemented on C's isolated branch; A must review and integrate DI/routing documentation. No schema or scaffold change.

## Ownership and behavior

- Admin-only management for the executable `OperationalEvent → ResponsePolicy → CREATE_OPERATIONAL_TASK/AUTOMATIC → Task` path.
- Policies apply only to `MonitoringRule.outcome_type = OPERATIONAL_EVENT`; they never create or convert an Incident.
- Saving a policy does not backfill Tasks for an already-open Event at the same priority.
- The API intentionally does not expose unsupported approval/recommendation/notification execution as if it were operational.

## Routes

All routes require role `ADMIN`.

| Method | Path | Result |
| --- | --- | --- |
| `GET` | `/api/monitoring-rules/{ruleId}/response-policies` | Existing WARNING/CRITICAL policies for the rule. Missing priority slots are omitted. |
| `PUT` | `/api/monitoring-rules/{ruleId}/response-policies/{priority}` | Idempotent create/update for `WARNING` or `CRITICAL`. |
| `DELETE` | `/api/monitoring-rules/{ruleId}/response-policies/{priority}` | Deletes the policy and its actions when `expectedVersion` matches. Returns 204. |

## Save request

```json
{
  "isEnabled": true,
  "expectedVersion": null,
  "action": {
    "isEnabled": true,
    "title": "Check congestion",
    "instructions": "Help customers move safely."
  }
}
```

- `expectedVersion` must be `null` when creating and must equal the current version when updating.
- Title is optional, trimmed, and limited to 200 characters. The existing dispatch fallback supplies an Event/Zone title when absent.
- Instructions are optional, trimmed, and limited to 4000 characters.
- The backend persists exactly the currently executable action type/mode: `CREATE_OPERATIONAL_TASK` / `AUTOMATIC`.

## Response/readiness

The response returns policy/action identifiers, version, normalized priority, enabled flags, task text, `readyForAutomaticTask`, and readiness issue codes:

- `RULE_DISABLED`
- `CONFIGURATION_NOT_ACTIVE`
- `POLICY_DISABLED`
- `ACTION_MISSING`
- `ACTION_DISABLED`
- `ACTION_AMBIGUOUS`
- `ACTION_NOT_AUTOMATIC`

Expected errors include `NOT_FOUND`, `FORBIDDEN`, `INVALID_STATUS`, `INVALID_POLICY_RULE`, `INVALID_POLICY_ACTION`, `INVALID_FIELD`, `POLICY_VERSION_CONFLICT`, `POLICY_AMBIGUOUS`, and `POLICY_ACTION_AMBIGUOUS`.

## Integration required from A

Call `services.AddResponsePolicyManagement()` from the API composition root and merge this contract into `docs/API.md`, OpenAPI, and aggregate validation. A also owns the route/navigation link from AI Config. C does not edit `Program.cs`, `App.tsx`, `AiConfig.tsx`, or shared navigation files.
