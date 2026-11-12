# Checkout AI monitoring protocol v2

This is the Backend-side contract for the AI-Service work that follows. It is not a claim that the current AI deployment supports it. Keep `Checkout:Monitoring:AiContractEnabled=false` until that deployment is validated.

## Session lifecycle

Reuse the private monitoring endpoints and owner lease:

- POST `/monitoring/sessions/{cameraId}/start`;
- GET `/monitoring/sessions/{cameraId}/measurements?after_sequence=...&limit=...&after_session_id=...` with `X-AI-Monitoring-Owner`;
- DELETE `/monitoring/sessions/{cameraId}` with the same ownership mechanism.

Existing internal service-key/credential handling remains unchanged. Credentials are private request fields, never measurement output or logs.

Generic-only snapshots send protocol_version=1 with the existing zones payload, without any checkout_mappings field. A camera containing checkout mappings sends protocol_version=2 and shares ONE session/inference pipeline for zones plus checkout mappings. A checkout-only camera may have an empty zones array.

## Added start fields

`checkout_mappings` is an array of at most 128 entries/camera. Each entry includes:

| Field | Meaning |
| --- | --- |
| mapping_id, counter_id, zone_id | Saved UUID identities |
| config_id, config_version | Zone monitoring identity and exact UTC timestamp version |
| mapping_version | Exact saved mapping updated_at, UTC |
| lane_roi, staff_roi | Independent normalized point arrays, existing `{x,y}` convention |
| service_roi | Required normalized polygon on the designated queue source; null on other mappings. |
| is_queue_source | Whether this mapping provides the counter queue count |
| confidence | Saved detection confidence for this configuration |

The common start confidence is the minimum required across that camera's generic and checkout entries. Use existing per-confidence tracker behavior where applicable; do not spawn a worker per mapping. The existing configuration_fingerprint identifies the complete private camera snapshot and must be echoed exactly.

## Added measurement fields

Measurement pages and their batches use protocol_version=2. Existing batch fields remain: session_id, continuity_id, sequence, captured_at (UTC), source_elapsed_ms, configuration_fingerprint and zones. Each batch must also contain a checkouts array with exactly one entry for every configured mapping on that camera:

| Field | Type/meaning |
| --- | --- |
| mapping_id, counter_id, config_id | Exact saved UUIDs |
| config_version, mapping_version | Exact UTC versions from start |
| person_present | boolean or null: raw person-presence evidence in Staff ROI; no employee ReID |
| queue_count | nonnegative integer or null; designated queue source supplies Lane ROI dwell count |
| max_wait_seconds | Nonnegative finite number or null; queue source supplies the longest currently tracked Lane-to-Service duration. Null for other mappings. |
| usable | false when the frame/source observation cannot be trusted |

Null person_present can coexist with valid lane evidence; it is not a negative detection. A non-queue camera should return null queue_count; Backend never uses its count as a fallback. A camera-wide health failure invalidates all its observations, independently of payload claims.

The Backend applies the configured 15-second source hysteresis and counter fusion. AI must not apply another 15-second delay on top or merge observations from different cameras. Track dwell within Lane ROI reuses the existing queue-count semantics (>=5 seconds in ROI). A track leaves the waiting set when it enters Service ROI. `max_wait_seconds` is the longest current time to reach that boundary, not proof that a transaction started; track loss and source discontinuity must not be interpreted as service completion. Transaction utilization remains out of scope.

## Ordering, limits and invalidation

- Pages contain at most the requested limit (1–64) ordered batches; valid buffer bounds and session/continuity IDs are required.
- Each batch permits at most 128 generic zones and 128 checkout observations. Duplicate mapping IDs, empty IDs, negative counts, invalid UTC timestamps or mismatched versions/fingerprints are rejected.
- Session ownership/version checks happen before data advances source state; replay is not counted again.
- captured_at must be fresh on the Backend clock (default max age 3 seconds, future tolerance 1 second). Source timing is local to its continuity; gap threshold defaults to 2000 ms. Deployments must synchronize clocks.
- Stalled/error/reconnecting/stopped/completed sources become unavailable rather than clearing an operational episode or emitting zero measurements. A new valid continuity must rebuild presence evidence.
- HTTP monitoring responses, including error bodies, are bounded by `AiPreview:MaxMonitoringResponseBytes` (default 4 MiB; supported 1 KiB–64 MiB). Both declared and streamed lengths are checked before JSON deserialization. Oversized data is rejected as AI_MEASUREMENT_INVALID.
- Recorded video is controlled test input. Do not present unrelated recording timelines as synchronized live multi-camera evidence.

The existing generic v1 contract and raw preview endpoints are unchanged. Backend rejects a v1 measurement page for a checkout-configured session with CHECKOUT_PROTOCOL_UNSUPPORTED.
