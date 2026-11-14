# MF-01 API contract

Base URL local: `http://localhost:5080/api`. JSON dùng camelCase; IDs là UUID; dates/timestamps gửi theo ISO 8601 UTC. Mọi thao tác ghi cấu hình yêu cầu JWT role ADMIN. `POST /auth/login` public; `GET /auth/me` dành cho mọi account ACTIVE đã đăng nhập.

Quyền đọc cho màn vận hành (Operator floor map, camera live):

| Quyền | Endpoints |
|---|---|
| ADMIN, OPERATOR, MANAGER | `GET /supermarkets`, `GET /supermarkets/{id}`, `GET /supermarkets/{id}/floors`, `GET /floors/{id}`, `GET /floors/{id}/map`, `GET /floors/{id}/zones`, `GET /zones/{id}`, `GET /zones/{id}/adjacent-zones`, `GET /floors/{id}/cameras`, `GET /cameras/{id}`, `GET /cameras/{id}/zones`, `GET /cameras/{id}/preview`, `POST /cameras/{id}/playback` |
| ADMIN, OPERATOR | `/cameras/{id}/ai-preview/start`, `status`, `frame`, `frame/next`, `stop` (mỗi Start thành công cấp một viewer lease; nhiều viewer dùng chung worker của cùng camera; tối đa hai camera worker) |
| Chỉ ADMIN | Mọi setup mutation không được liệt kê riêng ở hai dòng trên; `GET /cameras/{id}/connection` (có stream URI), `POST .../connection/test`, accounts, monitoring, camera health |

Controller và use case cùng kiểm tra quyền; STAFF không đọc được dữ liệu setup.

| Capability | Routes |
|---|---|
| Auth | POST /auth/login; GET /auth/me |
| Accounts | GET/POST /users; PATCH /users/{id}; POST /users/{id}/enable hoặc disable; GET /roles; GET /staff-specialties |
| Supermarket | GET/POST /supermarkets; GET/PATCH /supermarkets/{id} |
| Floors | GET/POST /supermarkets/{id}/floors; GET/PATCH /floors/{id}; PATCH /floors/{id}/details; POST/GET /floors/{id}/map |
| Zones | GET/POST /floors/{id}/zones; GET/PATCH /zones/{id}; GET/PUT /zones/{id}/adjacent-zones; normalized polygon, optional `colorHex`/`areaM2`, and Zone-level `minStaff >= 0` configured by Admin for Manager coverage planning |
| Cameras | GET/POST /floors/{id}/cameras; GET/PATCH /cameras/{id} |
| Connection | GET/PUT /cameras/{id}/connection; POST .../test, .../enable, .../disable |
| Preview | GET /cameras/{id}/preview: image/svg+xml cho demo, image/jpeg cho FFmpeg |
| Browser playback | POST /cameras/{id}/playback: provision MediaMTX path và trả WebRTC/WHEP URL đã sanitize |
| Geometry | GET /cameras/{id}/zones; PUT/DELETE /cameras/{id}/zones/{zoneId} |
| Checkout configuration | [Checkout API](CHECKOUT_API.md): GET/POST /zones/{zoneId}/checkout-counters; GET/PATCH/DELETE /checkout-counters/{counterId}; GET /cameras/{cameraId}/checkout-mappings; GET /checkout-counters/{counterId}/camera-mappings; PUT/DELETE /checkout-counters/{counterId}/camera-mappings/{cameraId} |
| Checkout runtime | GET /zones/{zoneId}/checkout-runtime (ADMIN/OPERATOR/MANAGER; no-store, missing measurements stay null) |
| AI catalog | GET /incident-types (ADMIN, read-only AI types) |
| Monitoring | GET/PUT/DELETE /zones/{zoneId}/monitoring; GET .../review; POST .../activate, .../deactivate |
| Setup dashboard | GET /setup/overview (ADMIN, read-only saved-data snapshot) |
| Health | GET /cameras/{id}/health; POST /cameras/{id}/health/check; GET /camera-health-events?cameraId=...&status=... |
| Investigation | POST /camera-health-events/{id}/investigate; POST .../resolve |
| Development demo | POST /demo/cameras/{id}/state?online=false hoặc true |

Liveness `/health/live`, readiness `/health/ready` nằm ngoài prefix `/api`. Readiness chỉ kiểm tra SQL connectivity, không xác nhận schema/seed/AI/camera. Swagger JSON `/swagger/v1/swagger.json` và UI `/swagger` bật ở Development hoặc khi cấu hình tường minh `Swagger:Enabled=true`; schema được tạo trực tiếp từ controller và DTO. Route demo vẫn chỉ có trong Development.

[Deploy Dev](DEPLOY_DEV.md) dùng public base URL `https://supermarket-api-dev.kitsuracloud.com/api`. Sau NPM, bật `ReverseProxy:Enabled` và liệt kê IP proxy trong `ReverseProxy:KnownProxies`; chỉ một hop `X-Forwarded-For`/`X-Forwarded-Proto` từ proxy tin cậy được dùng trước rate limit/auth. Request Host không lấy từ `X-Forwarded-Host`. Floor-map upload sinh URL theo scheme/host request đã xử lý; các tham chiếu cũ không tự rewrite. JWT/RBAC và response DTO không thay đổi.

`openapi.json` trong thư mục này là contract xuất từ API đã chạy để frontend import; xuất lại khi controller/DTO thay đổi. Runtime Swagger luôn là bản contract hiện tại.

### WebRTC playback qua MediaMTX

`POST /api/cameras/{id}/playback` dành cho ADMIN, OPERATOR và MANAGER. Camera phải `ACTIVE`; connection phải là `LIVE/RTSP`, đã test `SUCCESS` và enabled. Backend sinh path ổn định `cam-{cameraId:N}`, tạo hoặc đồng bộ path đó qua MediaMTX Control API nội bộ, rồi trả:

```json
{"cameraId":"<uuid>","transport":"WEBRTC","url":"https://stream-dev.kitsuracloud.com/cam-<uuid-without-dashes>/whep"}
```

Response không chứa RTSP URI, username, password hoặc protected credential reference. Cấu hình Control API thay đổi runtime của MediaMTX và không được lưu ngược vào `mediamtx.yml`; endpoint luôn bảo đảm lại path trước khi trả URL nên path được khôi phục sau khi MediaMTX restart. Nguồn FILE/HTTP/HLS hiện trả 422 `PLAYBACK_UNSUPPORTED` và frontend tiếp tục dùng JPEG/file preview hiện có. Gateway chưa bật trả 503 `MEDIA_GATEWAY_NOT_CONFIGURED`; gateway timeout/unavailable trả 504 `MEDIA_GATEWAY_TIMEOUT` hoặc 503 `MEDIA_GATEWAY_UNAVAILABLE`.

`MediaGateway:ControlBaseUrl` chỉ trỏ tới Docker network riêng, không public port 9997. `ControlUsername`/`ControlPassword` là secret deployment dùng riêng cho quyền MediaMTX `api`; không lưu trong DB, source control hoặc response. `PublicBaseUrl` là HTTPS origin mà browser truy cập. Endpoint này mới quản lý mapping động; viewer authorization trực tiếp tại WHEP ingress là bước hardening riêng, vì URL MediaMTX hiện vẫn public nếu người ngoài biết path.

### Dashboard tổng quan MF-01

`GET /api/setup/overview` chỉ ADMIN, trả `Cache-Control: no-store`. Controller và application cùng kiểm tra quyền. Đọc tuần tự theo batch trong transaction của `ISetupStore`; không tạo configuration cho zone chưa có, không probe camera, enable nguồn hoặc activate monitoring.

Response gồm:

- `generatedAt`, `hasDefaultStore`: thời điểm lấy snapshot UTC và tình trạng record store mặc định.
- `totals`: floor/zone/camera counts, configured zones, ACTIVE configurations, configurations đủ điều kiện Review/Activate, cameras ONLINE trong nhóm ACTIVE + connection enabled và unresolved health events.
- `steps[]`: `code, name, completed, total, description` cho floor-map/zone, source, test/enable, mapping/ROI, incident rules và activation. Mỗi bước đếm record đã lưu; `0/0` không có nghĩa setup hoàn tất. Test success không chứng minh Admin đã xem preview; preview completion chưa được lưu.
- `floors[]`: thông tin floor, `hasMap`, `zones[]`. Từng zone có configuration summary hoặc null, `setupReady`, `canActivate`, sanitized mapped-camera/ROI summaries, issues và warnings. `setupReady` dùng chung policy với Monitoring Review/Activate; `canActivate` còn yêu cầu configuration chưa ACTIVE. Review và Activate vẫn phải đọc lại dữ liệu khi Admin thao tác.
- `cameras[]`: lifecycle `status`, transport `healthStatus`, `monitoringReadiness`, `processingAvailability`, `activeHealthIssues`, `lastSeenAt`, source type/protocol, connection validation/enabled/test result/time và issues. Không trả stream URI, username hoặc credentials. Test time và last received frame là hai thời điểm khác nhau.
- `healthEvents[]`: unresolved events, mới nhất trước, gồm ID/camera code/type/status/detectedAt; không chứa probe error hoặc secret.

Configuration ACTIVE và camera health độc lập: camera OFFLINE hoặc visual issue không tự deactivate configuration. `healthStatus` chỉ là `UNKNOWN/ONLINE/OFFLINE`; ONLINE không có nghĩa sẵn sàng cho monitoring. `monitoringReadiness` là `READY/NOT_READY`, còn `processingAvailability` là `AVAILABLE/UNAVAILABLE/UNKNOWN`. Dashboard frontend polling snapshot mỗi 30 giây khi tab đang hiện; nút Refresh chỉ đọc. Check health gọi endpoint probe ADMIN hiện có; màn Cameras cung cấp sửa source và Test/Enable/Preview. Investigation/Resolve workflow vẫn dùng endpoints health-event hiện có, chưa được tích hợp thành form trên dashboard.

### Camera health và monitoring readiness

Worker chạy một lượt ngay khi backend khởi động, sau đó theo `CameraHealth:IntervalSeconds`, chỉ với connection enabled. `MaxConcurrentChecks` giới hạn số probe song song. Mỗi probe lấy một frame nhỏ; AI service endpoint nội bộ `/frame-health` chỉ decode/đo contrast, edge density và Laplacian blur trên CPU, không chạy YOLO/GPU. Không có darkness classifier. Frozen event được contract hỗ trợ nhưng chưa tự phát hiện nếu source không cung cấp freshness/sequence đáng tin cậy.

`GET /cameras/{id}/health` và `POST /cameras/{id}/health/check` trả:

```json
{"cameraId":"<uuid>","connectionStatus":"ONLINE","processingAvailability":"AVAILABLE","monitoringReadiness":"READY","activeHealthIssues":[],"lastSeenAt":"2026-10-03T12:00:00Z","observedAt":"2026-10-03T12:00:00Z"}
```

`STREAM_UNAVAILABLE` mở/resolve theo transport. Visual event dùng hysteresis cấu hình `BadObservationsToOpen` và `GoodObservationsToResolve`, unique theo camera + event type trong application runtime; các loại độc lập có thể đồng thời OPEN/INVESTIGATING. V1 gồm `CAMERA_VIEW_BLOCKED`, `CAMERA_VIEW_BLURRED`, `CAMERA_FRAME_INVALID`; `CAMERA_VIEW_FROZEN` dành cho adapter có freshness metadata đáng tin cậy. AI service unavailable chỉ làm processing unavailable/readiness NOT_READY, không tạo CameraHealthEvent và không đổi camera ONLINE thành OFFLINE. Health issue camera-wide; monitoring configuration vẫn ACTIVE. Monitoring runtime dùng readiness này làm fail-closed gate và không phát measurement/rule/event/incident khi camera `NOT_READY`.

