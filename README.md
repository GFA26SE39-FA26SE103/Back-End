# FA26SE103 MF-01 backend

Backend ASP.NET Core .NET 10 cho **Setup & System Configuration**, theo [AGENTS.md](AGENTS.md) và [context đầy đủ của team](docs/PROJECT_CONTEXT.md), baseline **02/10/2026**. SQL Server là nguồn dữ liệu chính. Backend có API setup, floor-plan upload, upload MP4 và AI preview; mức độ nối frontend cần đối chiếu với checkout frontend hiện tại. Phạm vi làm việc trước mắt là **MF-01**.

## Context chung khi clone backend

- Đọc [AGENTS.md](AGENTS.md) trước khi làm việc; đây là hướng dẫn ngắn ở root để coding agent tự nạp khi mở repo backend.
- Đọc [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) đầy đủ trước khi triển khai. File này là snapshot triển khai được đồng bộ trong repository, gồm MF-01..MF-04, BR, kiến trúc, ERD v3 facts, các quyết định còn mở và link RP1/RP2/BR/ERD. Các Mainflow sau là context thiết kế; phần đang tập trung triển khai là MF-01.
- Đối chiếu phần đã triển khai và các gap trong README, [API contract](docs/API.md), [OpenAPI](docs/openapi.json) và source liên quan. [VALIDATION.md](docs/VALIDATION.md) là bằng chứng kiểm tra tại ngày ghi trong file.

Bản hướng dẫn đầy đủ khoảng 63 KiB nên được tách khỏi file tự nạp; `AGENTS.md` yêu cầu agent đọc nó qua công cụ đọc file. Hai file đều nằm trong repo, không cần thư mục `AGENT` bên ngoài khi clone backend riêng.

Khi team chốt BR/Mainflow/ERD mới, cập nhật `docs/PROJECT_CONTEXT.md`, ngày baseline và những quy tắc tương ứng trong `AGENTS.md` trong cùng PR. Context được chia sẻ qua Git; link tài liệu ngoài không tự đồng bộ nội dung vào repo.

## Thành viên mới clone về cần gì?

Để chạy API với DB `FA26SE103_Dev` đã có sẵn, mỗi người cần .NET SDK 10, quyền truy cập SQL Server của team và **một file cấu hình riêng**: `src/Supermarket.Api/appsettings.Local.json`. Tạo file đó từ `appsettings.Local.example.json` đã có trong Git, điền connection string và sinh JWT key theo hướng dẫn dưới đây. JWT key có thể khác nhau nếu mỗi người đăng nhập vào API local của mình; chỉ dùng chung khi cần các instance chấp nhận cùng token.

Bạn chỉ cần cung cấp thông tin kết nối DB và tài khoản đăng nhập API Dev qua kênh riêng. Không cần gửi source bổ sung, `bin`, `obj`, `.tools`, `TestResults` hoặc bản publish. EF entities đã có trong source nên không cần scaffold lại để chạy. Script SQL chỉ cần khi tự tạo DB hoặc chạy SQL integration tests; kết nối DB có sẵn không cần file SQL. FFmpeg chỉ cần khi thử camera thật hoặc video recorded; camera DEMO không cần FFmpeg.

**Nếu cùng đọc mật khẩu camera đã mã hóa trong DB Dev:** các backend cần dùng chung Data Protection key ring của môi trường Dev. Chia sẻ riêng bộ key Dev tương ứng với dữ liệu đó, đặt trên máy từng người và cấu hình `DataProtection:KeyPath` trong file local tới thư mục vừa đặt key. Đồng bộ key ring khi có key mới; không lấy key production để dùng cho Dev và không commit key vào Git. Chỉ copy `appsettings.Local.json` không đủ để giải mã camera credentials đã được backend khác lưu.

