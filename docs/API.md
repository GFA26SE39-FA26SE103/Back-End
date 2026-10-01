# MF-01 API contract

Base URL local: `http://localhost:5080/api`. JSON dùng camelCase; IDs là UUID; dates/timestamps gửi theo ISO 8601 UTC. Tất cả configuration endpoints yêu cầu JWT role ADMIN. `POST /auth/login` public; `GET /auth/me` dành cho mọi account ACTIVE đã đăng nhập.

| Capability | Routes |
|---|---|
| Auth | POST /auth/login; GET /auth/me |
| Accounts | GET/POST /users; PATCH /users/{id}; POST /users/{id}/enable hoặc disable; GET /roles |
| Supermarket | GET/POST /supermarkets; GET/PATCH /supermarkets/{id} |
| Floors | GET/POST /supermarkets/{id}/floors; GET/PATCH /floors/{id} |
| Zones | GET/POST /floors/{id}/zones; GET/PATCH /zones/{id} |
| Cameras | GET/POST /floors/{id}/cameras; GET/PATCH /cameras/{id} |
| Connection | GET/PUT /cameras/{id}/connection; POST .../test, .../enable, .../disable |
| Preview | GET /cameras/{id}/preview: image/svg+xml cho demo, image/jpeg cho FFmpeg |
| Geometry | GET /cameras/{id}/zones; PUT/DELETE /cameras/{id}/zones/{zoneId} |
| Monitoring | GET/PUT /zones/{zoneId}/monitoring; POST .../activate, .../deactivate |
| Health | GET /cameras/{id}/health; POST /cameras/{id}/health/check; GET /camera-health-events?cameraId=...&status=... |
| Investigation | POST /camera-health-events/{id}/investigate; POST .../resolve |
| Development demo | POST /demo/cameras/{id}/state?online=false hoặc true |

Liveness `/health/live`, readiness `/health/ready` nằm ngoài prefix `/api`. Swagger JSON `/swagger/v1/swagger.json` và UI `/swagger` chỉ bật ở Development; schema được tạo trực tiếp từ controller và DTO.

`openapi.json` trong thư mục này là contract xuất từ API đã chạy để frontend import; xuất lại khi controller/DTO thay đổi. Runtime Swagger luôn là bản contract hiện tại.

Ví dụ zone:

```json
{"code":"CHECKOUT","name":"Checkout area","zoneType":"CHECKOUT","status":"ACTIVE","mapPolygon":[{"x":0.1,"y":0.1},{"x":0.8,"y":0.1},{"x":0.1,"y":0.8}]}
```

Ví dụ camera connection:

```json
{"sourceType":"LIVE","protocol":"RTSP","streamUri":"rtsp://10.0.1.21/main","username":"camera-user","password":"<camera credential>"}
```

Ví dụ mapping:

```json
{"roiPolygon":[{"x":0.1,"y":0.1},{"x":0.8,"y":0.1},{"x":0.1,"y":0.8}],"status":"ACTIVE"}
```

Ví dụ monitoring:

```json
{"name":"Checkout person monitoring","confidenceThreshold":0.5}
```

PATCH dùng full edit DTO; không gửi IDs/createdAt/updatedAt trong payload. PUT connection luôn invalidate test và disable connection. Không có public self-registration, account PENDING hoặc camera reviewer. Preview được gọi với Authorization header, ví dụ fetch → Blob URL ở React.

HTTP errors: 400 request shape, 401 token/login, 403 role, 404 missing resource, 409 duplicate/concurrency/workflow, 422 domain validation, 429 login rate limit, 500 sanitized unexpected error. Application/domain ProblemDetails có `code`, `requestId`, `title`, `detail`, `status`; request-shape validation dùng `errors` của ASP.NET Core. Client cần hỗ trợ cả hai dạng.

Các code thường gặp: `CROSS_FLOOR_MAPPING`, `INVALID_POLYGON`, `CONNECTION_NOT_READY`, `CONNECTION_CHANGED`, `LAST_ACTIVE_ADMIN`, `MONITORING_NOT_READY`, `MONITORING_ACTIVE`, `CONCURRENT_UPDATE`, `VIDEO_ADAPTER_REQUIRED`, `STREAM_UNAVAILABLE`.
