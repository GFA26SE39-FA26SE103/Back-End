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
| Floors | GET/POST /supermarkets/{id}/floors; GET/PATCH /floors/{id}; POST/GET /floors/{id}/map |
| Zones | GET/POST /floors/{id}/zones; GET/PATCH /zones/{id}; normalized polygon, optional `colorHex` and `areaM2` |
| Cameras | GET/POST /floors/{id}/cameras; GET/PATCH /cameras/{id} |
| Connection | GET/PUT /cameras/{id}/connection; POST .../test, .../enable, .../disable |
| Preview | GET /cameras/{id}/preview: image/svg+xml cho demo, image/jpeg cho FFmpeg |
| Geometry | GET /cameras/{id}/zones; PUT/DELETE /cameras/{id}/zones/{zoneId} |
| AI catalog | GET /incident-types (ADMIN, read-only AI types) |
| Monitoring | GET/PUT /zones/{zoneId}/monitoring; GET .../review; POST .../activate, .../deactivate |
| Health | GET /cameras/{id}/health; POST /cameras/{id}/health/check; GET /camera-health-events?cameraId=...&status=... |
| Investigation | POST /camera-health-events/{id}/investigate; POST .../resolve |
| Development demo | POST /demo/cameras/{id}/state?online=false hoặc true |

Liveness `/health/live`, readiness `/health/ready` nằm ngoài prefix `/api`. Swagger JSON `/swagger/v1/swagger.json` và UI `/swagger` chỉ bật ở Development; schema được tạo trực tiếp từ controller và DTO.

`openapi.json` trong thư mục này là contract xuất từ API đã chạy để frontend import; xuất lại khi controller/DTO thay đổi. Runtime Swagger luôn là bản contract hiện tại.

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

### Monitoring Draft / Review / Activate

`GET /api/incident-types` trả baseline AI types với `supported`, `thresholdUnit`, configurable defaults và `unsupportedReason`. Catalog read-only, không gồm staff-reported types. Checkout Capacity chưa supported vì counter/composite measurement chưa được chốt.

`PUT /zones/{zoneId}/monitoring` tạo hoặc thay toàn bộ Draft + rules trong transaction. `rules` bắt buộc; `[]` hợp lệ cho Draft nhưng không activate. Omit rule khỏi array để xóa. Mỗi type tối đa một rule/config. Khi cấu hình đã tồn tại, gửi `expectedUpdatedAt` từ GET/Save gần nhất; thiếu/stale trả 409 `CONFIGURATION_CHANGED`. ACTIVE trả 409 `MONITORING_ACTIVE`, không tự deactivate khi Save.

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

Admin kiểm thử confidence đã lưu bằng `POST /api/cameras/{id}/ai-preview/start?zoneId=<uuid>`: backend yêu cầu zone tồn tại, mapping ACTIVE cùng tầng và valid ROI/config confidence, stop session cũ trước khi start Python với zone confidence. Cấu hình có thể DRAFT, không cần Activate. Không có query thì preview ADMIN/OPERATOR giữ hành vi cũ/default `AiPreview:Confidence`; query có zone chỉ ADMIN. Restart reset camera-local track IDs và có thể ảnh hưởng viewer khác. Preview chỉ vẽ detections/tracks toàn frame, không đánh giá rule/ROI metrics. Source vẫn phải test-success/enabled, camera ACTIVE.

Rule validation codes: `INVALID_CONFIDENCE`, `INVALID_THRESHOLDS`, `INVALID_RULE_UNIT`, `INVALID_RULE_TIMING`, `INVALID_RULE_PARAMETERS`, `INCIDENT_TYPE_NOT_AI`, `INCIDENT_TYPE_INACTIVE`, `RULE_UNSUPPORTED`, `DUPLICATE_RULE`. Review issues thêm `NO_ENABLED_RULES`, `ZONE_AREA_REQUIRED`, `CAMERA_NOT_READY`, `AI_SOURCE_UNSUPPORTED`.

Floor plan upload dùng `POST /api/floors/{id}/map` với `multipart/form-data`, field bắt buộc tên `file`. Chấp nhận PNG, JPEG hoặc PDF tối đa 20 MB theo mặc định (`FloorPlan:MaxBytes`); server kiểm tra extension, MIME và file signature, sinh storage name, cập nhật `Floor.mapAssetUrl/mapWidth/mapHeight`, rồi xóa asset cũ sau khi commit thành công. Response:

```json
{"floorId":"<uuid>","mapUrl":"http://localhost:5080/api/floors/<uuid>/map?v=<generated-token>","mapWidth":1200,"mapHeight":800,"contentType":"image/png","updatedAt":"2026-10-03T00:00:00Z"}
```

`GET /api/floors/{id}/map` yêu cầu JWT ADMIN, OPERATOR hoặc MANAGER và stream bytes với Content-Type đã lưu. Asset không được public qua static files và response local dùng `Cache-Control: no-store`. File mặc định nằm ngoài `wwwroot` tại `src/Supermarket.Api/.local/floor-plans`; client không được biết hoặc gửi đường dẫn này. Upload thay thế cùng một floor được serialize để tránh hai request đồng thời xóa nhầm asset hiện hành.

Camera placement dùng contract camera hiện có: `mapX` và `mapY` normalized trong `[0,1]`, `mapRotationDeg` trong `[0,360)`. `PATCH /api/cameras/{id}` vẫn nhận full edit DTO; client phải giữ nguyên metadata, dates và status khi chỉ đổi placement. Lưu placement không tự test/enable connection hoặc activate monitoring.

PATCH dùng full edit DTO; không gửi IDs/createdAt/updatedAt trong payload. PUT connection luôn invalidate test và disable connection. Không có public self-registration, account PENDING hoặc camera reviewer. Preview được gọi với Authorization header, ví dụ fetch → Blob URL ở React.

HTTP errors: 400 request shape, 401 token/login, 403 role, 404 missing resource, 409 duplicate/concurrency/workflow, 422 domain validation, 429 login rate limit, 500 sanitized unexpected error. Application/domain ProblemDetails có `code`, `requestId`, `title`, `detail`, `status`; request-shape validation dùng `errors` của ASP.NET Core. Client cần hỗ trợ cả hai dạng.

Các code thường gặp: `CROSS_FLOOR_MAPPING`, `INVALID_POLYGON`, `CONNECTION_NOT_READY`, `CONNECTION_CHANGED`, `LAST_ACTIVE_ADMIN`, `MONITORING_NOT_READY`, `MONITORING_ACTIVE`, `CONCURRENT_UPDATE`, `FLOOR_MAP_SIZE_INVALID`, `FLOOR_MAP_FORMAT_INVALID`, `FLOOR_MAP_NOT_FOUND`, `VIDEO_ADAPTER_REQUIRED`, `STREAM_UNAVAILABLE`.
