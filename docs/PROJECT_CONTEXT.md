# PROJECT_CONTEXT.md — FA26SE103 Implementation Snapshot

> **Project:** AI-Powered Smart Supermarket Operations Monitoring System  
> **Project code:** FA26SE103  
> **Group:** GFA26SE39  
> **Purpose of this file:** provide a repository-owned synchronized implementation snapshot so a backend-only clone gives humans and coding agents one implementation-oriented view of the current agreed product, Mainflows, domain rules, architecture, and implementation scope before code is written.
>
> **Synchronization baseline:** project decisions and mentor/team feedback through **10/10/2026**, including the supplied Codex synchronization notes and provisional `FA26SE103_ERD_v4.drawio`.
>
> **Implementation-state warning:** ERD v4 and the 10/10/2026 synchronization notes describe the provisional target architecture. The current SQL Server development database, EF scaffold, APIs, and workers remain implementation state and do not automatically satisfy the target model.
>
> **Important:** this file is a synchronization guide, not a replacement for the official reports. If an official requirement changes, update this file together with the affected requirement/design artifact.

---

## 0. Read this first

The project is **not** a generic supermarket ERP and **not** a security-surveillance product.

The product is an **AI-assisted supermarket operations monitoring system**. Its target closed loop is:

```text
Camera / Video Stream
        ↓
AI Detection & Tracking
        ↓
Operational Measurement
        ↓
MonitoringRule
        ↓
OperationalEvent
        ↓
ResponsePolicy / ResponsePolicyAction
        ↓
Notification / Recommendation / Operational Task / qualifying Incident
        ↓
Staff Handling / Evidence / Independent Verification
        ↓
Historical Analytics
```

Staff reports are a separate confirmed input path. A routine `StaffReport` does not become an AI `OperationalEvent` merely because it may later create a Task.

The system focuses on supermarket operations such as queues, waiting time, crowding, checkout utilization, staff response, incident resolution, and operational reporting.

Do not silently expand the scope into POS, inventory, sales analytics, payroll, customer identification, theft detection, face recognition, or cross-camera person re-identification.

---

# 1. Source of truth and document alignment

## 1.0 — 10/10/2026 synchronization addendum (read first)

This addendum supersedes older Incident-centric wording elsewhere in this historical implementation snapshot. It records the latest approved target direction without claiming that the current code or database already implements it.

### Source-of-truth precedence

Resolve conflicts in this order:

1. Latest confirmed mentor feedback and explicit team decisions.
2. Current approved Business Rules and revised Reports 1–3.
3. Provisional ERD v4.
4. Current Mainflow specifications and detailed design documents.
5. Existing code, migrations, and live database metadata as implementation-state evidence.
6. Older diagrams, prototypes, and assumptions.

Do not silently resolve a material conflict. Record it and request clarification when implementation would change approved behavior. External documents were not independently re-read during this local synchronization; rely only on the supplied notes for their stated changes.

### Target operational architecture

```text
Camera / Video Stream
→ AI Detection & Tracking
→ Operational Measurements
→ MonitoringRule evaluation
→ OperationalEvent
→ ResponsePolicy
→ ResponsePolicyAction
→ Notification / Recommendation / Operational Task / qualifying Incident
→ Staff Task Handling
→ Independent Verification
→ Historical Analytics
```

`OperationalEvent` and `Incident` are different concepts. A threshold breach or CRITICAL OperationalEvent does not automatically create an Incident. Warning-to-Critical escalation remains within the same OperationalEvent episode; Incident creation requires separate safety/material-disruption qualification.

Baseline AI OperationalEvent types are `LONG_QUEUE`, `EXCESSIVE_WAIT`, `CONGESTION`, and `CHECKOUT_CAPACITY`. Specialized hazard detection must be explicitly validated, not assumed.

### Response and staff-report model

- `ResponsePolicy` is the response configuration selected for a MonitoringRule and OperationalEvent priority.
- `ResponsePolicyAction` is a reusable configured instruction, not a Task. Supported action types are `NOTIFY`, `GENERATE_RECOMMENDATION`, `CREATE_OPERATIONAL_TASK`, and `QUALIFY_INCIDENT`; actions may be `AUTOMATIC` or `APPROVAL_REQUIRED`.
- Do not add a separate `ResponseActionExecution` entity for the MVP.
- `StaffReport` is a separate confirmed staff-submission path for routine issues and potential safety/corrective Incidents. Staff review and confirm AI-extracted fields before submission. Do not fabricate an OperationalEvent for a routine staff report.
- A Task has exactly one origin: `OPERATIONAL_EVENT`, `INCIDENT`, `STAFF_REPORT`, or `MANUAL`. The corresponding source FK is exclusive; MANUAL has all three source FKs null.
- `Incident.staff_report_id` is nullable and unique. `OperationalEvent.incident_id` is nullable; multiple OperationalEvents may reference one Incident, while each OperationalEvent references at most one Incident.
- Tasks support dispatch, acceptance, execution, evidence, independent verification, rejection/rework, and closure. First-valid-acceptance-wins is atomic; eligibility considers checked-in/on-shift status, assigned zone, role, and availability. Do not universally require before photos for routine adjustments.

### ERD v4 provisional target

The supplied ERD v4 contains approximately 34 entities and 66 relationships, including the existing camera/setup entities plus `Shift`, `ShiftAssignment`, `StaffZoneAssignment`, `OperationalEvent`, `IncidentType`, `Incident`, `IncidentMedia`, `Task`, `TaskAssignmentHistory`, `TaskEvidence`, `Notification`, `NotificationRecipient`, `NotificationConfig`, `Recommendation`, `ZoneMetric`, `AuditLog`, `ZoneShiftRequirement`, `ZoneAdjacency`, `StaffUnavailability`, `StaffReport`, `OperationalEventType`, `ResponsePolicy`, and `ResponsePolicyAction`.

The draw.io XML was inspected for table vertices and relationship count. Full visual rendering/import was not independently validated; do not claim visual validation without inspecting the rendered diagram.

ERD v4 is a target baseline, not proof of migration. The last live database inspection found only 14 physical user tables and no `ZoneAdjacency`, `StaffReport`, `Task`, `ResponsePolicy`, or `OperationalEventType` table. Do not run migrations or modify the live database as part of context synchronization.

### Open refinements

These are not permission to invent schema:

- SLA configuration, deadlines, breach and escalation semantics;
- threshold/policy provenance and reproducibility;
- concurrency and idempotency constraints for Task acceptance, Event episodes, action outcomes and retries;
- durable StaffReport image/voice references and retention;
- optional direct `Task.response_policy_action_id` traceability, which is not approved;
- any additional behavior whose physical representation is not settled.

## 1.1 Current authoritative project documents

Use these documents as the project baseline:

1. **Report 1 — Project Introduction**
   - Defines product scope, FE-01..FE-12, actors implied by features, limitations and exclusions.
   - Current Drive source:
     `https://docs.google.com/document/d/1C3BnR_XkLTMWYbnAumNLsyNiraRVloaycCHzkTbdhIs`

2. **Report 2 — Project Management Plan**
   - Defines WBS, Mainflow-based incremental development, quality/testing expectations, stack, deployment, and project process.
   - Current Drive source:
     `https://docs.google.com/document/d/18o3oA7NQgxV8eJxstP2mQgRGRoqkq_d9e-MnHuxQfTI`

3. **GFA26SE39_Final_Agreed_Mainflows_2026-10-02.docx — current agreed Mainflow baseline**
   - Defines the latest agreed MF-01..MF-04 flow semantics and slide/swimlane behavior.
   - In particular, it freezes the MF-02 response/qualification flow, evidence verification in MF-03, and the independent Planning vs Analytics entry paths in MF-04. Do not infer mandatory Operator pre-dispatch review for every confirmed report or AI-qualified Incident.
   - The repository does not currently record a Drive URL for this document; do not invent one. Add it only when the team supplies the URL.

4. **FA26SE103_Business_Rules.docx — current working detailed BR**
   - Defines 40 consolidated business rules covering camera/zone behavior, OperationalEvent vs Incident, StaffReport, ResponsePolicy actions, Task origins, routing, shifts, calculations, scope rules, and lifecycles.
   - Current Drive source:
     `https://docs.google.com/document/d/1ODHDSkK0pLCcbKVS2nl3G0KhxIz6Q5PR/edit?usp=drivesdk`

5. **FA26SE103_ERD_v4.drawio — provisional target persistence baseline**
   - Use the supplied ERD v4 and the 10/10/2026 synchronization notes for target-domain reasoning. ERD v4 is provisional and does not prove that the live database or EF scaffold has been migrated.
   - Local reference inspected for this synchronization:
     `D:\Study\FA26\SEP490\Diagrams\FA26SE103_ERD_v4.drawio`
   - The draw.io XML contains approximately 34 table entities and 66 relationships. Full visual rendering/import was not independently confirmed.
   - Historical ERD v3 and earlier diagrams remain implementation/traceability references only.
   - **Do not change team-owned ERD/database artifacts unless you are assigned to ERD/database work.**

6. **Report 3 — Software Requirement Specification**
   - Currently being prepared.
   - The current Drive URL is not recorded in this repository; add it only when the team supplies the URL.
   - It will formalize Product Overview, Actors, Use Cases, System Functional Overview, Screen Flow, Screen Authorization, Non-Screen Functions, ERD/entity descriptions, detailed functions, NFRs, BRs, common requirements, and messages.

