# PROJECT_CONTEXT.md — FA26SE103 SRS Alignment & Implementation Snapshot

> **Project:** AI-Powered Smart Supermarket Operations Monitoring System
> **Project code:** FA26SE103
> **Group:** GFA26SE39
> **Repository:** [GFA26SE39-FA26SE103/Back-End](https://github.com/GFA26SE39-FA26SE103/Back-End)
> **SRS alignment date:** **19/11/2026**, current Report 3 §5.1 BR-01–BR-165. Source implementation history below remains dated 11/11/2026 and earlier.
> **Implementation evidence baseline (historical):** **11/11/2026** — old Physical ERD V7 / Database V16 and branch-specific StaffReport/SLA observations. Not an audit of source after this date.
> **11/11 implementation reconciliation (dated):** The Backend `dev` branch then contained five-ring dispatch and task-verification merges. It includes the StaffReport submit/draft/Incident/routine-Task paths, standalone TaskType SLA policy, operational SLA milestones, and specialty-aware same-Zone dispatch described below. This does not claim live provider, production database, camera, frontend or mobile acceptance.
> **Purpose:** repository-owned guide for developers and coding agents. It defines **working target behavior** separately from **verified source-code implementation**. It is not a substitute for the official reports or the canonical SQL schema.
> **Important:** ERD V7 was the 11/11 physical design reference; current Report 3 and the Database repository govern new changes; Database scripts are versioned artifacts and do not prove a target database was updated. Working requirements not represented in approved schema/code remain requirements, not implementation facts. External links are references, **not automatic synchronization**.
> **Current requirement IDs:** Report 3 contains **165 Business Rules (BR-01–BR-165)**, not the historical 38-BR baseline. Older numbered references below may be obsolete; verify before editing source/tests. See §§36, 41–43 for historical implementation checkpoints.

---

## 0. Read this first — approved SRS baseline (19/11/2026)

This is a single-supermarket AI-assisted operations monitoring capstone, not POS, ERP, inventory, payroll, revenue-reporting, customer identification or a general-purpose security classifier. The latest requirement authority is [Report 3 — SRS](https://docs.google.com/document/d/1puRtzzOr7T_TK-ZdnYkcn-bb9euMDxgjqC9wB7Z5qh8/edit), §3 FE-01–FE-12, §4 NFR, and §5.1 **BR-01–BR-165**. Earlier BR numbering in this file's dated implementation sections is historical.

### Independent operational and safety paths

~~~text
Camera/live source → validated AI measurements → enabled MonitoringRule
  → OPERATIONAL_EVENT (EventType only) → sustained OperationalEvent episode
  → episode-bound SLAPolicy and SLAPolicyAction
  → WARNING alert: Operator notification; CRITICAL alert: policy-bound Task
  → normal valid measurements over ClearDuration: clear event episode.

StaffReport (text, image or voice; editable AI-assisted draft)
  → explicit Staff review and confirmation
  → routine Task OR independently qualified Incident → corrective Tasks
  → evidence → authorized verification → independent Incident closure.

SOLE APPROVED AI SAFETY EXCEPTION
  approved Fallen Person AI → model-specific validated FALLEN_PERSON detection
  → create FALLEN_PERSON Incident directly, preserving detection source/evidence
  → deduplicate active fallen-person episode → dispatch TT-16 immediately
  → no fabricated StaffReport and no OperationalEvent intermediary.
~~~

- `MonitoringRule` must target `OPERATIONAL_EVENT` and an approved EventType only. It must never target IncidentTypes, create Incidents, or convert/escalate an OperationalEvent into an Incident (BR-15–19, BR-24). Old `outcome_type = INCIDENT` design below is superseded as a **business requirement** even if historically scaffolded; change code/SQL only through an approved database-first review.
- Alert Level `WARNING`/`CRITICAL` is **not** the same domain as SLA Response Priority `CRITICAL`/`HIGH`/`MEDIUM`/`LOW`. Missing or ambiguous SLA policies must raise configuration exceptions, not silent defaults (BR-33–44, BR-114–138).
- Event clearance must not close Incidents, verify accepted Tasks, or reset SLA clocks; missing/stale observations are unavailable, not zero (BR-26–32, BR-66–68).
- An Incident has **no Severity** field; it originates only in a qualified Staff-confirmed StaffReport or the approved FALLEN_PERSON detection exception (BR-19, BR-35, BR-45–53).
- Exactly four Task origins: `OPERATIONAL_EVENT`, `INCIDENT`, `STAFF_REPORT`, `MANUAL`, with exclusive source provenance (BR-69). Evidence and verification follow TaskType and SLA Response Priority (BR-99–113).
- Official workflows are **WF-01 Configure and Activate Store Monitoring; WF-02 Detect and Route Operational Issues; WF-03 Execute and Verify Operational Tasks; WF-04 Plan Staff Shifts and Resolve Coverage Changes; WF-05 Review Operational Performance and Generate Reports.** Old MF-01–MF-04 labels in dated notes below are legacy grouping only.
- **Qualitative NFRs** (Report 3 §4) require representative documented staging validation of usability, reliability, performance, security, maintainability, scalability and portability. Do not invent rigid numerical FPS/latency/accuracy/uptime guarantees. Explicit timing/configuration rules in BRs are separate.
- [Report 7 main](https://docs.google.com/document/d/19lYhKokPHMUSmUY3C-SUMpAn8xsZLA4Gg-bOeRX0duc/edit) §5.1 copies the same 165 rules. Submitted BM-04 and Registration Form are frozen; do not silently edit them or Review 2 copies.

### Approved target versus implementation reality

The old Backend snapshot has a generic `MonitoringRule` INCIDENT persistence/writer path, but the approved SRS now permits **only** dedicated approved Fallen Person AI-originated Incidents. This requires a documented gap analysis for validated detection inputs, provenance/evidence persistence, one-active-episode deduplication, incident source constraints, TT-16 auto-dispatch, policy and SQL compatibility. Neither the historic writer nor presence of a database column proves that this new exception is implemented or deployed. All sections below labeled with dates describe historical implementation observations until revalidated.

---

# 1. Source of truth and document alignment

## 1.0 Current requirements baseline — 19/11/2026

1. Latest explicit Council/team decisions and **current Report 3 §5.1 BR-01–BR-165**.
2. Main Report 1 (scope), Report 2 (plan), Report 3 (SRS), Reports 4–7 design/test/mapping; the original submitted Registration Form and BM-04 remain unchanged.
3. Approved ERD and versioned Database scripts, checked against the *actual* target SQL environment before schema changes.
4. Current source/tests and dated deployment evidence prove implementation, not authorization to keep superseded behavior.
5. Earlier reports, diagrams, notes and old BR IDs are retained as history only.

Never infer a current requirement from old BR-01–BR-38 references. Source/DB implementation differing from SRS must be called out as a gap, not silently made normative.

## 1.1 Authoritative project-document references

| Artifact | Reference / current interpretation |
| --- | --- |
| Report 1 — Project Introduction | [Main Google Doc](https://docs.google.com/document/d/1C3BnR_XkLTMWYbnAumNLsyNiraRVloaycCHzkTbdhIs/edit); main-copy requirement mapping. |
| Report 2 — Project Management Plan | [Main Google Doc](https://docs.google.com/document/d/18o3oA7NQgxV8eJxstP2mQgRGRoqkq_d9e-MnHuxQfTI/edit); main-copy BR/NFR synchronization. |
| **Report 3 — SRS** | [Authoritative main Google Doc](https://docs.google.com/document/d/1puRtzzOr7T_TK-ZdnYkcn-bb9euMDxgjqC9wB7Z5qh8/edit): FE-01–FE-12, qualitative NFR §4, **165 BRs (BR-01–BR-165)** as of 19/11/2026. |
| Report 7 | [Main Google Doc](https://docs.google.com/document/d/19lYhKokPHMUSmUY3C-SUMpAn8xsZLA4Gg-bOeRX0duc/edit); §5.1 now reproduces the same 165 BRs. |
| Standalone BR document | [Historical Google Doc](https://docs.google.com/document/d/1ODHDSkK0pLCcbKVS2nl3G0KhxIz6Q5PR/edit); its old 38-rule numbering must not be reused. |
| Physical ERD V7 | Historical approved baseline supplied as `FA26SE103_ERD_v7.drawio`; later Report 3 Entity Description and Database scripts need reconciliation before implementing new source/provenance. |
| Physical ERD V6 | [Historical draw.io](https://drive.google.com/file/d/17HEN6bpP-hxOfckGZWo2bKzPw4CHsP7S/view); not a current source of truth. |
| Logical ERD V6 | [Historical diagram](https://drive.google.com/file/d/1_9YhOCJOsaff-1uZVuNbUTh6zbjBdXEo/view); check current diagrams before schema work. |
| Context Diagram V2 | [Google Drive](https://drive.google.com/file/d/1hh3JUEey8Iy29bD6D8i294xiwwxIJVdm/view); compare with the current SRS. |
| Use Case Diagram V2 | [Google Drive](https://drive.google.com/file/d/1mMqMeVPe-a4H0YQcwMvgqGLgSE15TGjx/view); compare with the current SRS. |
| Report 4 — SDD | [Main Google Doc](https://docs.google.com/document/d/1oaQQ_NpTwmEFtYNjihoYzzeNDZ5CHcnj2Wdybq4lLCU/edit); Sequence and Class diagrams must reference matching classes, StateMachine belongs in Detailed Design; diagram edits are separate. |
| Report 5 — Test Documentation | [Main Google Doc](https://docs.google.com/document/d/1gF7EPNMVB_XDNGoPpJqH37C6g2Ln2ZWOyxIjn3OTnLY/edit); verify the live mapped BR references. |
| Backend status/contracts | [AGENTS.md](../AGENTS.md), [README.md](../README.md), [API.md](API.md), [VALIDATION.md](VALIDATION.md); evidence is dated and environment-specific. |

## 1.2 Conflict rule

Do not mix superseded `MonitoringRule → Incident` business behavior with the sole approved `FALLEN_PERSON` AI exception. Keep separate (a) SRS required target, (b) historical/current implementation, (c) actual deployed state and (d) cross-repository changes. Old SQL columns and API endpoints do not themselves authorize old behavior. Follow Database-first review before destructive schema/contract changes.

---

# 2. Product boundary — approved target

## 2.1 In scope

- One Supermarket, Floors, Zones, cameras/streams, camera-frame ROIs, health and auditable authorized administration (BR-01–12).
- Validated camera-local people counts, Zone entry/exit, queue length, waiting time and observed checkout capacity/utilization when evidence is sufficient (BR-13–14). Unvalidated signals are unavailable, not fabricated.
- Enabled `MonitoringRule` producing **OperationalEvent only**, sustained episode lifecycle and separate alert-level actions and SLA policy selection (BR-15–44).
- Staff-confirmed Incident and routine Task input, with AI-assisted editable drafts; **Fallen Person** is the sole approved direct AI Incident exception (BR-45–68).
- Specialty-based Task dispatch/offers, operator exceptions, evidence, AUTO/MANUAL verification, continuous SLA clocks, mitigation and Incident closure (BR-69–138).
- Shifts, Zone staffing, eligibility, check-in, SWAP/COVER/Manager assistance (BR-139–161).
- OperationalEvent/Incident-separated analytics, staff performance, exports, advisory recommendations, notifications and role-based access (BR-162–165 and FE-09–FE-12).

## 2.2 Explicitly out of scope

Multi-branch production, POS/payment/inventory/ERP integrations, revenue/sales figures, payroll/recruitment, persistent customer identification, facial recognition, cross-camera person re-identification, theft recognition, general-purpose safety-event AI, and unapproved autonomous consequential recommendations. Do not turn non-Fallen-Person hazards into camera-detected Incidents without a newly approved SRS change.

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

# 4. Five current business workflows (Report 3 names)

The SRS has **five** workflows. Legacy MF-01–MF-04 labels elsewhere in this dated file are not the current business taxonomy.

## WF-01 — Configure and Activate Store Monitoring

Authorized Admin manages single-store structure, Floor/Zone mappings, cameras, stream and frame ROIs, preview/activation, monitoring parameters, health and audit. A Camera belongs to one Floor, may monitor multiple Zones on that Floor, and a Zone may have multiple Cameras; do not overlap counting regions for one operational measurement or re-identify people across cameras (BR-01–12).

## WF-02 — Detect and Route Operational Issues

Validated AI measurements satisfy enabled EventType-bound `MonitoringRule` thresholds/persistence and open/update/clear a uniquely scoped OperationalEvent episode. WARNING notifies; CRITICAL triggers only the configured episode-bound SLAPolicyAction/Task. No duplicate notifications/Tasks from repeated measurements, no Event→Incident conversion (BR-13–44).

Separately, Staff reviews/confirms routine requests and potential Incidents, or the **sole** approved Fallen Person AI may directly create an evidence-backed, deduplicated FALLEN_PERSON Incident and dispatch TT-16 (BR-45–53). Operator dismissal/intervention, mitigation, verified Task handling and independent closure follow BR-54–68.

## WF-03 — Execute and Verify Operational Tasks

Eligible Task dispatch honors StaffSpecialty/RequiredSpecialty, published checked-in Shift, availability, Zone adjacency/distance and workload: same Zone → adjacent same-Floor Zones → remaining same Floor → nearby/other Floors. Concurrent ring offers, atomic first acceptance, next-ring expansion, no fixed retry count and Operator exception on exhaustion (BR-69–98).

Staff chooses Handle Now, Handle Later or Cannot Handle. Assigned Staff submit evidence or note, authorized AUTO or independent MANUAL verification applies per TaskType and SLA Priority; rejection/rework/reassignment and Operator escalation are audited (BR-99–113). SLA Response/Resolution timers and milestones remain continuous across all state changes (BR-114–138).

## WF-04 — Plan Staff Shifts and Resolve Coverage Changes

Manager manages future 4/6/8-hour Shifts and one-Zone ShiftAssignments, Zone minimum coverage, check-in, availability and overlap. Weekly working-hour limits follow applicable Vietnam Labour Code 2019 Article 105, not the old hard-coded 48-hour planning rule as a universal SRS constraint (BR-139–149). Staff peer SWAP/COVER, Manager-assisted COVER, and direct Manager-initiated eligible changes follow separate authorization, approval/cutoff, atomicity and audit requirements (BR-150–161).

## WF-05 — Review Operational Performance and Generate Reports

Operational metrics, separate OperationalEvent/Incident counts and durations, staff/task performance, SLA history and advisory insights. No revenue/sales KPI; dismissed Incidents are excluded from Incident KPIs while Tasks retain actual staff-performance outcomes (BR-162–165).

### Non-functional interpretation

Report 3 §4 describes usability, reliability, performance, security, maintainability, scalability and portability **qualitatively**. Test under documented representative staging configurations and record actual results. Do not insert fixed numerical performance, availability, model-accuracy or device-capacity guarantees without separately approved acceptance baselines. Explicit BR timing/configuration rules remain binding.

---

# 5. Domain vocabulary — current SRS meanings

| Term | Required meaning |
| --- | --- |
| **Detection** | AI inference output subject to model-specific criteria; not by itself an Event or Incident. |
| **Track / Measurement** | Camera-local tracked observation and validated aggregation; no cross-camera customer identity matching. |
| **MonitoringRule** | Configured operational condition with `OPERATIONAL_EVENT` outcome and valid EventType only (BR-15–18). |
| **OperationalEvent** | Independent sustained monitored episode with `WARNING`/`CRITICAL` Alert Level and qualification/clearance policy (BR-25–44). |
| **SLAPolicy / SLAPolicyAction** | Versioned policy and action. Response Priority is separately `CRITICAL`/`HIGH`/`MEDIUM`/`LOW` (BR-33–44, BR-114–138). |
| **Incident** | Qualified issue from confirmed StaffReport **or** approved AI FALLEN_PERSON detection only, with distinct mitigation and verified closure. No Severity. |
| **StaffReport** | Staff-confirmed routine or potential-Incident input; AI draft suggestions require explicit human review; no fabricated report for Fallen Person. |
| **FALLEN_PERSON** | Only approved AI-originated IncidentType; requires validated detection, source/evidence, episode dedup and immediate TT-16 Task dispatch (BR-49–53). |
| **Task / TaskOffer** | Work from exactly one of four origins with specialty-constrained offers, evidence/verification and independent SLA. |
| **Notification** | Delivery or in-app record; reading it does not mean Task acceptance. |
| **Recommendation** | Advisory only; does not enact operational changes by itself. |

**DB reconciliation:** Historical V7 source notes below mention `Incident.source_type = MONITORING_RULE`, `Incident.monitoring_rule_id` and `MonitoringRule.outcome_type = INCIDENT`. These are **not** the approved current SRS origin model. Determine the approved FALLEN_PERSON source/evidence schema with the Database team; never invent a StaffReport or silently repurpose a historical column.

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
- `ZoneMetric` holds historical metric aggregates; `config_version` was added by V6 and remains in the V7 baseline for configuration provenance.

---

# 9. Physical ERD V7 target vs implemented database

**Historical design baseline (11/11/2026):** ERD V7 had a 42-table persistence inventory. Later SQL scripts, Report 3's current Entity Description and the FALLEN_PERSON-only Incident source model need reconciliation; neither this inventory nor a deployed schema proves SRS alignment.

## 9.1 Complete V7 table inventory (42)

| Domain | Target entities |
| --- | --- |
| Identity / store | `Role`, `UserAccount`, `Supermarket`, `Floor`, `Zone` |
| Cameras / mapping | `Camera`, `CameraConnection`, `CameraZoneMapping`, `CameraHealthEvent` |
| AI monitoring | `MonitoringConfiguration`, `MonitoringRule`, `OperationalEventType`, `OperationalEvent`, `ZoneMetric` |
| Incident / reports | `IncidentType`, `Incident`, `IncidentMedia`, `StaffReport`, `StaffReportMedia` |
| Tasks | `Task`, `TaskType`, `TaskOffer`, `TaskAssignmentHistory`, `TaskEvidence` |
| Staff / scheduling | `StaffSpecialty`, `Shift`, `ShiftAssignment`, `StaffZoneAssignment`, `StaffUnavailability`, `ShiftSwapRequest`, `ZoneAdjacency` |
| Notifications / policies | `Notification`, `NotificationRecipient`, `UserDevice`, `SLAPolicy`, `SLAPolicyAction` |
| Administration / analytics | `Recommendation`, `AuditLog`, **`ServiceHealthEvent`**, **`SystemSetting`** |

## 9.2 V6 and V7 schema history

**Removed** `NotificationConfig` (settings now managed through `SystemSetting`) and `ZoneShiftRequirement` (`Zone.min_staff` is sufficient for the agreed scope). **Do not recreate either table without a new decision.**

**Added** `ServiceHealthEvent` (service name/status/error and detection/recovery timestamps), `SystemSetting` (unique key, value, type, updater and timestamp).

**V7 and later SQL history:** Database V9 adds `StaffSpecialty`, `TaskType`, `TaskOffer`, `SLAPolicy`, `SLAPolicyAction`, `UserAccount.specialty_id`, Task/Incident SLA fields, `MonitoringRule.clear_duration_sec`, `StaffReport.location_description`, and Rule+Zone active-event uniqueness. It removes `Incident.severity` and `IncidentType.default_severity`. Database V10 seeds seven specialties, sixteen TaskTypes and five IncidentType SLA policies. V9/V10 retained `ResponsePolicy`, `ResponsePolicyAction` and `TaskAssignmentHistory` for compatibility. The V11 cutover removed the two legacy response-policy tables; `TaskAssignmentHistory` remains audit history. V12–V15 add StaffReport IncidentType/category and duplicate-source support; the recorded application target was `FA26SE103_Dev`, not production. V16 adds TaskType-linked standalone SLA policy support. See the Database repository's actual scripts and `docs/VALIDATION.md`; do not infer the current deployed schema from this summary.

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

- **Current SRS target:** every `MonitoringRule` is OPERATIONAL_EVENT and must reference an EventType only (BR-16–18). Historical V7/Backend columns supporting an INCIDENT rule outcome are implementation debt and require separately approved migration/deprecation.
- Event and Incident are **parallel** branches; `OperationalEvent` has no `incident_id` FK in ERD V7.
- `SLAPolicy` / `SLAPolicyAction` apply to operational Event action routing. The old response-policy API route is a compatibility facade, not legacy-table storage.
- `Task.origin_type ∈ {OPERATIONAL_EVENT, INCIDENT, STAFF_REPORT, MANUAL}`; populate only the corresponding origin FK (none for MANUAL).
- `ShiftSwapRequest`: A requester, B colleague, their applicable future assignment(s), decision maker and cancellation actor; Manager approval applies changes.
- `NotificationRecipient` is user-level delivery summary; `UserDevice` is an active push token/device record. `NotificationRecipient` retry counters **do not represent a permanent per-device send-attempt audit table**.
- `SystemSetting` stores **non-secret** configuration only; credentials, tokens and cryptographic keys stay in protected environment/secret storage.

## 9.4 Historical DB implementation snapshot — 05/11/2026

**Historical implementation only — conflicts with approved 19/11 SRS:** At inspected `dev` commit `4df2864fa670fad64b37c93d1a705a9c9f2a3f56`, scaffold/code still allowed nullable Event/Incident MonitoringRule targets and an `Incident.monitoring_rule_id` writer path. Current BR-16–19/49–53 allow MonitoringRules to create only OperationalEvents, while only dedicated approved Fallen Person AI detections can create an AI Incident. Determine the new source/evidence mapping before enabling or removing old code; existing tests demonstrate the old contract, not acceptance of the new one.

This dated snapshot describes the scaffold and feature state at the inspected commit above; it is not the current StaffReport/SLA status. A generated entity is not proof that its workflow is implemented. The canonical Database repo and target SQL Server state must still be checked before migration or deployment claims. Current Backend implementation status is summarized in §36; dated test evidence remains in `docs/VALIDATION.md`.

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

The following **five editable UML Package Diagrams** define the agreed **target logical module boundaries** for subsequent implementation and Report 4. They supplement the SRS, business rules and Physical ERD V7; they do **not** supersede those artifacts, establish database changes, or prove the packages already exist as source folders.

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
3. **Preserve current SRS separation:** a `MonitoringRule` produces an `OPERATIONAL_EVENT` only; the sole AI Incident exception is a separately validated FALLEN_PERSON detection, without StaffReport or Event intermediary. Staff-confirmed reports independently qualify for routine Task or Incident. Tasks and Workforce remain separate modules.
4. **Backend is authoritative** for authentication, role checks, eligibility, shift swaps, Task offer first-winner concurrency, Event/Incident state and persistence. Frontend/Mobile call authenticated Backend APIs, **not** the AI Service directly. AI sends validated measurements/observations; Backend applies MonitoringRules and persists outcomes.
5. **Do not misread UML dependencies as a complete runtime call graph.** The diagrams describe intended package ownership and dependencies, not every class or future endpoint. Keep cross-package coupling narrow and avoid circular dependencies.
6. **Maintain a gap list** whenever diagram packages are planned but not implemented. The source snapshot in §§36, 39–43 and the dated validation log remain the evidence for implemented functionality; no diagram label changes that status. StaffReport confirmation and mandatory-task Incident closure are implemented in the current Backend source; complete analytics and validated INCIDENT-targeting AI remain open.
7. **For a scoped change:** identify its owning package, preserve existing contracts and generated SQL scaffold, write focused tests, update `docs/API.md`/`VALIDATION.md` where relevant, and document any remaining diagram↔code discrepancy. Seek approval before breaking changes or cross-repository refactors.

## Sections 12–31 are historical MF-01 implementation notes

The business workflow taxonomy is now WF-01–WF-05 as defined in §4. The following MF-01 implementation checkpoints remain dated engineering history and should not be used to derive current SRS behavior or BR numbers.

# 12. MF-01 service boundaries

Account/RBAC; floor-plan/zone geometry; camera metadata, stream credentials and validation; camera-zone mapping; MonitoringConfiguration/Rule; preview; background monitoring; camera health; status/overview. Keep camera preview read-only and monitoring processing owned by the runtime worker. Do not add unapproved functionality merely because a scaffold allows it.

---

# 13. MF-01 API capability checklist

The `dev` baseline contains controllers for accounts, store/floor setup, cameras, monitoring config/review/activation, monitoring live/health, recorded-video upload and AI preview, plus an IncidentType controller. The historically inspected integration branch additionally contained the scoped MF-02 Staff-work endpoints described in §41. For precise route/response status, inspect [`docs/API.md`](API.md), [`docs/openapi.json`](openapi.json) and actual controller source.

**Do not assume** the scoped MF-02 offer/response implementation itself provides final evidence verification, SWAP/COVER approval, arbitrary SystemSetting administration, ServiceHealthEvent handling, FCM delivery or complete analytics. StaffReport submission/draft and its Incident/routine-Task branching are implemented in the current feature branch; live OpenAI/Cloudinary and cross-client acceptance remain separate verification. A separate notification-settings increment manages only the four approved `notification.*` policy keys; MF-02 continues to read its two retry keys. **Historical code caveat:** a direct MonitoringRule-target Incident writer existed but is no longer approved as current business behavior; the only approved AI Incident path is validated FALLEN_PERSON with TT-16 dispatch (BR-49–53), and implementation is unverified.

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
| Database | Approved SQL Server schema migrations, indexes and EF re-scaffold (team coordination required). |
| AI/video | Tracked observations, ROI measurements, version/timestamp provenance, validated safety rules. |
| Web/Mobile | Monitoring dashboard, staff offers/evidence/report confirmation, shift request and Manager review. |
| QA/DevOps | Critical-flow tests, isolated fixtures, health/recovery, deployment and monitoring. |

Coordinate interface contracts first; do not allow separate teams to define conflicting Event/Incident source semantics.

---

# 23. Later-flow entities — do not prematurely force into MF-01

Later-flow entities (`Shift*`, `StaffReport*`, `Task*`, `Notification*`, `SLAPolicy*`, `ZoneMetric`, `Recommendation`, `AuditLog`, `SystemSetting`, `ServiceHealthEvent`) exist in the V7-aligned persistence/scaffold history, but workflow availability differs by feature. Only claim the subsets verified in §§36 and 41–43; inspect the target schema before further migration/scaffolding.

`ServiceHealthEvent` covers backend/AI/database/cache/notification service history. It is intentionally separate from `CameraHealthEvent`. `SystemSetting` stores admin-editable *non-secret* global settings and notification retry parameters. The notification-settings increment adds typed Admin management for the four approved FCM policy keys, a shared recipient-scoped Staff/Operator/Manager web inbox, and Staff-owned `UserDevice` register/rotation/revoke without changing the schema. Database V8 provides the idempotent four-row default seed. FCM transport and mobile acceptance remain pending unless verified in their current repositories.

---

# 24. Later-flow rules that MF-01 must not block

### Event episodes

A validated sustained breach opens/updates a corresponding unresolved episode. Priority can escalate in the same Event row. A sustained *valid recovery* clears it. Missing frames/data do **not** represent zero, recovery or closure. Do not auto-close Incidents when an Event clears.

### Task dispatch and acceptance

Notify eligible checked-in/on-shift Staff assigned to the target Zone according to the approved dispatch scope, subject to workload and channel settings. Explicit first valid acceptance wins atomically. `NotificationRecipient` delivery/read and retry state is not proof of assignment. Unaccepted offers retry according to *configured* values in the current implementation, then Operator escalation; critical Incident routing may also notify duty Manager subject to approved policy. Merged dev includes five-ring Zone/Floor dispatch via ZoneAdjacency; this is source state, not deployment acceptance. See §33.1 for the single ZoneAdjacency decision rule.

### Shift/Zone eligibility

`Zone.min_staff` sets one configured minimum per Zone (current BR-144, BR-148); no universal value is prescribed. The Admin creates the Zone and maintains its map/physical setup. The Manager owns the staffing baseline and plans/publishes `ShiftAssignment` + `StaffZoneAssignment` coverage against it. The baseline applies to every active Work Shift; a value of zero explicitly means no minimum coverage gate is configured for that Zone. `StaffUnavailability` and working-time constraints must be evaluated when assigning or approving scheduling changes. No shift-specific staffing-requirement table.

### SWAP/COVER

Normal peer flow still requires peer acceptance before the Manager's final decision. If Staff cannot find a colleague, Staff can ask the Manager to select cover without peer acceptance. The Manager may also directly COVER/SWAP future, unchecked-in slots with a mandatory reason. Pending requests can cancel or expire; assignments update transactionally only on the Manager action. Peer-request expiry stays exactly 24 h before the earliest affected shift; Manager assistance/direct intervention is allowed only before shift start.

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

Initial Long Queue/Wait/Congestion/Checkout event types are **categories**, not authority to hard-code universal numerical thresholds. `MonitoringRule` defines warning/critical threshold, unit, sustain/persistence, clear duration, cooldown and optional parameters. A continuous breach sustained for Persistence opens an episode; a continuous normal period of ClearDuration clears it; a brief normal interval keeps the same episode and must not create a duplicate Task. Detection confidence acts before tracking and measurement. **Current target:** Rule outcome is OPERATIONAL_EVENT only. The separately approved Fallen Person AI produces qualified direct FALLEN_PERSON Incidents after model-specific validation and evidence/episode checks; never infer other hazards from general people detection.

Legacy implementation constants (e.g., draft defaults and temporary people-count mode for congestion) remain **implementation checkpoints**, not a universal policy declaration. The Post-Review 2 seed values are planning assumptions for demo/test only, not validated thresholds. Waiting-time and Checkout Capacity are not validated runtime metrics; keep those rules disabled or exceptional until measurement coverage is proven.

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

- FE-03/04 (current): MonitoringRules produce OperationalEvents only. Staff-confirmed reports create routine Tasks or qualified Incidents; validated approved FALLEN_PERSON AI directly creates an Incident and immediately dispatches TT-16, independently of MonitoringRules.
- FE-05/06: recipient delivery distinct from Task acceptance; Tasks have exactly one source and independent verification.
- FE-07/08: Manager schedules and approves swaps; Staff A/B request/respond on mobile.
- FE-10: distinct historical metrics and proper demo/dismissed exclusions.
- FE-12: service-health history and non-secret global settings.

**Historical 23/10 alignment — superseded:** That SRS version used UC-01..UC-32 and BR-01..BR-38. **Current 19/11 mapping uses BR-01–BR-165**. Old code comments/tests citing BR IDs need remapping by rule meaning, never by numeric coincidence.

---

# 33. Open implementation gaps and approved 19/11 requirements

This section distinguishes **required behavior** from **verified implementation**. Review source/Database/AI-Service/Frontend/Mobile contracts before coding; do not assume old V7 fields are current approval.

1. **FALLEN_PERSON AI exception — integration gap:** approved model-specific validated detection → direct `FALLEN_PERSON` Incident (BR-49–53), evidence/source, active-episode idempotency, immediate `TT-16` response. It does not use MonitoringRule, StaffReport or OperationalEvent. Historic generic INCIDENT-target MonitoringRule runtime is unsupported as current requirement.
2. **Monitoring-rule contract:** update API/DB/UI so active MonitoringRules only reference EventTypes and emit OperationalEvents (BR-15–18); historical Incident type/outcome fields and old writer tests require deliberate reconciliation. Preserve Event/Incident analytical separation.
3. **Policy and SLA:** one active matching `SLAPolicy`, bound `SLAPolicyAction` where applicable, versioned snapshot, explicit configuration exception, independent alert level vs response priority; continuous wall-clock clocks. Current seed timings are *configuration examples*, not universal NFR numbers (BR-33–44, BR-114–138).
4. **Dispatch:** complete topology-aware same-Zone → adjacent same-Floor → remaining same-Floor → near/other-Floor rings, first atomic acceptance, no fixed retry count, Operator exception and specialty/shift/workload rechecks (BR-69–98). Source may have a partial or merged implementation; verify exact commit instead of relying on contradictory older snapshots.
5. **Task verification and Incident handling:** optional Before Photo, Incident After Photo *or* Completion Note, TaskType AUTO/MANUAL rules and mandatory MANUAL overrides, cumulative rejection/rework/reassignment. Mitigation differs from verified closure (BR-58–68, BR-99–113).
6. **Workforce:** check-in window, published 4/6/8-hour shifts, Zone coverage and legal weekly-hour limits, SWAP/COVER timing plus Manager-assisted paths (BR-139–161). The old 48-hour hard rule is historical implementation behavior, not current BR-143's universal requirement.
7. **AI measurement quality and privacy:** no customer identification/cross-camera dedup; unknown, missing, stale, ambiguous or insufficient measurements must not produce operational facts. Validate waiting time, staffed checkout and source coverage with recorded evidence (BR-07–14, BR-22–23).
8. **Notifications/exports/analytics/NFR:** separate Event/Incident KPIs, dismissed-Incident exclusion, advisory-only recommendations, auditable actions and qualitative quality attributes. Record representative staging results; never promise invented fixed performance/accuracy numbers (BR-162–165; Report 3 §4).

## 33.1 Historical implementation reconciliation — 11/11/2026

The 11/11 code/Database snapshots in §§36–43 document real increment history, not automatic acceptance of the updated SRS. Previously recorded seeds had four OperationalEvent categories (Long Queue, Excessive Waiting, Congestion and Checkout Capacity) and five staff-report Incident categories. **FALLEN_PERSON is now the single additional approved AI Incident exception**, with TT-16, under current BR-49–53.

Previous SQL/EF code supported generic INCIDENT `MonitoringRule.outcome_type` and `Incident.monitoring_rule_id`, and a runtime gate `INCIDENT_RULE_RUNTIME_UNSUPPORTED`. This is a specific **schema/API migration/implementation gap**; do not repurpose older generic incident routing for the new model without approved database design.

Previously recorded configuration included EventType-to-policy mappings and warning percentages/response targets, plus legacy source-specific assumptions about queue detection and weekly working hours. Such numbers describe dated implementation/configuration, not permanent SRS NFR service-level promises. Official timing obligations come from current BRs and approved policy configuration.

Consult [API.md](API.md), [VALIDATION.md](VALIDATION.md), current tests and the Database repository before asserting that any of these gaps are implemented. Do not rewrite the frozen BM-04 or Registration Form to hide a new gap.

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

Do not mix camera-frame ROI with floor polygon. Do not treat camera health as a shopper Incident. Do not derive physical density from pixel area. Do not confuse a generated source scaffold with proof that a shared/deployed database is migrated.

---

# 36. Current implementation checkpoint — StaffReport feature branch, 11/11/2026

**Dated 11/11/2026 observations from then-inspected source and API/VALIDATION; not a current run or proof of 19/11 SRS compliance:**

| Area | Code evidence | Status/caveat |
| --- | --- | --- |
| .NET 10 N-layer backend | `src/Supermarket.Api`, `Application`, `Domain`, `Infrastructure`, tests | **Present**. |
| Core setup/auth/camera/monitoring APIs | Current Controllers and README/API | **Present in repo**; do not infer live feature acceptance. |
| Preview/recorded-video/monitoring worker | AI clients, `MonitoringWorker`, camera workers | **Present in repo**, specialized count/queue subset. |
| OperationalEvent episodes | `MonitoringIncidentWriter`, scaffolded `OperationalEvent`, targeted tests | **Implemented subset**; Episode behavior detailed §40. |
| V7 persistence scaffold | Database scripts V9–V16, generated EF entities, `Scaffold.ps1` | **Aligned in current source** for SLA, specialty, TaskOffer, StaffReport and standalone TaskType policy fields. The named target database still needs direct verification. |
| Historical V7 rule outcome and provenance | Scaffolded MonitoringRule, OperationalEvent and Incident writer/tests | **SRS gap as of 19/11**: generic INCIDENT rule target was coded, but current BR-16–19 permit only OperationalEvent rules. Do not enable old routing. |
| AI FALLEN_PERSON exception | Historical monitoring writer/runtime | **Not established as implemented**: current BR-49–53 require independent validated FALLEN_PERSON evidence, source/idempotency and immediate TT-16 dispatch, not generic MonitoringRule INCIDENT outcome. |
| Incident and standalone Task SLA | `IncidentSlaPolicyService`, `StandaloneTaskSlaPolicyService`, policy workers/tests | Exact IncidentType or TaskType match, immutable timing snapshots and continuous wall-clock deadlines are present. Missing/ambiguous/invalid policy fails closed. |
| OperationalEvent SLA actions/milestones | Event policy/action services, `SlaWorker`, focused tests | EventType-only matching; alert-conditioned actions; warning/breach notifications for open Tasks/Incidents. This does not imply complete analytics/reporting. |
| MF-02 operational Task offers and Staff responses | `StaffWorkController`, `StaffTasks`, `StaffWork`, `TaskOfferWorker`, `StaffTaskTests` | **Target-Zone subset implemented**: specialty/check-in/Zone/workload eligibility, Handle Now/Later, Cannot Handle, atomic first acceptance, expiry/retry, no-active-duplicate guard and Operator exception/manual assignment. Cross-Zone ring expansion remains open. |
| StaffReport and AI-assisted draft | `StaffReportsController`, `StaffReportWork`, draft client, StaffReport tests; [`docs/API.md`](API.md) | Staff confirms category, IncidentType/TaskTypes and report content. Confirmed Incident reports create sourced Incident/required Tasks when policy is valid; routine reports create TaskType-policy Tasks; duplicate/overlap decisions are explicit. AI draft suggestions are editable and not persisted server-side. Live OpenAI/Cloudinary and cross-client acceptance are not verified. |
| Incident closure | Task verification flow and API contract | Verifying a linked Task closes the Incident in the same transaction only when every mandatory linked Task is verified; cancelled mandatory work blocks auto-closure. See API contract and dated validation before asserting runtime acceptance. |
| MF-04 SWAP/COVER and shift planning | `ShiftSwaps`, `ShiftPlanning`, controllers, workers and tests | **Merged to `dev`** with the bounded behavior in §42. |
| Camera playback/editing | `CameraPlayback`, `MediaMtxGateway`, `CameraSetup`, camera tests | Dynamic MediaMTX playback paths, sanitized WHEP URLs and preserve-password editing are present; see §43. |
| MF-03 Task evidence and verification | `TaskEvidenceWork`, `TaskVerification`, Task routes/storage and focused tests | Evidence-or-note, AUTO/MANUAL selection and fail-safe metric verification are implemented. Operational Event TT-01–TT-05 may AUTO verify only from a valid recovered Event metric and a non-`CRITICAL` SLA Response Priority; Incident, SLA-`CRITICAL`, TT-07 and TT-12–TT-15 remain MANUAL. Rejection two automatically continues D1 dispatch; a rejected replacement stops at Operator. Automatic Incident closure remains separate. |
| Manager analytics API (WF-05 / FE-09–10 subset) | `ManagerAnalytics`, `EfAnalyticsQueries`, Manager analytics controller | Read-only overview, daily cohorts and Zone aggregates for recorded Event/Incident/Task data. Task SLA reporting uses stored deadlines. [Contract/KPI definitions](MANAGER_ANALYTICS_API.md); UI, export, recommendations, staging latency and end-to-end acceptance remain open. |
| `SystemSetting`, `ServiceHealthEvent`, Zone staffing fields | Scaffold and focused application reads | MF-02 consumes two retry settings; Admin manages four approved `notification.*` keys. Other settings and service-health workflows require separate verification. |
| Deployment health / CI results | Historical README, workflow and VALIDATION docs | **Dated evidence only**, no live deployment check performed for this synchronization. |

Do not present a passing test score, deploy availability, migration state for a named database or end-to-end acceptance without running/verifying it.

---

# 37. Final goal of MF-01

A reviewer sees: authorized Admin prepares floor/zone/camera/ROI and monitoring configuration → preview/test/activate → continuous monitoring and health → handles a realistic failure/recovery. All actor-visible steps work with current configured sources, are documented, and have test evidence. This is distinct from MF-02 independent Event and Incident execution and later Staff/Manager workflows.

---

# 38. Current approved decisions and superseded models

## Superseded — do not reintroduce as business requirements

- Any `OperationalEvent → Incident` conversion, escalation or reference; CRITICAL Alert Level alone never qualifies an Incident.
- Any `MonitoringRule` with `INCIDENT` outcome, an IncidentType target, or a generic camera safety-incident classifier; the earlier V7 schema/code can remain historical until reviewed migration.
- Requiring an invented StaffReport for AI FALLEN_PERSON, or AI Incident creation for any other IncidentType.
- Universal Operator pre-dispatch acknowledgement, Scenario-derived arbitrary SLA targets, auto-verification from free-text claims, self-verification, or automatic AI recommendation actions.
- Separate TaskSeverity or Incident Severity/defaultSeverity, `ZoneShiftRequirement`, `NotificationConfig`, and unauthorized new persistence tables.
- The obsolete 38-rule ID set, 4-Mainflow organization or hard-coded historic weekly limit as the current SRS baseline.

## Approved 19/11/2026 target

- Exactly **165 BRs** in current Report 3 §5.1; all traceability must refer to those numbers.
- Five official workflows WF-01–WF-05.
- Camera AI → validated enabled OperationalEvent-only MonitoringRule; separate Staff-confirmed StaffReport → Task/Incident.
- Sole validated AI exception: FALLEN_PERSON → Incident directly + source/evidence + dedup + immediate TT-16; no StaffReport/OperationalEvent.
- Event Alert Level independent of SLA Response Priority; policy/action versions bound to episodes; continuous SLA clocks and configuration exceptions, no silent defaults.
- Seven StaffSpecialties and sixteen TaskTypes in approved catalog; specialty/shift/Zone-aware ring dispatch with atomic acceptance and Operator exhaustion handling.
- MANUAL verification overrides and TaskType-specific AUTO conditions; mitigation and Incident closure are separate.
- Zone minimum coverage; regular SWAP/COVER and Manager-assisted/direct eligible adjustments; legally compliant weekly-hour limits.
- Distinct Event/Incident/Task/Staff analytics, dismissed-Incident KPI exclusion, no revenue/POS reporting, no continuous video storage.
- Qualitative non-functional service expectations, validated using documented staging measurements rather than invented fixed numeric commitments.

The historical source and Database observations in §§36–43 remain useful evidence but **do not supersede** the above business target.

---

# 39. Assigned ROI monitoring increment — implementation snapshot, not target design

The inspected implementation uses camera-local tracked observations, ROI footpoints and a narrowed measurement set (people count / queue length). In the earlier increment, count-based congestion is expressed through `PEOPLE_COUNT` parameters, **not** density computed from screen area. Waiting-time and checkout-utilization full runtime require further measurement work. The runtime previously required exactly one ACTIVE camera mapping per Zone and does not provide automatic overlapping-camera deduplication.

A backend-owned worker consumes batches independently of a viewer. It must recheck current configuration/source mapping before persistence. Gaps/reconnect/config changes can reset sustain continuity; unavailable data must not be interpreted as a valid zero. Recorded video can complete at EOF and is not an automatically looping production stream. Preview is read-only relative to monitoring-owned runtime and does not itself generate production measurements.

**Historical code gap:** `MonitoringIncidentWriter` once supported both OPERATIONAL_EVENT and INCIDENT persistence, with runtime `INCIDENT_RULE_RUNTIME_UNSUPPORTED` gating generic INCIDENT rules. This is not current SRS permission; use only OPERATIONAL_EVENT MonitoringRules and separately implement approved FALLEN_PERSON detection → Incident + TT-16 with the Database/AI contract reviewed.

---

# 40. OperationalEvent episode implementation — evidence through inspected `dev`

The V7 active episode identity is:

```text
(MonitoringRule, Zone)
```

Database V9 and the generated scaffold define filtered unique index `UX_OperationalEvent_Active_Rule_Zone` for rows where `cleared_at IS NULL AND rule_id IS NOT NULL`. Writer lookup uses the same Rule+Zone key. The implemented episode behavior is:

```text
first sustained breach       → open one active OperationalEvent
subsequent observations      → update its metric + last_observed_at
WARNING → CRITICAL          → escalate same row, no automatic downgrade
valid sustained recovery     → mark CLEARED and set cleared_at
new breach after clear       → start a new episode
missing/unavailable readings → do NOT synthesize zero or clear automatically
```

Recovery/hysteresis, source capture timestamps, selected camera and stale-config protection require the exact implementation/tests to be checked for every change. This implementation checkpoint **does not** authorize Event-origin Incident generation. The scoped Task-offer and MF-04 implementations in §§41–42 do not imply complete SLA administration, external push delivery, historical KPI completeness or a verified deployed database migration.

---

# 41. MF-02 Staff-task implementation subset — V7 TaskOffer integration

The current feature branch implements this bounded V7 workflow. It is source/build evidence, not the complete Post-Review 2 target:

```text
AUTOMATIC CREATE_OPERATIONAL_TASK policy
→ resolve TaskType and RequiredSpecialty
→ create TaskOffer rows for eligible checked-in/on-shift Staff with matching specialty and capacity
→ expand SAME_ZONE → adjacent same-Floor Zones → remaining same-Floor Zones → nearest Floors → remaining Floors
→ HANDLE_NOW / HANDLE_LATER / CANNOT_HANDLE
→ first valid acceptance wins atomically
→ expire PENDING offers, advance one topology ring without repeating Staff, then flag for Operator assignment after exhaustion
```

Runtime offer truth is `TaskOffer` (`PENDING/ACCEPTED/DECLINED/EXPIRED/CANCELLED`). `TaskAssignmentHistory` is audit/current-assignment history only. The branch also contains Operator candidate lookup/manual assignment and preserves accepted Tasks when an Event clears while cancelling unaccepted offers. Exact routes and dated validation evidence are in [`docs/API.md`](API.md) and [`docs/VALIDATION.md`](VALIDATION.md).

When a Task has an SLA policy, `offer_timeout_sec` controls TaskOffer expiry; otherwise `task_offer.retry_interval_sec` falls back to 60 seconds. The legacy `task_offer.max_retry_count` key is retained for configuration compatibility but no longer controls the fixed five-ring topology. `NotificationConfig` remains retired.

### StaffReport and standalone Task paths in the current feature branch

- `StaffReport.report_category` distinguishes `ROUTINE_REQUEST` from `POTENTIAL_INCIDENT`; `incident_type_id` is nullable only for routine reports. V12–V15 added this persistence/workflow support; the recorded database application was `FA26SE103_Dev`, not production.
- For `POTENTIAL_INCIDENT`, Staff confirms the IncidentType and permitted TaskType codes. The backend validates them against the active catalog and IncidentType mapping, persists the confirmed report, and creates a sourced Incident plus required corrective Tasks when the matching SLA policy is valid. Duplicate choices are `ALREADY_REPORTED` (no new Incident/Tasks) or `DIFFERENT_INCIDENT` (allow a distinct Incident). `EXPOSED_ELECTRICAL_WIRE` is accepted only as Staff-confirmed Safety Hazard with TT-12 and TT-13; AI output alone cannot trigger that combination.
- For `ROUTINE_REQUEST`, Staff confirms an eligible TaskType; the standalone TaskType SLA path snapshots the policy and creates/dispatches the Task. An open same-Zone/TaskType overlap requires `ALREADY_COVERED` or `SEPARATE_TASK`; no silent merge.
- The StaffReport-based Incident/Task path is distinct from the **current approved FALLEN_PERSON AI exception**; the old MonitoringRule-originated Incident code is historical and must not be reused as general AI Incident routing (BR-16–19, BR-49–53).
- The draft endpoint accepts text, JPEG/PNG images and bounded voice recordings. Backend calls configured OpenAI for suggestions and voice transcription; it validates suggestions against catalogs and only proposes explicitly named Zone candidates. Drafts are not persisted server-side, model confidence is uncalibrated, and Staff must review/confirm every field. AI never creates or dispatches the report's Incident/Task.
- Submitted image/audio media uses the existing authenticated Cloudinary storage path; `StaffReportMedia.storage_key` stores the asset identifier, not credentials or image/audio bytes. Live OpenAI/Cloudinary calls and full Mobile acceptance have not been verified. Exact limits, DTOs, routes and errors are in [`docs/API.md`](API.md).

**25/10 mapper correction and SQL check:** removed obsolete Incident-qualification fields from the domain `ResponsePolicyAction` to match the approved V6 scaffold; the EF mapper now rejects incompatible domain properties explicitly. Two mapper regression tests and the non-SQL suite pass (340 tests). A rollback-only check against the configured 37-table Development database verified application check-in, evaluator → Event → Task/offer/IN_APP notification, retry, acceptance/start, priority escalation and recovery. Measurements were synthetic; camera/AI capture, HTTP/device delivery and concurrent acceptance were not exercised. The rollback retained the original DB configuration: the active rule had no response policy, camera/connection were inactive/disabled, and no current checked-in Staff covered the rule Zone. See the dated [validation entry](VALIDATION.md#mf-02-v6-task-mapper-and-live-sql-rollback-proof--2026-10-25).

**25/10 Operator integration continuation:** `9f8488b` split existing services/controllers by Staff vs Operator ownership with unchanged routes/namespaces. StaffTasks, OperatorTasks and TaskViews now have separate files; StaffMeController, TaskOffersController and TasksController likewise. Operator assignment accepts optional `expectedUpdatedAt` (409 TASK_CHANGED for a stale view); reassignment or outside-Zone assignment requires an audit reason. The companion web Task Board is implemented in the Front-End feature branch; rollout and mobile acceptance remain separate. Latest backend verification: 343 non-SQL tests, 15 actual local HTTP/auth checks, and rollback SQL proof including manual assignment/reassignment, history and notification. No permanent shared-data/schema change was made.

**Historical 25/10 open-list (superseded where later work merged):** policy administration/version-management UX, cross-Zone ring expansion, no-fixed-retry dispatch, and complete analytics were then open. Check actual current branch and current BR-69–98 before asserting status. Operational-event SLA action execution, SLA warning/breach milestones, StaffReport confirmation, and mandatory-task Incident closure are present in current source/API contracts; do not infer live provider, cross-repository or deployment acceptance from that. Task evidence/verification and MF-04 capabilities remain separate increments. See §33.1 for the single ZoneAdjacency decision rule.

---

# 42. MF-04 SWAP/COVER implementation subset

The MF-04 work merged into `dev` implements the shift-swap approval flow on the post-cleanup schema; V7 weekly-work-shift changes are described below:

```text
Staff A: SWAP (exchange with one of B's shifts) or COVER (B takes A's shift) on a future ShiftAssignment
→ PENDING_PEER → Staff B accepts (PENDING_MANAGER) or declines (DECLINED_BY_PEER)
→ Manager APPROVED / REJECTED (rejection reason required)
→ only approval updates ShiftAssignment.user_id, atomically for both rows
```

The 29/10 mentor clarification adds two Manager-owned exception paths without a schema change. A Staff member who cannot secure a peer can create a COVER request directly in `PENDING_MANAGER` with no target; the Manager then selects an eligible Staff member. A Manager can also directly COVER one future slot or SWAP two future slots. Direct actions require a reason, re-check hard eligibility, cancel conflicting pending requests, persist an APPROVED `ShiftSwapRequest` as audit, and notify affected Staff. They are explicit Manager decisions, not automatic optimization.

- A cancels while pending; B withdraws only while awaiting the Manager; actor, time and reason use the V5 cancellation columns.
- `expires_at` is the earliest affected local shift start minus 24 hours, stored in UTC. A background worker sets EXPIRED; reads and actions already treat past-deadline requests as expired.
- Historical 11/11 implementation rechecked active Staff, future/check-in/peer deadlines, one pending request per shift, overlap, unavailability and Zone coverage. It applied a hard 48-hour weekly limit and a 40-hour recommendation; **the current SRS BR-143 instead requires applicable Vietnam Labour Code 2019 Article 105 compliance**, so inspect/adjust policy configuration rather than treating 48 hours as a universal approved BR.
- `SPECIALTY_COMPATIBLE` additionally enforces matching assigned primary specialty in that shared rule path, including Manager candidates/direct intervention. Missing specialty blocks transfers until account configuration is corrected. This uses existing V7 `UserAccount.specialty_id`; no schema or DTO change.
- Notifications are in-app only. `Notification` has no swap-request reference column, so clients open the caller's request list.

Manager shift planning has subsequently been extended by Database V7 Weekly Work Shifts:

- `Shift` rows were Work Shifts of 4, 6 or 8 hours (current BR-139, historical code evidence), scoped to one local Monday–Sunday week by `week_start`; names are unique per supermarket/week. Times are locked while assignments use them.
- A Manager can copy ACTIVE Work Shifts from one week into an empty target week. This copies definitions only, never Staff assignments or publication state.
- Every assignment date must belong to its Work Shift week. Schedule coverage uses only the Work Shifts of the viewed day/week, preserving historical published assignments after later weeks change.
- `ShiftAssignment.status` is `DRAFT` while the Manager plans (hidden from Staff, no check-in, no swap), `SCHEDULED` once a date range is published, and `CANCELLED` when removed. Pre-existing rows already carry `SCHEDULED`.
- Publishing is refused while any future shift leaves an ACTIVE Zone below `Zone.min_staff` (current BR-144/148). Assignment edits and availability must meet current BR-140–149; old BR-02/BR-32/BR-33 references are invalid after the 165-BR split.
- Only active `STAFF` accounts can be scheduled. Scheduling remains Role/availability based; operational task dispatch on the current feature branch additionally requires the Staff primary Specialty to match the TaskType RequiredSpecialty.
- Cancelling an assignment also cancels pending swap requests on it. `StaffUnavailability` is Manager-maintained leave and is refused while the person still has an unfinished shift in that period.

Not implemented in this source snapshot: automatic recurring assignments, Staff self-service leave requests, a `SystemSetting` override for the weekly limit, and verified FCM delivery. Routes and validation evidence are in [`docs/API.md`](API.md) and [`docs/VALIDATION.md`](VALIDATION.md).

---

# 43. Recent MF-01 camera integration changes (22/10/2026)

Inspected `dev` includes three camera-focused updates after the prior context baseline:

- LIVE/RTSP browser playback provisions a stable `cam-{cameraId}` MediaMTX path through the internal Control API and returns a sanitized WHEP URL; clients no longer require build-time camera-path mappings.
- Editing a connection may set `preservePassword: true` to keep the encrypted stored password when the password field is intentionally blank. Responses expose only `hasCredentials`, never the password or secret reference.
- Stream failures are classified so transport/source errors are not mislabeled as camera visual-health failures; AI-service unavailability remains processing health, not an operational Incident.

These are source/test observations, not live deployment claims. See [`README.md`](../README.md), [`docs/API.md`](API.md) and camera-focused tests for current routes and configuration.

---

> **Maintenance note:** When this file is committed, update stale conflicting rules in root `AGENTS.md` and `README.md` in the **same reviewed PR**. Preserve the `AGENTS.md` instruction to read this guide in full. Update this synchronization date only after re-reading changed official documents and relevant code; do not append another superseding addendum to the top of the file.
