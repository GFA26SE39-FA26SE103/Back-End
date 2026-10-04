# MF-01 API contract

Base URL local: `http://localhost:5080/api`. JSON dùng camelCase; IDs là UUID; dates/timestamps gửi theo ISO 8601 UTC. Mọi thao tác ghi cấu hình yêu cầu JWT role ADMIN. `POST /auth/login` public; `GET /auth/me` dành cho mọi account ACTIVE đã đăng nhập.

Quyền đọc cho màn vận hành (Operator floor map, camera live):

| Quyền | Endpoints |
|---|---|
| ADMIN, OPERATOR, MANAGER | `GET /supermarkets`, `GET /supermarkets/{id}`, `GET /supermarkets/{id}/floors`, `GET /floors/{id}`, `GET /floors/{id}/map`, `GET /floors/{id}/zones`, `GET /zones/{id}`, `GET /floors/{id}/cameras`, `GET /cameras/{id}`, `GET /cameras/{id}/zones`, `GET /cameras/{id}/preview` |
| ADMIN, OPERATOR | `/cameras/{id}/ai-preview/start`, `status`, `frame`, `stop` (một phiên GPU dùng chung; người sau có thể nhận `AI_SESSION_CAPACITY`) |
| Chỉ ADMIN | Mọi POST/PUT/PATCH/DELETE, `GET /cameras/{id}/connection` (có stream URI), `POST .../connection/test`, accounts, monitoring, camera health |

Controller và use case cùng kiểm tra quyền; STAFF không đọc được dữ liệu setup.

| Capability | Routes |
|---|---|
| Auth | POST /auth/login; GET /auth/me |
| Accounts | GET/POST /users; PATCH /users/{id}; POST /users/{id}/enable hoặc disable; GET /roles |
| Supermarket | GET/POST /supermarkets; GET/PATCH /supermarkets/{id} |
| Floors | GET/POST /supermarkets/{id}/floors; GET/PATCH /floors/{id}; PATCH /floors/{id}/details; POST/GET /floors/{id}/map |
| Zones | GET/POST /floors/{id}/zones; GET/PATCH /zones/{id}; normalized polygon, optional `colorHex` and `areaM2` |
| Cameras | GET/POST /floors/{id}/cameras; GET/PATCH /cameras/{id} |
| Connection | GET/PUT /cameras/{id}/connection; POST .../test, .../enable, .../disable |
| Preview | GET /cameras/{id}/preview: image/svg+xml cho demo, image/jpeg cho FFmpeg |
| Geometry | GET /cameras/{id}/zones; PUT/DELETE /cameras/{id}/zones/{zoneId} |
| AI catalog | GET /incident-types (ADMIN, read-only AI types) |
| Monitoring | GET/PUT/DELETE /zones/{zoneId}/monitoring; GET .../review; POST .../activate, .../deactivate |
| Setup dashboard | GET /setup/overview (ADMIN, read-only saved-data snapshot) |
| Health | GET /cameras/{id}/health; POST /cameras/{id}/health/check; GET /camera-health-events?cameraId=...&status=... |
| Investigation | POST /camera-health-events/{id}/investigate; POST .../resolve |
| Development demo | POST /demo/cameras/{id}/state?online=false hoặc true |

Liveness `/health/live`, readiness `/health/ready` nằm ngoài prefix `/api`. Swagger JSON `/swagger/v1/swagger.json` và UI `/swagger` chỉ bật ở Development; schema được tạo trực tiếp từ controller và DTO.

`openapi.json` trong thư mục này là contract xuất từ API đã chạy để frontend import; xuất lại khi controller/DTO thay đổi. Runtime Swagger luôn là bản contract hiện tại.

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

`STREAM_UNAVAILABLE` mở/resolve theo transport. Visual event dùng hysteresis cấu hình `BadObservationsToOpen` và `GoodObservationsToResolve`, unique theo camera + event type trong application runtime; các loại độc lập có thể đồng thời OPEN/INVESTIGATING. V1 gồm `CAMERA_VIEW_BLOCKED`, `CAMERA_VIEW_BLURRED`, `CAMERA_FRAME_INVALID`; `CAMERA_VIEW_FROZEN` dành cho adapter có freshness metadata đáng tin cậy. AI service unavailable chỉ làm processing unavailable/readiness NOT_READY, không tạo CameraHealthEvent và không đổi camera ONLINE thành OFFLINE. Health issue camera-wide; monitoring configuration vẫn ACTIVE. MF-02 measurement producer phải dùng readiness gate và không phát measurement/rule/event/incident khi NOT_READY; producer đó chưa thuộc increment MF-01 hiện tại.

Ví dụ zone:

```json
{"code":"CHECKOUT","name":"Checkout area","zoneType":"CHECKOUT","status":"ACTIVE","colorHex":"#F97316","areaM2":42.5,"mapPolygon":[{"x":0.1,"y":0.1},{"x":0.8,"y":0.1},{"x":0.1,"y":0.8}]}
```

Ví dụ camera connection:

```json
{"sourceType":"LIVE","protocol":"RTSP","streamUri":"rtsp://10.0.1.21/main","username":"camera-user","password":"<camera credential>"}
```

Ví dụ mapping:

```json
{"roiPolygon":[{"x":0.1,"y":0.1},{"x":0.8,"y":0.1},{"x":0.1,"y":0.8}],"status":"ACTIVE"}
```

### Floor creation and metadata editing