7. **Canva Mainflow / Review deck**
   - Presentation/design reference with lower authority than the agreed BR/Mainflows:
     `https://www.canva.com/design/DAHWviilNDA/WQWyS79Eao8V8pXqkgt9Vg/edit`

## 1.2 Conflict rule

Do not guess when artifacts conflict.

Current working precedence:

```text
Latest explicit mentor/team decision
        ↓
Latest agreed Business Rules clarification
        ↓
Current Report 1 scope/features
        ↓
Current Report 2 implementation/project plan
        ↓
Provisional ERD v4 target model
        ↓
Current Mainflow specifications and detailed design documents
        ↓
Existing code, migrations and live database metadata as implementation-state evidence
        ↓
Older diagrams, prototypes and assumptions
```

Reports 1 and 2 were updated on 10/10/2026, and the canonical Business Rules contain 40 consolidated rules. Do not return to older 43-rule or Incident-centric interpretations. Conditional review and routing must follow the current target architecture in the synchronization addendum; CRITICAL priority alone is insufficient to qualify an Incident.

If code and a document disagree, raise the mismatch before adding more behavior.

---

# 2. Product boundary

## 2.1 In scope

The system supports:

- one supermarket branch for project evaluation, provided as a single default store (no store setup step);
- Floor / Zone configuration inside that default store;
- camera registration, mapping, stream configuration, testing, preview, and health;
- AI person detection and multi-object tracking;
- customer flow;
- queue length;
- waiting time;
- crowd analysis;
- checkout utilization;
- heatmaps;
- OperationalEvents;
- AI-detected and staff-reported Incidents;
- notifications;
- staff task acceptance and assignment;
- completion evidence;
- Operator verification;
- staff shifts and zone assignments;
- operational dashboard;
- historical operational analytics;
- AI-assisted operational recommendations;
- users, roles, permissions, audit/system administration.

## 2.2 Explicitly out of scope

Do not implement these unless requirements are formally changed:

- multi-branch production operation;
- POS/payment integration;
- inventory/ERP integration;
- revenue/sales/best-selling-product analytics;
- customer identity tracking;
- face recognition;
- cross-camera re-identification or person deduplication;
- theft detection;
- fall/violence detection;
- empty-shelf detection;
- payroll;
- recruitment;
- contracts / full HR;
- fully autonomous store-operation decisions;
- continuous video archival as a normal user feature.

Recorded/controlled video is allowed for **testing, AI evaluation, and fallback demonstration**. Live camera streams are the intended primary operational source.

---

# 3. Actors and roles

There are four system roles:

```text
ADMIN
OPERATOR
MANAGER
STAFF
```

## Admin

Responsible for system/configuration administration:

- user accounts and roles;
- floor/zone setup inside the single default store;
- camera configuration;
- camera connections;
- camera-zone mappings;
- AI/monitoring configuration;
- incident-type configuration;
- system health/configuration.

Only Admin creates accounts. Admin may create/edit/enable/disable user accounts, including Staff accounts, subject to authorization and lifecycle rules.

Account/Staff CRUD is a **supporting Admin capability**. It may be demonstrated separately when needed, but it is not a required box in the four Mainflow diagrams.

The **last active Admin account must not be disabled or removed**.

## Operator

Responsible for live operational coordination:

- monitors incidents and tasks;
- may inspect, intervene in, cancel, or dismiss incidents;
- dismissing requires a reason;
- can manually assign/reassign tasks;
- can override normal dispatch constraints only with a written reason;
- verifies submitted work;
- cannot be the same person who performed the task being verified.

Operator coordination remains available for intervention, manual assignment, reassignment, dismissal, and override. Response-policy actions may require approval, but do not encode a universal pre-dispatch review rule for every confirmed report or AI-qualified Incident.

## Manager

Responsible for operational planning and management:

- plans shifts;
- assigns staff to zones/shifts;
- handles schedule rules;
- receives certain escalations;
- uses analytics/reporting;
- reviews AI-assisted operational recommendations;
- makes final operational decisions.

AI recommendations do not automatically change store operations.

## Staff

Responsible for field execution:

- checks in to shifts;
- receives eligible task/incident alerts;
- accepts tasks;
- performs work;
- submits evidence;
- reports incidents directly;
- confirms AI-assisted report drafts before submission.

---

# 4. The four Mainflows

These are the shared end-to-end product Mainflows. Internal AI steps are components of a Mainflow, not separate Mainflows.

---

## MF-01 — Setup & System Configuration

**Primary actor:** Admin  
**Supporting concerns:** camera stream adapter, backend, database, AI configuration, health monitoring

### Goal

Prepare the floor/zone structure of the default store, cameras, camera-zone coverage, connection settings, and monitoring configuration so the system can safely enter operational monitoring.

### Main path

```text
Admin Login
    ↓
Set up Floor / Zone
    ↓
Register & Configure Camera
    ↓
Test Connection & Preview
    ↓
Map Camera to Zone(s) / ROI
    ↓
Configure Monitoring Rules
    ↓
Activate Monitoring
    ↓
System continuously checks Camera / Stream Health
```

`Test Connection & Preview` is an **Admin-triggered action**; the backend/camera adapter performs the actual connection test and returns success/error/preview. `Enable Camera Connection` remains an implementation state/precondition after successful validation, but it does not need to appear as a separate Mainflow slide box.

### Camera-health exception path

```text
Monitoring active
    ↓
Camera transport becomes OFFLINE, or the stream becomes visually unusable
    ↓
CameraHealthEvent / health issue reported
    ↓
Admin investigates and restores connection/configuration
    ↓
Admin retests
    ↓
System returns to continuous Camera / Stream Health Check
```

Camera/stream health is **system health**, not a supermarket Operational Incident.

### MF-01 configuration responsibilities

MF-01 should establish:

- authentication/RBAC baseline;
- Floor (inside the single default Supermarket record);
- Zone;
- Camera;
- CameraConnection;
- CameraZoneMapping;
- MonitoringConfiguration;
- monitoring rules/settings required by the provisional ERD v4 target;
- CameraHealthEvent;
- basic account administration required to operate the system (**supporting Admin capability; not a required Mainflow slide step**).

OperationalEventType and MonitoringRule configuration are logically system setup in the target model. The current MF-01 code/database still use the older IncidentType-based implementation; do not claim v4 persistence until an approved SQL change and re-scaffold exist.

---

## MF-02 — AI Monitoring → OperationalEvent → Response → qualified Incident/Task

**Primary actors:** Operator, Staff  
**System actors/components:** camera stream, AI service, routing/notification services

### AI-generated implementation path

```text
Camera Stream
    ↓
Person Detection
    ↓
Model-confidence FILTER
    ↓
ByteTrack / Tracking
    ↓
Operational Measurement
    ↓
OperationalEvent
    ↓
Zone-specific active MonitoringRule
    ↓
Condition satisfies threshold + sustain time
    ↓
OperationalEvent episode / priority updated
    ↓
ResponsePolicy
    ↓
ResponsePolicyAction evaluation
    ↓
Notification / Recommendation / Operational Task / separately qualified Incident
    ↓
Staff handling and independent verification
```

### Staff-reported path

```text
Staff finds issue
    ↓
Report text / image / voice
    ↓
System / AI extracts report data
    ↓
Staff confirms / corrects extracted data
    ↓
StaffReport stored
    ↓
Routine issue → optional Task
Safety/material-disruption qualification → Incident → corrective Task(s)
```

### Response-policy fallback

For a configured Task-producing response action:

```text
Notify or offer a Task to eligible Staff in affected Zone
    ↓
no eligible Staff OR no valid acceptance after retry policy
    ↓
Retry / Escalate
    ↓
Operator Review / Intervene
    ↓
Manual Assign / Reassign
    ↓
Task Assigned
```

Current retry baseline remains:

```text
re-notify every 60 s
up to 3 attempts
then Operator manually assigns/reassigns
```

Critical incidents also inform the Manager on duty.

### Important distinctions

`Model confidence` is **not incident confidence**.

Model confidence only filters raw model detections before tracking/measurement.

Do NOT implement:

```text
YOLO confidence 64% → Operator review
YOLO confidence 92% → auto-dispatch
```

That design is obsolete.

Current logic:

```text
raw detection
→ model-confidence filter
→ tracking
→ operational measurement
→ MonitoringRule
→ OperationalEvent episode / priority
→ ResponsePolicy / ResponsePolicyAction
→ notification / recommendation / Task / separately qualified Incident
```

### Incident behavior

- OperationalEvent episodes are deduplicated according to the approved event policy; Warning-to-Critical escalation remains the same episode.
- A threshold breach or CRITICAL priority does not automatically create an Incident.
- `QUALIFY_INCIDENT` requires independent safety/material-disruption qualification.
- ResponsePolicyAction evaluation must be idempotent and avoid duplicate outcomes.
- Recommendations are advisory and may require human approval.
- Operator intervention remains available; do not require mandatory Operator pre-dispatch review for every confirmed report or AI-qualified Incident.

### Staff dispatch behavior

Eligible recipient:

```text
on shift
AND checked in
AND assigned to affected Zone
AND below active-task workload limit
```

Current default workload limit: **2 active tasks**.

If no eligible affected-Zone Staff exist:

```text
escalate to Operator for manual assign / reassign
```

If eligible Staff exist but nobody accepts:

```text
re-notify every 60 s
up to 3 attempts
then escalate to Operator for manual assign / reassign
```

First valid staff acceptance wins atomically.

