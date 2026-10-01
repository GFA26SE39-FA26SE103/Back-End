# FA26SE103 MF-01 backend

Backend ASP.NET Core .NET 10 cho **Setup & System Configuration**, theo RP1/RP2 và `Project/AGENT/AGENTS.md`. SQL Server là nguồn dữ liệu chính. Frontend hiện vẫn dùng mocks; thay đổi này dựng backend và hợp đồng API để frontend tích hợp tiếp.

## Chạy local trên Windows

Cần .NET SDK 10, SQL Server Express và `sqlcmd`. Từ thư mục `Project/Back-End`:

Khi clone repository backend riêng, đặt checkout tại `Project/Back-End` và giữ script SQL của team tại `Project/DB/FA26SE103_Database_V0.1.sql`. Script SQL không được lưu trong repository backend.

```powershell
dotnet restore --configfile NuGet.Config
.\scripts\Initialize-Local.ps1
.\scripts\Start-Local.ps1 -BootstrapAdmin
```

Lần đầu nhập mật khẩu Admin dài 12–128 ký tự. Email mặc định `admin@mf01.local`; có thể đổi bằng `-AdminEmail`. Bootstrap chỉ tạo Admin khi database chưa có tài khoản; mật khẩu bootstrap chỉ truyền qua môi trường process, không ghi vào file. Các lần sau chạy `Start-Local.ps1` không có `-BootstrapAdmin`.

API: `http://localhost:5080`; Swagger: `http://localhost:5080/swagger`. Đăng nhập bằng `POST /api/auth/login`, sao chép `accessToken` vào **Authorize** của Swagger. Configuration API chỉ dành cho ADMIN. Token của tài khoản đã disable hoặc đổi role bị từ chối ngay ở request tiếp theo.

`Initialize-Local.ps1` tạo database **FA26SE103_MF01_Local** độc lập, không import lại nếu database đã tồn tại. Script sinh JWT key vào `appsettings.Local.json` đã được gitignore. Nếu muốn dùng instance/database khác, truyền `-Server` và `-Database`; tên database phải bắt đầu bằng `FA26SE103_MF01_`.

## Kiến trúc

```text
Supermarket.Api             HTTP controllers, JWT/RBAC, ProblemDetails, Swagger
Supermarket.Application     use cases, DTOs, repository/video/secret abstractions
Supermarket.Domain          business models, geometry validation, state rules
Supermarket.Infrastructure  scaffolded EF, repository mapping, encryption, video, worker
```

Domain không phụ thuộc ASP.NET/EF. Controller không truy cập DbContext. EF entities không được trả trực tiếp ra API. Domain và persistence model được map trong repository; predicate đơn giản được chuyển sang EF để lọc tại SQL. Transaction Serializable bảo vệ kiểm tra Admin cuối cùng, một branch và health-event deduplication; `updated_at` là concurrency token và được cập nhật mỗi mutation.

## Phạm vi đã implement

- Login JWT, `/auth/me`, roles, Admin tạo/sửa/enable/disable tài khoản, bảo vệ Admin cuối cùng.
- Supermarket, floors, map URL/dimensions, zones với polygon normalized `[0,1]`.
- Camera thuộc Floor; metadata lắp đặt/bảo hành, vị trí và rotation trên map.
- Connection 1:1: configure → test → preview → enable/disable. Mỗi lần PUT configuration sẽ disable và xóa kết quả test cũ.
- Camera N:M Zone qua mapping; cùng tầng; ROI normalized; PUT idempotent.
- MonitoringConfiguration per-zone, confidence `[0,1]`, DRAFT/ACTIVE/INACTIVE. Activation yêu cầu zone active và camera mapped active có connection enabled/tested.
- Health worker, check thủ công, OPEN → INVESTIGATING → RESOLVED. Khi có lại frame, worker tự resolve event và cập nhật last seen; Admin có thể thêm resolution note.
- Swagger/OpenAPI, ProblemDetails với `code` và `requestId`, rate limit login, CORS, liveness/readiness.

Floor map được tham chiếu bằng HTTP(S) asset URL. PATCH ở increment này nhận toàn bộ DTO chỉnh sửa, không phải JSON Patch hay merge patch. Camera không được chuyển sang tầng khác bằng PATCH; việc chuyển tầng cần use case xử lý mappings riêng.

## Demo MF-01

1. Login Admin, tạo supermarket, floor và zone với `mapPolygon` gồm ít nhất 3 điểm.
2. Tạo camera với `status: "ACTIVE"`, installation/warranty dates.
3. PUT connection với `{ "sourceType": "DEMO", "protocol": "HTTP", "streamUri": "demo://camera/main" }`.
4. POST connection/test, GET preview, POST connection/enable.
5. PUT camera/zones/{zoneId} với `roiPolygon`; PUT zone/monitoring và POST activate.
6. POST camera/health/check để thấy ONLINE.
7. Trong Development, POST `/api/demo/cameras/{id}/state?online=false`, rồi check health để mở event.
8. Investigate event; đổi demo state về true và check để thấy RESOLVED.

Preview hiện là ảnh frame (demo SVG hoặc JPEG), dùng được để vẽ ROI và refresh. Đây chưa phải browser HLS/WebRTC playback liên tục.

## Video và credential

RTSP, HTTP/HLS và recorded video dùng FFmpeg để đọc/giải mã một frame; thành công chỉ khi nhận được frame, không chỉ khi mở được TCP. Cấu hình `Video:FfmpegPath` trỏ tới FFmpeg trên máy. Recorded FILE phải nằm dưới `Video:RecordedRoot`. Dockerfile bao gồm FFmpeg. WebRTC yêu cầu adapter media gateway, hiện trả lỗi rõ ràng.