Ví dụ zone:

```json
{"code":"CHECKOUT","name":"Checkout area","zoneType":"CHECKOUT","status":"ACTIVE","colorHex":"#F97316","areaM2":42.5,"mapPolygon":[{"x":0.1,"y":0.1},{"x":0.8,"y":0.1},{"x":0.1,"y":0.8}]}
```

Ví dụ camera connection:

```json
{"sourceType":"LIVE","protocol":"RTSP","streamUri":"rtsp://10.0.1.21/main","username":"camera-user","password":"<camera credential>"}
```

`GET /api/cameras/{id}/connection` trả lại URL, username và `hasCredentials` để prefill form chỉnh sửa, nhưng không trả password. Khi sửa connection mà không đổi password, client gửi `password: null` và `preservePassword: true`; mặc định `preservePassword: false` vẫn thay hoặc xóa credential như trước.

Ví dụ mapping:

```json
{"roiPolygon":[{"x":0.1,"y":0.1},{"x":0.8,"y":0.1},{"x":0.1,"y":0.8}],"status":"ACTIVE"}
```

### Floor creation and metadata editing

`POST /api/supermarkets/{id}/floors` (ADMIN) dùng `FloorRequest` hiện có. Store Layout gửi `floorNumber`, `name` và `mapAssetUrl/mapWidth/mapHeight: null`; tạo floor trước, upload map sau. Store là default store từ seed, không thêm flow tạo/chọn store.

`PATCH /api/floors/{id}/details` (ADMIN) nhận `{"floorNumber":2,"name":"Upper floor"}`. Name trim, bắt buộc, tối đa 100 ký tự; floorNumber là int và unique trong cùng store (409 `DUPLICATE` nếu trùng). Không giới hạn tầng phải dương; tầng 0 hoặc tầng âm được phép. Endpoint chỉ thay name/number, giữ floorId, storeId và metadata map hiện tại đọc từ DB, cùng các zone/camera/mapping. Không gửi lại map URL cũ từ lúc mở form. Response là Floor. Full `PATCH /floors/{id}` vẫn giữ contract cũ; upload map dùng endpoint riêng.

### Zone adjacency

`GET /api/zones/{zoneId}/adjacent-zones` dành cho ADMIN, OPERATOR và MANAGER; trả các Zone kề trực tiếp theo `code`, không trả hàng liên kết nội bộ. `PUT /api/zones/{zoneId}/adjacent-zones` chỉ ADMIN, nhận `{"adjacentZoneIds":["<zone UUID>"]}` và thay toàn bộ tập Zone kề của Zone nguồn trong một transaction. Gửi mảng rỗng để bỏ toàn bộ liên kết của Zone nguồn.

Mỗi quan hệ được lưu hai chiều. API xóa cả hai chiều của liên kết cũ trước khi tạo tập mới, nhưng không chạm vào quan hệ giữa các Zone khác. Zone nguồn/đích phải tồn tại, khác nhau và cùng Floor; ID lặp trả 422 `ZONE_ADJACENCY_DUPLICATE`, tự nối trả 422 `ZONE_ADJACENCY_SELF`, khác Floor trả 422 `ZONE_ADJACENCY_CROSS_FLOOR`. Quan hệ này chỉ là dữ liệu không gian được Admin xác nhận cho dispatch; API không tự suy ra từ polygon hoặc vị trí trên bản đồ.

### Monitoring Draft / Review / Activate / Delete

`GET /api/incident-types` tạm giữ contract frontend cũ nhưng dữ liệu measurement/default được lấy từ `OperationalEventType`; `incidentTypeId` chỉ dùng để map catalog cũ sang event type tương ứng khi lưu. Rule `OPERATIONAL_EVENT` chỉ persist `event_type_id`, không persist `incident_type_id`. Catalog read-only, không gồm staff-reported-only types. Checkout Capacity chưa supported vì counter/composite measurement chưa được chốt.

`PUT /zones/{zoneId}/monitoring` tạo hoặc thay toàn bộ Draft + rules trong transaction. `rules` bắt buộc và phải có ít nhất một incident rule; `[]` trả 422 `RULES_REQUIRED`, không tạo hoặc thay cấu hình đã lưu. Omit rule khỏi array để xóa nhưng phải giữ ít nhất một rule. Mỗi type tối đa một rule/config. Khi cấu hình đã tồn tại, gửi `expectedUpdatedAt` từ GET/Save gần nhất; thiếu/stale trả 409 `CONFIGURATION_CHANGED`. ACTIVE trả 409 `MONITORING_ACTIVE`, không tự deactivate khi Save. Disabled Draft rules vẫn được lưu; Activate cần ít nhất một enabled supported rule và readiness hợp lệ.

Confidence 0–1 và threshold tối đa 4 decimal places; `0 <= warning < critical` (decimal(18,4)). PEOPLE phải nguyên, MINUTES/PEOPLE_PER_M2 có thể thập phân. Sustain/cooldown là int không âm theo giây; 0 được lưu đúng. Rule phải tham chiếu AI type; enabled type phải ACTIVE/supported. Disabled Checkout Draft vẫn cần explicit non-empty unit/valid thresholds. `parametersJson` null hoặc JSON object/array, tối đa 16000 ký tự; bảo toàn tham số mở rộng, không đánh giá trong MF-01.

Response gồm `configId, zoneId, name, confidenceThreshold, status, createdByUserId, createdAt, updatedAt, rules[]`; từng rule có `ruleId, incidentTypeId, incidentCode, incidentName` và toàn bộ fields cấu hình.

Ví dụ tạo Draft:

```json
{
  "name": "Queue monitoring",
  "confidenceThreshold": 0.5,
  "rules": [{
    "incidentTypeId": "<AI incident-type UUID from GET /api/incident-types>",
    "warningThreshold": 3, "criticalThreshold": 5, "thresholdUnit": "PEOPLE",
    "sustainSec": 30, "cooldownSec": 300, "enabled": true, "parametersJson": null
  }]
}
```

`GET .../review` trả `configuration, zone, cameras[], issues[], warnings[], canActivate`. Camera summary gồm mapping/ROI, source type/protocol, enabled, test result/time, readiness và issues; không trả stream URI, username hoặc camera credentials. Cần zone ACTIVE, ít nhất một supported rule enabled và đúng một ACTIVE mapped camera cùng tầng có ROI hợp lệ, nguồn LIVE HTTP/RTSP/HLS hoặc RECORDED FILE tested-success/enabled. Nguồn tổng hợp `DEMO` không còn được chấp nhận; row cũ phải cấu hình lại thành LIVE hoặc RECORDED. Density enabled cần area_m2 > 0 nhưng chưa được runtime hỗ trợ. Review kiểm tra dữ liệu đã lưu, không thay một connection probe mới. Multi-camera selection/overlap còn mở nên nhiều ACTIVE mappings là blocker. Recorded source là test/fallback và được cảnh báo rõ; runtime hiện hỗ trợ count/queue theo increment bên dưới.

`POST .../activate` và `POST .../deactivate` nhận:

```json
{"expectedUpdatedAt":"<updatedAt from reviewed/saved configuration, ISO UTC>"}
```

Activate revalidates readiness trong Serializable transaction (409 `MONITORING_NOT_READY` nếu blocker), không tin review cũ; đúng một ACTIVE camera mapping per-zone và enabled count/queue rule cần thiết. Response là ConfigurationView status ACTIVE/INACTIVE và updatedAt mới, **không** khẳng định worker đã RUNNING. Worker đọc cấu hình rồi khởi động/reconfigure AI. ADMIN only; Deactivate trước khi sửa rule/confidence/source/mapping/ROI. Thiếu schema runtime trả503 `MONITORING_SCHEMA_NOT_READY`.

`DELETE /api/zones/{zoneId}/monitoring` (ADMIN) nhận JSON body `{"configId":"<saved config UUID>","expectedUpdatedAt":"<saved updatedAt, ISO UTC>"}`. Chỉ xóa DRAFT hoặc INACTIVE; ACTIVE trả 409 `MONITORING_ACTIVE`, phải Deactivate trước. Config ID hoặc timestamp thay đổi trả 409 `CONFIGURATION_CHANGED`, kể cả khi config cũ đã bị xóa và tạo lại. Không có config trả 404. Thành công trả 204, xóa config và rules của nó trong cùng transaction; Zone, Camera và CameraZoneMapping/ROI được giữ. Historical OperationalEvent/Incident được giữ; `OperationalEvent.rule_id` và `Incident.monitoring_rule_id` được detach thành null, metadata snapshot không đổi. Các FK khác vẫn được tôn trọng và lỗi persistence rollback transaction. Sau xóa, GET monitoring trả 404, setup overview có configuration null; có thể tạo Draft mới. UI yêu cầu xác nhận tên config/zone/floor, có Cancel/close/Escape; thất bại giữ config và hộp thoại để Admin đọc lỗi.

Deactivate là desired SQL state trước khi worker nhả owner. Runtime trả `STOPPING / MONITORING_STOP_PENDING` trong khoảng này, không báo STOPPED sớm. Reactivate khi không còn ACTIVE configuration trên camera nhưng owner chưa được nhả trả409 `MONITORING_STOP_PENDING`; đợi STOPPED rồi thử lại. Thêm/reconfigure zone khi camera còn một configuration ACTIVE khác vẫn được phép và không rewind.

Admin kiểm thử saved confidence bằng `POST /api/cameras/{id}/ai-preview/start?zoneId=<uuid>`: mapping/ROI/config hợp lệ, nguồn tested/enabled. Mỗi Start thành công trả `viewerLeaseId` riêng. Khi camera thuộc active monitoring, Start chỉ attach vào worker đã có; nếu SQL đã ACTIVE nhưng worker còn đang khởi động, API trả 409 `AI_MONITORING_START_PENDING` để client retry có giới hạn và không tạo worker cạnh tranh. Draft có cấu hình khác worker đang chạy trả 409 `AI_PREVIEW_CONFIGURATION_CONFLICT`, không restart/ngắt viewer khác. Status gồm `sessionId, purpose(PREVIEW|MONITORING), configurationFingerprint, annotationContext, viewerLeaseId`. Boxes dùng annotation context; ROI counts dựa confidence từng zone. Zone query ADMIN only; ordinary view ADMIN/OPERATOR.

`GET /api/cameras/{id}/ai-preview/status`, `GET .../frame` và `GET .../frame/next` bắt buộc header `X-AI-Viewer-Lease: <viewerLeaseId>`. Thiếu/sai UUID bị request validation từ chối; lease đã hết hạn hoặc thuộc generation cũ trả 409 `AI_VIEWER_LEASE_EXPIRED`. `POST .../stop` nhận JSON `{"viewerLeaseId":"<uuid>"}` và chỉ release viewer đó; không dừng viewer khác hoặc monitoring owner. Không dùng lease trong URL/query/log thường lệ.

`GET /api/cameras/{id}/ai-preview/frame/next?afterSequence=N&afterSessionId=<uuid>` (ADMIN/OPERATOR) chờ tối đa 1 giây để có JPEG đã vẽ box/track ID mới. `200 image/jpeg` trả `X-Frame-Sequence` và `X-Session-Id` (CORS expose hai header), `204` nghĩa chưa có frame mới; cả hai `Cache-Control: no-store`. Gửi lại session ID giúp client nhận frame đầu khi phiên AI khởi động lại và sequence reset. Endpoint `GET .../frame` cũ vẫn được bảo vệ bằng lease nhưng đã deprecated. React dùng `frame/next` qua BE/JWT, không tải lặp JPEG và không có khoảng nghỉ 250 ms sau mỗi frame. Tốc độ thực tế vẫn phụ thuộc video nguồn, xử lý AI/JPEG, proxy và trình duyệt; monitoring worker tiếp tục xử lý tuần tự mọi frame để giữ đúng các mốc thời gian nguồn.