### Slide / swimlane target baseline

```text
Camera path:
Camera Stream
→ Detection & Tracking
→ Operational Measurement
→ Rule Evaluation
→ OperationalEvent episode created / updated
→ ResponsePolicy / ResponsePolicyAction
→ Notification / Recommendation / Operational Task / qualifying Incident

Response action requires approval:
ResponsePolicyAction
→ Operator approval / intervention
→ configured output (notification, recommendation, task, or Incident qualification)

Automatic response action:
ResponsePolicyAction
→ configured output
→ eligible Staff handling when a Task exists

Staff report (separate path):
Find Issue
→ Report Text / Image / Voice
→ Extract Report Data
→ Confirm / Correct Extracted Data
→ StaffReport stored
→ optional routine Task, or separately qualified Incident and corrective Task(s)

Response-policy fallback:
Notify Eligible Staff
→ No Acceptance / Escalation
→ Operator Review / Intervene
→ Manual Assign / Reassign
→ Task Assigned
```

---

## MF-03 — Staff Task Execution & Evidence

**Primary actor:** Staff  
**Verification actor:** Operator

### Main path

```text
Task Offered
    ↓
Staff Responds: Handle Now / Handle Later / Cannot Handle
    ↓
Accepted (responsibility accepted)
    ↓
Start now OR scheduled/deferred start
    ↓
Staff completes work + evidence
    ↓
Submitted
    ↓
Operator verifies
    ↓
Verified
    ↓
linked Incident may close
```

### Rejection loop

```text
Submitted
    ↓
Operator rejects evidence/work
    ↓
In Progress
    ↓
Staff corrects + resubmits
```

After the configured maximum rejections (current default: **2**), reassign the task to another Staff member.

### Task response semantics

The Staff response must preserve the distinction between:

- accepting responsibility for the task;
- starting work immediately;
- accepting responsibility but scheduling/deferring the start;
- declining/cannot-handle response;
- being actively in progress;
- becoming overdue and requiring escalation or re-routing.

The supported response choices are:

- **Handle Now**;
- **Accept and Handle Later** with a configurable delay/start time;
- **Cannot Handle**.

`Cannot Handle` is a task response and must not be treated as evidence rejection. Its reassignment/escalation behavior must follow the configured business policy. Do not force deferred acceptance into `In Progress` without retaining the intended start time and responsibility state.

### Evidence rules

- every completed task requires an **after photo**;
- hazard and cleanliness incident types also require a **before photo**;
- only the assigned Staff member performs the task;
- only Operator verifies/reassigns;
- verifier cannot be the performer.

### Incident closure

An Incident closes only when **all linked tasks are verified**.

### Slide / demo baseline

```text
SYSTEM
Send Task Notification
        ↓
STAFF
View & Accept Task
→ Start Task
→ Perform Task
→ Submit Evidence
        ↓
OPERATOR
Review Evidence
    ├─ Valid → Task Verified
    └─ Rejected → STAFF Continue / Correct Work → Submit Evidence again
        ↓
SYSTEM
Update Task & Incident Status
```

`Review Evidence` and `Task Verified` are intentionally separate semantics: review is the Operator action; `Verified` is the successful task state. `Continue / Correct Work` belongs to Staff.

### Current SLA targets

```text
CRITICAL → 10 min
WARNING  → 20 min
INFO     → 60 min
```

---

## MF-04 — Manager Operations Planning, Analytics & Reporting

**Primary actor:** Manager  
**System actor/component:** System / AI

MF-04 contains **two related but independently enterable subflows**:

1. Operations Planning
2. Analytics & Reporting

**Do not connect successful planning validation directly to `Open Dashboard`.** A Manager may open analytics without first creating or validating a new shift plan.

### A. Operations Planning

Manager actions:

```text
Create Shift Plan
    ↓
Assign Staff to Zones
    ↓
System validates Shift & Zone Coverage
```

If validation fails:

```text
Invalid / adjust plan
    ↓
back to Assign Staff to Zones
```

Planning rules:

- default minimum: 1 Staff per active Zone per Shift;
- no overlapping shifts;
- weekly work limit: 48 h;
- started Shift cannot be changed;
- staff marked unavailable cannot be scheduled;
- Staff may request a shift swap/change;
- swap approval is Operator-controlled, not direct Staff-to-Staff negotiation;
- the system proposes suitable Staff candidates after validating scheduling rules;
- the Operator selects/approves the candidate before the ShiftAssignment is updated;
- a swap must not create overlapping shifts, unavailable assignments, invalid zone coverage, invalid workload, or invalid working-hour totals;
- scheduling constraints are configurable business rules; do not hardcode one rigid scheduling policy;
- no HR/payroll/recruitment/general HR subsystem.

Account/Staff CRUD remains a supporting Admin capability and is not part of MF-04.

### B. Analytics & Reporting

The interaction should visibly alternate between Manager actions and System/AI processing:

```text
MANAGER
Open Dashboard
    ↓
Filter Analysis Scope
    ↓
SYSTEM / AI
Aggregate Operational Data
    ↓
Calculate KPIs & Trends
    ↓
MANAGER
Review KPIs, Trends & Staff Operational Performance
    ↓
SYSTEM / AI
Generate AI Advisory Recommendations
    ↓
MANAGER
Review AI-Assisted Recommendations
    ↓
View / Export Operational Report
```

`Aggregate Operational Data` means combining the selected monitoring, Incident, Task-resolution, and shift/assignment data for the chosen analysis scope before KPI calculation.

Manager may inspect operational analytics such as:

- traffic / footfall;
- queue history;
- waiting time;
- crowd patterns;
- heatmaps;
- incident volume/severity;
- response time;
- resolution time;
- task completion;
- staff operational performance.

No revenue/sales/best-selling-product/general ERP analytics.

### Decision support

AI may recommend operational actions such as:

- open another checkout counter;
- adjust staffing;
- investigate recurring congestion;
- investigate repeated incident patterns.

Recommendation is informational/advisory only:

```text
AI recommends
→ Manager reviews
→ human decides
```

AI recommendations must not automatically change shifts, staffing, or store operations.

---

# 5. Domain vocabulary — do not mix these concepts

## Detection

Raw model output, e.g. a YOLO person detection.

Example:

```json
{
  "class": "person",
  "confidence": 0.83
}
```

Low-confidence detections can be filtered before tracking.

## Track

A tracked person within one camera using ByteTrack.

Track IDs are **camera-local / stream-local**. They are not identities and must not be used for cross-camera re-identification.

## Operational Measurement

A derived operational value:

- people count;
- zone entry/exit;
- queue length;
- waiting time;
- crowd density;
- checkout utilization.

## OperationalEvent

A timestamped structured observation produced from operational measurements.

An OperationalEvent is **not automatically an Incident**.

## OperationalEventType and IncidentType

`OperationalEventType` is the target reusable classification for AI operational conditions. `IncidentType` remains for separately qualified Incident categories and staff-report workflows; do not treat the two catalogs as interchangeable.

### AI-detected baseline OperationalEventTypes

1. Long Queue
2. Excessive Waiting Time
3. Overcrowding / Congestion
4. Checkout Capacity Issue

### Staff-report / qualified-Incident baseline categories

5. Spill / Broken Equipment
6. Equipment Malfunction
7. Pathway Obstruction
8. Cleanliness Issue
9. Safety Hazard
10. Other Operational Issue

AI-detected OperationalEventTypes use operational measurements + MonitoringRules.

Staff-report categories do not require an AI measurement and follow the StaffReport confirmation path.

## MonitoringRule

Defines when an AI measurement becomes a business Incident for a particular monitoring context/Zone.

Conceptually it owns:

- OperationalEventType reference;
- Warning threshold;
- Critical threshold;
- threshold unit;
- sustain time;
- cooldown;
- enabled/status.

The same OperationalEventType may use different thresholds in different Zones.

**ERD v4 is the provisional target for the concrete MonitoringRule structure. The live database currently remains IncidentType-based; do not hardcode a target-only schema or add a competing migration.**

## Incident

Business-relevant operational situation.

Severity:

```text
INFO
WARNING
CRITICAL
```

For AI incidents, Warning/Critical derives from the active MonitoringRule.

Info is mainly useful for staff-reported/informational incidents that are not threshold-triggered.

---

# 6. Spatial model — critical implementation rules

The spatial hierarchy is:

```text
Supermarket
    └── Floor
         ├── Zone
         └── Camera
```

Do **not** model Camera as owned by Zone.

A Camera:

- belongs to exactly one Floor;
- may monitor one or more Zones on that Floor.

A Zone:

- belongs to exactly one Floor;
- may be monitored by multiple Cameras.

Therefore Camera ↔ Zone is many-to-many through `CameraZoneMapping`.

## 6.1 Same-floor invariant

When creating a `CameraZoneMapping`:

```text
Camera.floor_id MUST equal Zone.floor_id
```

Reject cross-floor mappings at the backend/domain layer.

## 6.2 Two different polygons exist

Do not confuse:

### `Zone.map_polygon`

The Zone boundary drawn on the **2D floor map**.

### `CameraZoneMapping.roi_polygon`

The Zone/monitoring ROI drawn in a **specific camera frame**.

These are different coordinate spaces and serve different purposes.

## 6.3 Normalized coordinates

Persist floor-map and camera-frame spatial points as normalized coordinates where applicable:

```text
x ∈ [0, 1]
y ∈ [0, 1]
```

Do not persist absolute stream pixel coordinates as the source of truth.

