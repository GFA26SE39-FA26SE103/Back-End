# PROJECT_CONTEXT.md — FA26SE103 Implementation Snapshot

> **Project:** AI-Powered Smart Supermarket Operations Monitoring System  
> **Project code:** FA26SE103  
> **Group:** GFA26SE39  
> **Purpose of this file:** provide a repository-owned synchronized implementation snapshot so a backend-only clone gives humans and coding agents one implementation-oriented view of the current agreed product, Mainflows, domain rules, architecture, and implementation scope before code is written.
>
> **Synchronization baseline:** team/Mainflow decisions through **02/10/2026**, aligned with `GFA26SE39_Final_Agreed_Mainflows_2026-10-02.docx`.
>
> **Important:** this file is a synchronization guide, not a replacement for the official reports. If an official requirement changes, update this file together with the affected requirement/design artifact.

---

## 0. Read this first

The project is **not** a generic supermarket ERP and **not** a security-surveillance product.

The product is an **AI-assisted supermarket operations monitoring system**. Its core closed loop is:

```text
Camera / Staff Report
        ↓
Operational observation
        ↓
Business condition / Incident
        ↓
Notification / Assignment
        ↓
Staff Task
        ↓
Evidence
        ↓
Operator Verification
        ↓
Operational Analytics / Recommendation
```

The system focuses on supermarket operations such as queues, waiting time, crowding, checkout utilization, staff response, incident resolution, and operational reporting.

Do not silently expand the scope into POS, inventory, sales analytics, payroll, customer identification, theft detection, face recognition, or cross-camera person re-identification.

---

# 1. Source of truth and document alignment

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
   - In particular, it freezes conditional Operator review in MF-02, evidence verification in MF-03, and the independent Planning vs Analytics entry paths in MF-04.
   - The repository does not currently record a Drive URL for this document; do not invent one. Add it only when the team supplies the URL.

4. **FA26SE103_Business_Rules.docx — current working detailed BR**
   - Defines detailed business semantics: camera/zone rules, OperationalEvent vs Incident, routing, task rules, shifts, calculations, scope rules, and lifecycles.
   - Current Drive source:
     `https://docs.google.com/document/d/1ODHDSkK0pLCcbKVS2nl3G0KhxIz6Q5PR/edit?usp=drivesdk`

5. **FA26SE103_ERD_v3.drawio — current physical persistence baseline**
   - Use ERD v3, not the historical `FA26SE103_ERD.drawio`, when documenting the approved physical data model.
   - Current Drive source:
     `https://drive.google.com/file/d/1NVmehb2jDjxqFU0qHmlqPug4GGB30613/view?usp=drivesdk`
   - Historical ERD reference, retained for traceability only:
     `https://drive.google.com/file/d/1xq6EkquGppDAcLqcVndJ4AhB9ILHj180/view?usp=drivesdk`
   - **Do not change this file unless you are assigned to ERD/database work.**

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
Approved ERD / database migration for physical persistence
        ↓
Report 3 / Figma / implementation details
```

Report 1 and Report 2 were recently updated to remove the old confidence-based incident review flow. Current review is **severity/type based**: Critical or review-required Incident Types go to Operator before assignment; non-review Info/Warning incidents may auto-route to eligible Staff.

If code and a document disagree, raise the mismatch before adding more behavior.

---

# 2. Product boundary

## 2.1 In scope

The system supports:

- one supermarket branch for project evaluation;
- Supermarket / Floor / Zone configuration;
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
- supermarket/floor/zone setup;
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

Operator pre-dispatch review is **conditional**: Critical or review-required Incident Types go to Operator before assignment; non-review Info/Warning incidents may auto-route to eligible Staff in the affected Zone.

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

Prepare the supermarket, floor/zone structure, cameras, camera-zone coverage, connection settings, and monitoring configuration so the system can safely enter operational monitoring.

### Main path

```text
Admin Login
    ↓
Set up Store / Floor / Zone
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
Camera / stream becomes OFFLINE / ERROR
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
- Supermarket;
- Floor;
- Zone;
- Camera;
- CameraConnection;
- CameraZoneMapping;
- MonitoringConfiguration;
- monitoring rules/settings required by the approved ERD;
- CameraHealthEvent;
- basic account administration required to operate the system (**supporting Admin capability; not a required Mainflow slide step**).

