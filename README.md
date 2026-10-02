# FA26SE103 MF-01 backend

Backend ASP.NET Core .NET 10 cho **Setup & System Configuration**, theo `CAPSTONE/AGENTS.md` (ERD v3, revision 01/10/2026). SQL Server là nguồn dữ liệu chính. React đã nối thật login, camera registry, đăng ký/cấu hình, upload MP4 và AI preview; Store Layout, AI Config và dashboard setup/activate vẫn còn mock.

## Thành viên mới clone về cần gì?

Để chạy API với DB `FA26SE103_Dev` đã có sẵn, mỗi người cần .NET SDK 10, quyền truy cập SQL Server của team và **một file cấu hình riêng**: `src/Supermarket.Api/appsettings.Local.json`. Tạo file đó từ `appsettings.Local.example.json` đã có trong Git, điền connection string và sinh JWT key theo hướng dẫn dưới đây. JWT key có thể khác nhau nếu mỗi người đăng nhập vào API local của mình; chỉ dùng chung khi cần các instance chấp nhận cùng token.

Bạn chỉ cần cung cấp thông tin kết nối DB và tài khoản đăng nhập API Dev qua kênh riêng. Không cần gửi source bổ sung, `bin`, `obj`, `.tools`, `TestResults` hoặc bản publish. EF entities đã có trong source nên không cần scaffold lại để chạy. Script SQL chỉ cần khi tự tạo DB hoặc chạy SQL integration tests; kết nối DB có sẵn không cần file SQL. FFmpeg chỉ cần khi thử camera thật hoặc video recorded; camera DEMO không cần FFmpeg.

**Nếu cùng đọc mật khẩu camera đã mã hóa trong DB Dev:** các backend cần dùng chung Data Protection key ring của môi trường Dev. Chia sẻ riêng bộ key Dev tương ứng với dữ liệu đó, đặt trên máy từng người và cấu hình `DataProtection:KeyPath` trong file local tới thư mục vừa đặt key. Đồng bộ key ring khi có key mới; không lấy key production để dùng cho Dev và không commit key vào Git. Chỉ copy `appsettings.Local.json` không đủ để giải mã camera credentials đã được backend khác lưu.