Capacity tính theo camera worker, không theo số viewer: nhiều viewer cùng camera dùng chung một reader/YOLO/ByteTrack; tối đa hai camera có thể xử lý đồng thời và camera thứ ba nhận 409 `AI_SESSION_CAPACITY`. Hai camera có tracker độc lập; track ID chỉ có nghĩa trong camera/session hiện tại, không được re-identify, deduplicate hay persist giữa camera. Lease registry nằm trong một process nên AI service phải chạy đúng một Uvicorn worker.

Rule validation codes: `INVALID_CONFIDENCE`, `INVALID_THRESHOLDS`, `INVALID_RULE_UNIT`, `INVALID_RULE_TIMING`, `INVALID_RULE_PARAMETERS`, `INCIDENT_TYPE_NOT_AI`, `INCIDENT_TYPE_INACTIVE`, `RULE_UNSUPPORTED`, `DUPLICATE_RULE`. Review issues thêm `NO_ENABLED_RULES`, `ZONE_AREA_REQUIRED`, `CAMERA_NOT_READY`, `AI_SOURCE_UNSUPPORTED`.

### ROI monitoring / incident feed increment

Catalog có additive `measurementOptions[]` (`mode, unit, supported, reason, defaultWarningThreshold, defaultCriticalThreshold`). Overcrowding count là PEOPLE_COUNT/PEOPLE, cần threshold nguyên do Admin nhập và `parametersJson` object có `measurementMode:"PEOPLE_COUNT"`; giữ các key khác. Density cũ PEOPLE_PER_M2 không tự convert, chưa runtime. Queue QUEUE_LENGTH/PEOPLE supported. Waiting/Checkout disabled Draft; legacy enabled unsupported phải tắt hoặc chuyển mode rõ trước activation. Enabled unsupported trả `RULE_RUNTIME_UNSUPPORTED`; nhiều ACTIVE mappings trả `MEASUREMENT_SOURCE_AMBIGUOUS` trong review. Event history được giữ khi bỏ rule, reference rule_id trở null. Save phải giữ ít nhất một rule; array rỗng trả422 RULES_REQUIRED.

`GET /api/cameras/{id}/monitoring-runtime` — JWT **ADMIN/OPERATOR**, read-only. Response `cameraId,state,reason,errorCode,videoSourceType,sessionId,sourceElapsedMs,observedAt,isStale,annotationContext,zones[]`. State STARTING/RUNNING/ERROR/COMPLETED, hoặc STOPPED với NO_ACTIVE_CONFIGURATION. Per-zone: `zoneId,zoneName,configId?,configVersion?,configurationStatus,confidence,peopleCount?,queueCount?,issueCode?,rules[]`. Counts chưa đo là **null**, không giả0. Per-rule: `ruleId,incidentCode,incidentName,mode,unit,warningThreshold,criticalThreshold,sustainSec,cooldownSec,clearDurationSec,metricValue?,warningProgressMs,criticalProgressMs,cooldownRemainingSec,reason,incidentId?`. Progress theo source-video ms. QUEUE_DWELL_PENDING nghĩa đang chờ5s membership; SUSTAIN_PENDING là chờ thời gian trên threshold. Errors/stale giữ last-known, không khẳng định còn live. Poll không tạo measurement hay activate.

`GET /api/cameras/{id}/incidents?limit=20` — JWT **ADMIN/OPERATOR**, read-only; theo zone đang ACTIVE mapped vào camera. Default chỉ open (`closed_at IS NULL`), `includeEnded=true` thêm history. Limit1–100; cursor phải có **cả** `afterCreatedAt` (ISO UTC) và `afterIncidentId` (UUID), seek theo `(created_at DESC, incident_id DESC)` trong SQL, Take(limit+1), không tải toàn history. Response `items[],hasMore,nextCreatedAt?,nextIncidentId?`. Item có incident/zone/type names, status/title, UTC created/updated/closed, `slaPolicyId?`, `slaPriorityCode?`, `slaPolicyVersion?`, `responseDeadlineAt?`, `resolutionDeadlineAt?`, `mitigatedAt?`, latest `metricValue?,measurementMode?,thresholdUnit?`, `videoSourceType` LIVE/RECORDED/UNKNOWN và `isDemo`. Incident severity đã bị loại khỏi ERD V7; SLA priority đến từ snapshot policy đã áp dụng. Feed trống thật khác503/error.

Rule `outcome_type=OPERATIONAL_EVENT` chỉ tạo/cập nhật episode OperationalEvent và không tạo Incident. Rule `outcome_type=INCIDENT` tạo/cập nhật Incident **DETECTED**, source AI_DETECTED, reportedBy null và không tạo OperationalEvent; chưa dispatch. Automated activation hiện fail-closed với `INCIDENT_RULE_RUNTIME_UNSUPPORTED` cho tới khi rule có measurement/model contract đã xác nhận. Warning/Critical sustain độc lập; Incident branch không downgrade/tự đóng và cooldown tính từ terminal timestamp SQL. Không có dispatch/task/close/reset endpoint mới. Feed Incident không suy diễn measurement/camera từ OperationalEvent vì hai nhánh độc lập. Missing camera404, invalid query400 INVALID_FEED_QUERY, forbidden403, unauthorized401, chưa chạy V6 Cleanup trả503 MONITORING_SCHEMA_NOT_READY. Runtime errors sanitized: AI_SERVICE_UNAVAILABLE, AI_MEASUREMENT_INVALID, AI_SESSION_CAPACITY, AI_SESSION_OWNER_MISMATCH, AI_SOURCE_TIME_INVALID, CAMERA_STREAM_UNAVAILABLE, CONFIGURATION_CHANGED/RULE_RUNTIME_UNSUPPORTED; không trả/log owner token, local file/stream URI hoặc credentials.

Worker mặc định `Monitoring:Enabled=true, PollIntervalMs=250, MaxBatch=64, MaxObservationGapMs=2000`. AI private protocol v1 dùng service key+owner lease, bounded ordered buffer256; BE retry1/2/4/8/16/30s. Gap/reconnect/session/source/version đổi reset sustain/dwell; same-source context change không rewind video. Public Stop không làm thay đổi ACTIVE configuration. EOF drains once, owner stop/new start mới replay; deactivate tất cả zone active trên camera và đợi runtime STOPPED trước reactivate.

Floor plan upload dùng `POST /api/floors/{id}/map` với `multipart/form-data`, field bắt buộc tên `file`. Chấp nhận PNG, JPEG hoặc PDF tối đa 20 MB theo mặc định (`FloorPlan:MaxBytes`); server kiểm tra extension, MIME và file signature, sinh storage name, cập nhật `Floor.mapAssetUrl/mapWidth/mapHeight`, rồi chỉ xóa asset đã được tham chiếu trước đó sau khi commit thành công. Response:

```json
{"floorId":"<uuid>","mapUrl":"http://localhost:5080/api/floors/<uuid>/map?v=<generated-token>","mapWidth":1200,"mapHeight":800,"contentType":"image/png","updatedAt":"2026-10-03T00:00:00Z"}
```

`GET /api/floors/{id}/map` yêu cầu JWT ADMIN, OPERATOR hoặc MANAGER và trả bytes với Content-Type đã lưu. Asset không được public qua static files và response dùng `Cache-Control: no-store`. File Local mặc định nằm ngoài `wwwroot` tại `src/Supermarket.Api/.local/floor-plans`; client không được biết hoặc gửi đường dẫn này. Upload thay thế cùng một floor được serialize trong process; EF version/transaction checks vẫn bảo vệ cập nhật DB. Cleanup chỉ dùng exact prior token, không liệt kê/xóa toàn prefix.

`FloorPlan:Provider=Cloudinary` lưu upload mới trên Cloudinary `authenticated` bằng signed server-side API. PNG/JPEG là `image`, PDF là `raw`; giữ file gốc, không crop/transform. API và FE DTO không đổi; `mapAssetUrl` vẫn trỏ về API map của backend với opaque version token. Token local cũ vẫn được đọc từ `FloorPlan:Root`; không tự migrate. Cloud token chứa cloud name, floor owner, asset ID và format, không chứa key/secret hoặc URL hết hạn. Backend kiểm tra token, tải file bằng signed `POST /asset/download` rồi trả bytes; không gửi JWT hay Cloudinary credentials cho client. POST chỉ ADMIN; GET viewer roles giữ nguyên.

Upload gửi `Cloudinary:Folder` bằng `asset_folder` cho Media Library dynamic folders và dùng cùng giá trị làm public ID prefix. Ảnh mới vào folder đã cấu hình; ảnh cũ ở Home không tự di chuyển và vẫn tải bằng asset ID như trước. Di chuyển folder trong Cloudinary Console không đổi tham chiếu backend.