IncidentType configuration is logically system setup, and its physical persistence must follow ERD v3. The current MF-01 code may keep this behind a stable interface until the MF-02 persistence increment is assigned.

---

## MF-02 — AI Monitoring → Incident → Assignment

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
Incident Created / Updated
    ↓
Severity & Routing
    ↓
┌───────────────────────────────────────────────┬────────────────────────────────────┐
│ Info / Warning AND not review-required       │ Critical OR review-required        │
│                                               │                                    │
│ Notify eligible Staff in affected Zone        │ Operator Review                    │
│        ↓                                      │        ↓                           │
│ Staff accepts                                 │ Decide handling                    │
│        ↓                                      │        ↓                           │
│ Task Assigned                                 │ Manual Assign / Reassign           │
│                                               │        ↓                           │
│                                               │ Task Assigned                      │
└───────────────────────────────────────────────┴────────────────────────────────────┘
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
Incident Created / Updated
    ↓
Severity & Routing
    ↓
same conditional routing policy as above
```

### Auto-route fallback

For an Info/Warning incident that is allowed to auto-route:

```text
Notify eligible Staff in affected Zone
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
→ Incident Created / Updated
→ Severity & Routing
```

### Incident behavior

- only one open Incident of the same IncidentType may exist in the same Zone;
- repeated OperationalEvents update the existing open Incident rather than creating duplicates;
- after closing, cooldown must pass before the same type can be raised again in that Zone;
- **Critical or review-required Incident Types go to Operator before assignment**;
- **non-review Info/Warning incidents may auto-route to eligible Staff in the affected Zone**;
- Operator may still inspect/intervene while an Incident is open;
- Operator also handles auto-route fallback when notification/acceptance fails.

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

### Slide / swimlane baseline

```text
Camera path:
Camera Stream
→ Detection & Tracking
→ Operational Measurement
→ Rule Evaluation
→ Incident Created / Updated
→ Severity & Routing

Info / Warning, non-review:
Severity & Routing
→ Notify Eligible Staff
→ Receive Alert
→ Accept Task
→ Task Assigned

Critical / review-required:
Severity & Routing
→ Operator Review / Intervene
→ Manual Assign / Reassign
→ Task Assigned

Staff report:
Find Issue
→ Report Text / Image / Voice
→ Extract Report Data
→ Confirm / Correct Extracted Data
→ Incident Created / Updated
→ same Severity & Routing branch

Auto-route fallback:
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
Staff Accepts
    ↓
Accepted
    ↓
In Progress
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
- **no shift-swap approval workflow in the current baseline**;
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

## IncidentType

Reusable business classification.

### AI-detected baseline IncidentTypes

1. Long Queue
2. Excessive Waiting Time
3. Overcrowding / Congestion
4. Checkout Capacity Issue

### Staff-report-only baseline IncidentTypes

5. Spill / Broken Equipment
6. Equipment Malfunction
7. Pathway Obstruction
8. Cleanliness Issue
9. Safety Hazard
10. Other Operational Issue

AI-detected IncidentTypes use operational measurements + MonitoringRules.

Staff-report-only IncidentTypes do not require an AI measurement.

## MonitoringRule

Defines when an AI measurement becomes a business Incident for a particular monitoring context/Zone.

Conceptually it owns:

- IncidentType reference;
- Warning threshold;
- Critical threshold;
- threshold unit;
- sustain time;
- cooldown;
- enabled/status.

The same IncidentType may use different thresholds in different Zones.

**ERD v3 defines the concrete physical MonitoringRule structure. Do not hardcode an incompatible schema or add a competing migration.**

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

### ERD v3 persistence

`Zone.area_m2` exists in ERD v3. It is the physical area value used for the agreed people-per-square-metre density formula and may be entered by Admin for Zones that use physical density.

Do **not** implement camera-based physical area estimation, homography-based local-density estimation, or 3D calibration just to obtain square metres unless the scope is formally expanded.

Keep heatmap implementation independent from `area_m2`; heatmaps use tracked positions and do not require real-world square metres.