Frontend:

```text
normalized_x = click_x / displayed_frame_width
normalized_y = click_y / displayed_frame_height
```

AI runtime:

```text
pixel_x = normalized_x * current_frame_width
pixel_y = normalized_y * current_frame_height
```

This prevents ROI breakage when stream resolution changes.

Normalization handles resolution scaling, **not arbitrary aspect-ratio changes, crop changes, or lens/perspective changes**. Keep equivalent aspect/crop behavior or define a canonical processing frame.

## 6.4 Polygon validation

`ISJSON()` only validates JSON syntax.

Application logic must validate:

- at least 3 points;
- each x/y is within `[0,1]`;
- no malformed points;
- polygon geometry is usable;
- same-floor Camera/Zone rule.

Self-intersection policy should be consistent across frontend/backend.

---

# 7. Multiple cameras monitoring the same Zone

The system explicitly does **not** perform cross-camera identity matching.

Therefore do not attempt:

```text
Camera A track 14 == Camera B track 83
```

or global person identity assignment.

When multiple cameras monitor one Zone, an operational measurement should use either:

- a designated source camera; or
- configured non-overlapping counting regions.

This rule prevents double counting without introducing cross-camera re-identification.

The exact measurement-source configuration remains an open design item. Do not sum overlapping camera counts or infer cross-camera identity until the team approves the strategy.

---

# 8. Crowd analysis and heatmaps

Heatmap and physical crowd density are related but not identical.

## Heatmap

Heatmap uses tracked positions accumulated over time:

```text
tracks
→ positions
→ spatial accumulation
→ heatmap
```

A heatmap answers:

> Where do people tend to concentrate?

It does not require real-world square metres.

## Crowd density

Current BR formula:

```text
crowd density = current people in Zone / Zone area in m²
```

Current baseline thresholds:

```text
Warning  >= 2 people/m²
Critical >= 3 people/m²
```

This requires a real physical area value.

### ERD v4 target persistence

`Zone.area_m2` exists in ERD v4. It is the physical area value used for the agreed people-per-square-metre density formula and may be entered by Admin for Zones that use physical density.

Do **not** implement camera-based physical area estimation, homography-based local-density estimation, or 3D calibration just to obtain square metres unless the scope is formally expanded.

Keep heatmap implementation independent from `area_m2`; heatmaps use tracked positions and do not require real-world square metres.

---

# 9. MF-01 database baseline and ERD v4 target

The current live development database was inspected on 10/10/2026 and contains 14 physical user tables: `Supermarket`, `Floor`, `Zone`, `Camera`, `CameraConnection`, `CameraZoneMapping`, `CameraHealthEvent`, `MonitoringConfiguration`, `MonitoringRule`, `IncidentType`, `OperationalEvent`, `Incident`, `Role`, and `UserAccount`.

This is implementation-state evidence, not the ERD v4 target. ERD v4 additionally includes StaffReport, OperationalEventType, ResponsePolicy, ResponsePolicyAction, Task and related assignment/evidence entities, notification/recommendation entities, shift/coverage entities, ZoneAdjacency, ZoneMetric, and AuditLog. Do not claim those target entities are physically migrated without inspecting the live database or an approved migration.

The existing MF-01 database baseline contains the following core entities.

## Stable / safe to implement now

### `Role`

Purpose: RBAC role definition.

Expected roles:

```text
ADMIN
OPERATOR
MANAGER
STAFF
```

### `UserAccount`

Purpose: login identity.

Important decisions:

- no separate generic `User` table is required for login identity;
- no self-registration approval workflow;
- no `PENDING`;
- no `reviewed_by_user_id`;
- no `reviewed_at`;
- status is `ACTIVE` / `DISABLED`;
- password is stored only as a password hash.

Use ASP.NET Core Identity / `PasswordHasher` or an equivalent proven password hasher.

Never store plaintext passwords or hand-roll SHA-256 password storage.

### `Supermarket`

One branch for the project baseline. **Decision 03/10/2026:** the system runs with exactly one default Supermarket record, provided by seed/initial data. There is no store setup step, screen, or create/edit store workflow; Admin setup starts at Floor. Keep the table and the `Floor.supermarket_id` FK as in the ERD; clients read the default store with `GET /supermarkets` and do not let users pick or create one.

### `Floor`

Belongs to `Supermarket`.

Can store:

- floor number;
- name;
- floor map asset URL;
- map width / height metadata.

Map width/height are render/debug metadata. Normalized spatial points remain the logical source of truth.

### `Zone`

Belongs to `Floor`.

Current/target important values:

- code;
- name;
- zone type;
- `map_polygon`;
- `area_m2` from ERD v4 for physical density calculations;
- status.

`map_polygon` uses normalized floor-map coordinates.

### `Camera`

Belongs to `Floor`.

Current important values include:

- camera code/name;
- manufacturer/model/serial;
- installation date;
- warranty end date;
- most recent maintenance/repair date once ERD is updated for BR-03;
- floor-map x/y;
- rotation;
- operational status;
- health status;
- last seen timestamp.

Camera map x/y should be normalized `[0,1]`.

Application transport-health states:

```text
UNKNOWN
ONLINE
OFFLINE
```

The current database constraint may still accept legacy `ERROR`, but application health checks do not emit it. Visual usability and processing readiness are separate from transport status and are represented by unresolved `CameraHealthEvent` types plus a derived `READY` / `NOT_READY` service projection.

### `CameraConnection`

Current MVP relationship:

```text
Camera 1 : 1 CameraConnection
```

Purpose:

- live/demo/test source;
- protocol;
- stream URI;
- optional snapshot URI;
- username;
- recoverable camera credential reference;
- enabled state;
- last connection test result/message.

Expected source types:

```text
LIVE
RECORDED
```

Synthetic `DEMO` camera connections are not accepted. Existing legacy `DEMO` rows are invalid configuration and must be reconfigured as `LIVE` or `RECORDED`; they must not report ONLINE from a generated frame.

Expected protocols can include:

```text
RTSP
HTTP
HLS
WEBRTC
FILE
```

New connection should start disabled.

Intended flow:

```text
configure
→ test
→ preview/manual verification
→ enable
```

### `CameraZoneMapping`

Bridge for Camera N:M Zone.

Stores the camera-specific Zone ROI.

Constraint:

```text
UNIQUE(camera_id, zone_id)
```

The backend must ensure both belong to the same Floor.

### `MonitoringConfiguration`

Current MF-01 concept: per-Zone monitoring/AI configuration.

Current baseline assumes at most one active/current configuration per Zone in the MVP.

Contains AI/runtime settings such as model detection confidence.

### `CameraHealthEvent`

Separate system-health history for camera/stream failures and recovery. Transport, visual usability, and processing availability are distinct: an ONLINE camera may still have `CAMERA_VIEW_BLOCKED`, `CAMERA_VIEW_BLURRED`, `CAMERA_VIEW_FROZEN`, or `CAMERA_FRAME_INVALID`. AI-service availability is broader pipeline health and must not be mislabeled as a camera failure.

Example states:

```text
OPEN
INVESTIGATING
RESOLVED
```

`investigated_by_user_id` is valid here because it records who actually investigated a health problem.

This is unrelated to the removed account/camera-review fields.

## ERD v4 target model and remaining alignment

### `MonitoringRule`

ERD v4 target rules reference `OperationalEventType`, not `IncidentType`. The target concepts include:

```text
rule_id
config_id
operational_event_type_id
warning_threshold
critical_threshold
threshold_unit
sustain_sec
cooldown_sec
parameters_json
enabled
UNIQUE(config_id, operational_event_type_id)
```

Do not resurrect the historical generic `rule_type` / `threshold_value` / `severity` shape. The current live database and scaffold still expose the older `incident_type_id` shape; do not change it without an approved ERD v4 SQL migration and re-scaffold.

### `IncidentType` / `OperationalEventType`

The current live database has a concrete `IncidentType` table. ERD v4 additionally introduces `OperationalEventType` for the AI event catalog. Their target relationship and migration are not implemented merely because the diagram contains them.

```text
incident_type_id
code
name
description
source_type
measurement_type
requires_before_photo
default_severity
status
timestamps
```

Incident qualification and any review/approval behavior must follow the 40-rule Business Rules. Do not invent a `requires_operator_review` column or create a competing table/migration while its target persistence representation remains unsettled.

---

# 10. Authentication and secret handling

## User password

Must be one-way hashed.

```text
password → proper password hasher → password_hash
```

Never recover plaintext user password.

## Camera/RTSP credential

Different requirement: the system must use the secret to connect to the camera.

Therefore it cannot be a one-way password hash.

Preferred pattern:

```text
CameraConnection
    credential_secret_ref
             ↓
 encrypted secret / secret store
```

Do not embed credentials directly in persisted RTSP URLs.

Never log camera passwords/tokens.

For local development use environment-specific secrets.

---

# 11. Current architecture / stack

## Web frontend

```text
ReactJS
TypeScript
Vite
```

## Mobile

```text
Flutter
```

MF-01 is mainly Web/Admin. Do not block MF-01 on the mobile app.

## Backend

```text
ASP.NET Core .NET 10
EF Core
```

Backend is responsible for:

- authentication/authorization;
- business validation;
- relational persistence;
- camera configuration APIs;
- monitoring configuration APIs;
- domain invariants;
- service integration.

## AI / video analytics

```text
Python
PyTorch
Ultralytics YOLO
ByteTrack
OpenCV
ONNX Runtime
FFmpeg
```