`POST /api/supermarkets/{id}/floors` (ADMIN) dùng `FloorRequest` hiện có. Store Layout gửi `floorNumber`, `name` và `mapAssetUrl/mapWidth/mapHeight: null`; tạo floor trước, upload map sau. Store là default store từ seed, không thêm flow tạo/chọn store.

`PATCH /api/floors/{id}/details` (ADMIN) nhận `{"floorNumber":2,"name":"Upper floor"}`. Name trim, bắt buộc, tối đa 100 ký tự; floorNumber là int và unique trong cùng store (409 `DUPLICATE` nếu trùng). Không giới hạn tầng phải dương; tầng 0 hoặc tầng âm được phép. Endpoint chỉ thay name/number, giữ floorId, storeId và metadata map hiện tại đọc từ DB, cùng các zone/camera/mapping. Không gửi lại map URL cũ từ lúc mở form. Response là Floor. Full `PATCH /floors/{id}` vẫn giữ contract cũ; upload map dùng endpoint riêng.

### Monitoring Draft / Review / Activate / Delete

`GET /api/incident-types` trả baseline AI types với `supported`, `thresholdUnit`, configurable defaults và `unsupportedReason`. Catalog read-only, không gồm staff-reported types. Checkout Capacity chưa supported vì counter/composite measurement chưa được chốt.

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

`GET .../review` trả `configuration, zone, cameras[], issues[], warnings[], canActivate`. Camera summary gồm mapping/ROI, source type/protocol, enabled, test result/time, readiness và issues; không trả stream URI, username hoặc camera credentials. Cần zone ACTIVE, ít nhất một supported rule enabled và một mapped camera ACTIVE cùng tầng có ROI hợp lệ, nguồn LIVE HTTP/RTSP/HLS hoặc RECORDED FILE tested-success/enabled. Density enabled cần area_m2 > 0; DEMO không là AI source. Review kiểm tra dữ liệu đã lưu, không thay một connection probe mới. Multi-camera selection/overlap còn mở, recorded source là test/fallback, MF-02 runtime chưa chạy: trả warnings rõ ràng.

`POST .../activate` và `POST .../deactivate` nhận:

```json
{"expectedUpdatedAt":"<updatedAt from reviewed/saved configuration, ISO UTC>"}
```

Activate revalidates readiness trong Serializable transaction (409 `MONITORING_NOT_READY` nếu blocker), không tin kết quả review cũ. Response là ConfigurationView status ACTIVE/INACTIVE và updatedAt mới. Mọi thao tác chỉ ADMIN. Deactivate trước khi thay rule/confidence/source/mapping/ROI. Activate chưa tạo AI worker, measurements hay incidents.

`DELETE /api/zones/{zoneId}/monitoring` (ADMIN) nhận JSON body `{"configId":"<saved config UUID>","expectedUpdatedAt":"<saved updatedAt, ISO UTC>"}`. Chỉ xóa DRAFT hoặc INACTIVE; ACTIVE trả 409 `MONITORING_ACTIVE`, phải Deactivate trước. Config ID hoặc timestamp thay đổi trả 409 `CONFIGURATION_CHANGED`, kể cả khi config cũ đã bị xóa và tạo lại. Không có config trả 404. Thành công trả 204, xóa config và rules của nó trong cùng transaction; Zone, Camera và CameraZoneMapping/ROI được giữ. Các FK khác vẫn được tôn trọng và lỗi persistence rollback transaction. Sau xóa, GET monitoring trả 404, setup overview có configuration null; có thể tạo Draft mới. UI yêu cầu xác nhận tên config/zone/floor, có Cancel/close/Escape; thất bại giữ config và hộp thoại để Admin đọc lỗi.

Admin kiểm thử confidence đã lưu bằng `POST /api/cameras/{id}/ai-preview/start?zoneId=<uuid>`: backend yêu cầu zone tồn tại, mapping ACTIVE cùng tầng và valid ROI/config confidence, stop session cũ trước khi start Python với zone confidence. Cấu hình có thể DRAFT, không cần Activate. Không có query thì preview ADMIN/OPERATOR giữ hành vi cũ/default `AiPreview:Confidence`; query có zone chỉ ADMIN. Restart reset camera-local track IDs và có thể ảnh hưởng viewer khác. Preview chỉ vẽ detections/tracks toàn frame, không đánh giá rule/ROI metrics. Source vẫn phải test-success/enabled, camera ACTIVE.

Rule validation codes: `INVALID_CONFIDENCE`, `INVALID_THRESHOLDS`, `INVALID_RULE_UNIT`, `INVALID_RULE_TIMING`, `INVALID_RULE_PARAMETERS`, `INCIDENT_TYPE_NOT_AI`, `INCIDENT_TYPE_INACTIVE`, `RULE_UNSUPPORTED`, `DUPLICATE_RULE`. Review issues thêm `NO_ENABLED_RULES`, `ZONE_AREA_REQUIRED`, `CAMERA_NOT_READY`, `AI_SOURCE_UNSUPPORTED`.

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

Các code thường gặp: `CROSS_FLOOR_MAPPING`, `INVALID_POLYGON`, `CONNECTION_NOT_READY`, `CONNECTION_CHANGED`, `LAST_ACTIVE_ADMIN`, `MONITORING_NOT_READY`, `MONITORING_ACTIVE`, `CONCURRENT_UPDATE`, `FLOOR_MAP_SIZE_INVALID`, `FLOOR_MAP_FORMAT_INVALID`, `FLOOR_MAP_NOT_FOUND`, `VIDEO_ADAPTER_REQUIRED`, `STREAM_UNAVAILABLE`.