---

# 9. MF-01 database baseline

The existing MF-01 database draft contains the following core entities.

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

One branch for the project baseline.

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

Current important values:

- code;
- name;
- zone type;
- `map_polygon`;
- `area_m2` from ERD v3 for physical density calculations;
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

Health states currently expected:

```text
UNKNOWN
ONLINE
OFFLINE
ERROR
```

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
DEMO
```

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

Separate system-health history for camera failures/recovery.

Example states:

```text
OPEN
INVESTIGATING
RESOLVED
```

`investigated_by_user_id` is valid here because it records who actually investigated a health problem.

This is unrelated to the removed account/camera-review fields.

## ERD v3 physical baseline and remaining alignment

### `MonitoringRule`

ERD v3 defines the physical rule structure. Its current concepts include:

```text
rule_id
config_id
incident_type_id
warning_threshold
critical_threshold
threshold_unit
sustain_sec
cooldown_sec
parameters_json
enabled
UNIQUE(config_id, incident_type_id)
```

Do not resurrect the historical generic `rule_type` / `threshold_value` / `severity` shape. Do not add a competing migration; the current MF-01 code does not yet expose full MonitoringRule persistence.

### `IncidentType`

ERD v3 defines current physical concepts including:

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

The business behavior is agreed: Critical or an Incident Type configured as requiring Operator review goes to Operator before assignment. ERD v3 has no obvious `requires_operator_review` field, so the exact persistence representation remains a documented BR ↔ ERD synchronization item. Do not invent a column or create a competing table/migration.

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
CreateSupermarket
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

Do not freeze or invent scaffolded contracts for the remaining unresolved alignment points: the persistence representation of Incident Type "requires Operator review", multiple-camera measurement-source selection, checkout-counter representation, Manager-on-duty representation, or the operational use of `ZoneAdjacency`. ERD v3 already defines `Zone.area_m2`, the physical `MonitoringRule` and `IncidentType` structures, and the `ZoneAdjacency` entity.

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

```text
GET    /supermarkets
POST   /supermarkets
GET    /supermarkets/{id}
PATCH  /supermarkets/{id}
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

Exact MonitoringRule/IncidentType endpoints should follow the merged ERD.

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
ERROR
```

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
2. Admin creates/opens the supermarket.
3. Admin creates a Floor.
4. Admin attaches a floor-map asset.
5. Admin draws a Zone polygon on the floor map.
6. Admin registers a Camera on that Floor.
7. Admin places the Camera on the floor map.
8. Admin configures RTSP/demo connection settings.
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
19. Health becomes OFFLINE/ERROR and a CameraHealthEvent opens.
20. Admin investigates/restores the connection.
21. Health event resolves.

Although ERD v3 defines MonitoringRule/IncidentType persistence, steps that depend on the unresolved Incident Type review-flag representation may be stubbed behind a stable domain/service interface rather than committing a competing migration.

---

# 18. MF-01 Definition of Done

MF-01 is not "done" just because CRUD pages exist.

It is done when a working end-to-end setup can be demonstrated and tested.

Minimum DoD:

- [ ] Admin authentication works.
- [ ] RBAC protects Admin configuration endpoints.
- [ ] Supermarket/Floor/Zone CRUD works.
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
create supermarket
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

Integrate future MonitoringRule/IncidentType persistence against ERD v3 and the approved SQL schema. Coordinate only the remaining review-flag representation before adding behavior; do not create a competing migration.

## Web/Admin track

Can start against API contracts/mocks:

- login;
- supermarket/floor management;
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

- RTSP/demo adapter;
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

# 23. Future entity needs — do not prematurely force into MF-01

Expected later entities/concepts include:

```text
IncidentType
OperationalEvent
Incident
Notification
Staff
Shift
ShiftAssignment
Task
TaskEvidence
Recommendation
AuditLog
```

Potential concepts such as Manager-on-duty are required by later escalation/shift rules.

ZoneAdjacency exists in ERD v3. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.

Do not add speculative tables simply to make the schema "look complete".

Add them when the corresponding Mainflow contract is being implemented and the ERD is approved.

---

# 24. Later-flow rules that MF-01 must not block

Even though MF-01 does not implement them yet, avoid architectural decisions that make these impossible:

## Incident deduplication

```text
one open incident
per IncidentType
per Zone
```

## Sustain time

AI condition must remain true for the MonitoringRule sustain duration before Incident creation.

Current baseline:

```text
30 s
```

## Cooldown

After closure, same IncidentType + Zone is suppressed until cooldown passes.

Current baseline:

```text
5 min
```

## Notification / assignment

Routing is severity/type based:

```text
CRITICAL or review-required
→ Operator review before assignment