The AI service should not own business truth.

AI produces detections/tracks/measurements/events. ASP.NET/domain persistence remains responsible for operational business workflow.

## Database

```text
Microsoft SQL Server — primary relational source of truth
Redis — transient/cache/realtime support when required
```

Do not use Redis as the authoritative store for MF-01 configuration.

## Deployment

```text
Kitsura VPS
Nginx
Docker
GitHub Actions
```

---

## 11.1 Backend design pattern — pragmatic N-layer architecture

The backend uses a pragmatic N-layer architecture with four primary layers:

```text
API / Presentation
        ↓
Application / Use Cases
        ↓
Domain / Business Rules
        ↓
Infrastructure / Technical Services
```

### API / Presentation

Owns HTTP controllers/endpoints, authentication and authorization middleware, request/response DTOs, basic request-shape validation, exception handling, HTTP status mapping, and OpenAPI documentation.

Rules:

- Controllers stay thin and call Application use cases.
- Controllers must not contain business decisions or access `DbContext` directly.
- Scaffolded database entities must not be exposed as public API DTOs.

### Application / Use Cases

Owns commands, queries, use-case orchestration, use-case authorization, transaction boundaries, repository/service abstractions, external-service abstractions, pagination/filtering/idempotency, application validation, and mapping to API DTOs.

Expected MF-01 use cases include:

```text
CreateFloor
CreateZone
RegisterCamera
TestCameraConnection
EnableCameraConnection
DisableCameraConnection
MapCameraToZone
SaveMonitoringConfiguration
ActivateMonitoringConfiguration
ResolveCameraHealthEvent
```

Application services coordinate use cases; they must not become one global business-logic service. Application code may depend on Domain, but must use abstractions instead of concrete SQL Server, EF Core, HTTP, or camera implementations. Typical abstractions include `ICameraRepository`, `ICameraConnectionTester`, `ICurrentUser`, `IUnitOfWork`, and `IClock`.

### Domain / Business Rules

Owns entities, aggregates, value objects, domain services/events, business invariants, state transitions, and domain validation.

The Domain layer must enforce these rules:

```text
Camera belongs to a Floor, not directly to a Zone.
Camera ↔ Zone is many-to-many through CameraZoneMapping.
Camera and mapped Zone must belong to the same Floor.
Floor-map Zone polygon and camera-frame ROI are different coordinate spaces.
Spatial coordinates are persisted as normalized values where applicable.
OperationalEvent is not Incident.
CameraHealthEvent is not an operational Incident.
The last active Admin account cannot be disabled or removed.
Camera connections cannot be enabled before required validation succeeds.
```

Domain must not depend on ASP.NET Core, EF Core, external APIs, or configuration files. It must be unit-testable without a database or running web server.

### Infrastructure / Technical Services

Owns EF Core persistence, scaffolded database entities, entity configurations, repositories, transactions, SQL Server, Redis adapters, camera/RTSP adapters, health-monitoring workers, secret handling, external clients, and Python AI-service integration.

Infrastructure implements interfaces defined by Application or Domain. SQL Server is authoritative; Redis is only transient/cache/realtime support. The AI service produces detections, tracks, measurements, and events; the ASP.NET Core backend owns business workflow and persistence.

### Dependency direction and request flow

```text
Domain
  ↑
Application
  ↑
Infrastructure

API → Application
API → Infrastructure (dependency-injection composition only)
```

```text
HTTP Request
    ↓
API Controller
    ↓
Application Command or Query
    ↓
Application Validation
    ↓
Domain Entity or Domain Service
    ↓
Repository or External Adapter
    ↓
Unit of Work / Database Transaction
    ↓
Application Result
    ↓
API Response DTO
```

### Validation boundaries

- **API:** request shape, required fields, string lengths, enum format, JSON structure, and basic numeric ranges.
- **Application:** resource existence, permission, allowed workflow operation, and duplicate-request handling.
- **Domain:** same-floor mapping, normalized coordinate ranges, polygon minimum points, connection preconditions, status transitions, and last-active-Admin protection.

Frontend validation is for user experience only. Backend and Domain validation remain authoritative.

### Error handling and testing

Use consistent `ProblemDetails` responses: `400` malformed request, `401` unauthenticated, `403` unauthorized, `404` not found, `409` business conflict, `422` semantic validation failure, and `500` unexpected failure. Never expose stack traces, SQL errors, connection strings, camera credentials, or secrets.

Test at the layer boundary:

```text
Domain tests        → invariants and state transitions
Application tests   → use-case orchestration and validation
Infrastructure tests→ EF mappings, repositories, persistence
API integration     → auth, HTTP contracts, end-to-end workflows
```

Do not put business rules only in React, call `DbContext` from controllers, expose EF entities as API DTOs, put ASP.NET/EF dependencies in Domain, allow AI to modify incidents/tasks directly, use Redis as the source of truth, combine OperationalEvent with Incident, or treat CameraHealthEvent as an operational Incident.

## 11.2 Database-first persistence and EF Core scaffolding

The backend uses a database-first persistence workflow. The approved SQL Server schema and synchronized ERD are authoritative for tables, columns, keys, relationships, nullability, constraints, indexes, and persistence-specific status values. EF Core entities and `DbContext` code are scaffolded from the approved database schema.

Generated persistence code belongs under Infrastructure:

```text
Supermarket.Infrastructure/
  Persistence/
    Scaffolded/
      AppDbContext.cs
      UserAccount.cs
      Supermarket.cs
      Floor.cs
      Zone.cs
      Camera.cs
      CameraConnection.cs
      CameraZoneMapping.cs
      MonitoringConfiguration.cs
      CameraHealthEvent.cs
    Configurations/
    Mappings/
    Repositories/
```

Scaffolded files are generated artifacts. Do not put business rules or API behavior in them, manually patch them expecting changes to survive re-scaffolding, or use them directly as API DTOs. Put custom behavior in Domain, Application, Infrastructure adapters, mappings, or supported partial extensions. The generated `DbContext` must not contain production secrets or hardcoded connection strings; configure it through application configuration and dependency injection.

### Database-first change workflow

```text
Approved ERD or database-schema change
    ↓
Apply the approved SQL Server schema change
    ↓
Re-scaffold EF Core entities and DbContext
    ↓
Review generated changes
    ↓
Update Infrastructure mappings and repositories
    ↓
Update Domain/Application behavior if required
    ↓
Update API contracts and tests if required
```

Database changes must not be introduced only by changing C# classes. Migration scripts or database deployment tooling may apply approved changes, but must remain synchronized with the approved ERD and SQL Server schema.

Keep persistence and business models separate:

```text
Scaffolded EF entity ↕ Infrastructure mapping
Domain entity/value object ↕ Application mapping
Application DTO ↕ API mapping
API request/response
```

Direct mapping may be used for simple MF-01 operations only when it does not weaken business rules. Important invariants remain in Application or Domain code.

Do not freeze or invent scaffolded contracts for the remaining unresolved alignment points: SLA/escalation semantics, threshold/policy provenance, concurrency/idempotency, StaffReport media retention, multiple-camera measurement-source selection, checkout-counter representation, Manager-on-duty representation, optional policy-action traceability, or the operational use of `ZoneAdjacency`. ERD v4 defines `Zone.area_m2`, the concrete target `MonitoringRule`/`OperationalEventType` relationship, and the `ZoneAdjacency` entity; the live schema remains older.

# 12. Recommended MF-01 service boundaries

Keep components separable even if deployed together initially.

```text
React Admin Web
       ↓ HTTPS
ASP.NET Core API
       ├── MSSQL
       ├── Secret handling
       ├── Camera connection/preview adapter
       ├── Health monitoring worker
       └── AI service configuration/integration
                         ↓
                    Python AI Service
```

Avoid frontend → AI service direct calls for persistent configuration.

Backend should own authorization and persistence.

---

# 13. MF-01 API capability checklist

Exact route naming may vary, but the capabilities should exist consistently.

## Authentication / accounts

```text
POST   /auth/login
GET    /users
POST   /users
PATCH  /users/{id}
POST   /users/{id}/disable
POST   /users/{id}/enable
GET    /roles
```

Rules:

- Admin-only account administration;
- no public self-registration;
- last active Admin cannot be disabled/removed.

## Supermarket

Single default store, read-only for clients (no store setup in the current baseline):

```text
GET    /supermarkets
GET    /supermarkets/{id}
```

## Floors

```text
GET    /supermarkets/{supermarketId}/floors
POST   /supermarkets/{supermarketId}/floors
GET    /floors/{id}
PATCH  /floors/{id}
```

Support floor-map metadata/upload flow as agreed by the frontend/storage implementation.

## Zones

```text
GET    /floors/{floorId}/zones
POST   /floors/{floorId}/zones
GET    /zones/{id}
PATCH  /zones/{id}
```

Validate `map_polygon`.

## Cameras

```text
GET    /floors/{floorId}/cameras
POST   /floors/{floorId}/cameras
GET    /cameras/{id}
PATCH  /cameras/{id}
```

## Camera connection

```text
PUT    /cameras/{id}/connection
POST   /cameras/{id}/connection/test
GET    /cameras/{id}/preview
POST   /cameras/{id}/connection/enable
POST   /cameras/{id}/connection/disable
```

Enabling should not silently bypass required connection validation.

Manual preview approval does not need a `reviewed_by` database field.

## Camera-Zone mapping