Data Protection key được backend tự sinh khi cần bảo vệ dữ liệu và nằm tại đường dẫn `DataProtection:KeyPath`; mặc định là `src/Supermarket.Api/.local/keys` khi chạy local bằng launch profile `http`. Key này phục vụ `CameraConnection.credential_secret_ref`; mật khẩu tài khoản API được hash bằng PasswordHasher, JWT dùng `Jwt:Key`. Giữ các key đã dùng cùng bản backup DB có camera credentials; mất key sẽ phải nhập lại mật khẩu camera. Tham khảo [cấu hình Data Protection của ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

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

Nếu DB chưa có tài khoản Admin, dùng `Start-Local.ps1 -BootstrapAdmin` để nhập mật khẩu và tạo tài khoản đầu tiên. Bootstrap chỉ tạo Admin khi database chưa có tài khoản. Với DB có sẵn của team, không cần scaffold lại chỉ để đổi connection string.

API: `http://localhost:5080`; Swagger: `http://localhost:5080/swagger`. Đăng nhập bằng `POST /api/auth/login`, sao chép `accessToken` vào **Authorize** của Swagger. Configuration API chỉ dành cho ADMIN. Token của tài khoản đã disable hoặc đổi role bị từ chối ngay ở request tiếp theo.

### Cấu hình khi tự host trên VPS

Chưa deploy backend lên VPS. GitHub Actions chạy build/test và tạo publish artifact; workflow hiện không upload hoặc khởi động backend trên server.

```powershell
dotnet publish src/Supermarket.Api -c Release -o .local/publish
```

Khi bạn đưa publish output lên VPS, đặt `ASPNETCORE_ENVIRONMENT=Production`. Điền connection string thật với **`Database=FA26SE103`** và JWT key riêng vào `appsettings.Production.json` trong thư mục publish trên VPS, hoặc cấu hình qua biến môi trường `ConnectionStrings__SqlServer` và `Jwt__Key`. Cấu hình thêm `Cors:Origins` theo địa chỉ frontend và `DataProtection:KeyPath` tới thư mục lưu key lâu dài trên VPS. Không commit thông tin thật từ VPS vào file mẫu trên GitHub. `appsettings.Local.json` và file example không nằm trong publish output.

Thứ tự ghi đè cấu hình hiện tại: `appsettings.json` → `appsettings.{Environment}.json` → `appsettings.Local.json` nếu có → biến môi trường. Khi chạy bản publish trên VPS, file production cùng biến môi trường cung cấp cấu hình thật.

## Tạo Admin đầu tiên trên DB đã có sẵn

Sau khi cấu hình connection string và JWT key cho database đã có sẵn, chạy từ `Project/Back-End`:

```powershell
dotnet restore --configfile NuGet.Config
.\scripts\Start-Local.ps1 -BootstrapAdmin
```

Lần đầu nhập mật khẩu Admin dài 12–128 ký tự. Email mặc định `admin@mf01.local`; có thể đổi bằng `-AdminEmail`. Bootstrap chỉ tạo Admin khi database chưa có tài khoản; mật khẩu bootstrap chỉ truyền qua môi trường process, không ghi vào file. Các lần sau chạy `Start-Local.ps1` không có `-BootstrapAdmin`.

Backend không cung cấp script tự khởi tạo DB local. `Start-Local.ps1` chạy API với cấu hình hiện tại; `Scaffold.ps1` dành cho đồng bộ EF sau khi team đã duyệt và áp dụng thay đổi schema.

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
- Upload/replace floor plan PNG, JPEG hoặc PDF qua `POST /api/floors/{id}/map` (multipart `file`, ADMIN, mặc định tối đa 20 MB); asset được lưu ngoài `wwwroot` và chỉ đọc qua authenticated `GET /api/floors/{id}/map`.
- Camera thuộc Floor; metadata lắp đặt/bảo hành, vị trí và rotation trên map.
- Connection 1:1: configure → test → preview → enable/disable. Mỗi lần PUT configuration sẽ disable và xóa kết quả test cũ.
- Upload MP4 làm nguồn RECORDED/FILE qua `POST /api/cameras/{id}/recorded-video` (multipart `file`, ADMIN, tối đa 200 MB). Kiểm tra header và decode frame bằng FFmpeg; server sinh filename, lưu ngoài wwwroot. Không sửa schema DB.
- Proxy AI preview start/status/frame/stop tới native YOLO + ByteTrack, hỗ trợ LIVE HTTP/RTSP/HLS và RECORDED FILE. React nhận JPEG đã vẽ person box/track ID, không gọi Python trực tiếp.
- Camera N:M Zone qua mapping; cùng tầng; ROI normalized; PUT idempotent.
- MonitoringConfiguration và MonitoringRule per-zone được lưu SQL: confidence `[0,1]`, incident type AI-detected, warning/critical/unit, sustain/cooldown/enabled, DRAFT/ACTIVE/INACTIVE. Review và activation kiểm tra lại zone, rule, diện tích density, camera/source/test/enable và ROI. Cấu hình ACTIVE phải deactivate trước khi sửa rule, confidence, source hoặc mapping/ROI.
- Health worker, check thủ công, OPEN → INVESTIGATING → RESOLVED. Khi có lại frame, worker tự resolve event và cập nhật last seen; Admin có thể thêm resolution note.
- Swagger/OpenAPI, ProblemDetails với `code` và `requestId`, rate limit login, CORS, liveness/readiness.

Floor map có thể được khai báo bằng HTTP(S) asset URL khi chỉnh floor hoặc upload vào local storage qua API mới. Local storage mặc định là `src/Supermarket.Api/.local/floor-plans`, cấu hình bằng `FloorPlan:Root`; giới hạn mặc định `FloorPlan:MaxBytes=20971520`. Storage name do server sinh, không dùng original filename làm path, không serve static, và thư mục `.local` không được commit. Local storage chỉ dành cho increment development; `IFloorPlanStorage` là boundary để thay bằng S3/MinIO sau này mà không đổi controller/use case.

Store Layout frontend tải floor/camera thật, lấy map dưới dạng authenticated Blob, render trang PDF đầu tiên bằng PDF.js, và đặt camera bằng sprite body/muzzle/FOV. **Create floor / Edit floor** mở dialog có Cancel/close/Escape. Create dùng default store đã seed; Edit chỉ gửi name/floorNumber qua `PATCH /api/floors/{id}/details`, giữ map hiện tại, zone/camera/ROI. Số tầng unique trong store; lỗi giữ input. Drag hoặc keyboard chỉ sửa draft; **Save placement** mới gửi full camera DTO với `mapX/mapY` normalized và `mapRotationDeg`. Lưu placement không tự test/enable connection hay activate monitoring.

PATCH ở increment này nhận toàn bộ DTO chỉnh sửa, không phải JSON Patch hay merge patch. Camera không được chuyển sang tầng khác bằng PATCH; việc chuyển tầng cần use case xử lý mappings riêng.

## Demo MF-01

1. Login Admin, dùng default supermarket đã seed, tạo floor trong Store Layout và zone với `mapPolygon` gồm ít nhất 3 điểm.
2. Tạo camera với `status: "ACTIVE"`, installation/warranty dates.
3. PUT connection với `{ "sourceType": "DEMO", "protocol": "HTTP", "streamUri": "demo://camera/main" }`.
4. POST connection/test, GET preview, POST connection/enable.
5. PUT camera/zones/{zoneId} với `roiPolygon`. DEMO chỉ kiểm thử connection/health; không activate cấu hình AI từ nguồn DEMO.
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

React: **Cameras → Add camera → Uploaded video** (hoặc chọn camera ACTIVE → **Upload video**) → chọn MP4 → **Save video source → Test & enable → Start AI preview**. Default store phải được seed; tạo floor qua **Store Layout → Create floor** nếu chưa có.

Upload dừng session AI cũ, lưu filename ngẫu nhiên, configure RECORDED/FILE, xóa credentials/test và disable connection. Response upload không trả local URI; connection view hiện có vẫn trả URI cho Admin. File không public qua static files. Upload lỗi/canceled hoặc configure thất bại cleanup file mới. File upload thành công được giữ, kể cả sau khi bị thay nguồn; chưa có retention/delete API, cần quản lý dung lượng và backup riêng. Không commit video, `.local`, keys hoặc secret.

Không thay nguồn (upload hoặc PUT connection) khi camera map tới monitoring ACTIVE; deactivate trước. Mapping/ROI được giữ, phải xem lại ROI nếu cảnh đổi. Upload là controlled fallback/evaluation; live CCTV vẫn là mục tiêu chính.

AI đọc tuần tự mọi frame, pace theo FPS và tốc độ inference (có thể chậm hơn thời gian thật), không tự loop. EOF trả `COMPLETED`, giữ JPEG cuối; stop/start replay với tracker mới. Preview chưa ghi measurement, OperationalEvent hay Incident. Health worker hiện probe khả năng đọc một frame của file, không phản ánh GPU/tiến độ/EOF; file còn đọc được có thể vẫn ONLINE sau playback.

Proxy: `POST /api/cameras/{id}/ai-preview/start`, `GET .../status`, `GET .../frame`, `POST .../stop`; ADMIN/OPERATOR JWT. `AiPreview:BaseUrl` mặc định `http://127.0.0.1:8090`. Internal key nếu dùng phải khớp `AI_INTERNAL_SERVICE_KEY`. Model/device/default confidence từ `AiPreview`; Admin có thể dùng `start?zoneId=<uuid>` để restart camera session với confidence của cấu hình zone đã lưu. Zone phải có mapping/ROI cùng tầng hợp lệ; chưa cần Activate để kiểm thử Draft.

RTSP, HTTP/HLS và recorded video dùng FFmpeg để đọc/giải mã một frame; thành công chỉ khi nhận được frame, không chỉ khi mở được TCP. Cấu hình `Video:FfmpegPath` trỏ tới FFmpeg trên máy. Recorded FILE phải nằm dưới `Video:RecordedRoot`. Dockerfile bao gồm FFmpeg. WebRTC yêu cầu adapter media gateway, hiện trả lỗi rõ ràng.

Camera URI không được chứa userinfo, query hay fragment. Gửi `username`/`password` riêng; password được mã hóa bằng ASP.NET Data Protection trong `credential_secret_ref`, DTO chỉ trả `hasCredentials`. PUT thay toàn bộ credentials; bỏ password sẽ xóa credentials cũ. Không trả secret reference, không log FFmpeg stderr hay SQL exception detail. Key ring phải được giữ qua restart; production cần volume riêng, ACL phù hợp và cấu hình bảo vệ key ring at rest. Giữ JWT key/DB credentials trong secrets môi trường, đặt HTTPS tại reverse proxy.

## Configure monitoring → Review & activate (2026-10-03)

Sau khi camera LIVE hoặc MP4 đã **Test & enable**, vào **Store Layout → Camera coverage**, map camera với zone và lưu ROI trên frame. Chọn **Configure monitoring for …** ở ROI editor hoặc **Configure monitoring: …** trong Cameras để mở AI Config đúng zone.

1. AI Config hiển thị tổng quan config theo Floor/Zone. Chọn zone để xem detail read-only; bấm **Create configuration** hoặc **Edit configuration** mới mở form đặt tên/confidence (0–1, tối đa 4 chữ số thập phân). **Cancel** bỏ local draft, không gọi API lưu.
2. **Add rule** từ catalog AI thật. Long Queue dùng PEOPLE (3/5), Excessive Waiting Time dùng MINUTES (4/8), Overcrowding dùng PEOPLE_PER_M2 (2/3). Đây là giá trị gợi ý theo PROJECT_CONTEXT §26, có thể sửa. Warning phải nhỏ hơn critical; PEOPLE phải là số nguyên.
3. Đặt sustain/cooldown theo giây, bật/tắt rule → **Save configuration** (Draft). Mặc định 30/300 theo PROJECT_CONTEXT §24. Save bắt buộc có ít nhất một incident rule; array rỗng trả 422 `RULES_REQUIRED` và không tạo/thay config. Save thay toàn bộ tập rule; remove trên UI chỉ có hiệu lực khi lưu và phải giữ ít nhất một rule. Save thành công đóng form và cập nhật overview/detail.
4. **Configuration activation → Review & activate** đọc lại SQL, hiển thị zone/config/version/confidence, camera/source, ROI, từng rule và blockers. Density enabled cần `Zone.area_m2 > 0`; cần ít nhất một camera cùng tầng ACTIVE, mapping/ROI hợp lệ, nguồn LIVE HTTP/RTSP/HLS hoặc RECORDED FILE đã test thành công và enabled, cùng ít nhất một rule supported enabled.
   Có thể chọn **Preview zone confidence: [camera]** để chạy YOLO/ByteTrack bằng confidence đã lưu. Thao tác restart phiên camera dùng chung, reset track IDs và có thể gián đoạn viewer khác; boxes toàn frame, chưa tính ROI measurements.
5. **Activate configuration** kiểm tra lại trong transaction; expectedUpdatedAt chống activate bản cũ. **Deactivate configuration** trước khi sửa rule/confidence/source/mapping/ROI. Có lỗi concurrency thì dùng **Reload saved configuration**, không ghi đè thay đổi của người khác.
6. **Delete configuration** mở hộp xác nhận, chỉ DRAFT/INACTIVE mới được xóa. ACTIVE phải Deactivate trước. DELETE kiểm tra configId và expectedUpdatedAt để không xóa config đã thay đổi/tạo lại. Chỉ config/rules bị xóa, giữ Zone/Camera/ROI; zone trở về Not configured và có thể tạo Draft mới.

Draft lưu DB thật, không phải trạng thái React. Checkout Capacity chưa có counter/composite definition nên chỉ lưu rule disabled với đơn vị placeholder do Admin nhập; không được enable. Recorded source được đánh dấu test/fallback. Review kiểm tra trạng thái cấu hình và kết quả test đã lưu, không thực hiện một FFmpeg probe mới; dùng Test & enable/health check để xác nhận khả năng đọc video hiện tại.

**Giới hạn rõ ràng:** MF-01 Activate đánh dấu cấu hình áp dụng; chưa chạy sustained-threshold evaluator, ROI measurements, OperationalEvent/Incident/dispatch của MF-02. AI Config có zone-confidence preview; nút preview thường ở Cameras vẫn dùng `AiPreview:Confidence` (hoặc session đang chạy). Với N:M camera-zone, selection nguồn đo vẫn chờ team; không cộng counts chồng lấn hoặc ReID. Dashboard Activate mẫu không thay thế thao tác thật ở AI Config.

## Database-first

Database SQL Server đã được team cấu hình là nguồn persistence. Entities scaffold hiện có trong source đủ để chạy API với database tương ứng; không dùng script SQL V0.1 cũ để suy ra contract mới. Backend không giữ bản sao schema riêng. Test project vẫn có cấu hình liên kết file SQL bên ngoài để tạo database test tạm; đây là prerequisite của SQL tests, không phải bước khởi động API.

Khi team phê duyệt schema mới: áp dụng thay đổi đã duyệt vào database, trỏ connection scaffold tới đúng database, rồi scaffold lại:

```powershell
$env:MF01_SCAFFOLD_CONNECTION = '<connection string tới database đã duyệt>'
.\scripts\Scaffold.ps1
dotnet test --settings coverage.runsettings --collect:'XPlat Code Coverage'
```

Generated files nằm tại `Infrastructure/Persistence/Scaffolded`; custom EF configuration nằm ở partial extension bên ngoài thư mục generated. Script scaffold 12 bảng, gồm MonitoringRule và IncidentType, mặc định build Release để không đụng Debug API đang chạy. Increment dùng migration đã duyệt `Database/migrations/20261003_01_monitoring_rules_erd_v3.sql`; không chạy V0.1 installer lên DB hiện có. Không dùng `EnsureCreated` hoặc code-first migrations. Tham khảo [EF Core reverse engineering](https://learn.microsoft.com/en-us/ef/core/managing-schemas/scaffolding/).

## Kiểm thử

```powershell
dotnet test --settings coverage.runsettings --collect:'XPlat Code Coverage' --results-directory TestResults
dotnet publish src/Supermarket.Api -c Release
```

Integration tests tạo database ngẫu nhiên `FA26SE103_MF01_Test_<guid>` từ baseline SQL và migration monitoring trong repo Database, rồi xóa đúng database đó sau test. Mặc định tìm repo sibling `CAPSTONE/Database`, dùng Windows auth trên `.\SQLEXPRESS`. Đổi instance bằng `MF01_TEST_SERVER`, hoặc dùng SQL auth qua `MF01_TEST_CONNECTION`. Không trỏ vào môi trường dùng chung: tests cần quyền tạo/xóa database riêng. Không bỏ qua SQL tests nếu thiếu kết nối/schema/migration; test sẽ báo fail.

GitHub Actions mặc định chạy build, các test không cần SQL và Release publish cho repository backend độc lập. Test dùng `SqlApiFixture` phải có `[Trait("Category", "SqlIntegration")]` trên class; các phần `partial` của `ApiFlowTests` dùng chung trait này. Job `verify` chạy `Category!=SqlIntegration`, job SQL chạy `Category=SqlIntegration`, gồm cả concurrency tests. Job SQL integration được bật khi variable `MF01_SCHEMA_REPOSITORY` trỏ tới repo Database. Đặt `MF01_SCHEMA_REF` tới branch/commit đã có migration (hiện `feature/mf01-monitoring-rules`), `MF01_SCHEMA_FILE` (mặc định baseline V0.1), `MF01_MONITORING_MIGRATION_FILE` (mặc định `migrations/20261003_01_monitoring_rules_erd_v3.sql`) và secret read-only `MF01_SCHEMA_READ_TOKEN` nếu private. Job skipped không phải bằng chứng SQL đã được kiểm tra trên GitHub.

Nếu SQL nằm ở vị trí khác, truyền cả `-p:Mf01SchemaPath=<baseline SQL>` và `-p:Mf01MonitoringMigrationPath=<migration SQL>`. Các test không cần SQL chạy bằng `dotnet test -c Release --filter "Category!=SqlIntegration"`; full suite cần đủ schema/migration/SQL Server. Dockerfile/workflow chưa deploy lên VPS.

## Các điểm còn chờ team đồng bộ

- Context baseline 02/10 xác định camera thuộc floor, N:M zone và model confidence chỉ là input filter. Đối chiếu các tài liệu/source cũ với [guide đầy đủ](docs/PROJECT_CONTEXT.md) trước khi mở rộng hành vi.
- MonitoringRule/IncidentType đã persistence theo migration ERD v3 đã duyệt; catalog baseline do Database repo seed (4 AI + 6 staff). API monitoring chỉ trả loại AI. Cờ "requires Operator review", measurement-source, checkout-counter và Manager-on-duty vẫn chờ team; không tự thêm schema.
- Store Layout đã có tạo/sửa floor, zone editing, camera→zone selector và camera-frame ROI editor. Cameras/ROI editor có link tới AI Config đúng zone. AI Config có Review/Activate/Deactivate và Delete với xác nhận/version guard. Dashboard đã nối `GET /api/setup/overview` để hiển thị saved setup counts, readiness dùng chung Review/Activate, camera health và unresolved events; action links chọn đúng floor/camera/zone. Health-event Investigation/Resolve UI và mini-map Cameras geometry vẫn cần hoàn thiện. Store mặc định từ seed; không thêm flow tạo/chọn store.
- Rule CRUD Draft và review/activate MF-01 đã có. Checkout capacity chỉ lưu disabled Draft vì chưa có counter/composite measurement. Waiting-time rule lưu đơn vị MINUTES nhưng preview chưa đo entry-to-counter; runtime cần thiết kế counter khi triển khai MF-02.
- Activation kiểm tra cấu hình và ít nhất một camera hợp lệ; không khởi động continuous AI worker. Preview chưa ROI measurements, sustained-threshold evaluator, OperationalEvents, incident dedup/cooldown hay dispatch. Health events tách biệt Operational Incidents. Zone confidence đã nối cho preview có chọn zone, chưa có runtime monitoring.
- Multiple-camera measurement-source selection và một số operational limits còn mở ([PROJECT_CONTEXT §33](docs/PROJECT_CONTEXT.md#33-implementation-questions-that-are-currently-open)); không tự cộng người từ các camera nhìn cùng zone hay suy ra cross-camera identity. Chưa xác nhận full MF-01 UI acceptance hoặc hiệu năng live stream.
- Backend dùng repository riêng `GFA26SE39-FA26SE103/Back-End`: `main` là stable, `dev` là integration, feature branches qua PR vào dev và cần review trước khi merge.