Thiếu/cấu hình sai provider key hoặc đổi sang cloud khác với asset trả 503 `FLOOR_MAP_STORAGE_NOT_CONFIGURED`; bật provider khi thiếu key fail startup. Provider/network/auth/quota/invalid response trả 502 `FLOOR_MAP_STORAGE_UNAVAILABLE`, timeout 504 `FLOOR_MAP_STORAGE_TIMEOUT`; không trả upstream error body/secret. Asset không tồn tại trả 404. Lỗi upload/provider/DB giữ map đã lưu; lỗi cleanup sau commit vẫn trả kết quả thành công và ghi warning để kiểm tra asset không được tham chiếu. Cấu hình và chuyển ảnh local xem [README](../README.md#cloudinary-cho-floor-plans).

Camera placement dùng contract camera hiện có: `mapX` và `mapY` normalized trong `[0,1]`, `mapRotationDeg` trong `[0,360)`. `PATCH /api/cameras/{id}` vẫn nhận full edit DTO; client phải giữ nguyên metadata, dates và status khi chỉ đổi placement. Lưu placement không tự test/enable connection hoặc activate monitoring.

PATCH dùng full edit DTO; không gửi IDs/createdAt/updatedAt trong payload. PUT connection luôn invalidate test và disable connection. Không có public self-registration, account PENDING hoặc camera reviewer. Preview được gọi với Authorization header, ví dụ fetch → Blob URL ở React.

HTTP errors: 400 request shape, 401 token/login, 403 role, 404 missing resource, 409 duplicate/concurrency/workflow, 422 domain validation, 429 login rate limit, 500 sanitized unexpected error. Application/domain ProblemDetails có `code`, `requestId`, `title`, `detail`, `status`; request-shape validation dùng `errors` của ASP.NET Core. Client cần hỗ trợ cả hai dạng.

Các code thường gặp: `CROSS_FLOOR_MAPPING`, `INVALID_POLYGON`, `CONNECTION_NOT_READY`, `CONNECTION_CHANGED`, `LAST_ACTIVE_ADMIN`, `MONITORING_NOT_READY`, `MONITORING_ACTIVE`, `CONCURRENT_UPDATE`, `FLOOR_MAP_SIZE_INVALID`, `FLOOR_MAP_FORMAT_INVALID`, `FLOOR_MAP_NOT_FOUND`, `VIDEO_ADAPTER_REQUIRED`, `AUTHENTICATION_FAILED`, `TIMEOUT`, `CONNECTION_REFUSED`, `STREAM_UNAVAILABLE`.

Camera connection test giữ `lastTestResult` ở mức `SUCCESS`/`FAILED` và trả nguyên nhân đã chuẩn hóa trong `lastTestMessage`: RTSP 401/auth failure → `AUTHENTICATION_FAILED`, timeout → `TIMEOUT`, TCP refusal → `CONNECTION_REFUSED`, còn lại → `STREAM_UNAVAILABLE`. FFmpeg stderr chỉ được đọc có giới hạn để phân loại trong bộ nhớ; URI có credential và stderr thô không được trả về hoặc ghi log.

### Global notification settings và web inbox

ERD V6 đã có `SystemSetting`, `Notification`, `NotificationRecipient`, `UserDevice` và `AuditLog`; increment này không đổi schema và không tạo lại `NotificationConfig`.

| Quyền | Route | Ghi chú |
|---|---|---|
| ADMIN | `GET /admin/settings/notifications` | Trả bốn policy FCM hiệu lực, `pushConfigured` và aggregate `updatedAt`. Nếu các row chưa được seed, effective defaults là master `false`, ba category `true`. Không trả credential/token. |
| ADMIN | `PUT /admin/settings/notifications` | Body `{pushEnabled, taskOfferPushEnabled, taskUpdatePushEnabled, shiftSwapPushEnabled, expectedUpdatedAt}`. Upsert nguyên tử bốn key `notification.*`, ghi một `AuditLog`; stale version trả 409 `SETTINGS_CHANGED`. Bật master trước khi provider được cấu hình trả 422 `PUSH_NOT_CONFIGURED`. |
| STAFF, OPERATOR, MANAGER | `GET /me/notifications` | Tối đa 100 `IN_APP` notification mới nhất của chính caller; không nhận `userId` từ client. |
| STAFF, OPERATOR, MANAGER | `POST /me/notifications/read-all` | Đánh dấu read chỉ các `IN_APP` recipient row của caller; không đổi Task/Shift/Swap state. |
| STAFF | `POST /me/devices` | Body `{fcmToken, platform, appVersion?, deviceId?}`; platform `ANDROID`/`IOS`. Idempotent theo token; `deviceId` hỗ trợ rotation. Response không trả raw token. |
| STAFF | `DELETE /me/devices/{deviceId}` | Soft-revoke đúng device của caller, idempotent; device của user khác trả 404. |

V1 Admin chỉ quản lý `notification.push_enabled`, `notification.task_offer_push_enabled`, `notification.task_update_push_enabled`, `notification.shift_swap_push_enabled`. In-app records và web inbox luôn hoạt động; không có switch tắt persistence. Staff device lifecycle đã có trên `UserDevice`, nhưng `pushConfigured` vẫn `false` vì FCM sender, credential, Mobile integration và Gate B staging chưa được implement/enable. Vì vậy lưu policy, web inbox và device registration đã có, nhưng **chưa có FCM delivery**. `FA26SE103_Database_V8_Notification_Settings.sql` seed bốn default row idempotently; API không tự mutate DB khi startup.

### Operator Incident intervention

| Quyền | Route | Ghi chú |
|---|---|---|
| OPERATOR | `POST /api/incidents/{incidentId}/dismiss` | Body `{reason}`; reason sau khi trim phải dài 1–2000 ký tự. Trong một transaction, Incident đang mở chuyển sang `DISMISSED`, lưu `status_reason`, `closed_at`, `updated_at` và một `AuditLog` `DISMISS_INCIDENT`. Incident không tồn tại trả 404 `NOT_FOUND`; Incident đã `CLOSED`, `DISMISSED`, `CANCELLED` hoặc đã có `closed_at` trả 409 `INCIDENT_ALREADY_ENDED`; reason rỗng/quá dài trả 422. Không tạo Task, không reset SLA và không thay thế auto-close sau khi mandatory Tasks được Verified. |

### MF-02 staff tasks (V7 TaskOffer + specialty dispatch)

Cần database ERD V7/V9, gồm `TaskOffer`, `TaskType`, `StaffSpecialty`, các bảng Task/Shift/Notification/ResponsePolicy và `UserAccount.specialty_id`. Nếu thiếu, các route dưới đây trả 503 `TASK_SCHEMA_NOT_READY`; monitoring vẫn chạy, chỉ không tạo task.

Luồng tạo task: OperationalEvent episode mở hoặc tăng priority → `ResponsePolicy` (rule_id + event_priority, enabled) → `ResponsePolicyAction` `CREATE_OPERATIONAL_TASK` + `AUTOMATIC` + enabled → một `Task` origin `OPERATIONAL_EVENT`, status `OFFERED`. Mỗi episode tối đa một task; WARNING→CRITICAL cập nhật priority của task đó. `action_parameters_json` hỗ trợ `{"title","instructions","taskTypeCode"}`. Nếu không cấu hình `taskTypeCode`, backend chỉ suy ra khi `OperationalEventType.name` khớp đúng một `TaskType.source_category`; thiếu/mơ hồ thì fail closed và không tạo task. Action `APPROVAL_REQUIRED` chưa chạy. Không có policy thì không có task.

Runtime offer nằm trong `TaskOffer`, trạng thái `PENDING/ACCEPTED/DECLINED/EXPIRED/CANCELLED`; `TaskAssignmentHistory` chỉ ghi audit/assignment history. Dispatch tự động mở tối đa năm vòng theo thứ tự `SAME_ZONE` → `ADJACENT_SAME_FLOOR` → `REMAINING_SAME_FLOOR` → `ADJACENT_FLOOR` → `OTHER_FLOOR`; mỗi vòng có `dispatchCycleId`, `waveNumber` và `dispatchScope` riêng. `ADJACENT_SAME_FLOOR` dùng `ZoneAdjacency`; `ADJACENT_FLOOR` là tầng đang tồn tại gần nhất theo `Floor.floor_number` trong cùng Supermarket. Đề nghị giao tay dùng `dispatchScope=MANUAL`. Staff đủ điều kiện phải là STAFF ACTIVE, đã check-in vào ca đang diễn ra, dưới 2 task `ACCEPTED/IN_PROGRESS`, có `StaffZoneAssignment` ACTIVE thuộc Zone của vòng hiện tại, và `UserAccount.specialty_id` khớp `TaskType.required_specialty_id`. Một Staff chỉ nhận tối đa một offer tự động của cùng Task; Operator vẫn có thể chọn ngoài Zone khi ghi lý do.

Worker `TaskOffers` (mặc định quét 2 s) chuyển offer quá `expiresAt` sang `EXPIRED` (không phải Declined), ghi audit, rồi chuyển sang vòng topology kế tiếp. Mỗi vòng có Staff chờ đúng một `offer_timeout_sec`; các vòng rỗng được bỏ qua ngay. `SLAPolicy.offer_timeout_sec` được ưu tiên khi Task có policy; nếu chưa có thì dùng `task_offer.retry_interval_sec`. Khóa legacy `task_offer.max_retry_count` không còn điều khiển số vòng. Dispatch không tạo lại Task, không gửi lại cùng Staff và không reset SLA snapshot/deadline. Hết cả năm vòng thì báo Operator đúng một lần (`TASK_NEEDS_ASSIGNMENT`). `Notification.severity` giữ priority của Task; Incident không có severity. SLA warning/breach/escalation đầy đủ chưa thuộc increment này.

Event `CLEARED` (tạm theo open point 5): task chưa ai nhận → offer `CANCELLED` reason `EVENT_CLEARED`, task `CANCELLED`, báo các Staff được offer. Task đã nhận giữ nguyên, assignee nhận `EVENT_CLEARED`; view có `eventClearedAt`.

| Quyền | Route | Ghi chú |
|---|---|---|
| STAFF | `GET /me/shifts?from=&to=` | Mặc định hôm qua → +7 ngày, tối đa 62 ngày. Giờ ca là giờ cửa hàng (`Store:TimeZone`, mặc định Asia/Ho_Chi_Minh), trả về UTC; ca qua đêm kết thúc ngày hôm sau. |
| STAFF | `POST /me/shifts/{shiftAssignmentId}/check-in` | Mở trước giờ bắt đầu 15 phút. 422 `CHECK_IN_TOO_EARLY`, 409 `SHIFT_ENDED`, 409 `SHIFT_CANCELLED`; check-in lại trả kết quả cũ. |
| STAFF | `GET /me/eligibility` | `checkedIn` chỉ true khi đang trong giờ ca. |
| STAFF | `GET /me/task-offers?status=ALL` | Mặc định chỉ offer đang mở; `ALL` thêm offer 24 giờ qua kèm `status` và `closedReason`. |
| STAFF | `GET /task-offers/{offerId}` | Một offer của chính mình. |
| STAFF | `POST /task-offers/{offerId}/respond` | Body `{response, plannedStartAt?, reason?}`. `HANDLE_NOW` → `IN_PROGRESS`; `HANDLE_LATER` → `ACCEPTED` + `plannedStartAt` (sau hiện tại, không quá giờ hết ca); `CANNOT_HANDLE` → offer `DECLINED`, cần `reason` là một trong `BUSY`, `UNAVAILABLE`, `NOT_QUALIFIED`, `OTHER`, trả `task: null`. Lỗi: 409 `TASK_ALREADY_TAKEN`, `OFFER_CLOSED`, `OFFER_EXPIRED`, `NOT_ON_SHIFT`, `WORKLOAD_LIMIT`, `SPECIALTY_MISMATCH`; 422 `REASON_REQUIRED`, `INVALID_DECLINE_REASON`, `INVALID_PLANNED_START`. Ai nhận hợp lệ trước thì được (transaction serializable + unique accepted-offer index). |
| STAFF | `GET /me/tasks?status=ACCEPTED,IN_PROGRESS` | Mặc định ACCEPTED, IN_PROGRESS, SUBMITTED. |
| STAFF | `POST /tasks/{taskId}/start` | `ACCEPTED` → `IN_PROGRESS`; 409 `TASK_NOT_ACCEPTED`. |
| STAFF, OPERATOR, MANAGER | `GET /me/notifications`, `POST /me/notifications/read-all` | 100 thông báo in-app gần nhất của caller; xem Global notification settings ở trên. |
| STAFF (người liên quan), ADMIN, OPERATOR, MANAGER | `GET /tasks/{taskId}` | Staff chỉ đọc task mình được offer hoặc được giao. |
| ADMIN, OPERATOR, MANAGER | `GET /tasks?status=&needsAssignment=true` | Mặc định OFFERED, ACCEPTED, IN_PROGRESS, SUBMITTED; tối đa 200. |
| OPERATOR | `GET /tasks/{taskId}/assignable-staff` | Staff đang trong ca, đúng specialty và còn dưới giới hạn task; `assignedToZone` đứng trước. |
| OPERATOR | `POST /tasks/{taskId}/assign` | Body `{staffUserId, reason?, expectedUpdatedAt?}`. Tạo một `TaskOffer` đích danh `PENDING/MANUAL`; Task và assignee hiện tại chỉ đổi sau khi Staff phản hồi `HANDLE_NOW/HANDLE_LATER` qua endpoint offer. `CANNOT_HANDLE` hoặc hết hạn trả Task chưa có assignee về Operator; đề nghị reassign bị từ chối/hết hạn không làm mất assignee hiện tại. Rút các offer đang mở khác (`MANUALLY_ASSIGNED`). Web gửi `updatedAt` đã đọc vào `expectedUpdatedAt`; 409 `TASK_CHANGED` nếu dữ liệu đã đổi. Reassign hoặc giao ngoài Zone bắt buộc reason (422 `REASON_REQUIRED`). 409 `TASK_NOT_ASSIGNABLE`, `STAFF_NOT_ON_SHIFT`, `WORKLOAD_LIMIT`. |

Notification types: `TASK_OFFER`, `TASK_OFFER_WITHDRAWN`, `TASK_ASSIGNED`, `TASK_NEEDS_ASSIGNMENT`, `TASK_PRIORITY_RAISED`, `EVENT_CLEARED`, cùng các loại MF-03 bên dưới. Web inbox dùng kênh `IN_APP`; device-token API và optional Firebase delivery worker đã có, nhưng external push chỉ hoạt động khi provider/credentials được cấu hình và policy được bật. `sourceCode` (`EVT-xxxxxxxx`) là mã hiển thị suy từ ID, không phải cột DB. Chưa làm: StaffReport. Đổi ca: xem mục MF-04 bên dưới.

Task/offer/evidence views trả thêm `verificationMode` và `verificationMethod`. Operational Event TT-01–TT-05 mặc định `AUTO` với method `OPERATIONAL_EVENT_METRIC` khi SLA Response Priority không phải `CRITICAL` và có metric hợp lệ; Task origin Incident, SLA Response Priority `CRITICAL`, TT-07 và TT-12–TT-15 luôn `MANUAL` với `OPERATOR_EVIDENCE`. `OperationalEvent.priority` (`WARNING`/`CRITICAL`) là Alert Level độc lập và không quyết định verification lane. Các TaskType khác fail-safe về MANUAL khi chưa có metric/checklist tự động đã duyệt.

### MF-03 evidence, submit và Operator verify

Report 3 §3.7.4–3.7.5, §3.9.4–3.9.5; BR-25–BR-28. Dùng bảng có sẵn `TaskEvidence` và cột `Task.rejection_count`, không đổi schema. Mỗi dòng `TaskEvidence` là một ảnh (`BEFORE_PHOTO`/`AFTER_PHOTO`, `evidence_image` = token lưu file), ghi chú hoàn thành (`COMPLETION_NOTE`) hoặc lý do ngoại lệ khi thiếu ảnh (`MISSING_PHOTO_REASON`). `review_status`: `DRAFT` (đã upload, chưa nộp) → `PENDING` (đã nộp) → `VERIFIED`/`REJECTED` (+ `reject_reason`). Một lần nộp = một dòng `COMPLETION_NOTE`, lý do thiếu ảnh nếu có, cùng các ảnh upload sau lần nộp trước.

Yêu cầu evidence (BR-26, Review 2): task origin `INCIDENT` bắt buộc **ảnh after HOẶC ghi chú hoàn thành**; nếu dùng ghi chú thay ảnh after thì `missingPhotoReason` cũng bắt buộc để audit lý do ngoại lệ. Ảnh before luôn tuỳ chọn. Task `OPERATIONAL_EVENT`/`STAFF_REPORT`/`MANUAL` bắt buộc ghi chú hoàn thành, ảnh tuỳ chọn và không nhận `missingPhotoReason`. Ảnh JPEG/PNG ≤ 10 MB, kiểm tra content type + chữ ký file; lưu theo `TaskEvidence:Provider` (`Local`/`Cloudinary`; để trống thì theo `FloorPlan:Provider`, nên Dev dùng Cloudinary). Cloudinary dùng chung credentials mục `Cloudinary`, folder `TaskEvidence:CloudinaryFolder` (mặc định `fa26se103/task-evidence`), ảnh `authenticated` và chỉ tải qua API có JWT. Local lưu ở `TaskEvidence:Root` (mặc định `.local/task-evidence`, cần volume bền).

| Quyền | Route | Ghi chú |
|---|---|---|
| STAFF (người liên quan), ADMIN, OPERATOR, MANAGER | `GET /tasks/{taskId}/evidence` | Trả `verificationMode`, `verificationMethod`, `{requiresBeforePhoto, requiresAfterPhoto, rejectionCount, drafts[], submissions[]}`; Review 2 trả hai cờ photo là `false` vì before là tuỳ chọn và after có thể được thay bằng note. Mỗi submission có `note`, `missingPhotoReason`, `reviewStatus`, `rejectReason`, `photos[]`; Staff chỉ thấy evidence của chính mình. |
| STAFF (assignee) | `POST /tasks/{taskId}/evidence` | multipart `file` + `type` (`BEFORE_PHOTO`/`AFTER_PHOTO`). Chỉ khi task `IN_PROGRESS` (409 `TASK_NOT_IN_PROGRESS`). Upload lại cùng loại thay ảnh nháp cũ, nên retry không tạo trùng. 422 `EVIDENCE_FORMAT_INVALID`, `EVIDENCE_SIZE_INVALID`, `EVIDENCE_UPLOAD_INCOMPLETE`. |
| STAFF (assignee) | `DELETE /tasks/{taskId}/evidence/{evidenceId}` | Xoá ảnh nháp trước khi nộp. |
| STAFF (chủ ảnh), ADMIN, OPERATOR, MANAGER | `GET /tasks/{taskId}/evidence/{evidenceId}/image` | Stream ảnh, cần JWT, `no-store`. |
| STAFF (assignee) | `POST /tasks/{taskId}/submit` | Body `{note?, expectedUpdatedAt?, missingPhotoReason?}`. Incident chấp nhận after photo hoặc note; nếu không có after photo thì bắt buộc cả note và `missingPhotoReason` (tối đa 1000 ký tự). Có after photo hoặc task không phải Incident thì không nhận field này. Các origin khác vẫn bắt buộc note. `IN_PROGRESS` → `SUBMITTED`, ghi `submitted_at`; ảnh nháp/lý do → `PENDING`. MANUAL báo Operator `TASK_SUBMITTED`. AUTO chỉ chuyển `VERIFIED` khi Event đã `CLEARED` và metadata có measurement mode/unit, metric khớp Event, warning threshold hợp lệ và recovery metric thấp hơn threshold; báo Staff `TASK_AUTO_VERIFIED`. Thiếu/sai source hoặc metric chuyển sang `MANUAL/OPERATOR_METRIC_EXCEPTION` và báo `TASK_VERIFICATION_EXCEPTION`. 422 `INVALID_FIELD`, `EVIDENCE_REQUIRED`, `MISSING_PHOTO_REASON_REQUIRED`, `MISSING_PHOTO_REASON_NOT_APPLICABLE`; 409 `TASK_CHANGED`. |
| OPERATOR | `POST /tasks/{taskId}/verify` | Body `{expectedUpdatedAt?}`. Task MANUAL `SUBMITTED` → `VERIFIED`, ghi `verified_by_user_id`, `verified_at`, kết thúc assignment; báo Staff `TASK_VERIFIED`. Task AUTO chưa đủ điều kiện trả 409 `AUTO_VERIFICATION_PENDING`; dữ liệu metric lỗi đã được đổi sang MANUAL nên Operator có thể xử lý. Người làm không được tự verify (403 `SELF_VERIFICATION`). 409 `TASK_NOT_SUBMITTED`, `TASK_CHANGED`. Không tự resolve OperationalEvent hay đóng Incident. |
| OPERATOR | `POST /tasks/{taskId}/reject` | Body `{reason, expectedUpdatedAt?}`, reason bắt buộc. `rejection_count` + 1. Lần đầu của Staff ban đầu → `IN_PROGRESS` để làm lại, báo `TASK_REJECTED`. Lần thứ hai → bỏ assignee và tự tiếp tục các vòng dispatch D1 để offer cho Staff đủ điều kiện khác; Staff cũ bị loại khỏi cả auto/manual assignment. Nếu submission của Staff thay thế bị từ chối tiếp → dừng auto-dispatch, task `OFFERED`/không assignee, báo Operator `TASK_REASSIGNMENT_REQUIRED`; cả hai performer bị loại khỏi `assignable-staff` và `assign` trả 409 `STAFF_REJECTED_TWICE`. Không tạo Task mới hoặc reset SLA. |

### MF-04 đổi ca SWAP/COVER

Specialty eligibility (06/11/2026): cả SWAP và COVER yêu cầu hai Staff có cùng `UserAccount.specialty_id` khác null/rỗng. Hai tài khoản cùng thiếu specialty không được xem là tương thích. Shared check `SPECIALTY_COMPATIBLE` được dùng cho peer/Manager candidate, create, peer accept, Manager approve, assign-cover và manager-direct; recheck giá trị hiện tại để chặn account thay specialty sau khi gửi request. Candidate không phù hợp bị ẩn; mutation trả 409 `SWAP_NOT_ALLOWED` với lý do specialty; Manager detail có `checks[]` tương ứng. Decline/cancel/reject vẫn hoạt động để kết thúc yêu cầu không còn phù hợp. Không đổi request/response DTO.

Cần ERD V6: `ShiftSwapRequest` (V4) với `expires_at` và `cancelled_*` (V5), `StaffUnavailability`, `Zone.min_staff`. Nếu thiếu, các route dưới đây trả 503 `SWAP_SCHEMA_NOT_READY`.

Luồng peer: Staff A gửi yêu cầu cho một ca tương lai của mình → `PENDING_PEER` → Staff B đồng ý (`PENDING_MANAGER`) hoặc từ chối (`DECLINED_BY_PEER`) → Manager `APPROVED` / `REJECTED`. `SWAP` đổi ca của A với một ca của B; `COVER` chuyển ca của A cho B. Nếu A không tìm được đồng nghiệp, A có thể nhờ Manager: request được lưu `PENDING_MANAGER` với `targetUserId = null`; Manager chọn Staff đủ điều kiện và chỉ định COVER, không cần peer acceptance. Manager cũng có thể trực tiếp COVER/SWAP trên lịch tương lai; lý do bắt buộc và request `APPROVED` được lưu làm audit. A huỷ được khi còn `PENDING_*`; B rút được khi đang `PENDING_MANAGER`; sau khi duyệt thì không huỷ. Chỉ hành động cuối của Manager mới đổi `ShiftAssignment.user_id` trong transaction; `StaffZoneAssignment` đi theo slot ca nên số Staff mỗi Zone giữ nguyên. Từ chối, huỷ, hết hạn: lịch giữ nguyên.

Hết hạn: `expires_at` = giờ bắt đầu ca sớm nhất bị ảnh hưởng (SWAP: ca sớm hơn của A/B; COVER: ca của A) trừ 24 giờ, tính theo giờ cửa hàng rồi lưu UTC. Gửi yêu cầu khi đã qua mốc này bị từ chối. Worker `ShiftSwaps` (mặc định quét 60 s) chuyển yêu cầu quá hạn sang `EXPIRED`; trước khi worker chạy, view đã trả `EXPIRED` và mọi thao tác trả 409 `SWAP_EXPIRED`.

Luật kiểm lại khi gửi, khi B đồng ý và khi Manager quyết định (view của Manager trả trong `checks[]`, mỗi mục `{code, passed, message}`; Staff nhận `checks: []` vì chi tiết có giờ làm và lịch bận của đồng nghiệp): `ACTIVE_STAFF`, `FUTURE_SHIFT` (chưa bắt đầu, chưa check-in, không CANCELLED), `DECISION_DEADLINE`, `DIFFERENT_SHIFT` (SWAP không đổi cùng ca cùng ngày), `ONE_OPEN_REQUEST` (mỗi ca tối đa một yêu cầu đang chờ; DB không có unique index nên kiểm ở app), rồi cho từng người nhận ca mới: `NO_OVERLAP`, `AVAILABLE` (không trùng `StaffUnavailability`), `WEEKLY_HOURS` (giới hạn cứng ≤ 48 giờ trong tuần Thứ Hai–Chủ Nhật theo giờ cửa hàng), và `ZONE_COVERAGE`. `weeklyHoursAfter ≤ 40` được trả thêm là `recommended=true`; 40–48 giờ vẫn hợp lệ nhưng UI cảnh báo tải cao để chừa sức cho cover. Manager direct bỏ mốc quyết định 24 giờ nhưng vẫn chỉ được làm trước khi ca bắt đầu và vẫn kiểm mọi luật cứng.

| Quyền | Route | Ghi chú |
|---|---|---|
| STAFF | `GET /me/shifts/{shiftAssignmentId}/swap-candidates?type=SWAP\|COVER` | Mặc định SWAP. Chỉ trả đồng nghiệp đạt mọi luật; SWAP liệt kê từng ca của họ trong ±7 ngày. `hiddenCount` = số đồng nghiệp bị ẩn; `deadline` = mốc 24 giờ theo ca của mình. |
| STAFF | `POST /swap-requests` | Body `{requesterShiftAssignmentId, type, targetUserId, targetShiftAssignmentId?, reasonCode?, note?}`. SWAP bắt buộc `targetShiftAssignmentId`, COVER không được có. `reasonCode` ∈ PERSONAL, HEALTH, STUDY, OTHER; `note` ≤ 1000. 422 `INVALID_SWAP_TYPE`, `TARGET_SHIFT_REQUIRED`, `TARGET_SHIFT_NOT_ALLOWED`, `INVALID_REASON`, `TARGET_NOT_STAFF`; 409 `SWAP_NOT_ALLOWED`. |
| STAFF | `POST /me/swap-requests/manager-help` | Body `{requesterShiftAssignmentId, reasonCode?, note?}`. Tạo COVER `PENDING_MANAGER` chưa có target và báo các Manager. Hết hạn khi ca bắt đầu; mỗi ca vẫn chỉ có một request mở. |
| STAFF | `GET /me/swap-requests?box=sent\|received` | Yêu cầu mình gửi và/hoặc nhận, mới nhất trước, tối đa 100. |
| STAFF (A hoặc B), MANAGER | `GET /swap-requests/{id}` | Staff khác nhận 404. View có `viewerRole`, `canCancel`, `canRespond`, `canDecide`, hai ca kèm Zone; `checks[]` chỉ có cho Manager. |
| STAFF (B) | `POST /swap-requests/{id}/respond` | Body `{accept}`. Đồng ý: kiểm lại luật → `PENDING_MANAGER`, báo A và mọi Manager. Từ chối → `DECLINED_BY_PEER`. 409 `SWAP_NOT_PENDING`, `SWAP_EXPIRED`, `SWAP_NOT_ALLOWED`. |
| STAFF (A hoặc B) | `POST /swap-requests/{id}/cancel` | Body `{reason?}`. A: khi `PENDING_PEER`/`PENDING_MANAGER`; B: chỉ khi `PENDING_MANAGER`. Lưu `cancelled_by/at/reason`, báo bên còn lại (và Manager nếu đang chờ duyệt). |
| MANAGER | `GET /swap-requests?status=` | Mặc định `PENDING_MANAGER`, sắp theo hạn gần nhất; nhận danh sách status hoặc `ALL`, tối đa 200. |
| MANAGER | `POST /swap-requests/{id}/approve` | Body `{note?}`. Kiểm lại toàn bộ luật rồi đổi ca nguyên tử; lưu `decided_by/at`, `decision_note`. |
| MANAGER | `POST /swap-requests/{id}/reject` | Body `{note}` bắt buộc (422 `INVALID_FIELD` nếu trống). |
| MANAGER | `GET /swap-requests/{id}/manager-candidates` | Chỉ cho request `PENDING_MANAGER` chưa có target; trả Staff đủ điều kiện, `weeklyHoursAfter` và cờ khuyến nghị 40 giờ. |
| MANAGER | `POST /swap-requests/{id}/assign-cover` | Body `{targetUserId, note}`; note bắt buộc. Manager chọn cover, backend kiểm lại luật, đổi owner của slot ca, lưu quyết định và báo hai Staff. |
| MANAGER | `POST /swap-requests/manager-direct` | Body `{type, requesterShiftAssignmentId, targetUserId, targetShiftAssignmentId?, note}`; note bắt buộc. COVER thay người trên một slot; SWAP đổi owner hai slot. Pending request trên slot bị huỷ có audit trước khi thay đổi trực tiếp. |

Notification types (in-app, không gắn task): `SWAP_REQUEST_RECEIVED`, `SWAP_PEER_ACCEPTED`, `SWAP_PEER_DECLINED`, `SWAP_PENDING_APPROVAL`, `SWAP_MANAGER_HELP`, `SWAP_CANCELLED`, `SWAP_APPROVED`, `SWAP_REJECTED`, `SWAP_EXPIRED`. Bảng `Notification` không có cột trỏ tới yêu cầu đổi ca, nên mobile mở danh sách `GET /me/swap-requests` khi nhận các loại này. Chỉ ca `SCHEDULED` (đã publish) mới đổi được. Chưa làm: giới hạn giờ/tuần qua `SystemSetting` (đang dùng hard limit 48 giờ và planning target 40 giờ), push FCM.

### MF-04 xếp ca cho Manager

Cần ERD V6 như đổi ca và Database V7 Weekly Work Shifts (503 `SWAP_SCHEMA_NOT_READY` nếu thiếu). Không có bảng "lịch" riêng: mỗi `Shift` là một **Work Shift của đúng một tuần** qua `week_start`; `ShiftAssignment` là một Staff trong Work Shift vào một ngày thuộc tuần đó; `StaffZoneAssignment` là Zone của dòng assignment.

Trạng thái `ShiftAssignment.status`: `DRAFT` (Manager đang xếp, Staff không thấy, không check-in, không đổi ca được) → publish → `SCHEDULED` (Staff thấy ở `GET /me/shifts`) → `CANCELLED`. Dòng có sẵn từ trước mang `SCHEDULED` nên vẫn hiện như cũ. Chỉ gán được tài khoản Role `STAFF` đang `ACTIVE`; không có staff skill, phân việc theo Zone.

Luật: Work Shift dài 4, 6 hoặc 8 giờ (BR-33; giờ kết thúc ≤ giờ bắt đầu nghĩa là qua đêm) và `weekStart` luôn là Thứ Hai; tên duy nhất trong một supermarket/tuần. Admin tạo/bố trí Zone nhưng Manager quyết định `Zone.min_staff`; đây là một baseline cấp Zone áp dụng cho mọi Work Shift ACTIVE, không phải cấu hình riêng của ca đang xem. Assignment phải có `workDate` thuộc tuần Work Shift, nếu không trả 422 `WORK_SHIFT_WEEK_MISMATCH`. Không trùng ca, không trong thời gian nghỉ, tối đa 48 giờ/tuần Thứ Hai–Chủ Nhật (BR-34/35, cùng luật với đổi ca); ca đã bắt đầu hoặc đã check-in thì không gán/sửa/huỷ (BR-35, 409 `SHIFT_LOCKED`). Publish một khoảng ngày bị chặn nếu ca tương lai nào có Zone ACTIVE dưới `Zone.min_staff` (BR-02, 409 `COVERAGE_GAPS`, detail liệt kê tối đa 5 chỗ thiếu). Độ phủ của mỗi ngày chỉ tính Work Shift ACTIVE thuộc tuần của ngày đó × Zone ACTIVE có `min_staff > 0` (hoặc đã có người), đếm cả `DRAFT` và `SCHEDULED`.

| Quyền | Route | Ghi chú |
|---|---|---|
| ADMIN, OPERATOR, MANAGER | `GET /shifts?weekStart=` | Work Shift của tuần chứa ngày được gửi; bỏ trống dùng tuần hiện tại. Trả `weekStart`, `hours`, `inUse`. |
| MANAGER | `POST /shifts` | Body `{weekStart, name, startTime, endTime}` (`weekStart` là Thứ Hai, giờ `"HH:mm:ss"`). 422 `INVALID_WEEK_START`/`INVALID_SHIFT_LENGTH`, 409 `DUPLICATE`. |
| MANAGER | `POST /shifts/copy-week` | Body `{sourceWeekStart, targetWeekStart}`. Copy các Work Shift ACTIVE sang tuần đích đang trống; không copy assignment. 409 `TARGET_WEEK_NOT_EMPTY`. |
| MANAGER | `PATCH /shifts/{id}` | Body `{name?, startTime?, endTime?, status?}` (`ACTIVE`/`INACTIVE`). Đổi giờ khi đang có assignment → 409 `SHIFT_IN_USE`. Work Shift INACTIVE không nhận người mới (409 `SHIFT_INACTIVE`). |
| MANAGER | `PATCH /zones/{id}/minimum-staff` | Body `{minStaff, expectedUpdatedAt?}`. Đặt baseline Staff tối thiểu của Zone cho mọi Work Shift ACTIVE; `0` nghĩa là chưa áp dụng coverage gate. Giá trị âm → 422 `INVALID_MIN_STAFF`; version cũ → 409 `ZONE_CHANGED`. Admin không có quyền route này. |
| ADMIN, OPERATOR, MANAGER | `GET /schedule?from=&to=` | Mặc định tuần hiện tại; tối đa 31 ngày. Trả `assignments[]` (status, zones, `editable`, `pendingSwapRequestId`), `coverage[]` (`planned`, `published`, `minStaff`, `belowMinimum`), `draftCount`, `gapCount`. |
| MANAGER | `GET /schedule/staff-options?shiftId=&workDate=&replacingAssignmentId=` | Mọi Staff ACTIVE kèm `eligible`, `weeklyHoursAfter`, `problems[]`, `specialtyName` và `specialtyCode`. Hai trường specialty trả về `null` nếu chưa gán. |
| MANAGER | `POST /schedule/assignments` | Body `{userId, shiftId, workDate, zoneIds:[zoneId]}` → `DRAFT`; mỗi Staff trong một ca phải có đúng một Zone ACTIVE. Dòng `CANCELLED` cùng người/ca/ngày được dùng lại (DB unique). 409 `ALREADY_ASSIGNED`, `SCHEDULE_CONFLICT`, `SHIFT_LOCKED`; 422 `NOT_STAFF`, `ZONE_REQUIRED`, `MULTIPLE_ZONES_NOT_ALLOWED`, `ZONE_NOT_FOUND`. |
| MANAGER | `PATCH /schedule/assignments/{id}` | Body `{userId?, zoneIds?:[zoneId]}`; đổi người kiểm lại luật; khi đổi Zone thì Zone cũ được đóng (`unassigned_at`). `zoneIds` nếu gửi phải chứa đúng một Zone ACTIVE; nhiều Zone trả 422 `MULTIPLE_ZONES_NOT_ALLOWED`. 409 `SWAP_PENDING` nếu ca đang có đơn đổi ca chờ. Ca đã publish: báo người cũ `SHIFT_CANCELLED`, người mới `SHIFT_CHANGED`. |
| MANAGER | `POST /schedule/assignments/{id}/cancel` | Body `{reason?}` → `CANCELLED`, đóng Zone, huỷ luôn đơn đổi ca đang chờ của ca đó (báo hai bên), báo Staff nếu ca đã publish. |
| MANAGER | `POST /schedule/publish` | Body `{from, to}` → mọi `DRAFT` tương lai trong khoảng thành `SCHEDULED`; mỗi Staff nhận một `SHIFT_PUBLISHED`. Trả `{published, notified}`. |
| MANAGER | `GET /staff-unavailability?from=&to=&userId=` | Mặc định hôm nay → +30 ngày. |
| MANAGER | `POST /staff-unavailability` | Body `{userId, startAt, endAt, note?}` (UTC). Trùng ca chưa kết thúc của người đó → 409 `LEAVE_CONFLICTS_SCHEDULE` (xếp lại ca trước). |
| MANAGER | `DELETE /staff-unavailability/{id}` | 204. |

Notification types: `SHIFT_PUBLISHED`, `SHIFT_CHANGED`, `SHIFT_CANCELLED` (in-app). Copy tuần chỉ copy cấu hình Work Shift; Manager vẫn chủ động xếp Staff và publish từng tuần. Chưa làm: lặp assignment tự động, giới hạn giờ/tuần qua `SystemSetting`, Staff tự gửi đơn nghỉ.
## Manager recommendations (WF-05 / FE-11)

All three routes require `MANAGER` and use the single configured supermarket.

- `GET /api/manager/recommendations` accepts `status`, `zoneId`, `page` (default 1) and `pageSize` (default 50, maximum 100). Results are ordered newest first and return `items`, `page`, `pageSize` and `hasMore`. `EXPIRED` includes pending recommendations whose deadline has passed.
- `GET /api/manager/recommendations/{id}` returns a recommendation with its effective status, source Event/Zone IDs, timestamps and latest reviewer. Unrecognized IDs and rows outside the configured supermarket return 404.
- `POST /api/manager/recommendations/{id}/decision` accepts `{"decision":"DEFERRED","reason":"Review after the next observation"}`. The allowed decisions are `DEFERRED`, `DISMISSED` and `ACCEPTED`. Defer and dismiss require a reason. Reasons are trimmed and limited to 2000 characters. The decision and audit entry commit together.
- `POST /api/manager/recommendations/generate` accepts `{"eventId":"<uuid>"}`. It returns `{ recommendation, reason }`. With no valid current evidence or no supported advice it returns HTTP 200 with `recommendation: null` and one of `INSUFFICIENT_VALID_EVIDENCE`, `EVIDENCE_CHANGED` or `NO_SUPPORTED_RECOMMENDATION`. A missing or out-of-store Event returns 404. Provider configuration, transport and invalid output failures return 503.

Generation uses an active Event with an active supported rule, matching observation metadata and an observation less than 30 seconds old. It sends only event type, alert level, numeric measurement, thresholds, sustain duration and optional SLA priority to the model. The model chooses from a server-owned action catalog and returns qualitative confidence. The Backend checks the source again before saving an `AVAILABLE` row with a five-minute deadline. Details include an actionable `guidance` object (`rationale`, ordered `immediateSteps`, `recheckAfterMinutes` and `escalationCondition`) plus a typed evidence summary with observation time and qualitative confidence. Guidance is built from the selected catalog action and validated source signal; it remains advisory and does not create a Task. Set `OperationalRecommendationAi__ApiKey`, `OperationalRecommendationAi__Model` and optionally `OperationalRecommendationAi__TimeoutSeconds` on the Backend. Keep credentials outside Git.

Acceptance rechecks the source Event in the recommendation's Zone and supermarket. Generated recommendations allow newer observations when the condition still meets the threshold and the rule, configuration, alert level and SLA policy versions still match. Earlier exact-snapshot rows use the stored observation timestamps. Missing, stale or changed evidence returns 409 `RECOMMENDATION_EVIDENCE_UNAVAILABLE`. Expired and terminal rows also return 409 for disallowed decisions. The response excludes the internal supporting JSON.

### Operator handoff

- `GET /api/operator/recommendations` lists `ACCEPTED` recommendations for the configured supermarket. It accepts `zoneId`, `page` and `pageSize`.
- `GET /api/operator/recommendations/{id}` returns an accepted recommendation. Unknown IDs, other stores and recommendations in any other state return 404. Both routes require `OPERATOR`.
- Acceptance also writes a `RECOMMENDATION_ACCEPTED` in-app inbox notification for all active Operators in the same transaction as the decision and audit. The notification includes the recommendation ID in the message and the source `eventId`. With no active Operator, the accepted item remains available through the queue API.

Operators review the recommendation and use the existing manual Task workflow if action is needed. Acceptance itself only records the decision and handoff. The Operator screen, notification deep link and historical evidence validation remain separate work.

## Manager analytics (WF-05 / FE-09–10)

Operational Event drill-down is available through read-only `GET /api/operational-events`, `/api/operational-events/zones` and `/api/operational-events/{eventId}` for ADMIN/OPERATOR/MANAGER. Includes All/status/priority/Zone/date filters, SQL paging and linked Tasks. [Event history contract and verification](OPERATIONAL_EVENTS_API.md).

MANAGER-only `GET /api/manager/analytics/overview`, `/trends`, `/zones` accept `from`, `to` (local dates, inclusive, at most 93 days) and optional `zoneId`. They return SQL-aggregated recorded Event/Incident/Task KPIs, sample-weighted durations and snapshot-deadline SLA results. `GET /api/manager/analytics/export` reuses the same filters/calculations and accepts `format=csv|xlsx|pdf`; generated files include the period, timezone, applied Zone, generation time, summary, daily/Zone breakdowns and explicit data-availability notes. Successful/failed generation is audited without personal data or credentials. Empty samples/rates are null; unavailable SQL returns 503, not successful zeros. Cohorts use detection/creation dates with current statuses, not historical end-of-day snapshots. Full schemas, KPI definitions, examples and limits: [Manager analytics API](MANAGER_ANALYTICS_API.md).

## Operational SLA compatibility cutover (2026-11-06)

`GET /api/task-types` (ADMIN only) returns the existing fixed TaskType catalog, ordered by code: `[{taskTypeId, code, name, description, requiredSpecialtyId}]`. Response-policy editors display `code — name`, show the selected description, and submit the selected code as `action.taskTypeCode`. This endpoint is read-only; it does not create task types or change specialty mappings.

The existing `/api/monitoring-rules/{ruleId}/response-policies` routes are retained, backed by `SLAPolicy`/`SLAPolicyAction`, not legacy response-policy tables. Rules sharing an EventType share the enabled SLA policy and version. WARNING manages a notification action only; CRITICAL manages a task action with explicit `taskTypeCode`. Missing/ambiguous SLA configuration fails closed; the API does not fabricate priorities or deadlines. Runtime configuration exceptions notify Operators with an event-linked `OPERATIONAL_SLA_CONFIGURATION_EXCEPTION` notification. Tasks retain operational alert priority while their immutable SLA snapshot stores the distinct SLA priority and deadlines.
# Staff Report API

Submit also requires a client-generated `reportId` UUID; Mobile must reuse the same ID on retries. A repeated ID with matching content returns the existing report/Incident, while changed content returns 409.

If the confirmed IncidentType has no valid enabled SLA policy, submission still keeps the Staff Report and sourced Incident without an arbitrary SLA snapshot, records the policy error on the Incident and creates an in-app Operator exception notification.

`GET /api/staff-reports/incident-types` and `GET /api/staff-reports/zones?floorId=...` (STAFF) supply the active Staff-reported IncidentType catalog and selectable floor/Zone list. `POST /api/staff-reports` (STAFF) accepts `multipart/form-data`: required client-generated `reportId`, `zoneId`, `title`, and `reportCategory` (`ROUTINE_REQUEST` or `POTENTIAL_INCIDENT`); optional `description`, `locationDescription`, `incidentTypeCode`, `duplicateDecision`, `routineTaskDecision`, `confirmedScenarioCode`, and up to three `photos` (10 MB each; total request limit 48 MB). `ROUTINE_REQUEST` forbids IncidentType and Incident duplicate decision, requires exactly one eligible `taskTypeCodes` value (TT-01/02/05/09/11), and creates a StaffReport-originated Task using the enabled TaskType SLA policy. Missing/ambiguous policy fails closed without saving that routine report. `POTENTIAL_INCIDENT` requires an active Staff-reported `incidentTypeCode`, resolved to `incidentTypeId`. The server assigns caller identity, confirmation time, and status. Request retries with the same `reportId` must keep the same content. Responses include `reportCategory`; legacy records may return null. The obsolete string `classification` is not accepted. Photos use the authenticated Cloudinary uploader; the database stores the asset token in `StaffReportMedia.storage_key`, not image bytes or credentials. A confirmed typed report creates its sourced Incident and SLA snapshot atomically; the response includes `incidentId`. If an open same-Zone/type Incident exists, Staff must choose `ALREADY_REPORTED` (save the report, no new Incident) or `DIFFERENT_INCIDENT` (create a new Incident); that choice is retained in `original_input_json`.

`GET /api/staff-reports/{reportId}` and `GET /api/staff-reports/{reportId}/media/{mediaId}` allow the reporting Staff member or an Operator/Admin to read the report and photos. `POST /api/staff-reports/{reportId}/qualify-incident` (OPERATOR) is an idempotent exception/recovery path; normal typed submissions create the Incident immediately. An open same-Zone/type Incident does not get silently merged; a distinct new Incident requires Staff's `DIFFERENT_INCIDENT` decision. V13 removes the legacy unique open Zone/type index while retaining its non-unique lookup index; V14 preserves uniqueness per open monitoring rule + Zone + type.

`POST /api/staff-reports/draft` (STAFF) accepts multipart `note`, up to three JPEG/PNG `photos` (10 MB each), and one optional `voice` recording (10 MB; total request limit 48 MB). Voice is transcribed before recommendation; M4A, MP3, WAV and WEBM are supported for analysis. Backend supplies active IncidentType/TaskType catalogs and explicitly mentioned Zone candidates, then calls OpenAI directly when `StaffReportAi:ApiKey`, `StaffReportAi:Model`, and (for voice) `StaffReportAi:TranscriptionModel` are configured. Response includes editable `title`, `description`, `locationDescription`, `reportCategory`, `incidentTypeCode`, `suggestedTaskTypeCodes`, `suggestedZoneId`, `transcript`, `confidenceLevel` (uncalibrated model self-assessment), `evidence`, and `needsReview=true`. Suggestions are revalidated against catalogs/candidates; nothing is confirmed or dispatched by the draft endpoint. By product decision, drafts are **not persisted server-side**; leaving/restarting Mobile can lose the review draft. A missing/failed provider returns 503 so Mobile can offer manual completion. Configure secrets only in `appsettings.Local.json` or environment variables, never in Mobile or Git. Draft text/photos/audio leave Backend for the configured external provider; use real staff/customer data only after team approval.

`GET /api/staff-reports/duplicates?zoneId=...&incidentTypeCode=...` (STAFF) returns open same-Zone/type Incident IDs and related Task IDs for the pre-submit warning. If a new duplicate appears after submission, the reporter can call `PUT /api/staff-reports/{reportId}/duplicate-decision` with `{ "decision": "DIFFERENT_INCIDENT" }` (or `ALREADY_REPORTED`) before qualification.

`GET /api/staff-reports/task-types` (STAFF) lists TaskTypes for confirmation with `routineEligible`; `GET /api/staff-reports/incident-types` supplies `allowedTaskTypeCodes` per type so clients need not hardcode eligibility. A `POTENTIAL_INCIDENT` multipart submission requires one to eight repeated `taskTypeCodes` fields. The backend validates each code against its IncidentType mapping (Spill/Broken Equipment: TT-06–09; Equipment Malfunction: TT-07–09; Pathway Obstruction: TT-10; Cleanliness: TT-11; Safety: TT-12–16). TT-13 also requires TT-12. `confirmedScenarioCode=EXPOSED_ELECTRICAL_WIRE` is accepted only with Safety Hazard and exactly TT-12 plus TT-13; it is Staff-confirmed, not AI-inferred. Staff-confirmed codes are retained with the report, returned as `confirmedTaskTypeCodes`, and reused for duplicate-choice recovery. When an Incident SLA policy is valid, the same transaction creates mandatory Incident-originated Tasks, snapshots the Incident policy at each Task's creation time, and offers them through specialty-aware dispatch. Task rows link to `Incident.incident_id`; their StaffReport source is retrieved through the Incident. `ALREADY_REPORTED` creates no new Incident or Tasks. Missing Incident SLA policy keeps the safety Incident and notifies the Operator but does not dispatch policy-less Tasks. MonitoringRule-originated Incidents do not use this StaffReport task path.

`GET /api/staff-reports/routine-overlaps?zoneId=...&taskTypeCode=...` warns about open same-Zone/TaskType Tasks, including Event-originated Tasks. When one exists, routine submit requires `routineTaskDecision=ALREADY_COVERED` (save report, no Task) or `SEPARATE_TASK` (create another Task); no silent merge. `POST /api/tasks/manual` (OPERATOR) accepts `{taskId, zoneId, taskTypeCode, title, instructions}` and uses the same standalone TaskType policy for TT-01–11. Both standalone paths snapshot policy version, priority, response/resolution deadlines and offer timeout on continuous UTC wall-clock time and dispatch by specialty. V16 adds `SLAPolicy.task_type_id`, a single enabled policy per TaskType, and seeds TT-01–11; Incident and OperationalEvent policies are unchanged.

# Incident closure and SLA milestones (2026-11-09)

Incident closure (BR-22): when an Operator verifies a Task linked to an Incident, the backend closes that Incident in the same transaction if every `is_mandatory` Task of the Incident is now `VERIFIED`. The Incident gets `status=CLOSED`, `status_reason=ALL_MANDATORY_TASKS_VERIFIED` and `closed_at`, an `AuditLog` row `CLOSE_INCIDENT`, and an in-app `INCIDENT_CLOSED` notification to Operators. Verifying an optional Task never closes an Incident; a cancelled mandatory Task blocks automatic closure and is left to the Operator. Already ended Incidents (`CLOSED`, `DISMISSED`, `CANCELLED`) are not touched. Independent closure conditions beyond mandatory Tasks are not modelled yet. No new route.

Mitigation (BR-21/K5, 11/11/2026): only catalog TaskType code `TT-12` (Isolate Hazardous Area), linked to a valid open Incident in the same Zone, is a designated mitigation Task. In the existing Operator MANUAL-verification transaction, successful verification records `Incident.mitigated_at = Task.verified_at` only if unset, before the independent closure check, and writes a `MITIGATE_INCIDENT` audit referencing the Task and reviewer. Staff submission does not set it; TT-13 and other TaskTypes do not set it; later verification never overwrites the first timestamp. Mitigation alone does not close the Incident, complete other Tasks or stop/reset resolution SLA. Incidents without a designated mitigation Task keep this field null. No new endpoint, DTO, database migration or OpenAPI change.

SLA milestones (BR-38–BR-41): the `SlaWorker` background service (config `SlaMonitor: { Enabled, PollSeconds }`, default 15 s) checks open Tasks and Incidents. The clock starts at the snapshot `appliedAt` (or `created_at`) and the warning percentage comes from the bound snapshot (`warningPercent`, default 80 %); breach is at 100 % of the deadline. Task response milestones (`SLA_RESPONSE_WARNING`, `SLA_RESPONSE_BREACH`) apply only until the first valid acceptance; resolution milestones (`SLA_RESOLUTION_WARNING`, `SLA_RESOLUTION_BREACH`) apply until `VERIFIED` or `CANCELLED`. Incidents get `INCIDENT_SLA_WARNING` and `INCIDENT_SLA_BREACH` until they end. Each milestone is one Notification row written at most once, so it stays auditable after late completion; a breach found late also records its warning. Warnings go to Operators (`RecipientReason=SLA_WARNING`); breaches escalate to Operators and Managers (`SLA_ESCALATION`). Offer expiry stays a dispatch event. No schema change and no new route.

Staff Report voice notes (2026-11-09): `POST /api/staff-reports` also accepts one optional `voice` file in the same multipart form (at most 10 MB; the whole request limit is now 48 MB). The backend detects the format from the bytes, not the declared type: M4A/MP4 audio (`audio/mp4`), AAC (`audio/aac`), MP3 (`audio/mpeg`), WAV (`audio/wav`), OGG/Opus (`audio/ogg`) or WEBM (`audio/webm`); anything else returns 422 `REPORT_VOICE_FORMAT`, an oversized file 422 `REPORT_VOICE_SIZE`, and a server without audio storage 503 `REPORT_VOICE_STORAGE_NOT_CONFIGURED`. The note is stored as a `StaffReportMedia` row with `media_type=AUDIO` in Cloudinary (resource type `video`, `authenticated`, folder `StaffReportMedia:CloudinaryFolder`) and is removed again if the report cannot be saved. The report view keeps `photos` for images only and adds `voiceNote` (`mediaId`, `contentType`, `sizeBytes`, `imageUrl`); download it through the same `GET /api/staff-reports/{reportId}/media/{mediaId}` route. Draft analysis can transcribe supported formats before Staff confirmation; final submission stores the recording. Staff still confirms the title, Zone and type.

# SLA administration, Incident board and Operator task details (L1/L3/L4/L7, 2026-11-11)

Team decision 11/11/2026: SLA priority stays `LOW` / `MEDIUM` / `HIGH` / `CRITICAL` (no P1–P4). Shared validation lives in `Supermarket.Domain.SlaPolicyRules`.

| Quyền | Route | Ghi chú |
|---|---|---|
| ADMIN | `GET /api/operational-event-types/sla-policies` | Mọi OperationalEventType kèm `policy` (null khi chưa có) và `issues` (`POLICY_MISSING`, `POLICY_AMBIGUOUS`, `POLICY_DISABLED`, `PRIORITY_INVALID`, `TIMING_INVALID`). |
| ADMIN | `PUT /api/operational-event-types/{eventTypeId}/sla-policy` | Body `{expectedVersion, isEnabled, priorityCode, responseTargetSec, resolutionTargetSec, offerTimeoutSec, warningPercent}`. `expectedVersion: null` tạo v1; sửa thì phải gửi version hiện tại, rồi version tăng 1. Sai version trả 409 `SLA_POLICY_VERSION_CONFLICT`; priority khác LOW–CRITICAL trả `INVALID_SLA_PRIORITY`; resolution nhỏ hơn response hoặc offer timeout lớn hơn resolution thì bị từ chối. Snapshot đã gắn vào Task/Incident không bị sửa. |
| ADMIN | `GET /api/task-types/sla-policies` | SLA theo TaskType (problem_domain `STANDALONE_TASK`) cho Task routine từ StaffReport và Task Operator tạo tay. |
| ADMIN | `PUT /api/task-types/{taskTypeId}/sla-policy` | Cùng body và quy tắc version như trên; `StandaloneTaskSlaPolicyService` dùng policy này. |
| ADMIN | `GET /api/sla-policies/{policyId}/escalation` | `{policyId, problemDomain, version, warningRoles, breachRoles, usesDefault}`. Khi chưa cấu hình thì mặc định warning → OPERATOR, breach → OPERATOR + MANAGER. |
| ADMIN | `PUT /api/sla-policies/{policyId}/escalation` | Body `{expectedVersion, warningRoles, breachRoles}`, mỗi danh sách ≥1 role trong `OPERATOR`/`MANAGER`. Ghi lại các dòng `SLAPolicyAction` có `action_type` `SLA_WARNING_NOTIFY`/`SLA_BREACH_ESCALATE`, `condition_json {"sla_milestone":"WARNING"\|"BREACH"}` và `target_role_id`, rồi tăng version của policy. `SlaMonitor` gửi mốc warning/breach theo các role này. Các dòng này không bao giờ khớp action WARNING/CRITICAL theo alert_level. |
| ADMIN | `PUT /api/monitoring-rules/{ruleId}/response-policies/WARNING` | `action.targetRoleCode` tuỳ chọn: `OPERATOR` (mặc định, lưu `target_role_id = null`) hoặc `MANAGER`. Thông báo `OPERATIONAL_EVENT_WARNING` gửi cho role này. Gửi `targetRoleCode` cho CRITICAL trả `INVALID_TARGET_ROLE`. View action trả thêm `targetRoleCode`. |
| OPERATOR | `GET /api/incidents?state=open\|ended\|all` | Mặc định `open`, mới nhất trước, tối đa 200 dòng. Mỗi dòng có type, zone, status/statusReason, `mitigatedAt` (BR-21), `closedAt` (BR-22), SLA priority và deadline, số mandatory Task đã VERIFIED và danh sách Task (`taskTypeCode`; `isMitigation` = true với TT-12, Task mà khi được MANUAL-verify sẽ ghi `mitigatedAt`). |
| OPERATOR | `GET /api/incidents/{incidentId}` | Một dòng như trên. |

`StaffTaskView` (`GET /api/tasks`, `GET /api/tasks/{id}`, `/api/me/tasks`) trả thêm `rejectionCount`, `pendingManualStaffUserId` / `pendingManualStaffName` (Operator giao tay và đang chờ Staff chấp nhận) và `lastAssignmentEndReason` (`REJECTED_TWICE`, `REJECTED_AFTER_REASSIGNMENT`, ...).

# Audit log inspection (FE-12 §3.13.3, 2026-11-12)

| Quyền | Route | Ghi chú |
|---|---|---|
| ADMIN | `GET /api/admin/audit-logs` | Query `from`, `to` (ngày local của store, inclusive), `userId`, `action`, `entityType`, `entityId`, `result`, `search` (chứa trong action/entity type/reason/tên hoặc email user), `page` 1–10000, `pageSize` 1–100 (mặc định 50). Mới nhất trước; filter và phân trang làm ở SQL. Trả `{items, totalCount, page, pageSize, generatedAt, timeZone}`; mỗi item gồm actor (tên, email, role), action, entity, reason, result, IP, `oldValuesJson`, `newValuesJson`. Không trả password hash. Sai tham số trả 400 `INVALID_AUDIT_QUERY`; DB lỗi trả 503 `AUDIT_DATA_UNAVAILABLE`. `Cache-Control: no-store`. |
| ADMIN | `GET /api/admin/audit-logs/facets` | Các giá trị đang có trong log để làm dropdown filter: `actions`, `entityTypes`, `results`, `actors`. |
| ADMIN | `GET /api/admin/audit-logs/{auditLogId}` | Một entry; không có thì trả 404. |

API chỉ đọc. Audit log là append-only: không có route sửa hoặc xoá.

# Monitoring rule dual threshold (hysteresis, 2026-11-14)

`MonitoringRuleRequest` (lưu cấu hình monitoring của Zone) nhận thêm hai trường tuỳ chọn: `warningExitThreshold` và `criticalExitThreshold`. `MonitoringRuleView` và runtime view của rule (`GET /api/cameras/{id}/monitoring-runtime`) trả lại hai trường này. Runtime view luôn trả giá trị đang có hiệu lực.

- Một mức được **vào** khi giá trị ≥ ngưỡng của mức đó (`warningThreshold` / `criticalThreshold`), và được **giữ** cho tới khi giá trị < ngưỡng ra. Vùng giữa ngưỡng ra và ngưỡng vào là vùng đệm: ở trong đó thì giữ nguyên mức hiện tại.
- Thời gian clear event chỉ bắt đầu tính khi giá trị < `warningExitThreshold`.
- Ràng buộc: `0 ≤ warningExit ≤ warning`, `warning < criticalExit ≤ critical`, tối đa 4 chữ số thập phân, đơn vị `PEOPLE` phải là số nguyên. Sai ràng buộc trả `INVALID_EXIT_THRESHOLDS`.
- Không gửi (null) = ngưỡng ra bằng ngưỡng vào, tức là chạy đúng như trước. Giá trị được lưu trong `parameters_json` (khoá `warningExitThreshold`, `criticalExitThreshold`), không đổi schema DB. Các khoá khác trong `parameters_json` (ví dụ của checkout) được giữ nguyên.
- Priority của OperationalEvent vẫn chỉ tăng trong một episode, không bao giờ hạ (phương án A).