Data Protection key được backend tự sinh khi cần bảo vệ dữ liệu và nằm tại đường dẫn `DataProtection:KeyPath`; mặc định là `src/Supermarket.Api/.local/keys` khi chạy local bằng launch profile `http`. Script tạo DB SQL Express đặt đường dẫn riêng tại `.local/keys` ở root backend. Key này phục vụ `CameraConnection.credential_secret_ref`; mật khẩu tài khoản API được hash bằng PasswordHasher, JWT dùng `Jwt:Key`. Giữ các key đã dùng cùng bản backup DB có camera credentials; mất key sẽ phải nhập lại mật khẩu camera. Tham khảo [cấu hình Data Protection của ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

| Thư mục/file trên máy | Cần gửi cho người clone? |
| --- | --- |
| `bin/`, `obj/` | Không; `dotnet restore`/build tự sinh lại. |
| `.tools/` | Không; bản cài EF tool tạm. Scaffold dùng tool manifest `.config/dotnet-tools.json` trong Git. |
| `TestResults/`, `.local/publish/` | Không; kết quả kiểm thử và build đã sinh trên máy. |
| `appsettings.Local.json` | Mỗi người tạo từ mẫu; cung cấp thông tin DB riêng, không copy đường dẫn tuyệt đối của máy khác. |
| Data Protection key ring Dev | Cần dùng chung khi nhiều backend giải mã cùng camera credentials trong DB Dev. |

## Appsettings và hai database có sẵn

Các file cấu hình nằm tại `src/Supermarket.Api`.

| File | Mục đích |
| --- | --- |
| `appsettings.json` | Cấu hình chung, có các mục `ConnectionStrings:SqlServer` và `Jwt:Key` để trống. |
| `appsettings.Development.json` | Cấu hình khi chạy local bằng launch profile `http`, cho phép camera demo. |
| `appsettings.Local.example.json` | Mẫu SQL Authentication cho DB `FA26SE103_Dev`; copy thành `appsettings.Local.json` rồi điền thông tin thật. |
| `appsettings.Local.json` | Connection string/JWT key riêng trên máy; gitignore và không đưa vào publish. |
| `appsettings.Production.json` | Cấu hình production cho VPS, connection string/JWT key để trống cho bạn cấu hình khi host. |

Với hai DB của team, hướng dẫn này quy ước **`FA26SE103_Dev` cho phát triển** và **`FA26SE103` cho production**. Mỗi instance backend dùng một connection string `SqlServer`, chọn database theo môi trường; không cần đồng thời kết nối cả hai DB. Backend không tự tạo database, import SQL hay chạy migrations khi khởi động.

### Chạy local với DB `FA26SE103_Dev` có sẵn

Cần .NET SDK 10 và quyền kết nối SQL Server của team. Từ thư mục `Project/Back-End`:

```powershell
if (!(Test-Path src/Supermarket.Api/appsettings.Local.json)) {
    Copy-Item src/Supermarket.Api/appsettings.Local.example.json src/Supermarket.Api/appsettings.Local.json
}
```

Chỉ copy khi chưa có file local để giữ cấu hình riêng hiện tại. Trong `appsettings.Local.json`, thay `YOUR_SQL_HOST`, `YOUR_SQL_USER`, `YOUR_SQL_PASSWORD`; giữ `Database=FA26SE103_Dev`. Tên server trong ảnh SSMS của team là `ksr-capstone-supermarket-ai,1433`; máy chạy backend cần truy cập được địa chỉ đó. `TrustServerCertificate=False` yêu cầu certificate SQL hợp lệ; với server dev dùng certificate tự ký, có thể đặt `True` theo cấu hình của team. SQL username/password khác với tài khoản đăng nhập API.

Sinh JWT key riêng bằng PowerShell rồi điền kết quả vào `Jwt:Key` trong file local:

```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet restore --configfile NuGet.Config
.\scripts\Start-Local.ps1
```

Nếu DB chưa có tài khoản Admin, dùng `Start-Local.ps1 -BootstrapAdmin` để nhập mật khẩu và tạo tài khoản đầu tiên. Bootstrap chỉ tạo Admin khi database chưa có tài khoản. Với DB có sẵn của team, không cần chạy `Initialize-Local.ps1` hoặc scaffold lại chỉ để đổi connection string.

API: `http://localhost:5080`; Swagger: `http://localhost:5080/swagger`. Đăng nhập bằng `POST /api/auth/login`, sao chép `accessToken` vào **Authorize** của Swagger. Configuration API chỉ dành cho ADMIN. Token của tài khoản đã disable hoặc đổi role bị từ chối ngay ở request tiếp theo.

### Cấu hình khi tự host trên VPS

Chưa deploy backend lên VPS. GitHub Actions chạy build/test và tạo publish artifact; workflow hiện không upload hoặc khởi động backend trên server.

```powershell
dotnet publish src/Supermarket.Api -c Release -o .local/publish
```

Khi bạn đưa publish output lên VPS, đặt `ASPNETCORE_ENVIRONMENT=Production`. Điền connection string thật với **`Database=FA26SE103`** và JWT key riêng vào `appsettings.Production.json` trong thư mục publish trên VPS, hoặc cấu hình qua biến môi trường `ConnectionStrings__SqlServer` và `Jwt__Key`. Cấu hình thêm `Cors:Origins` theo địa chỉ frontend và `DataProtection:KeyPath` tới thư mục lưu key lâu dài trên VPS. Không commit thông tin thật từ VPS vào file mẫu trên GitHub. `appsettings.Local.json` và file example không nằm trong publish output.

Thứ tự ghi đè cấu hình hiện tại: `appsettings.json` → `appsettings.{Environment}.json` → `appsettings.Local.json` nếu có → biến môi trường. Khi chạy bản publish trên VPS, file production cùng biến môi trường cung cấp cấu hình thật.

## Tạo DB SQL Express độc lập để thử local

Cần .NET SDK 10, SQL Server Express và `sqlcmd`. Từ thư mục `Project/Back-End`:

Khi clone repository backend riêng, đặt checkout tại `Project/Back-End` và giữ script SQL của team tại `Project/DB/FA26SE103_Database_V0.1.sql`. Script SQL không được lưu trong repository backend.

```powershell
dotnet restore --configfile NuGet.Config
.\scripts\Initialize-Local.ps1
.\scripts\Start-Local.ps1 -BootstrapAdmin
```

Lần đầu nhập mật khẩu Admin dài 12–128 ký tự. Email mặc định `admin@mf01.local`; có thể đổi bằng `-AdminEmail`. Bootstrap chỉ tạo Admin khi database chưa có tài khoản; mật khẩu bootstrap chỉ truyền qua môi trường process, không ghi vào file. Các lần sau chạy `Start-Local.ps1` không có `-BootstrapAdmin`.

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
- Upload MP4 làm nguồn RECORDED/FILE qua `POST /api/cameras/{id}/recorded-video` (multipart `file`, ADMIN, tối đa 200 MB). Kiểm tra header và decode frame bằng FFmpeg; server sinh filename, lưu ngoài wwwroot. Không sửa schema DB.
- Proxy AI preview start/status/frame/stop tới native YOLO + ByteTrack, hỗ trợ LIVE HTTP/RTSP/HLS và RECORDED FILE. React nhận JPEG đã vẽ person box/track ID, không gọi Python trực tiếp.
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

### Upload video thay camera để test YOLO + ByteTrack

Từ `C:\FPT University\CAPSTONE\Backend\Back-End`, chạy backend như bình thường. Nếu FFmpeg chưa có trên PATH, đặt đường dẫn portable trong cửa sổ PowerShell chạy BE:

```powershell
$env:Video__FfmpegPath = 'C:\FPT University\CAPSTONE\setup\tools\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe'
.\scripts\Start-Local.ps1
```

`Video:RecordedRoot` mặc định `.local/videos`, resolve theo API content root thay vì thư mục terminal. Từ source, mặc định là `C:\FPT University\CAPSTONE\Backend\Back-End\src\Supermarket.Api\.local\videos`. AI service mặc định đọc đúng thư mục đó. Nếu đổi folder/di chuyển checkout/publish, đặt `Video:RecordedRoot` và `AI_RECORDED_ROOT` tới cùng đường dẫn tuyệt đối. Chạy ở máy/container khác phải share/mount storage; URI file trên laptop không tự truy cập được từ server khác.

React: **Cameras → Add camera → Uploaded video** (hoặc chọn camera ACTIVE → **Upload video**) → chọn MP4 → **Save video source → Test & enable → Start AI preview**. Store/floor phải tồn tại trong DB; tạo qua API/Swagger nếu chưa có.

Upload dừng session AI cũ, lưu filename ngẫu nhiên, configure RECORDED/FILE, xóa credentials/test và disable connection. Response upload không trả local URI; connection view hiện có vẫn trả URI cho Admin. File không public qua static files. Upload lỗi/canceled hoặc configure thất bại cleanup file mới. File upload thành công được giữ, kể cả sau khi bị thay nguồn; chưa có retention/delete API, cần quản lý dung lượng và backup riêng. Không commit video, `.local`, keys hoặc secret.

Không thay nguồn (upload hoặc PUT connection) khi camera map tới monitoring ACTIVE; deactivate trước. Mapping/ROI được giữ, phải xem lại ROI nếu cảnh đổi. Upload là controlled fallback/evaluation; live CCTV vẫn là mục tiêu chính.

AI đọc tuần tự mọi frame, pace theo FPS và tốc độ inference (có thể chậm hơn thời gian thật), không tự loop. EOF trả `COMPLETED`, giữ JPEG cuối; stop/start replay với tracker mới. Preview chưa ghi measurement, OperationalEvent hay Incident. Health worker hiện probe khả năng đọc một frame của file, không phản ánh GPU/tiến độ/EOF; file còn đọc được có thể vẫn ONLINE sau playback.

Proxy: `POST /api/cameras/{id}/ai-preview/start`, `GET .../status`, `GET .../frame`, `POST .../stop`; ADMIN JWT. `AiPreview:BaseUrl` mặc định `http://127.0.0.1:8090`. Internal key nếu dùng phải khớp `AI_INTERNAL_SERVICE_KEY`. Model/device/confidence từ `AiPreview` được gửi tới Python; chưa lấy từ rule của zone.

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
- AGENTS revision ERD v3 đã chốt MonitoringRule, IncidentType, Zone.area_m2 và camera maintenance. Code chỉ scaffold 10 bảng stable; rule mới là legacy DTO, chưa endpoint/persistence/seed IncidentType. Đây là thiếu implementation và đồng bộ SQL, không còn là chờ quyết định ERD. Không tự sửa generated EF hoặc tạo schema phỏng đoán. Chưa tìm thấy SQL authoritative trong CAPSTONE checkout; full SQL tests cần file đúng phiên bản.
- Nối Store/floor/zone UI thật; thêm camera→zone selector và camera-frame ROI editor qua mapping API. Floor map polygon khác ROI trên frame; không dùng sample geometry thay tọa độ thật.
- Đồng bộ SQL authoritative ERD v3/scaffold, triển khai rule CRUD per-zone: chỉ IncidentType AI-detected, warning <= critical, unit/measurement đúng loại, sustain/cooldown >= 0, enabled. Có area_m2 > 0 cho density; không tính people/m² khi thiếu diện tích.
- Activation hiện chỉ bật configuration, kiểm tra zone ACTIVE và ít nhất một mapped camera ACTIVE/tested/enabled; chưa kiểm tra đủ rule/measurement readiness, chưa khởi động continuous AI worker. Preview đã chạy nhưng chưa ROI measurements, sustained-threshold evaluator, OperationalEvents, incident dedup/cooldown. Nối runtime và lifecycle activate/deactivate trước khi gọi là monitoring thật. Health events tách biệt Operational Incidents.
- Multiple-camera measurement-source selection và một số operational limits còn mở (AGENTS §33); không tự cộng người từ các camera nhìn cùng zone hay suy ra cross-camera identity. Chưa xác nhận full MF-01 UI acceptance hoặc hiệu năng live stream.
- Backend dùng repository riêng `GFA26SE39-FA26SE103/Back-End`: `main` là stable, `dev` là integration, feature branches qua PR vào dev và cần review trước khi merge.