INFO / WARNING and not review-required
→ notify eligible Staff in affected Zone
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

ZoneAdjacency exists in ERD v3. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.

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

`Zone.area_m2` is present in ERD v3 and is the physical area input for this formula.

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
- Supermarket/Floor/Zone screens;
- Camera setup screens;
- connection test/preview screens;
- monitoring config screens;
- health screens;
- authorization matrix;
- non-screen functions for connection health/monitoring startup;
- entity descriptions.

---

# 33. Implementation questions that are currently OPEN

Do not silently decide these in code without team agreement. ERD v3 facts are not open questions; the items below are the remaining alignment points:

1. **Incident Type review flag representation**
   - Business behavior requires Operator review for Critical or review-required Incident Types.
   - ERD v3 has no obvious `requires_operator_review` field.
   - Agree the physical representation before adding full IncidentType persistence.

2. **Measurement source when several cameras monitor one Zone**
   - designated source camera vs non-overlapping measurement ROIs;
   - no cross-camera ReID.

3. **Checkout counter representation**
   - needed later for checkout utilization/capacity;
   - do not invent complex POS integration.

4. **Manager-on-duty representation**
   - required later for escalation;
   - likely derived from shift/duty assignment.

5. **Operational use of `ZoneAdjacency`**
   - ZoneAdjacency exists in ERD v3. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.


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
21. Do not auto-dispatch every Incident: Critical or review-required Incident Types require Operator review before assignment.
22. For non-review Info/Warning incidents, notify eligible Staff in the affected Zone; fallback to Operator when no eligible Staff or acceptance retries fail.
23. ZoneAdjacency exists in ERD v3. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.
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
MonitoringRule / IncidentType
   (ERD v3 physical baseline; review-flag representation pending alignment)

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

The team should **coordinate before freezing migrations** for:

```text
measurement-source selection
IncidentType review-flag representation
measurement-source selection
checkout-counter representation
Manager-on-duty representation
operational use of ZoneAdjacency
```

Those are not reasons to block the rest of MF-01.

---

# 37. Final goal of MF-01

At the end of MF-01, a reviewer should be able to watch this happen:

> An Admin logs in, sets up the store/floor/zone structure, registers and configures a camera, triggers connection testing and preview, maps the camera to one or more valid Zones using normalized ROIs, configures and activates monitoring, sees continuous camera/stream health monitoring, then investigates, retests, and restores a detected connection/stream/configuration issue.

That is an end-to-end **setup/configuration Mainflow**, not a collection of disconnected CRUD screens.

The next increment, MF-02, begins when camera/video data becomes operational measurements/events and those events begin producing and routing Incidents.

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

## Current

- Model confidence filters raw detections only.
- Operator pre-dispatch review is conditional: Critical or review-required Incident Types go to Operator before assignment.
- Non-review Info/Warning Incidents may auto-route to eligible Staff in the affected Zone; failed acceptance falls back to Operator.
- Staff-report extraction is a separate path from camera Detection & Tracking.
- Operator independently verifies submitted evidence.
- MF-04 Planning and Analytics are independently enterable.
- AI recommendations are advisory; they do not automatically change store operations.
- `Zone.area_m2`, the physical `MonitoringRule` and `IncidentType` structures, and `ZoneAdjacency` are represented in ERD v3. The Incident Type review-flag persistence and the other alignment items in section 33 remain open.
- ZoneAdjacency exists in ERD v3. Its use for MF-02 dispatch is currently an unresolved BR/Mainflow alignment point. Do not implement or remove adjacent-Zone dispatch until the behavior is explicitly confirmed by the team.