```text
GET    /cameras/{id}/zones
PUT    /cameras/{id}/zones/{zoneId}
DELETE /cameras/{id}/zones/{zoneId}
```

Mapping payload includes normalized `roi_polygon`.

Reject cross-floor mapping.

## Monitoring configuration

```text
GET    /zones/{zoneId}/monitoring
PUT    /zones/{zoneId}/monitoring
POST   /zones/{zoneId}/monitoring/activate
POST   /zones/{zoneId}/monitoring/deactivate
```

Exact MonitoringRule/OperationalEventType endpoints should follow the approved target contract; the current live API may still expose older IncidentType-based routes until migration.

## Camera health

```text
GET    /cameras/{id}/health
GET    /camera-health-events
POST   /camera-health-events/{id}/investigate
POST   /camera-health-events/{id}/resolve
```

These routes are examples of capabilities, not mandatory naming.

---

# 14. Backend validation rules for MF-01

At minimum enforce:

## Accounts

- email unique;
- role exists;
- user status valid;
- last active Admin protection.

## Supermarket / Floor / Zone

- floor belongs to supermarket;
- floor number unique inside supermarket;
- zone belongs to floor;
- zone code unique inside floor;
- floor map dimensions are both null or both positive;
- polygon JSON valid;
- polygon points valid and normalized.

## Camera

- camera belongs to floor;
- normalized map x/y if present;
- valid rotation;
- valid operational/health status;
- installation/warranty metadata obeys sensible chronology;
- update most recent maintenance/repair timestamp only when maintenance occurs.

## CameraConnection

- supported source type/protocol;
- no plaintext secret persisted;
- connection test updates `last_tested_at`, result, message;
- new connection disabled by default;
- enabling failed/invalid connection should be rejected.

## CameraZoneMapping

- Camera and Zone exist;
- same Floor;
- unique pair;
- ROI JSON valid;
- ROI coordinates normalized;
- geometry valid.

## MonitoringConfiguration

- Zone exists;
- model confidence range `[0,1]`;
- status valid;
- only the allowed current/active configuration policy for the MVP.

---

# 15. Timestamp conventions

Use UTC for persisted timestamps:

```text
SYSUTCDATETIME()
DateTimeOffset / UTC DateTime in application code
```

Important SQL Server detail:

```text
updated_at DEFAULT SYSUTCDATETIME()
```

only initializes the value on INSERT.

It does **not** automatically update on UPDATE.

EF Core/application code must update `updated_at` on mutations, or use another explicit DB mechanism.

---

# 16. MF-01 state semantics

## Camera operational status

Represents administrative availability:

```text
ACTIVE
INACTIVE
DISABLED
```

## Camera health status

Represents runtime health:

```text
UNKNOWN
ONLINE
OFFLINE
```

This field is transport/connectivity-oriented. Visual health uses event-specific `CameraHealthEvent` records and monitoring usability is derived as `READY` / `NOT_READY`. Monitoring activation remains independent and is not automatically changed by temporary health failures.

Do not use these interchangeably.

Example:

```text
Camera.status = ACTIVE
Camera.health_status = OFFLINE
```

means the camera is configured/expected to run but currently unavailable.

## CameraConnection enabled

`is_enabled` means the configured connection may be used operationally.

It should not mean "a test succeeded once".

Test outcome is separately represented by:

```text
last_tested_at
last_test_result
last_test_message
```

---

# 17. MF-01 sequence to implement and demo

A clean end-to-end demo should be possible in this order:

1. Admin logs in.
2. Admin opens the default store (pre-seeded; no store creation step).
3. Admin creates a Floor.
4. Admin attaches a floor-map asset.
5. Admin draws a Zone polygon on the floor map.
6. Admin registers a Camera on that Floor.
7. Admin places the Camera on the floor map.
8. Admin configures the approved live or recorded-source connection settings.
9. Backend tests the connection.
10. Admin previews the stream.
11. Admin manually enables the connection.
12. Admin maps the Camera to a Zone.
13. Admin draws that Zone's ROI in the Camera frame.
14. Backend stores normalized ROI coordinates.
15. Admin configures the Zone monitoring configuration.
16. Admin activates monitoring.
17. Camera health becomes ONLINE / last_seen is updated.
18. Simulate camera failure.
19. Transport becomes OFFLINE or a confirmed visual-health issue opens its event-specific CameraHealthEvent.
20. Admin investigates/restores the connection.
21. Health event resolves.

Although ERD v4 defines the target MonitoringRule/OperationalEventType relationship, steps that depend on unapproved target persistence may be stubbed behind a stable domain/service interface rather than committing a competing migration.

---

# 18. MF-01 Definition of Done

MF-01 is not "done" just because CRUD pages exist.

It is done when a working end-to-end setup can be demonstrated and tested.

Minimum DoD:

- [ ] Admin authentication works.
- [ ] RBAC protects Admin configuration endpoints.
- [ ] Default store is available; Floor/Zone CRUD works.
- [ ] Floor map can be referenced/displayed.
- [ ] Zone polygon can be created and reloaded.
- [ ] Camera can be registered and placed on a Floor.
- [ ] Camera has installation/warranty metadata.
- [ ] Camera connection can be configured.
- [ ] Connection test works with deterministic result handling.
- [ ] Preview path works for the agreed live/demo source.
- [ ] New connection starts disabled.
- [ ] Admin can enable/disable after validation.
- [ ] Camera can map to multiple Zones on the same Floor.
- [ ] A Zone can map to multiple Cameras.
- [ ] Cross-floor mapping is rejected.
- [ ] ROI polygon persists in normalized coordinates.
- [ ] Monitoring configuration can be saved/activated.
- [ ] Camera health state updates.
- [ ] Health events can be opened/investigated/resolved.
- [ ] Credentials are not persisted/logged in plaintext.
- [ ] Core validation has unit tests.
- [ ] Critical API + DB integration paths have integration tests.
- [ ] OpenAPI/API contract is kept in sync.
- [ ] No MF-02/MF-03 feature is accidentally implemented as hidden MF-01 coupling.

---

# 19. MF-01 required tests

## Unit/domain tests

At minimum:

- normalized coordinate validation;
- polygon minimum point count;
- cross-floor CameraZoneMapping rejection;
- unique mapping;
- connection enable preconditions;
- status transition rules;
- model confidence range;
- last active Admin protection;
- floor map width/height validation;
- credential sanitization/no secret in log DTO;
- `updated_at` application behavior where appropriate.

## Integration tests

At minimum:

### Store setup

```text
default supermarket exists
→ create floor
→ create zone
→ reload
```

### Camera setup

```text
create camera
→ create connection
→ test
→ enable
→ reload
```

### Camera-zone geometry

```text
create camera + zone same floor
→ save mapping
→ reload normalized ROI
```

Negative:

```text
camera floor A + zone floor B
→ reject
```

### Camera health

```text
online
→ simulated disconnect
→ CameraHealthEvent OPEN
→ investigate
→ restore
→ RESOLVED
```

---

# 20. Report 2 quality expectations relevant to coding

Current project process expects:

- incremental Mainflow-based implementation;
- end-to-end demonstrable behavior;
- at least **80% coverage of testable core business logic** as the unit-test target;
- **100% of identified critical integration flows** covered by integration testing;
- critical workflow behavior verified at system/acceptance level;
- Pull Requests reviewed before merging into `dev`.

Do not chase a numeric test metric by testing trivial getters. Prioritize deterministic domain rules and critical boundaries.

---

# 21. Git and contribution conventions

## 21.1 Branching and integration workflow

`dev` is the integration branch. Feature work must not start directly on `dev`.

Standard workflow:

```text
Update local dev from the shared dev branch
    ↓
Create a feature/fix branch from dev
    ↓
Implement and commit only on that branch
    ↓
Run focused tests, build checks, and required validation
    ↓
Request review through a pull request
    ↓
Merge into dev only after review and verification
```

Recommended branch names:

```text
feature/<short-description>
fix/<short-description>
refactor/<short-description>
docs/<short-description>
```

Rules:

- Never implement feature work directly on `dev`.
- Never commit unfinished or unverified feature work to `dev`.
- Keep unrelated changes out of the feature branch.
- Keep commits focused and reviewable.
- Do not bypass pull-request review for normal feature work.
- Resolve merge conflicts against the current `dev` before merging.
- A branch is not complete until focused verification evidence is available.

## 21.2 Agent instruction and project-convention workflow

Before implementation, the coding agent must read:

1. The repository-root `AGENTS.md`.
2. Any relevant project-specific instruction files.
3. Relevant architecture, ERD, business-rule, and validation documents.
4. Existing contribution, branching, and test instructions.

Prior project configurations and architecture documents may be used as reference material, but this repository's `AGENTS.md`, approved ERD, current business rules, and explicit team decisions take precedence.

The agent must:

- preserve established team conventions unless this repository explicitly overrides them;
- inspect the current branch and working tree before editing;
- make changes on a feature/fix branch created from `dev`;
- avoid direct edits to `dev` for feature implementation;
- keep generated database code separate from business logic;
- add or update tests for behavior changes;
- run focused verification before requesting review;
- report changed files and verification evidence;
- ask before destructive or irreversible workspace actions.

Current project plan:

```text
main
  └── stable

dev
  └── integration

feature/<feature-name>
  └── feature work
```

Commit prefixes:

```text
feat:
fix:
refactor:
docs:
test:
```

Expected workflow:

```text
feature branch
→ PR
→ at least one review
→ dev
→ milestone/release validation
→ main
```

