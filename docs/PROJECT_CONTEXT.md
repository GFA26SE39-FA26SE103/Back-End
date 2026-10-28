# PROJECT_CONTEXT.md — FA26SE103 Implementation Snapshot

> **Project:** AI-Powered Smart Supermarket Operations Monitoring System
> **Project code:** FA26SE103
> **Group:** GFA26SE39
> **Repository:** [GFA26SE39-FA26SE103/Back-End](https://github.com/GFA26SE39-FA26SE103/Back-End), branch `dev`
> **Synchronization baseline:** **23/10/2026** — synchronized the current Report 3 SRS, Business Rules, Context/Use Case diagrams V2, Report 4/5 drafts, approved Physical/Logical ERD **V6**, and inspected backend `dev` at commit `4df2864fa670fad64b37c93d1a705a9c9f2a3f56`.
> **Purpose:** repository-owned guide for developers and coding agents. It defines **approved target behavior** separately from **verified source-code implementation**. It is not a substitute for the official reports or the canonical SQL schema.
> **Important:** This replacement removes superseded ERD v3/v4 assumptions, historical approval paths, and old OperationalEvent-to-Incident design. Links to external documents are references, **not automatic synchronization**.
> **Integration note:** the MF-02 Staff-task and MF-04 scheduling/SWAP/COVER subsets are merged into `dev` on the exact post-cleanup ERD V6 scaffold. The 23/10 SRS now contains **32 Use Cases** and **38 Business Rules**; old BR numbers must not be reused. See §§36, 41–43.

---

## 0. Read this first

This is a one-branch, AI-assisted **supermarket operations monitoring** capstone. It is **not** a POS, ERP, inventory, revenue-reporting, face-recognition, or general security system. Live camera streams are the intended operational source. Recorded/controlled video supports repeatable tests, AI evaluation and fallback demonstrations.

**Current approved target** has three independent origins/paths:

```text
Camera / live video → validated AI observations → MonitoringRule
                                                ├── outcome_type = OPERATIONAL_EVENT
                                                │   → OperationalEvent → ResponsePolicy
                                                │   → Notification / Recommendation / Operational Task
                                                └── outcome_type = INCIDENT
                                                    → qualified Incident directly
                                                    → corrective Task → evidence → Operator verification

StaffReport (text/image/voice draft → staff review and confirmation)
    → routine operational Task OR independently qualified Incident

OperationalEvent NEVER creates, converts into, references or escalates into Incident.
```

- `OperationalEvent` = tracked operational condition/episode, with priority and resolution when the **condition clears**.
- `Incident` = independently qualified safety/material-disruption issue, with severity, corrective handling and verified closure.
- A CRITICAL Event is **not** automatically an Incident. Model confidence filters detections; it is not an Incident-routing approval gate.
- Current backend persistence and writer code separate Event and Incident outcomes. The INCIDENT runtime remains disabled until a validated safety measurement/model contract exists; see §§36 and 39–40.
- An ERD shows structure. Do **not** load it with SQL constraints/business-logic descriptions merely to represent workflow rules.

---

# 1. Source of truth and document alignment

## 1.0 — 23/10/2026 synchronization baseline (read first)

1. Latest explicitly confirmed team/mentor decisions and approved Business Rules.
2. Current Report 1 (scope), Report 2 (plan), Report 3 (SRS, detailed functional requirements).
3. **Physical ERD V6** for the *target* persistence shape; logical ERD and current diagrams where aligned.
4. Code and deployed schema as evidence of what **actually exists**, not as automatic approval to retain old behaviors.
5. Old reports, ERDs, Figma drafts, older planning notes and outdated comments only as history.

If target requirements disagree with code or an old document, **record the mismatch**. Do not modify live/shared SQL or automatically implement a speculative fix.

## 1.1 Current authoritative project documents

| Artifact | Link / status |
| --- | --- |
| Report 1 — Project Introduction | [Google Doc](https://docs.google.com/document/d/1C3BnR_XkLTMWYbnAumNLsyNiraRVloaycCHzkTbdhIs/edit); updated for independent Incident sources and shift swapping. |
| Report 2 — Project Management Plan | [Google Doc](https://docs.google.com/document/d/18o3oA7NQgxV8eJxstP2mQgRGRoqkq_d9e-MnHuxQfTI/edit); synchronized 17/10; 225 man-days retained **pending re-estimation**. |
| Report 3 — SRS | [Google Doc](https://docs.google.com/document/d/1puRtzzOr7T_TK-ZdnYkcn-bb9euMDxgjqC9wB7Z5qh8/edit); synchronized 23/10: 32-use-case inventory, expanded FE-03..FE-06 details, V6 entity overview and 38 renumbered BRs. |
| Business Rules | [Google Drive](https://docs.google.com/document/d/1ODHDSkK0pLCcbKVS2nl3G0KhxIz6Q5PR/edit); synchronized 23/10 to SRS §5.1 with **38 BRs**, 8 calculation rules and 8 scope rules. |
| **Physical ERD V6** | [Google Drive draw.io](https://drive.google.com/file/d/17HEN6bpP-hxOfckGZWo2bKzPw4CHsP7S/view); **37 tables / 72 relationship edges**. |
| Physical ERD V5 | [Google Drive](https://drive.google.com/file/d/1H831nHrqMmRwUnD8K8R-mO0FpIvRBk2Y/view); retained for history; **not the new target**. |
| **Logical ERD V6** | [Google Drive](https://drive.google.com/file/d/1_9YhOCJOsaff-1uZVuNbUTh6zbjBdXEo/view); current logical model, updated 20/10. |
| Context Diagram V2 | [Google Drive](https://drive.google.com/file/d/1hh3JUEey8Iy29bD6D8i294xiwwxIJVdm/view); refined actor/system boundaries and external CCTV/FCM integrations on 22/10. |
| Use Case Diagram V2 | [Google Drive](https://drive.google.com/file/d/1mMqMeVPe-a4H0YQcwMvgqGLgSE15TGjx/view); current 32-use-case diagram, updated 23/10. |
| Report 4 — Software Design Document | [Google Doc](https://docs.google.com/document/d/1oaQQ_NpTwmEFtYNjihoYzzeNDZ5CHcnj2Wdybq4lLCU/edit); initial architecture/database/design skeleton; UML and code-level specifications remain pending. |
| Report 5 — Software Test Documentation | [Google Doc](https://docs.google.com/document/d/1gF7EPNMVB_XDNGoPpJqH37C6g2Ln2ZWOyxIjn3OTnLY/edit); planning skeleton only; test cases/results and confirmed thresholds remain pending. |
| Backend working contract | [`AGENTS.md`](../AGENTS.md), [`README.md`](../README.md), [`docs/API.md`](API.md), [`docs/VALIDATION.md`](VALIDATION.md). |

## 1.2 Conflict rule

**Do not merge old and new Incident models.** Do not infer current DB tables from V6 alone. Distinguish approved target, coded subset, and actual deployed DB. Report discrepancies as an explicit implementation backlog. Record approval before destructive schema changes, workflow changes, or altering documents owned by teammates.

The 20–23/10 Report 3 update resolved the earlier V5 entity-overview and incomplete Use Case drift: `ZoneShiftRequirement` / `NotificationConfig` are removed, the V6 entities are listed, and UC-01..UC-32 are defined. Report 4 and Report 5 are still draft skeletons, so placeholders and proposed test/design content are **not implementation evidence**.

---

# 2. Product boundary

## 2.1 In scope

- One configured supermarket (single branch), floors, mapped operational zones, cameras, RTSP connections, camera-zone ROIs, and independent operational/system health.
- People detection and tracking; customer flow, ROI counts, queues, waiting time, crowd/congestion and checkout utilization where feasible and validated.
- Configurable zone MonitoringRules; exactly one **rule outcome**: OperationalEvent or qualified Incident.
- Operational ResponsePolicy actions, event and Incident feeds, task handling and independent verification.
- Staff shift and zone assignments, eligibility, availability, future `SWAP`/`COVER` requests, Manager approval.
- Staff reports (text, image, voice transcript), staff-confirmed AI-assisted extraction; mobile/web notifications.
- Operational KPI dashboards, historical analytics, exports, advisory recommendations; admin/RBAC/audit/global settings.

## 2.2 Explicitly out of scope

Full multi-branch production, POS/payment/inventory/ERP integrations, revenue/sales reporting, payroll/recruitment, customer identification or facial recognition, cross-camera person re-identification, theft detection, empty-shelf AI baseline and unapproved safety-detection models. Automated consequential action from recommendations is not authorized.

---

# 3. Actors and roles

| Actor | Responsibility | Not responsible for |
| --- | --- | --- |
| **System Administrator** | User accounts/roles, floor/zone/camera setup, monitoring/notification settings, system and camera health, global settings, audit. | Owning shift schedules or approving swaps. |
| **Operator** | Live monitoring, Incident intervention, Task assignment/reassignment, evidence verification, closure checks. | Mandatory pre-dispatch approval for every Incident; Manager scheduling approval. |
| **Manager** | Future shifts, zone/staff coverage, peer-accepted SWAP/COVER approval, analytics, reporting, advisory review. | Account/RBAC administration. |
| **Staff** | Check-in, receiving/accepting eligible Task offers, task execution/evidence, staff reporting, requesting/responding to swap/cover. | Verifying their own Task; approving final SWAP/COVER. |

The AI Analytics Engine is an **internal module**, not a human actor. Camera/video is an external input; FCM/external push service is an integration.

---

# 4. The four Mainflows

Mainflows organize demonstrable end-to-end user work, not every background AI process.

## MF-01 — Setup & System Configuration

```text
Admin login → configure the single store and its Floor/Zone layout → camera registration
→ connection test / preview → CameraZoneMapping + camera-frame ROI
→ zone MonitoringConfiguration + MonitoringRule → review/activate
→ camera/stream health monitoring → investigate/retest/recover exceptions
```

- Support exactly **one configured supermarket branch**. The current implementation seeds/uses a default record, while current SRS UC-01/FE-01 also permits authorized Admin maintenance of that single branch. Do not introduce multi-branch selection.
- An area polygon on a floor plan and camera-frame ROI are separate coordinate spaces.
- A preview/test request is initiated by Admin; runtime monitoring continues independently of viewers.
- Camera operations/configuration, health, connection enablement, and monitoring activation are distinct states.

## MF-02 — AI Monitoring → independent Event / Incident response

```text
Observed measurements + rule context
  → OPERATIONAL_EVENT rule → Event episode → ResponsePolicy/Actions
       → notification, recommendation, operational Task where configured
  → INCIDENT rule (separate validated safety path) → Incident directly
       → severity-based route → corrective Task / Operator intervention
```

- Operational event baselines: **Long Queue**, **Excessive Waiting Time**, **Overcrowding/Congestion**, **Checkout Capacity Issue**.
- Incident qualification requires approved risk/safety criteria and confirmed source. Facility faults, spills and cleanliness matters are staff-reported unless a specific validated AI capability exists.
- A distinct confirmed StaffReport can produce an Incident or a routine Task; it does not pass through camera tracking and is not an OperationalEvent.
- Operator inspection/intervention is allowed, but universal Operator pre-dispatch approval is not required.
- MonitoringRule outcomes are mutually exclusive; `ResponsePolicy` applies to the **OperationalEvent** branch only.

## MF-03 — Staff Task Execution & Evidence

```text
Task from OPERATIONAL_EVENT / INCIDENT / STAFF_REPORT / MANUAL
→ notify eligible, checked-in on-shift Staff for the affected and configured adjacent zones
→ first valid explicit acceptance wins → Handle Now / Handle Later
→ Staff works → submit evidence → independent Operator verification
→ Verified (or Rejected with reason → rework/reassignment)
```

- Staff may decline/cannot-handle with reason. `Accept and Handle Later` can set `planned_start_at`, but does not eliminate response targets.
- Only the current eligible assignee performs the work; competing offers withdrawn transactionally.
- Task evidence may require before/after photos by category; no universal before-photo requirement.
- `TaskEvidence.reject_reason` already exists. `Task.rejection_count` supports BR-28 **reassign after two rejections**.
- A linked Incident closes only after required corrective Tasks are verified and independent Incident closure conditions hold.

## MF-04 — Manager Operations Planning, Analytics & Reporting

**Planning:** future staff shifts, zone coverage, availability and `SWAP`/`COVER` approval.
**Analytics:** independent Event and Incident KPIs, staff/task performance, camera health, incident response and resolution, exports and advisory recommendations.

Planning and analytics are **independently accessible**. Do not force a schedule-edit action before viewing analytics. The proposed `Zone.min_staff` is **zone-level**, not shift-specific; do not reintroduce `ZoneShiftRequirement`.

### Shift-swap approval flow (current)

```text
Staff A submits SWAP/COVER on future ShiftAssignment
   → PENDING_PEER (Staff B accepts or declines)
   → PENDING_MANAGER (if B accepts)
   → Manager APPROVES / REJECTS (records reason as applicable)
   → atomic shift-assignment update ONLY on approval
```

- SWAP exchanges affected A/B assignments; COVER transfers coverage for A's shift using an eligible B.
- A may cancel while pending. B may decline before acceptance, or withdraw/cancel while awaiting Manager. No cancellation after final approval.
- Unresolved requests **expire exactly when 24 hours remain before the earliest affected shift begins**. For SWAP use the earlier of both shifts; for COVER use A's shift. This is **not** 24h after request creation.
- Recheck future start, overlap, availability, weekly hours, relevant zone coverage and permissions **at approval time**; preserve original assignments on rejection, cancellation or expiry.
- Preserve actor/time/reason for peer/Manager/cancellation outcomes as allowed by the schema.

---

# 5. Domain vocabulary — do not mix these concepts

| Term | Meaning |
| --- | --- |
| **Detection** | One AI inference result (bbox/class/confidence), not a final operational event. |
| **Track** | Camera-local tracking of observations; no cross-camera ID. |
| **Measurement** | Aggregated count, waiting time, flow, density or utilization over validated camera/zone/time context. |
| **MonitoringRule** | One configurable condition with `outcome_type`: `OPERATIONAL_EVENT` **or** `INCIDENT`. |
| **OperationalEvent** | Sustained operational condition/episode and operational priority (`WARNING`/`CRITICAL` as applicable). |
| **ResponsePolicy/Action** | Configured response to OperationalEvents, not an independent Incident generator. |
| **Incident** | Qualified safety/corrective issue, sourced directly from one approved INCIDENT rule or one confirmed StaffReport. |
| **StaffReport** | Explicitly confirmed staff-origin input after human review of AI extraction. |
| **Task** | Work assignment from one of four origins, separate from notification and evidence. |
| **Notification** | Delivery/in-app record; provider send or read is **not** Task acceptance. |
| **Recommendation** | Advisory only, never automatically enacts a consequential change. |

`Incident.source_type ∈ {STAFF_REPORT, MONITORING_RULE}`. An Incident refers to **exactly one** corresponding source; rule-originated Incidents do not carry a fabricated staff reporter. Event-originated Incidents are not allowed in target behavior.

---

# 6. Spatial model — critical implementation rules

- `Supermarket` 1:N `Floor`; `Floor` 1:N `Zone` and 1:N `Camera`.
- `Camera` N:M `Zone` via `CameraZoneMapping`; each mapping must reference the **same Floor**.
- `Zone.map_polygon`: **floor-plan coordinates**; `CameraZoneMapping.roi_polygon`: **camera-frame coordinates**. Do not project automatically.
- Existing backend persists normalized `[0,1]` geometry. Keep polygon validity/point-count requirements and boundary checks.
- Floor-plan replacement affects floor/map geometry references; do not silently delete unrelated camera mappings.
- One Zone may be seen by multiple cameras. That does not justify summing counts or cross-camera deduplication without an approved source-selection strategy.

---

# 7. Multiple cameras monitoring the same Zone

The schema supports many camera-zone mappings. **Current ROI runtime does not yet support arbitrary multi-camera measurement-source selection**; a narrower one-active-mapping-per-Zone activation policy was used in its October implementation. Preserve the general ERD cardinality while leaving runtime selection/fallback as a planned design decision. Avoid double-counting people across overlapping camera views.

---

# 8. Crowd analysis and heatmaps

- **Queue length:** count tracks satisfying queue-region residence criteria; existing baseline uses ≥5s presence.
- **Waiting time:** time spent from entry to checkout service, not simply queue count converted to minutes.
- **Crowd density:** qualified people count ÷ calibrated real zone area in m². Never infer m² solely from a normalized polygon.
- **Heatmap:** aggregate occupancy/movement spatially within a defined frame/time, with source and calibration limitations documented.
- **Checkout utilization:** staffed checkout counters ÷ total relevant counters; a validated counter model/source is still needed for full implementation.
- `ZoneMetric` holds historical metric aggregates; V6 includes `config_version` for configuration provenance.

---

# 9. Physical ERD V6 target vs implemented database

**Approved design:** [V6](https://drive.google.com/file/d/17HEN6bpP-hxOfckGZWo2bKzPw4CHsP7S/view), **37 native draw.io tables and 72 Crow's Foot relationship edges**. The current backend source is scaffolded from the post-cleanup V6 shape, but that does not prove a particular shared/deployed SQL Server instance has the same migration state.

## 9.1 Complete V6 table inventory (37)

| Domain | Target entities |
| --- | --- |
| Identity / store | `Role`, `UserAccount`, `Supermarket`, `Floor`, `Zone` |
| Cameras / mapping | `Camera`, `CameraConnection`, `CameraZoneMapping`, `CameraHealthEvent` |
| AI monitoring | `MonitoringConfiguration`, `MonitoringRule`, `OperationalEventType`, `OperationalEvent`, `ZoneMetric` |
| Incident / reports | `IncidentType`, `Incident`, `IncidentMedia`, `StaffReport`, `StaffReportMedia` |
| Tasks | `Task`, `TaskAssignmentHistory`, `TaskEvidence` |
| Staff / scheduling | `Shift`, `ShiftAssignment`, `StaffZoneAssignment`, `StaffUnavailability`, `ShiftSwapRequest`, `ZoneAdjacency` |
| Notifications / policies | `Notification`, `NotificationRecipient`, `UserDevice`, `ResponsePolicy`, `ResponsePolicyAction` |
| Administration / analytics | `Recommendation`, `AuditLog`, **`ServiceHealthEvent`**, **`SystemSetting`** |

## 9.2 V6 changes from V5

**Removed** `NotificationConfig` (settings now managed through `SystemSetting`) and `ZoneShiftRequirement` (`Zone.min_staff` is sufficient for the agreed scope). **Do not recreate either table without a new decision.**

**Added** `ServiceHealthEvent` (service name/status/error and detection/recovery timestamps), `SystemSetting` (unique key, value, type, updater and timestamp).

**Added attributes:**

| Table | Attributes |
| --- | --- |
| `Zone` | `min_staff` |
| `ShiftSwapRequest` | `expires_at`, `cancelled_by_user_id` (FK), `cancelled_at`, `cancellation_reason` |
| `AuditLog` | `result_status` |
| `ZoneMetric` | `config_version` |

| `Incident` | `monitoring_rule_id` |
| `Task` | `rejection_count` |
| `NotificationRecipient` | `attempt_count`, `last_attempt_at`, `provider_status` |

**Not added:** `NotificationDeliveryAttempt`, `TaskVerification`, `MonitoringRun`, or additional shift staffing requirement tables. Existing `TaskEvidence.review_status` and `TaskEvidence.reject_reason` provide evidence-review outcomes; existing `Task` records current verified state. The team deliberately preferred minimal persistence.

## 9.3 Important relationships / provenance

- Exactly one `MonitoringRule.outcome_type`; its Event/Incident type reference follows the chosen outcome.
- Event and Incident are **parallel** branches; `OperationalEvent` has no target `incident_id` FK in V6.
- `ResponsePolicy` / `ResponsePolicyAction` apply to operational Event action routing.
- `Task.origin_type ∈ {OPERATIONAL_EVENT, INCIDENT, STAFF_REPORT, MANUAL}`; populate only the corresponding origin FK (none for MANUAL).
- `ShiftSwapRequest`: A requester, B colleague, their applicable future assignment(s), decision maker and cancellation actor; Manager approval applies changes.
- `NotificationRecipient` is user-level delivery summary; `UserDevice` is an active push token/device record. `NotificationRecipient` retry counters **do not represent a permanent per-device send-attempt audit table**.
- `SystemSetting` stores **non-secret** configuration only; credentials, tokens and cryptographic keys stay in protected environment/secret storage.

## 9.4 DB implementation status — do not overstate

At inspected `dev` commit `4df2864fa670fad64b37c93d1a705a9c9f2a3f56`, the generated scaffold has nullable Event/Incident targets plus `MonitoringRule.outcome_type`; `OperationalEvent` no longer links to Incident; `Incident.monitoring_rule_id` supports direct rule provenance. `MonitoringIncidentWriter` writes exactly the configured outcome and its tests assert that an OPERATIONAL_EVENT rule never creates an Incident.

The scaffold covers V6 persistence, but a generated entity is not proof that its workflow is implemented. `SystemSetting` is consumed for the two MF-02 retry keys documented in §41, and the notification-settings increment provides typed Admin GET/PUT for the four approved `notification.*` FCM policy keys with audit/concurrency handling plus Staff-owned `UserDevice` registration lifecycle. ShiftSwapRequest and Manager shift planning have application/API/test coverage (§42). `ServiceHealthEvent`, StaffReport/evidence, FCM delivery and full analytics are not established. The canonical Database repo and applied SQL Server version must still be checked before migrations or deployment claims.

---

# 10. Authentication and secret handling

- Hash staff/admin passwords. Keep account disabled/revoked/role changes effective at API authorization boundaries.
- Only Admin creates/manages accounts; prevent disabling/deleting the last active Admin.
- JWT keys, SQL credentials, camera passwords and Data Protection keys must **never** be committed or logged.
- Camera credentials may be encrypted via ASP.NET Core Data Protection; service instances sharing encrypted credentials need the correct protected key ring.
- Keep `appsettings.Local.json`, deployment `api.env`, build output and recorded videos outside Git. Use example files only as templates.

---

# 11. Current architecture / stack

```text
Web frontend / Flutter mobile app
           ↓ HTTPS + authenticated API
ASP.NET Core (.NET 10)
  ├── Supermarket.Api            controllers, auth, API composition
  ├── Supermarket.Application    use cases, contracts, transactions
  ├── Supermarket.Domain         business rules, geometry, state validation
  └── Supermarket.Infrastructure SQL Server EF scaffolds, AI/video/health adapters
           ↓
SQL Server authoritative DB       Redis only for transient coordination/cache
Python AI service / OpenCV / detection + tracking   → observations (not DB ownership)
Firebase Cloud Messaging / push   → communication (not Task acceptance)
```

- SQL Server is canonical persistence; no code-first schema migration or EF `EnsureCreated` for the shared database.
- `src/Supermarket.Infrastructure/Persistence/Scaffolded/` is **generated**. Put custom behavior outside; re-scaffold only after approved SQL changes.
- Keep thin controllers, role-aware application services, domain invariants independent of ASP.NET/EF, explicit DTOs, sanitized errors and bounded retries.
- Deployment workflow/config lives under `.github/workflows`, `deploy/dev`, and [`docs/DEPLOY_DEV.md`](DEPLOY_DEV.md); historical deployment validation must not be presented as a fresh availability guarantee.

---

## 11.1 Approved UML Package Diagrams — implementation alignment (25/10/2026)

The following **five editable UML Package Diagrams** define the agreed **target logical module boundaries** for subsequent implementation and Report 4. They supplement the SRS, business rules and Physical ERD V6; they do **not** supersede those artifacts, establish database changes, or prove the packages already exist as source folders.

| Scope | Approved diagram |
| --- | --- |
| Overall System | [Overall System Package Diagram](https://drive.google.com/file/d/15VccEY3mjZE873lEd9ZewgQ28ODFT5gd/view) |
| Backend | [Backend Package Diagram](https://drive.google.com/file/d/16Hds29vypPHzu7LiUNI5KtKLZyEG7ZsL/view) |
| Frontend | [Frontend Package Diagram](https://drive.google.com/file/d/12uenwf5n_vMIOg6OIoanxsncZdxlDBH9/view) |
| Mobile | [Mobile Package Diagram](https://drive.google.com/file/d/12Zye5FYjqmUfLslm1QjMv6YqxIh_Uttb/view) |
| AI Service | [AI Service Package Diagram](https://drive.google.com/file/d/19_2c3UbZclmVOGyy3Zvg82WWO0U_nKr6/view) |

### Backend target package map

Preserve the four actual .NET project roots. Names below are **logical package responsibilities**; do not assume matching physical namespaces/folders already exist.

- **`Supermarket.Api`**: `Controllers`, `Authentication`, `Configuration` — transport endpoints, authentication/authorization wiring, dependency injection and HTTP composition; keep controllers thin.
- **`Supermarket.Application`**: `Identity`, `SupermarketManagement`, `Cameras`, `Monitoring`, `OperationalEvents`, `Incidents`, `StaffReports`, `Tasks`, `Workforce`, `Notifications`, `Analytics`, `Administration`, `Common` — use cases, orchestration, contracts/DTOs, transaction coordination and interfaces for external dependencies.
- **`Supermarket.Domain`**: `Identity`, `SupermarketStructure`, `Monitoring`, `OperationalEvents`, `Incidents`, `StaffReports`, `Tasks`, `Workforce`, `Common` — entities, value types, invariants, validation, state transitions and business policies; no HTTP, EF Core, SQL, AI HTTP calls or push-delivery implementation.
- **`Supermarket.Infrastructure`**: `Persistence`, `AI`, `Video`, `MediaStorage`, `Messaging`, `Caching`, `Security`, `BackgroundWorkers` — EF/SQL and external adapters, storage, workers and technical integration. Keep generated EF scaffold in its existing generated location until an approved migration/refactor.

**Naming decisions:** use `Application.SupermarketManagement` for store setup/maintenance use cases and `Domain.SupermarketStructure` for the physical `Supermarket`–`Floor`–`Zone`–`Camera` structure and constraints. Use `Domain.Common` rather than `Domain.Shared`; `Application.Common` remains its own shared application-level utilities/contracts. Do not confuse the `Supermarket` entity with the package name. The `Supermarket.*` prefix is appropriate for the actual .NET projects; do not force it into Python, Dart or React module names.

### Other repositories' target package map

These names provide cross-repository **contract alignment**, not a mandate to rename working directories in unrelated repos without team approval.

- **Frontend (React):** `App & Routing` (`Bootstrap`, `RouteRegistry`); `Pages / Features` (`Authentication`, `Administration`, `SupermarketLayout`, `Cameras`, `Monitoring`, `OperationalEvents`, `Incidents`, `Tasks`, `Workforce`, `Notifications`, `Dashboards`, `AnalyticsReports`, `DecisionSupport`, `SystemSetup`); `Components` (`Layouts`, `SharedUI`, `CameraMapWidgets`); `API` (`HttpClient`, `FeatureEndpoints`, `Contracts`); `Auth` (`Session`, `RoleGuards`); `Data / Config / Styles` (`ClientData`, `Configuration`, `StyleTokens`, `Assets`). `SupermarketLayout` means floor/zone layout, not POS or inventory.
- **Mobile (Flutter, Staff-focused):** `App Shell` (`Bootstrap`, `NavigationShell`); `Features` (`Auth`, `StaffReports`, `Alerts`, `Shifts`, `Tasks`, `ShiftSwaps`, `TaskEvidence`, `StaffProfile`); `Data` (`Models`, `OperationsRepository`, `HttpRepository`, `DemoRepository`, `Providers`); `Core` (`Routing`, `Config`, `NetworkProviders`, `DeviceNotifications`, `Theme`, `PushMessaging`); `Shared` (`Widgets`, `Formatting`). `StaffReports` and `StaffProfile` are logical labels, even where current source paths still use `report` and `me`; TaskEvidence and push messaging remain subject to actual implementation status.
- **AI Service (Python/FastAPI):** `InternalAPI` (`PreviewRoutes`, `MonitoringRoutes`, `HealthRoutes`); `Session Runtime` (`SessionManager`, `LeaseOwnership`, `SourceClock`); `Video Analytics` (`CameraReader`, `PreviewTracker`, `MonitoringTracker`, `FrameHealth`); `Measurements` (`ROIMeasurements`, `QueueDwell`, `MeasurementBuffer`); `Contracts & Config` (`SessionSchemas`, `MonitoringSchemas`, `Settings`). `InternalAPI` is an internal Backend-facing integration boundary, not a direct Web/Mobile API; it does not own business Incident/Task decisions or SQL persistence.

### Implementation rules for coding agents and developers

1. **Use the diagrams as target boundaries when creating or refactoring modules.** First inspect the real repo, interfaces, tests and existing dependencies. Do not move all files or rename public APIs/namespaces in one unreviewed change merely to mirror a diagram.
2. **Keep ownership explicit:** API receives requests; Application coordinates use cases and transactions; Domain enforces business invariants; Infrastructure implements technical integrations. Domain must not depend on API/Infrastructure. Prefer interfaces owned by Application for external integrations.
3. **Preserve approved business separation:** one `MonitoringRule` produces either `OPERATIONAL_EVENT` or `INCIDENT`; an OperationalEvent never becomes an Incident. StaffReports independently qualify for a routine Task or Incident. Tasks and Workforce remain separate business modules.
4. **Backend is authoritative** for authentication, role checks, eligibility, shift swaps, Task offer first-winner concurrency, Event/Incident state and persistence. Frontend/Mobile call authenticated Backend APIs, **not** the AI Service directly. AI sends validated measurements/observations; Backend applies MonitoringRules and persists outcomes.
5. **Do not misread UML dependencies as a complete runtime call graph.** The diagrams describe intended package ownership and dependencies, not every class or future endpoint. Keep cross-package coupling narrow and avoid circular dependencies.
6. **Maintain a gap list** whenever diagram packages are planned but not implemented. The source snapshot in §§36, 39–43 remains the evidence for implemented functionality; no diagram label changes that status. In particular, evidence verification, confirmed StaffReports, FCM, complete analytics and validated INCIDENT-targeting AI remain unverified/incomplete as described there.
7. **For a scoped change:** identify its owning package, preserve existing contracts and generated SQL scaffold, write focused tests, update `docs/API.md`/`VALIDATION.md` where relevant, and document any remaining diagram↔code discrepancy. Seek approval before breaking changes or cross-repository refactors.

# 12. MF-01 service boundaries

Account/RBAC; floor-plan/zone geometry; camera metadata, stream credentials and validation; camera-zone mapping; MonitoringConfiguration/Rule; preview; background monitoring; camera health; status/overview. Keep camera preview read-only and monitoring processing owned by the runtime worker. Do not add unapproved functionality merely because a scaffold allows it.

---

# 13. MF-01 API capability checklist

The `dev` baseline contains controllers for accounts, store/floor setup, cameras, monitoring config/review/activation, monitoring live/health, recorded-video upload and AI preview, plus an IncidentType controller. This integration branch additionally contains the scoped MF-02 Staff-work endpoints described in §41. For precise route/response status, inspect [`docs/API.md`](API.md), [`docs/openapi.json`](openapi.json) and actual controller source.

**Do not assume** the scoped MF-02 offer/response implementation itself provides final evidence verification, StaffReport confirmation, SWAP/COVER approval, arbitrary SystemSetting administration, ServiceHealthEvent handling, FCM delivery or complete analytics. A separate notification-settings increment manages only the four approved `notification.*` policy keys; MF-02 continues to read its two retry keys. Direct rule-target Incident persistence is implemented independently of OperationalEvents, but its activation contract remains fail-closed where measurement/model semantics are unresolved.

---

# 14. Backend validation rules for MF-01

- Check foreign keys/roles and same-floor camera-zone relation; reject invalid or self-intersecting polygons.
- Normalize floor/camera coordinates; never mix map coordinates and camera ROI.
- Validate camera connection source/protocol/credential state before enabling, and invalidate stale test results after changes.
- Validate MonitoringConfiguration readiness, rule thresholds/units, ROI mapping and source constraints before activation.
- New AI capabilities require actual model/measurement readiness; do not enable an unsupported checkout/wait-time detector for a demo.
- Enforce status transitions in backend; do not rely solely on frontend disable/visibility.

---

# 15. Timestamp conventions

Persist unambiguous timestamps (normally UTC) and convert to the configured supermarket local timezone in UI/reporting. Treat video capture time and processing time separately for sustained-condition evaluation. For SWAP/COVER expiry, calculate the **absolute deadline** from the earliest affected local shift start and store an unambiguous instant in `expires_at`. Never subtract 24 hours from request creation.

---

# 16. MF-01 state semantics

| State category | Example meaning |
| --- | --- |
| Camera status | Asset/admin operational enablement, not proof stream works. |
| Camera health | Recent stream/observation health and health-history events. |
| CameraConnection enabled | Configured source may be consumed, subject to validated test. |
| MonitoringConfiguration state | DRAFT / ACTIVE / INACTIVE or equivalent; separate from camera health and preview. |
| Runtime state | Starting/active/stopping/completed/error; capture-worker ownership matters. |

A status change in one category must not silently imply changes to the others.

---

# 17. MF-01 sequence to implement and demo

Admin login → existing default store → create floor/zone/map → register Camera → configure/test/preview connection → map ROI → save/review monitoring rules → activate → see monitoring/health status → simulate connection/config error → investigate/fix/retest → recover. Show auth failures and invalid geometry as negative paths.

---

# 18. MF-01 Definition of Done

The API behavior is implemented, documented and tested; relevant SQL schema is approved/applied; user-facing flows are demonstrable; errors are sanitized; permissions verified; non-SQL tests pass; relevant SQL integration tests run when maintained fixtures are available; limitations and deferred capabilities are recorded. A successful local build alone does not prove completed MF-01 acceptance.

---

# 19. MF-01 required tests

- Geometry valid/invalid, normalized map/ROI, same-floor mapping.
- Camera connection test/enable/invalidation, stream unavailable/recovery and health-history behavior.
- Monitoring draft/review/activation/deactivation, confidence/threshold validation, runtime worker ownership and recorded EOF.
- Auth/Admin-only setup, last-Admin protection, secret redaction, exception/status codes.
- Concurrent/repeated monitoring batches and active episode idempotency where implemented.

Tests must be explicitly identified as local unit, mock integration, disposable SQL or actual stream/GPU smoke tests; do not conflate them.

---

# 20. Report 2 quality expectations relevant to coding

Report 2 plans ≥80% coverage of **testable core business logic**, 100% of critical identified integration flows and 100% of P0/P1 Mainflow test scenarios; these are *targets*, not claims of achievement. Focus additional tests on direct Incident qualification, independent Event lifecycles, atomic Task acceptance, evidence rejection/reassignment and Staff A → B → Manager SWAP/COVER cancellation/expiry.

---

# 21. Git and contribution conventions

- `main` stable, `dev` integration; make changes on reviewed feature/fix/docs branches and PR into `dev`.
- Read root [`AGENTS.md`](../AGENTS.md) and relevant scoped instructions before implementation.
- Keep `docs/PROJECT_CONTEXT.md`, root AGENTS and README aligned when a **new approved** behavior changes shared context.
- Commit only intended changes. Do not rewrite teammate diagrams, Drive reports, SQL history or generated scaffolds outside the assigned scope.
- Run `git diff --check` for doc changes; for code changes use focused tests + applicable build. Never commit private credentials.

---

# 22. Suggested parallel implementation split

| Workstream | Responsibility |
| --- | --- |
| Backend/API | Controllers, domain flows, secure DTOs, response policies, Task/Incident workflow. |
| Database | Approved SQL Server schema V6 migration, indexes and EF re-scaffold (team coordination required). |
| AI/video | Tracked observations, ROI measurements, version/timestamp provenance, validated safety rules. |
| Web/Mobile | Monitoring dashboard, staff offers/evidence/report confirmation, shift request and Manager review. |
| QA/DevOps | Critical-flow tests, isolated fixtures, health/recovery, deployment and monitoring. |

Coordinate interface contracts first; do not allow separate teams to define conflicting Event/Incident source semantics.

---

# 23. V6 target entities — do not prematurely force into MF-01

Later feature tables (`Shift*`, `StaffReport*`, `Task*`, `Notification*`, `ResponsePolicy*`, `ZoneMetric`, `Recommendation`, `AuditLog`, `SystemSetting`, `ServiceHealthEvent`) exist in the V6 scaffold, but workflow availability differs by feature. Only claim the subsets verified in §§36 and 41–43; SQL first, then scaffold, code, tests and docs for further changes.

`ServiceHealthEvent` covers backend/AI/database/cache/notification service history. It is intentionally separate from `CameraHealthEvent`. `SystemSetting` stores admin-editable *non-secret* global settings and notification retry parameters. The notification-settings increment adds typed Admin management for the four approved FCM policy keys, a shared recipient-scoped Staff/Operator/Manager web inbox, and Staff-owned `UserDevice` register/rotation/revoke without changing the V6 schema. Database V8 provides the idempotent four-row default seed. FCM transport and mobile acceptance remain pending; push stays unavailable/off until Gate B.

---

# 24. Later-flow rules that MF-01 must not block

### Event episodes

A validated sustained breach opens/updates a corresponding unresolved episode. Priority can escalate in the same Event row. A sustained *valid recovery* clears it. Missing frames/data do **not** represent zero, recovery or closure. Do not auto-close Incidents when an Event clears.

### Task dispatch and acceptance

Notify eligible checked-in/on-shift Staff assigned to the target Zone **and any configured adjacent Zone simultaneously**, subject to workload and channel settings. Explicit first valid acceptance wins atomically. `NotificationRecipient` delivery/read and retry state is not proof of assignment. Unaccepted offers retry according to *configured* values, then Operator escalation; critical Incident routing may also notify duty Manager subject to approved policy. Current code has target-Zone offers only, so adjacent-Zone routing remains an implementation gap.

### Shift/Zone eligibility

`Zone.min_staff` sets one minimum per Zone; actual coverage is represented by `ShiftAssignment` + `StaffZoneAssignment`. `StaffUnavailability` and working-time constraints must be evaluated when assigning or approving scheduling changes. No shift-specific staffing-requirement table.

### SWAP/COVER

Peer accepts first; Manager makes final decision; pending requests can cancel or expire; assignments update transactionally on approval. Keep expiry exactly 24h before earliest affected shift begins.

---

# 25. Baseline calculations for later flows

| Measure | Definition / caveat |
| --- | --- |
| Queue length | Count qualified people continuously inside queue ROI (baseline residence ≥5 seconds). |
| Waiting time | Elapsed time from queue entry to checkout service; aggregate over actual observations. |
| Crowd density | Count / calibrated `Zone.area_m2`; never area inferred from screen pixels. |
| Checkout utilization | Counters with cashier present / relevant total counters. |
| Task response time | Explicit Task acceptance timestamp − actionable offer dispatch timestamp. |
| Incident resolution time | Approved Incident start/dispatch reference through verified resolution/closure as defined by policy. |
| Footfall | Directional line-crossing count with defined ROI/source and dedup limitations. |

Keep Event and Incident statistics **separate**. Do not include revenue/sales metrics.

---

# 26. Baseline AI thresholds and configuration

Initial Long Queue/Wait/Congestion/Checkout event types are **categories**, not authority to hard-code universal numerical thresholds. `MonitoringRule` defines warning/critical threshold, unit, sustain and cooldown, and optional parameters. Detection confidence acts *before* tracking and measurement. Rule `outcome_type` controls which entity can be produced. Incident-targeting hazard rules require independently approved safety-risk criteria and **validated model feasibility**; do not treat general person detection as spill/equipment fault detection.

Legacy implementation constants (e.g., draft defaults and temporary people-count mode for congestion) remain **implementation checkpoints**, not a universal policy declaration. If an operational SLA/default is unapproved, mark it configurable/TBD.

---

# 27. Privacy / data handling constraints

- Live monitoring primary; uploaded recordings are testing/evaluation/fallback materials, not default operational capture.
- **Do not store continuous video by default**. Clips/snapshots only when justified for a qualifying Incident or documented operational review and permitted by policy.
- Proposed **20-second clip** and **30-day retention** are *provisional*, not approved production limits.
- Visible surveillance notice for monitored areas; no customer identity extraction/storage.
- Minimize exposure of staff, video and health data; role-based access, secured transport, audit administrative actions.
- Dismissed Incidents are excluded from Incident performance KPIs.
- Apply only finalized retention, security and legal requirements for production release; unresolved details need approval.

---

# 28. Error handling conventions

Validate inputs at the API boundary and invariants in application/domain. Use structured error codes/ProblemDetails and authorization failures. Avoid leaking stream URIs, passwords, internal server paths or Data Protection key material. Distinguish no observation, unavailable source, invalid camera configuration, rejected approval, expired shift request and concurrency conflict.

---

# 29. Logging and observability

Capture sanitized structured logs with correlation IDs and measured timings where feasible. `CameraHealthEvent` captures camera faults; **V6 `ServiceHealthEvent`** captures service health over time. Health telemetry is **not** operational `Incident`. `AuditLog.result_status` records action outcome. Detailed transient FCM attempt traces can remain operational logs; the team chose **not** to add a `NotificationDeliveryAttempt` SQL table.

---

# 30. Do not over-engineer

Do not introduce new tables just for logs, business state rules, or one-off edge cases when existing entities/attributes suffice. In particular, do not re-add `ZoneShiftRequirement`, `NotificationConfig`, `NotificationDeliveryAttempt` or `TaskVerification`. Do not add unapproved AI model types, automatic recommendation execution, speculative scheduling optimization or unnecessary cross-camera identity logic.

---

# 31. Do not under-engineer either

Maintain first-winner concurrency, idempotent Event episodes, correct role/eligibility checks, distinct Event/Incident lifecycles and source provenance, cancellation/expiry for swaps, evidence rejection/reassignment, password/credential protection, test-KPI exclusion, and traceable operational state changes. Minimized schema **does not** relax these requirements.

---

# 32. Report 3 implications while implementation proceeds

Report 3 FE-01..FE-12 remains the detailed behavior source. The following requirements are especially important:

- FE-03/04: `MonitoringRule` outcome selects separate Event vs Incident storage, not a conversion.
- FE-05/06: recipient delivery distinct from Task acceptance; Tasks have exactly one source and independent verification.
- FE-07/08: Manager schedules and approves swaps; Staff A/B request/respond on mobile.
- FE-10: distinct historical metrics and proper demo/dismissed exclusions.
- FE-12: service-health history and non-secret global settings.

**23/10 alignment:** Report 3 now removes the retired V5 entities, defines UC-01..UC-32, and renumbers its consolidated rule set to BR-01..BR-38. Use the new BR numbers in code comments, tests and documentation; older references after the former duplicate BR-17/BR-35 positions are stale.

---

# 33. Implementation questions and V6 refinements currently OPEN

These require scoped decisions rather than a coding agent's invention:

1. **Canonical V6 SQL migration:** exact approved SQL change sequence, applied DB version, re-scaffolding schedule and data migration of retired tables.
2. **Safety Incident feasibility:** validated detection models, safety qualification and approval for INCIDENT-targeting rules, not inferred from Event confidence.
3. **Multi-camera coverage:** source selection, failover and count aggregation without duplicate persons; preserve schema N:M while runtime remains limited.
4. **Analytics/SLA:** final severity-specific deadlines/thresholds, timestamp reference points and operational reporting acceptance.
5. **Task rejection history:** minimal `Task.rejection_count` + existing `TaskEvidence` evidence status/reject reason sufficient for agreed workflow; check actual query/audit needs during build.
6. **Notification delivery:** recipient-level counters/status are approved; provider/device acceptance semantics and transient log retention still need engineering definition.
7. **Global settings:** allowed keys, type validation, precedence with deployment configuration and audit; never put secrets here.
8. **Privacy and video:** approved retention/storage/access controls for deployment; provisional durations are not binding.
9. **Draft design/test artifacts:** Report 4 UML/code-level design and Report 5 test cases, environments, milestones and execution evidence remain to be completed and reviewed.

These open items do not reverse approved target architecture or grant permission to create speculative tables.

---

# 34. Coding-agent behavior for this repository

1. Identify assigned Mainflow/issue and read this guide plus root AGENTS, README, relevant API/domain/SQL sources.
2. Inspect target branch + `git status` and latest approved artifacts. Preserve unrelated work.
3. State which facts are **target requirements**, **present in code**, **in current database**, or **unknown**.
4. Propose minimal schema/contracts first for database-impacting work. Require team-approved SQL before re-scaffolding.
5. Prefer existing architecture/conventions; avoid changes to unrelated frontend, diagrams or team-owned documents.
6. Verify changed behavior with focused tests; document which SQL/live/hardware tests **were not run**.
7. Summarize changed files, requirements satisfied, known divergence, risks and follow-up actions for PR review.

---

# 35. MF-01 quick mental model

```text
One configured Supermarket (current implementation seeds a default)
  → Floor: floor plan
  → Zone: polygon on floor plan, min_staff, area_m2
  → Camera: registered on same Floor
  → CameraConnection: tested/validated source and credentials
  → CameraZoneMapping: video-frame ROI for Zone
  → MonitoringConfiguration + MonitoringRule: review and activate
  → CameraHealthEvent: camera-specific health history
  → MonitoringWorker: background measurements/events
```

Do not mix camera-frame ROI with floor polygon. Do not treat camera health as a shopper Incident. Do not derive physical density from pixel area. Do not confuse the V6 source scaffold with proof that a shared/deployed database is migrated.

---

# 36. Current implementation checkpoint — inspected `dev` at `4df2864fa670fad64b37c93d1a705a9c9f2a3f56`

**Observed from source tree and specific inspected files, not fresh runtime execution:**

| Area | Code evidence | Status/caveat |
| --- | --- | --- |
| .NET 10 N-layer backend | `src/Supermarket.Api`, `Application`, `Domain`, `Infrastructure`, tests | **Present**. |
| Core setup/auth/camera/monitoring APIs | Current Controllers and README/API | **Present in repo**; do not infer live feature acceptance. |
| Preview/recorded-video/monitoring worker | AI clients, `MonitoringWorker`, camera workers | **Present in repo**, specialized count/queue subset. |
| OperationalEvent episodes | `MonitoringIncidentWriter`, scaffolded `OperationalEvent`, targeted tests | **Implemented subset**; Episode behavior detailed §40. |
| V6 rule `outcome_type` and provenance | Scaffolded `MonitoringRule`, `OperationalEvent`, `Incident`; writer tests | **Aligned in source scaffold/writer**: mutually exclusive targets and no Event→Incident link. Applied shared-DB state is not inferred. |
| Direct Incident outcome | `MonitoringIncidentWriter`, monitoring review/live readers | Writer/persistence branch is present, but activation/runtime is deliberately rejected as `INCIDENT_RULE_RUNTIME_UNSUPPORTED` until a validated safety contract exists. |
| MF-02 operational Task offers and Staff responses | `StaffWorkController`, `StaffTasks`, `StaffWork`, `TaskOfferWorker`, `StaffTaskTests` | **Merged to `dev`**: check-in, target-Zone offers, Handle Now/Later, Cannot Handle, atomic first acceptance, retry/exhaustion and Operator manual assignment. Adjacent-Zone dispatch is not implemented. See §41. |
| MF-04 SWAP/COVER and shift planning | `ShiftSwaps`, `ShiftPlanning`, controllers, workers and tests | **Merged to `dev`** with the bounded behavior in §42. |
| Camera playback/editing | `CameraPlayback`, `MediaMtxGateway`, `CameraSetup`, camera tests | Dynamic MediaMTX playback paths, sanitized WHEP URLs and preserve-password editing are present; see §43. |
| Evidence verification, confirmed StaffReport, FCM delivery, complete analytics | No complete application flow established | **Do not claim implemented**. |
| `SystemSetting`, `ServiceHealthEvent`, V6 zone staffing fields | V6 scaffold plus focused application reads | MF-02 consumes two retry settings; Admin manages the four approved notification policy keys. Other settings and service-health workflows remain unimplemented unless separately verified. |
| Deployment health / CI results | Historical README, workflow and VALIDATION docs | **Dated evidence only**, no live deployment check performed for this synchronization. |

Do not present a passing test score, deploy availability, complete SQL V6 migration or shipped features without running/verifying them.

---

# 37. Final goal of MF-01

A reviewer sees: authorized Admin prepares floor/zone/camera/ROI and monitoring configuration → preview/test/activate → continuous monitoring and health → handles a realistic failure/recovery. All actor-visible steps work with current configured sources, are documented, and have test evidence. This is distinct from MF-02 independent Event and Incident execution and later Staff/Manager workflows.

---

# 38. Current decisions / superseded designs

## Superseded — do NOT reintroduce

- `OperationalEvent → Incident` conversion or `QUALIFY_INCIDENT` as an OperationalEvent ResponsePolicyAction; `OperationalEvent.incident_id` as a target relationship.
- One mixed MonitoringRule simultaneously requiring both an Event and Incident type.
- Operator final approval of Staff SWAP/COVER; direct automatic swap without peer acceptance and Manager decision.
- `ZoneShiftRequirement` and shift-specific `min_staff` requirements; `Zone.min_staff` is the current design.
- `NotificationConfig` as a separate table; retry config is in `SystemSetting`.
- `NotificationDeliveryAttempt` / `TaskVerification` as new persistence entities; use the agreed existing summary and evidence fields.
- Universal Operator pre-review for qualified Incidents; all confidence levels routing to manual Incident review.
- Universal before-photo requirement, Staff self-verification, automatically executed AI recommendations.
- V3/V4 ERD table counts and old v4 design-only relationships as a basis for new migrations.

## Current target decisions

- Separate `OPERATIONAL_EVENT` and `INCIDENT` MonitoringRule outcomes; direct staff-confirmed Incidents.
- Exactly four Task origins, explicit first-eligible acceptance and independent verification.
- Staff A → B → Manager SWAP/COVER; 24h-before-shift expiry and cancellation data in V6.
- Per-zone minimum staffing in `Zone.min_staff`, not per-shift requirement records.
- V6 37-table simplified ERD including non-secret `SystemSetting` and service-level `ServiceHealthEvent`.
- Dismissed-Incident KPI exclusions; continuous video storage excluded by default; privacy numbers provisional.

---

# 39. Assigned ROI monitoring increment — implementation snapshot, not target design

The inspected implementation uses camera-local tracked observations, ROI footpoints and a narrowed measurement set (people count / queue length). In the earlier increment, count-based congestion is expressed through `PEOPLE_COUNT` parameters, **not** density computed from screen area. Waiting-time and checkout-utilization full runtime require further measurement work. The runtime previously required exactly one ACTIVE camera mapping per Zone and does not provide automatic overlapping-camera deduplication.

A backend-owned worker consumes batches independently of a viewer. It must recheck current configuration/source mapping before persistence. Gaps/reconnect/config changes can reset sustain continuity; unavailable data must not be interpreted as a valid zero. Recorded video can complete at EOF and is not an automatically looping production stream. Preview is read-only relative to monitoring-owned runtime and does not itself generate production measurements.

`MonitoringIncidentWriter` now persists OPERATIONAL_EVENT and INCIDENT outcomes independently. However, the monitoring review/live runtime rejects INCIDENT rules until an approved and validated safety observation contract exists. Do not bypass `INCIDENT_RULE_RUNTIME_UNSUPPORTED` by reusing person-count thresholds as hazard evidence.

---

# 40. OperationalEvent episode implementation — evidence through inspected `dev`

The earlier implemented episode identity is the unresolved tuple:

```text
(Camera, Zone, OperationalEventType)
```

Code and dated plan describe an active filtered uniqueness constraint (`UX_OperationalEvent_Active_Camera_Zone_EventType`) where `cleared_at IS NULL`; verify the actual SQL Server index before depending on it. The intended implemented episode behavior is:

```text
first sustained breach       → open one active OperationalEvent
subsequent observations      → update its metric + last_observed_at
WARNING → CRITICAL          → escalate same row, no automatic downgrade
valid sustained recovery     → mark CLEARED and set cleared_at
new breach after clear       → start a new episode
missing/unavailable readings → do NOT synthesize zero or clear automatically
```

Recovery/hysteresis, source capture timestamps, selected camera and stale-config protection require the exact implementation/tests to be checked for every change. This implementation checkpoint **does not** authorize Event-origin Incident generation. The scoped Task-offer and MF-04 implementations in §§41–42 do not imply complete ResponsePolicy execution, confirmed StaffReports, external push delivery, historical KPI completeness or a verified deployed V6 migration.

---

# 41. MF-02 Staff-task implementation subset — integration evidence

The MF-02 work merged into `dev` implements the following bounded workflow:

```text
AUTOMATIC CREATE_OPERATIONAL_TASK policy
→ Task OFFERED to eligible checked-in/on-shift Staff assigned to the Zone
→ HANDLE_NOW / HANDLE_LATER / CANNOT_HANDLE
→ first valid acceptance wins atomically
→ retry unaccepted offers, then flag for Operator assignment
```

The branch also contains Operator candidate lookup/manual assignment and preserves accepted Tasks when an Event clears while cancelling an unaccepted offer. Exact routes and dated validation evidence are in [`docs/API.md`](API.md) and [`docs/VALIDATION.md`](VALIDATION.md).

This subset is now mapped to the exact 37-table post-cleanup V6 scaffold. Retry overrides use `SystemSetting` keys `task_offer.retry_interval_sec` and `task_offer.max_retry_count`; invalid or absent values fall back to 60 seconds and three total attempts. `NotificationConfig` remains retired, and no generated scaffold file was hand-edited to restore it.

**25/10 mapper correction and SQL check:** removed obsolete Incident-qualification fields from the domain `ResponsePolicyAction` to match the approved V6 scaffold; the EF mapper now rejects incompatible domain properties explicitly. Two mapper regression tests and the non-SQL suite pass (340 tests). A rollback-only check against the configured 37-table Development database verified application check-in, evaluator → Event → Task/offer/IN_APP notification, retry, acceptance/start, priority escalation and recovery. Measurements were synthetic; camera/AI capture, HTTP/device delivery and concurrent acceptance were not exercised. The rollback retained the original DB configuration: the active rule had no response policy, camera/connection were inactive/disabled, and no current checked-in Staff covered the rule Zone. See the dated [validation entry](VALIDATION.md#mf-02-v6-task-mapper-and-live-sql-rollback-proof--2026-10-25).

**25/10 Operator integration continuation:** `9f8488b` split existing services/controllers by Staff vs Operator ownership with unchanged routes/namespaces. StaffTasks, OperatorTasks and TaskViews now have separate files; StaffMeController, TaskOffersController and TasksController likewise. Operator assignment accepts optional `expectedUpdatedAt` (409 TASK_CHANGED for a stale view); reassignment or outside-Zone assignment requires an audit reason. The companion web Task Board is implemented in the Front-End feature branch; rollout and mobile acceptance remain separate. Latest backend verification: 343 non-SQL tests, 15 actual local HTTP/auth checks, and rollback SQL proof including manual assignment/reassignment, history and notification. No permanent shared-data/schema change was made.

Not implemented by this subset: approval-required policy actions, SLA/deadline/overdue rules, FCM/SignalR delivery, task evidence and verification, confirmed StaffReports, simultaneous `ZoneAdjacency` dispatch or complete analytics. SWAP/COVER and Manager-maintained `StaffUnavailability` are separate MF-04 capabilities in §42.

---

# 42. MF-04 SWAP/COVER implementation subset

The MF-04 work merged into `dev` implements the current shift-swap approval flow on the post-cleanup V6 scaffold:

```text
Staff A: SWAP (exchange with one of B's shifts) or COVER (B takes A's shift) on a future ShiftAssignment
→ PENDING_PEER → Staff B accepts (PENDING_MANAGER) or declines (DECLINED_BY_PEER)
→ Manager APPROVED / REJECTED (rejection reason required)
→ only approval updates ShiftAssignment.user_id, atomically for both rows
```

- A cancels while pending; B withdraws only while awaiting the Manager; actor, time and reason use the V5 cancellation columns.
- `expires_at` is the earliest affected local shift start minus 24 hours, stored in UTC. A background worker sets EXPIRED; reads and actions already treat past-deadline requests as expired.
- Rules are re-checked on request, peer acceptance and approval: active Staff, future and not checked-in shifts, decision deadline, different shifts for SWAP, one pending request per shift (application check, no DB unique index), no overlap, `StaffUnavailability`, 48 h per local Monday–Sunday week, and Zone coverage (informational, because zone rows move with the assignment).
- Notifications are in-app only. `Notification` has no swap-request reference column, so clients open the caller's request list.

Manager shift planning has subsequently been extended by Database V7 Weekly Work Shifts:

- `Shift` rows are now Work Shifts of 4, 6 or 8 hours (BR-31), scoped to one local Monday–Sunday week by `week_start`; names are unique per supermarket/week. Times are locked while assignments use them.
- A Manager can copy ACTIVE Work Shifts from one week into an empty target week. This copies definitions only, never Staff assignments or publication state.
- Every assignment date must belong to its Work Shift week. Schedule coverage uses only the Work Shifts of the viewed day/week, preserving historical published assignments after later weeks change.
- `ShiftAssignment.status` is `DRAFT` while the Manager plans (hidden from Staff, no check-in, no swap), `SCHEDULED` once a date range is published, and `CANCELLED` when removed. Pre-existing rows already carry `SCHEDULED`.
- Publishing is refused while any future shift leaves an ACTIVE Zone below `Zone.min_staff` (BR-02). Assigning, editing and leave enforce the current BR-32/BR-33 overlap, weekly-hours, future-shift and availability constraints; started or checked-in shifts are locked.
- Only active `STAFF` accounts can be scheduled; dispatch eligibility remains Role + Zone, with no staff-skill table.
- Cancelling an assignment also cancels pending swap requests on it. `StaffUnavailability` is Manager-maintained leave and is refused while the person still has an unfinished shift in that period.

Not implemented: automatic recurring assignments, Staff self-service leave requests, a `SystemSetting` override for the weekly limit, and FCM delivery. Routes and validation evidence are in [`docs/API.md`](API.md) and [`docs/VALIDATION.md`](VALIDATION.md).

---

# 43. Recent MF-01 camera integration changes (22/10/2026)

Inspected `dev` includes three camera-focused updates after the prior context baseline:

- LIVE/RTSP browser playback provisions a stable `cam-{cameraId}` MediaMTX path through the internal Control API and returns a sanitized WHEP URL; clients no longer require build-time camera-path mappings.
- Editing a connection may set `preservePassword: true` to keep the encrypted stored password when the password field is intentionally blank. Responses expose only `hasCredentials`, never the password or secret reference.
- Stream failures are classified so transport/source errors are not mislabeled as camera visual-health failures; AI-service unavailability remains processing health, not an operational Incident.

These are source/test observations, not live deployment claims. See [`README.md`](../README.md), [`docs/API.md`](API.md) and camera-focused tests for current routes and configuration.

---

> **Maintenance note:** When this file is committed, update stale conflicting rules in root `AGENTS.md` and `README.md` in the **same reviewed PR**. Preserve the `AGENTS.md` instruction to read this guide in full. Update this synchronization date only after re-reading changed official documents and relevant code; do not append another superseding addendum to the top of the file.