Camera URI không được chứa userinfo, query hay fragment. Gửi `username`/`password` riêng; password được mã hóa bằng ASP.NET Data Protection trong `credential_secret_ref`, DTO chỉ trả `hasCredentials`. PUT thay toàn bộ credentials; bỏ password sẽ xóa credentials cũ. Không trả secret reference, không log FFmpeg stderr hay SQL exception detail. Key ring phải được giữ qua restart; production cần volume riêng, ACL phù hợp và cấu hình bảo vệ key ring at rest. Giữ JWT key/DB credentials trong secrets môi trường, đặt HTTPS tại reverse proxy.

## Database-first

Schema SQL chỉ nằm tại `Project/DB/FA26SE103_Database_V0.1.sql`, bên cạnh thư mục `Project/Back-End`. Backend không giữ bản sao schema riêng. Script khởi tạo đọc file này trực tiếp; test project liên kết cùng file và chỉ sao chép vào build output để test runner đọc.

Scaffold local dùng Windows Authentication (`Integrated Security=True`) trên `.\SQLEXPRESS`, không dùng SQL username/password. Bản SQL hiện tại đã có UserAccount ACTIVE/DISABLED, không có cột account review và connection mặc định disabled; EF của các bảng đã implement được scaffold theo bản này.

Khi team phê duyệt schema mới: cập nhật file SQL trong `Project/DB`, áp dụng vào database, rồi scaffold lại:

```powershell
$env:MF01_SCAFFOLD_CONNECTION = 'Server=.\SQLEXPRESS;Database=FA26SE103_MF01_Local;Integrated Security=True;TrustServerCertificate=True'
.\scripts\Scaffold.ps1
dotnet test --settings coverage.runsettings --collect:'XPlat Code Coverage'
```

Generated files nằm tại `Infrastructure/Persistence/Scaffolded`; custom EF configuration nằm ở partial extension bên ngoài thư mục generated. Script chỉ scaffold 10 bảng stable, bỏ MonitoringRule. Không dùng `EnsureCreated` hoặc code-first migrations. Tham khảo [EF Core reverse engineering](https://learn.microsoft.com/en-us/ef/core/managing-schemas/scaffolding/).

## Kiểm thử

```powershell
dotnet test --settings coverage.runsettings --collect:'XPlat Code Coverage' --results-directory TestResults
dotnet publish src/Supermarket.Api -c Release
```

Integration tests tạo database ngẫu nhiên `FA26SE103_MF01_Test_<guid>` từ file SQL trong `Project/DB` và xóa đúng database đó sau test. Mặc định dùng Windows auth trên `.\SQLEXPRESS`. Đổi instance bằng `MF01_TEST_SERVER`, hoặc dùng SQL auth qua `MF01_TEST_CONNECTION`. Không trỏ vào database chứa dữ liệu cần giữ: tests luôn chọn catalog tạm riêng và cần quyền tạo/xóa database. Không bỏ qua SQL tests nếu thiếu kết nối; test sẽ báo fail.

GitHub Actions mặc định chạy build, domain unit tests và Release publish cho repository backend độc lập. Job SQL integration được bật khi repository variable `MF01_SCHEMA_REPOSITORY` trỏ tới repository chứa SQL authoritative của team. Có thể đặt `MF01_SCHEMA_REF` (mặc định main), `MF01_SCHEMA_FILE` (mặc định tên file SQL) và secret read-only `MF01_SCHEMA_READ_TOKEN` nếu repository schema là private. Khi chưa cấu hình nguồn schema, job integration hiển thị skipped; đó không phải bằng chứng đã kiểm tra SQL trên GitHub.

Nếu SQL nằm ở vị trí khác khi chạy local, truyền `-p:Mf01SchemaPath=<đường dẫn SQL>` cho `dotnet test`. Unit tests có thể build/chạy độc lập bằng `--filter FullyQualifiedName~Supermarket.Tests.DomainTests`; full integration tests vẫn báo fail rõ ràng nếu thiếu schema. Dockerfile/workflow được cung cấp để tích hợp, chưa deploy lên VPS.

## Các điểm còn chờ team đồng bộ

- RP1/RP2/BR local vẫn có mô tả camera thuộc zone và confidence-based incident review đã lỗi thời. Backend theo quyết định cập nhật trong AGENTS.md: camera thuộc floor, N:M zone, model confidence chỉ là input filter.
- MonitoringRule/IncidentType chỉ có DTO contract; chưa tạo endpoint hay persistence theo phạm vi triển khai hiện tại trong AGENTS.md. Bảng MonitoringRule trong file SQL của `Project/DB` chưa được backend sử dụng.
- `area_m2`, measurement-source selection và most-recent-maintenance field chưa có trong schema stable; backend không tự thêm cột.
- Activation hiện bật configuration trong backend; pipeline YOLO/ByteTrack và OperationalEvents thuộc MF-02, chưa chạy ở increment này.
- Nối các trang React với API, live CCTV cụ thể và playback gateway là các bước tích hợp tiếp theo. Chưa xác nhận full MF-01 UI acceptance hoặc hiệu năng live stream.
- Backend dùng repository riêng `GFA26SE39-FA26SE103/Back-End`: `main` là stable, `dev` là integration, feature branches qua PR vào dev và cần review trước khi merge.