Link work/PRs to Jira tickets when applicable.

Do not commit:

- secrets;
- camera passwords;
- `.env` with credentials;
- build outputs;
- local databases;
- generated caches.

---

# 22. Suggested parallel implementation split for MF-01

This is a coordination suggestion, not a formal RACI replacement.

## Backend / Database track

Can start immediately on stable entities:

- Role/UserAccount;
- Supermarket/Floor/Zone;
- Camera;
- CameraConnection;
- CameraZoneMapping;
- CameraHealthEvent;
- MonitoringConfiguration baseline;
- auth/RBAC;
- validation;
- migrations;
- API contracts.

Integrate future MonitoringRule/OperationalEventType, ResponsePolicy, Task, and StaffReport persistence only against an approved ERD v4 SQL schema. Do not create a competing migration from this context file.

## Web/Admin track

Can start against API contracts/mocks:

- login;
- floor management (single default store);
- floor map view;
- zone editor;
- camera management;
- connection form;
- connection test state;
- preview page/component;
- camera placement;
- camera-zone ROI editor;
- monitoring configuration;
- health status.

## Camera/video track

Can start independently:

- live/recorded-source adapter;
- connection test;
- snapshot/preview;
- reconnect behavior;
- health heartbeat/status;
- FFmpeg/MediaMTX/WebRTC/HLS approach if used.

## AI track

For MF-01, only configuration/integration scaffolding is necessary.

Full OperationalEvent generation belongs to MF-02.

Can prepare:

- configuration contract;
- normalized ROI loading;
- de-normalization to current frame dimensions;
- stream startup/shutdown interface;
- health/heartbeat interface.

---

# 23. ERD v4 target entities — do not prematurely force into MF-01

ERD v4 includes the following target entities/concepts. Their presence in the diagram does not mean they are already present in the current database or implementation:

```text
OperationalEvent
OperationalEventType
Incident
StaffReport
ResponsePolicy
ResponsePolicyAction
Notification
Staff
Shift
ShiftAssignment
Task
TaskAssignmentHistory
TaskEvidence
Recommendation
ZoneAdjacency
ZoneMetric
AuditLog
```

The current live database is still the 14-table MF-01 implementation baseline. Add target entities only through an approved SQL/ERD implementation task; do not infer a migration from this context file.

Potential concepts such as Manager-on-duty remain required by later escalation/shift rules, but their representation is not settled.

ZoneAdjacency exists in ERD v4. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.

Do not add speculative tables simply to make the schema "look complete".

Add them when the corresponding Mainflow contract is being implemented and the ERD is approved.

---

# 24. Later-flow rules that MF-01 must not block

Even though MF-01 does not implement them yet, avoid architectural decisions that make these impossible:

## OperationalEvent episode continuity

```text
one active OperationalEvent episode
per event type / configured scope
```

Do not collapse an OperationalEvent episode into an Incident. Incident qualification is a separate response-policy outcome.

## Sustain time

AI condition must remain true for the MonitoringRule sustain duration before an OperationalEvent episode is opened or escalated.

Current baseline:

```text
30 s
```

## Cooldown

After closure, the same event type and configured scope is suppressed until cooldown passes.

Current baseline:

```text
5 min
```

## Notification / assignment

Response is policy/action based:

```text
OperationalEvent + priority
→ select ResponsePolicy
→ execute or request approval for ResponsePolicyAction
→ notification / recommendation / Operational Task / qualifying Incident
→ first valid acceptance wins

no eligible Staff OR retries exhausted
→ Operator manual assign / reassign
```

Need atomic "first accept wins".

Do not design task offers in a way that can assign multiple active Staff members to the same single-assignee Task.

## Shift / Zone eligibility

Future dispatch depends on:

```text
shift
check-in
zone assignment
workload
```

ZoneAdjacency exists in ERD v4. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.

## Shift swap / change workflow

The approved workflow is:

```text
Staff requests shift change/swap
    ↓
Operator reviews the schedule
    ↓
System validates suitable Staff candidates
    ↓
System proposes candidate(s)
    ↓
Operator reviews/selects
    ↓
Approved ShiftAssignment is updated
```

Candidate validation must respect the active scheduling rules, including availability, no overlap, zone coverage, workload, and configured working-hour limits. Use a transparent rule-based recommendation mechanism; do not introduce an optimization engine unless separately approved.

---

# 25. Baseline calculations for later flows

Current BR calculation definitions:

## Queue length

Count people who have remained inside the queue area for at least 5 seconds.

## Waiting time

Time from entering queue area to reaching counter.

Dashboard average:

```text
mean over people served in last 5 min
```

## Crowd density

```text
people in Zone / Zone area (m²)
```

`Zone.area_m2` is present in ERD v4 and is the physical area input for this formula.

## Checkout utilization

```text
counters with cashier present / total counters
```

Exact checkout-counter representation will be finalized with the AI/monitoring design.

## Response time

```text
staff accepted time - incident dispatched time
```

## Resolution time

```text
task verified time - incident created time
```

## SLA %

```text
verified tasks resolved within SLA / all verified tasks in period
```

## Footfall

Count entries crossing the configured entrance line during the period.

---

# 26. Baseline AI incident thresholds

These are configurable defaults, not hardcoded universal constants.

## Long Queue

```text
Warning  >= 3 waiting people
Critical >= 5 waiting people
```

## Excessive Waiting Time

```text
Warning  >= 4 min
Critical >= 8 min
```

## Overcrowding / Congestion

```text
Warning  >= 2 people/m²
Critical >= 3 people/m²
```

## Checkout Capacity Issue

Warning and Critical depend on the condition of open counters and Long-Queue state. Treat this as a configurable composite operational rule, not a YOLO class.

## Model detection confidence

Current starting value:

```text
0.50
```

This is an AI input filter only.

Do not use it for Incident routing.

---

# 27. Privacy / data handling constraints

Implementation must respect current project privacy/security rules.

- display video-recording notice in monitored areas operationally;
- customers are never identified by the system;
- no face recognition;
- no persistent customer identity;
- continuous video is not stored as a normal feature;
- incident-linked clips/snapshots may be retained according to the approved requirement;
- staff operational personal data is only for store operations;
- camera sensitive credentials must be protected;
- RBAC + least privilege.

Do not introduce a feature that requires customer identity unless scope is formally changed.

---

# 28. Error handling conventions

Recommended project-wide API convention:

- `400` malformed/validation request;
- `401` unauthenticated;
- `403` authenticated but unauthorized;
- `404` resource not found;
- `409` uniqueness/state/concurrency conflict;
- `422` optional for valid syntax but rejected domain state, if used consistently;
- `500` unexpected server error with sanitized response.

Do not expose:

- stack traces;
- database connection strings;
- RTSP credentials;
- tokens;
- internal secret references.

Use stable application error codes/messages for frontend behavior.

---

# 29. Logging and observability

Log events that help diagnose operational failures without leaking secrets.

Useful structured fields:

```text
request_id
user_id
camera_id
floor_id
zone_id
connection_id
health_event_id
event_type
status
timestamp
```

Never log:

```text
password
password_hash
camera password
JWT
full RTSP URI if it embeds credentials
secret value
```

---

# 30. Do not over-engineer

For this capstone, prefer explicit, defendable behavior over research-heavy infrastructure.

Do not add without requirement:

- Kafka;
- Kubernetes;
- event sourcing;
- CQRS everywhere;
- distributed microservices purely for style;
- cross-camera ReID;
- 3D calibration for zone area;
- complicated geometry engines when normalized polygon validation is enough;
- separate databases for every module.

A modular monolith backend plus a separate Python AI service is acceptable and easier to integrate for the current scope.

---

# 31. Do not under-engineer either

The following are core and should not be hand-waved:

- authorization;
- password hashing;
- secret handling;
- database constraints;
- transaction/concurrency control;
- normalized ROI persistence;
- same-floor camera-zone validation;
- camera connection test/health handling;
- predictable state transitions;
- unit/integration tests;
- API contract;
- observability;
- reproducible environment/config.

---

# 32. Report 3 implications while implementation proceeds

The current SRS/Report 3 draft will need:

- Product Overview + System Context Diagram;
- Actors;
- Use Case Diagram(s);
- Use Case summary/descriptions;
- System Functional Overview;
- Screen Flow;
- Screen Descriptions;
- Screen Authorization;
- Non-Screen Functions;
- ERD + entity descriptions;
- detailed feature/function specifications later;
- external interfaces;
- quality attributes;
- Business Rules;
- common requirements;
- application messages.

Whenever implementation introduces or changes behavior, make sure the corresponding Report 3 function/use-case/BR/ERD entry stays synchronized.

For MF-01, implementation should produce enough concrete behavior to populate:

- Admin screen flow;
- Floor/Zone screens;
- Camera setup screens;
- connection test/preview screens;
- monitoring config screens;
- health screens;
- authorization matrix;
- non-screen functions for connection health/monitoring startup;
- entity descriptions.

---

# 33. Implementation questions and ERD v4 refinements that are currently OPEN

Do not silently decide these in code without team agreement. ERD v4 is the provisional target; the current live database is not proof that these target concepts are migrated.

1. **SLA and escalation semantics**
   - Define deadlines, breach behavior, escalation timing, and the responsible role.

2. **Threshold and policy provenance**
   - Preserve which MonitoringRule/policy version produced an event and make the result reproducible.

3. **Concurrency and idempotency**
   - Define durable guarantees for Task acceptance, OperationalEvent episodes, action outcomes, retries, and duplicate delivery.

4. **StaffReport media retention**
   - Confirm durable image/voice references, storage ownership, retention, and deletion behavior.

5. **Optional policy-action traceability**
   - `Task.response_policy_action_id` is not approved; do not add it without an explicit decision.

6. **Measurement source when several cameras monitor one Zone**
   - designated source camera vs non-overlapping measurement ROIs;
   - no cross-camera ReID.

7. **Checkout counter representation**
   - needed later for checkout utilization/capacity;
   - do not invent complex POS integration.

8. **Manager-on-duty representation**
   - required later for escalation;
   - likely derived from shift/duty assignment.

9. **Operational use of `ZoneAdjacency`**
   - ZoneAdjacency exists in ERD v4. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.


---

# 34. Coding-agent behavior for this repository

When an AI coding agent works on this project:

1. Read this file before making architectural changes.
2. Preserve the Mainflow boundaries.
3. Do not invent new business requirements.
4. Do not change ERD/schema files owned by another task unless explicitly assigned.
5. If a required business-to-persistence representation remains **OPEN/PENDING alignment**, implement against an interface/DTO/mock or stop and ask rather than creating a competing schema.
6. Keep domain/business validation in backend/domain code, not only frontend.
7. Keep UI logic from becoming the only source of business truth.
8. Add tests with behavior changes.
9. Do not expose or commit secrets.
10. Keep API contracts/documentation in sync.
11. Avoid scope creep.
12. If a change affects Report 1/2/3 or BR semantics, flag it in the PR.
13. Prefer small reviewable PRs by feature/capability.
14. Do not "simplify" OperationalEvent and Incident into one object.
15. Do not use model confidence as Incident routing confidence.
16. Do not implement cross-camera person identity.
17. Do not persist camera ROI in absolute pixels.
18. Do not create a camera-reviewer field; manual preview verification is not tracked by user.
19. Do not create an account-review/PENDING workflow.
20. Keep CameraHealthEvent separate from supermarket Operational Incidents.
21. Do not turn every OperationalEvent or CRITICAL priority into an Incident. Incident qualification is a separate ResponsePolicyAction outcome.
22. Keep ResponsePolicyAction execution distinct from Task creation; actions may be automatic or approval-required. Route Tasks only through the configured response and eligibility rules.
23. ZoneAdjacency exists in ERD v4. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.
24. Keep MF-04 Planning and Analytics as independent entry paths; successful shift validation is not a prerequisite for opening the dashboard.
25. Keep Admin account/Staff CRUD as a supporting capability, not a required step in the four Mainflow diagrams.

---

# 35. MF-01 quick mental model

If you remember only one thing before coding MF-01, remember this:

```text
ADMIN CONFIGURES THE WORLD
────────────────────────────────────────

UserAccount + Role

Supermarket
    ↓
Floor
   ├─────────────┐
   ↓             ↓
 Zone          Camera
   ↑             │
   └── CameraZoneMapping
       (normalized camera ROI)

Camera
   ↓
CameraConnection
   ↓
test → preview → enable

Zone
   ↓
MonitoringConfiguration
   ↓
MonitoringRule / OperationalEventType
   (ERD v4 target; live DB still exposes IncidentType)

OperationalEvent
   ↓
ResponsePolicy / ResponsePolicyAction
   ↓
Notification / Recommendation / Task / qualifying Incident

StaffReport
   ↓
routine Task or separately qualified Incident

Camera
   ↓
health monitor
   ↓
CameraHealthEvent
```

And the most important invariants:

```text
Camera belongs to Floor, not Zone.

Camera N:M Zone.

Camera and mapped Zone must be on same Floor.

Floor-map Zone polygon != camera-frame ROI polygon.

Persist normalized coordinates, not absolute stream pixels.

Model confidence filters detections only.

OperationalEvent != Incident.

Camera health event != operational Incident.

No account review workflow.

No camera reviewer tracking.

No cross-camera ReID.

MSSQL is source of truth.
```

---

# 36. Current implementation checkpoint

The team is ready to start MF-01 implementation **as long as the shared code treats the following as stable**:

```text
Auth / RBAC
Supermarket
Floor
Zone
Camera
CameraConnection
CameraZoneMapping
MonitoringConfiguration
CameraHealthEvent
normalized spatial model
camera connection test/preview/enable flow
camera health flow
```

The team should **coordinate before freezing target migrations** for:

```text
measurement-source selection
policy/threshold provenance and action traceability
checkout-counter representation
Manager-on-duty representation
operational use of ZoneAdjacency
```

Those are not reasons to block the rest of MF-01.

---

# 37. Final goal of MF-01

At the end of MF-01, a reviewer should be able to watch this happen:

> An Admin logs in, sets up the floor/zone structure of the default store, registers and configures a camera, triggers connection testing and preview, maps the camera to one or more valid Zones using normalized ROIs, configures and activates monitoring, sees continuous camera/stream health monitoring, then investigates, retests, and restores a detected connection/stream/configuration issue.

That is an end-to-end **setup/configuration Mainflow**, not a collection of disconnected CRUD screens.

The next increment, MF-02, begins when camera/video data becomes operational measurements/events and those events begin producing policy-driven responses, Tasks, notifications, recommendations, or separately qualified Incidents.

---

# 38. Current decisions / superseded designs

This section prevents historical designs from being reintroduced while preserving the current agreed behavior.

## Superseded

- Model-confidence-based Incident routing or review.
- High YOLO confidence meaning auto-dispatch and low confidence meaning Operator review.
- Universal Operator pre-dispatch review for every Incident.
- Universal Staff auto-notification for every Incident.
- Staff reports passing through the camera Detection & Tracking pipeline.
- Staff directly closing or verifying their own Task.
- MF-04 Planning validation being a mandatory prerequisite for opening Analytics.

## Current target decisions

- Model confidence filters raw detections only.
- OperationalEvent and Incident remain separate; threshold breach/CRITICAL priority alone does not create an Incident.
- ResponsePolicyAction may be automatic or approval-required; do not impose universal Operator pre-dispatch review on confirmed reports or AI-qualified Incidents.
- Task origins are exactly OPERATIONAL_EVENT, INCIDENT, STAFF_REPORT, or MANUAL, with exclusive nullable source FKs.
- Staff-report extraction is a separate path from camera Detection & Tracking.
- Operator independently verifies submitted evidence.
- MF-04 Planning and Analytics are independently enterable.
- AI recommendations are advisory; they do not automatically change store operations.
- `Zone.area_m2`, the concrete `MonitoringRule` and `IncidentType` structures, and `ZoneAdjacency` are represented in ERD v4. `MonitoringRule` targets `OperationalEventType`; the live database remains older and IncidentType-based.
- ZoneAdjacency exists in ERD v4. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.

# 39. Assigned ROI monitoring increment — 03/10/2026

This is the current implementation checkpoint for the assigned ROI increment, **not** the full ERD v4 target implementation. Preserve the 10/10/2026 MF-02/MF-03 design as the eventual workflow. Temporary overcrowding uses **people count in ROI**, `PEOPLE` + explicit `parameters_json.measurementMode=PEOPLE_COUNT`; no m² inferred from normalized pixels/perspective. Legacy density remains untouched/unsupported at activation. Density calibration awaits team discussion. Long Queue counts a person after ≥5s continuously observed inside ROI, then threshold+sustain. Waiting/Checkout runtime stays deferred.

Confidence applies before ByteTrack. ROI membership uses bbox bottom-center, current observed tracks only; per-confidence tracking contexts, no cross-camera identity/dedup. Each zone needs exactly one ACTIVE camera mapping to activate this increment. N:M camera-zone modeling remains unchanged; one camera can monitor several zones independently, counts never summed across cameras.

After activation, a BE-owned worker consumes ordered AI batches even without a viewer, checks the current source/mapping/configuration version again when writing, evaluates independent Warning/Critical sustain clocks and records aggregate OperationalEvents. Gaps/reconnect/version changes reset continuity. Recorded-video source time, not inference wall time, drives sustain. The current compatibility path still uses one open Incident per zone/type and records DETECTED Incidents; that is an implementation divergence, not the ERD v4 target rule. Closure tests seed terminal state only in isolated fixtures; no new product closure endpoint.

New AI Incident status is **DETECTED — not dispatched** for this increment. The current compatibility path and its SQL history are older than the ERD v4 target: 14 scaffolded entities are present, while Task, ResponsePolicy, StaffReport, OperationalEventType, notification, dispatch, evidence/media, and ZoneMetric/analytics workflows are not implemented in this increment. The application never auto-migrates; shared Dev must be backed up and migrated by the user/team. CameraHealthEvent remains a separate system-health concept.

FE keeps team floor overview/detail/edit and ROI/minimap; Live adds runtime/count/progress/cooldown and real incident feed with DEMO tags. Active viewer start/stop attaches/detaches without restarting monitoring. Recorded EOF is COMPLETED, never auto-loop; stop all active configurations on that camera and let the worker release ownership before reactivation/replay. See README/API for contracts and VALIDATION for actual test evidence; separate fake transport SQL tests from native GPU smoke and full-system acceptance.

Deactivate requests an owner stop asynchronously. Runtime reports STOPPING until the worker clears its owned job; reactivation with no other active zone is rejected with MONITORING_STOP_PENDING during that interval. This avoids falsely treating desired INACTIVE as proof that a recorded source is released/replay-ready.
