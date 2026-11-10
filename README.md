# Restaurant Food Manager

Backend ASP.NET Core 8 + SQL Server; frontend HTML/CSS/JavaScript thuần, không cần npm.
Nhánh làm việc: `dong`. Tiến độ sau khi gộp `hai`: [ghi chú tích hợp](docsforfe/integration-progress.md).

## Chuẩn bị

Các lệnh dưới đây dùng **Windows PowerShell**. Cần cài:

- .NET SDK 8 và SQL Server/SQL Express đang chạy.
- Python 3 để phục vụ frontend tĩnh; không cần `npm install` hay `npm run dev`.
- Git nếu tải bằng lệnh clone. Node.js 20+ chỉ cần khi chạy test frontend.

Kiểm tra công cụ:

```powershell
dotnet --list-sdks
python --version
Get-Service -Name 'MSSQL$SQLEXPRESS'
```

Nếu SQL Express đang dừng, mở PowerShell bằng quyền Administrator và chạy
`Start-Service -Name 'MSSQL$SQLEXPRESS'`. Đổi tên dịch vụ nếu dùng SQL instance khác.

Máy chưa có source thì tải nhánh `dong`:

```powershell
git clone --branch dong https://github.com/hai21225/RestaurantFoodManagerSystem.git
Set-Location .\RestaurantFoodManagerSystem
```

Máy Đông đã có source thì mở **hai cửa sổ PowerShell** tại:

```powershell
Set-Location 'D:\Hoc_Tap\quanliduan1111\hkdn\RestaurantFoodManagerSystem'
```

## Chạy nhanh cả backend và frontend bằng file BAT

Sau khi đã cài .NET SDK 8, Python 3 và SQL Server đang chạy, bấm đúp [start-backend.bat](start-backend.bat)
ở thư mục gốc repository, hoặc chạy trong PowerShell:

```powershell
.\start-backend.bat
```

File tự chọn đúng thư mục và mở hai cửa sổ: backend HTTP/Development ở cổng 5225 (tắt migration/seed)
và frontend Python ở cổng 5500. Hỗ trợ cả `py -3` và `python` khi có Python 3.
Database và JWT lấy từ cấu hình backend/biến môi trường hiện có. Cổng nào đã được dùng thì
bỏ qua khởi động phần đó để tránh chạy trùng. Đợi backend build xong rồi mở
[trang đăng nhập](http://localhost:5500/pages/login.html); nếu có lỗi, xem thông báo trong cửa sổ tương ứng.
Nhấn `Ctrl+C` ở từng cửa sổ Backend/Frontend để dừng. Các phần Terminal 1/2 bên dưới là cách chạy
thủ công, không cần chạy thêm sau khi đã dùng file BAT.

## Terminal 1 — chạy backend

### 1. Chọn database

**Chạy lại database gốc trên máy Đông:** dùng cấu hình này khi database
`RestaurantFoodManager` đã tồn tại và đã có schema phù hợp. Không tự migrate/seed khi mở ứng dụng.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = 'Server=localhost\SQLEXPRESS;Database=RestaurantFoodManager;Integrated Security=True;TrustServerCertificate=True'
$env:Database__MigrateOnStartup = 'false'
$env:Database__SeedOnStartup = 'false'
```

**Máy mới, chưa có database:** dùng khối dưới đây **thay cho khối database gốc**.
Backend sẽ tạo database phát triển riêng, áp dụng migration và seed dữ liệu cấu hình ban đầu.
Tài khoản Windows đang chạy backend phải có quyền tạo database trên SQL Server.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = 'Server=localhost\SQLEXPRESS;Database=RestaurantFoodManagerDev;Integrated Security=True;TrustServerCertificate=True'
$env:Database__MigrateOnStartup = 'true'
$env:Database__SeedOnStartup = 'true'
```

Đổi `localhost\SQLEXPRESS` theo SQL instance trên máy, ví dụ `localhost` hoặc
`(localdb)\MSSQLLocalDB`. Clone Git **không tải kèm dữ liệu database trên máy Đông**.
Database mới được tạo schema từ migration, không chứa lịch sử đơn hàng/phiếu thu trên máy Đông.
Khi bật `Database__SeedOnStartup=true`, backend hiện gọi `BasicDataSeeder` của Hải để
nạp tài khoản và dữ liệu mẫu còn thiếu. Đây không phải bản sao dữ liệu gốc trên máy Đông
và không bảo đảm tạo đúng các tài khoản demo được bàn giao trong bảng bên dưới.
Nếu cần dữ liệu cũ, phải được cung cấp và khôi phục backup riêng trước khi chạy.

### 2. Nạp khóa JWT riêng trên máy và khởi động

Chạy tiếp trong **cùng Terminal 1**, sau khi chọn một cấu hình database ở trên.
Khóa được tạo một lần tại `%LOCALAPPDATA%\RestaurantFoodManagerSystem\jwt-signing-key.txt`,
nằm ngoài repository, rồi được dùng lại ở những lần chạy sau. Không gửi hoặc đưa file khóa lên Git.

```powershell
$taskConfigDirectory = Join-Path $env:LOCALAPPDATA 'RestaurantFoodManagerSystem'
$taskJwtKeyPath = Join-Path $taskConfigDirectory 'jwt-signing-key.txt'
New-Item -ItemType Directory -Path $taskConfigDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath $taskJwtKeyPath)) {
    $taskJwtBytes = New-Object byte[] 48
    $taskRandom = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $taskRandom.GetBytes($taskJwtBytes)
    } finally {
        $taskRandom.Dispose()
    }
    [System.IO.File]::WriteAllText($taskJwtKeyPath, [Convert]::ToBase64String($taskJwtBytes))
}
$env:Authentication__Jwt__Key = [System.IO.File]::ReadAllText($taskJwtKeyPath).Trim()
dotnet restore RestaurantFoodManagerSystem.sln
dotnet run --project RestaurantFoodManagerSystem --no-launch-profile --urls http://localhost:5225
```

Giữ Terminal 1 mở. Swagger: [http://localhost:5225/swagger](http://localhost:5225/swagger).
`Auth:Enforce=true` yêu cầu đăng nhập bằng JWT. Nếu backend trước đây dùng khóa khác,
phiên cũ sẽ không hợp lệ khi chuyển sang khóa vừa tạo; đăng nhập lại để lấy token mới.
Mỗi lần mở Terminal mới cần chạy lại cấu hình database và khối nạp khóa/khởi động.

## Terminal 2 — chạy frontend

Tại thư mục gốc repository:

```powershell
python -m http.server 5500 --bind localhost --directory frontend
```

Nếu máy dùng Python Launcher, thay `python` bằng `py -3`.
Mở [trang đăng nhập](http://localhost:5500/pages/login.html).
Nếu đang dùng cổng **5501**, chạy lệnh sau thay cho lệnh 5500 và mở đúng URL tương ứng:

```powershell
python -m http.server 5501 --bind localhost --directory frontend
```

[Trang đăng nhập cổng 5501](http://localhost:5501/pages/login.html).
Frontend mặc định gọi API `http://localhost:5225/api`.
Dùng `localhost` với cổng 5500 hoặc 5501 để khớp CORS Development.
Không mở file HTML trực tiếp bằng `file://`.

**Đăng nhập:** dùng tài khoản đã có trong database đang kết nối. Chỉ khi bảng người dùng
còn rỗng thì ứng dụng mới chuyển sang màn tạo Admin đầu tiên. Nếu đã bật seed dữ liệu mẫu,
backend có thể đã tạo tài khoản trước bước này; xem `BasicDataSeeder` để biết dữ liệu seed.
Admin tạo nhân viên và gán vai trò; menu hiện theo quyền của tài khoản.
Thu ngân kiêm Phục vụ cần được gán cả `Cashier` và `Waiter`.

**Dừng chương trình:** bấm `Ctrl+C` trong mỗi Terminal. Không cần xóa database.

## Tài khoản demo theo vai trò

Các tài khoản dưới đây do nhóm bàn giao, đã kiểm tra đăng nhập và đúng vai trò trên
database gốc `RestaurantFoodManager` ngày **10/11/2026**.

| Vai trò | Tên đăng nhập hoặc email | Mật khẩu |
| --- | --- | --- |
| Bếp (`Chef`) | `bep123` | `123456` |
| Phục vụ (`Waiter`) | `phucvu` | `123456` |
| Thu ngân (`Cashier`) | `dongtn0472@gmail.com` | `0000000000` |
| Quản trị (`Admin`) | `dong123@gmail.com` | `dong@123` |
| Nhân viên kho (`WarehouseManager`) | `kho123@example.com` | `kho123` |

**Clone Git không tự tải tài khoản/mật khẩu từ SQL Server.** Bảng này áp dụng khi dùng
database/backup đã được chuẩn bị với các tài khoản trên. Nếu chạy database mới, cần dùng
Admin tạo nhân viên và gán vai trò tương ứng; không coi bảng này là kết quả tự seed của mọi máy.
Đây là thông tin đăng nhập của bản demo học tập được nhóm chủ động chia sẻ; không dùng lại
các mật khẩu này cho hệ thống vận hành thật.

## Xử lý lỗi thường gặp

| Hiện tượng | Kiểm tra / cách xử lý |
| --- | --- |
| Không kết nối được API | Mở Swagger cổng 5225, kiểm tra Terminal backend và SQL Server; frontend cần backend chạy đồng thời. |
| Thiếu khóa JWT hoặc khóa dưới 32 byte | Chạy lại khối nạp khóa JWT trong chính Terminal dùng để chạy backend. |
| SQL Server không kết nối được | Kiểm tra tên instance, dịch vụ SQL Server và quyền tài khoản Windows trong chuỗi kết nối. |
| Cổng 5225/5500/5501 đang được dùng | Kiểm tra ứng dụng đang chạy trên cổng đó. Nếu frontend đã chạy thì dùng URL hiện có, hoặc chọn cổng frontend còn trống ở trên. |
| 401 / phiên không hợp lệ | Đăng nhập lại; khóa JWT đổi hoặc token hết hạn có thể làm phiên cũ mất hiệu lực. |
| 403 / không có quyền mở màn | Kiểm tra vai trò tài khoản; Thu ngân không tự có quyền Phục vụ hoặc Bếp. |
| “Máy chủ gặp lỗi” (500) | Xem lỗi cụ thể trong Terminal backend. Đây chưa đủ để kết luận lỗi frontend; database cũ có thể cần đối chiếu schema/migration. Xem [ghi chú database cũ](docsforfe/original-database-and-delete-progress.md) và [sửa kiểu số lượng](docsforfe/legacy-order-quantity-fix.md). |
| Giao diện vẫn hiện bản cũ | Tải lại bằng `Ctrl+Shift+R` tại đúng trang/cổng frontend đang dùng. |

## Kiểm tra

Cần Node.js 20+ để chạy test frontend; việc mở/chạy giao diện không cần Node.js.

```powershell
dotnet build RestaurantFoodManagerSystem.sln
$taskFrontendTests = Get-ChildItem -Path .\frontend\tests\*.test.cjs -File | Select-Object -ExpandProperty FullName
node --test $taskFrontendTests
dotnet test RestaurantFoodManagerSystem.Tests
```

Các lệnh kiểm tra là bước tùy chọn, không phải lệnh khởi động ứng dụng.
Các kết quả theo mốc dưới đây là lịch sử, không phải xác nhận đã chạy lại toàn bộ kiểm tra khi cập nhật README.

F0 đã có lưu/gửi JWT, phân biệt 401/403, đăng xuất và helper tải CSV/header tuỳ chọn.
POS đã nối mở đơn theo bàn, gọi thêm, đổi số lượng, hủy món, hủy đơn Admin và lịch sử:
xem [bàn giao F1](docsforfe/f1-pos-progress.md). Áp/gỡ khuyến mãi của F1 và các màn F6–F8 còn lại
được theo dõi trong [kế hoạch frontend](docsforfe/fe-work-plan.md).
Bếp F2 đã có nhận từng món, báo số phần, thiếu/hỏng có lý do và mở lại: xem [bàn giao F2](docsforfe/f2-kitchen-progress.md).
Thu ngân F3 đã có thu trực tiếp, lịch sử/lọc, xem trước bill, hoàn tiền, MoMo intent/poll và nút thử Mock:
xem [bàn giao F3](docsforfe/f3-cashier-progress.md). In giấy/PDF và MoMo provider thật chưa được xác minh.
MoMo đang dùng provider `Mock`; chưa phải thanh toán thật.
Kho/Nhập hàng F4 đã nối điều chỉnh có lý do, lịch sử/tồn cần xử lý, phiếu nhập và nhà cung cấp:
xem [bàn giao F4](docsforfe/f4-warehouse-progress.md). Danh mục nguyên liệu/công thức đã nối ở F6 phần 1 bên dưới.
Tổng quan/Báo cáo F5 đã nối API thật, chọn kỳ ngày/tuần/tháng và tải CSV dành cho Admin:
xem [bàn giao F5](docsforfe/f5-reporting-progress.md). CSV đọc số liệu mới của kỳ tại lúc xuất.

F6 phần 1 đã nối Nguyên liệu/Công thức: mở **Quản trị → Nguyên liệu / Công thức** bằng Admin/Nhân viên kho.
Có thêm/sửa/ngừng/khôi phục, khóa đơn vị đã dùng và phiên bản công thức;
xem [bàn giao F6](docsforfe/f6-catalog-progress.md). Các mục Quản trị khác vẫn còn.

Kết quả ngày 2026-10-29: build tại F0 đạt; 54/54 test frontend (F0 + POS + Bếp) đạt;
9/9 kiểm tra API POS và 11/11 kiểm tra API Bếp/POS thật đạt;
backend baseline 518/697 đạt, 179 lỗi đã có trên `hai` (F1 không sửa backend).
Chi tiết và giới hạn kiểm thử nằm trong [ghi chú tích hợp](docsforfe/integration-progress.md).

Kết quả F3 ngày 2026-10-31: 80/80 test frontend và 13/13 kiểm tra API Thu ngân thật đạt trên database Integration riêng.
F3 không sửa/rerun bộ test backend; baseline lỗi cũ bên trên vẫn còn.

Kết quả F4 ngày 2026-10-31: 106/106 test frontend và 14/14 kiểm tra API Kho/Nhập hàng thật đạt trên database Integration riêng.
F4 không thay đổi backend hoặc chạy lại bộ test C#.

Kết quả F5 ngày 2026-11-01: 128/128 test frontend và 12/12 kiểm tra API Báo cáo thật đạt,
đối chiếu SQL và file CSV tải qua trình duyệt trên database Integration riêng. F5 không sửa backend.

Kết quả F6 phần 1 ngày 2026-11-01: 155/155 test frontend và 15/15 kiểm tra API thật đạt;
đã kiểm tra giao diện Admin, liên kết Nhập hàng và màn hình 390px. Không sửa/rerun test backend.

F6 phần 2 — Nhân viên/Vai trò: mở **Quản trị → Nhân viên / Phân quyền** bằng Admin.
Đã nối sửa hồ sơ, khóa/mở, reset mật khẩu, nhiều vai trò và danh sách role thật.
183/183 test frontend, 13/13 API thật đạt; xem [bàn giao F6 phần 2](docsforfe/f6-staff-progress.md).
Backend chưa thu hồi JWT cũ khi khóa/thu quyền/reset; không coi đây là thu hồi phiên tức thời.
Tài khoản cá nhân và các mục Quản trị khác còn tiếp tục.

## Cập nhật bàn giao 10/11/2026

- Backend trên nhánh local đã đồng bộ từ `hai` tới `a2f24d8`; lượt bàn giao này giữ nguyên
  source backend đó, bổ sung frontend và README của Đông.
- Frontend đã có menu/màn theo vai trò, chặn các nút quản lý bàn với Phục vụ và cải thiện
  bố cục/vùng bấm trên điện thoại. Chi tiết: [ghi chú frontend](frontend/ROLE_ACCESS.md).
- Các kiểm tra cũ phía trên là kết quả lịch sử theo từng phase, không phải số test hiện tại.
  Bộ frontend mới nhất có **236/236 test đạt**. Bốn màn Bàn/Đặt món/Bếp/Thanh toán đã được
  kiểm tra bằng fixture ở độ rộng 320, 390, 768, 1366px; không thay thế nghiệm thu mọi màn trên thiết bị thật.
- Backend build đạt; chạy lại trước push ngày 10/11 có **682 đạt, 22 lỗi, 7 bỏ qua**.
  Danh sách 22 test lỗi trùng lượt kiểm tra bản Hải ngày 09/11; không có test lỗi mới trong lần chạy này.
  Lượt bàn giao giữ nguyên backend Hải, không coi backend đã vượt toàn bộ test hoặc mọi luồng đã nghiệm thu.
- MoMo vẫn dùng Mock; chia/gộp bill, ca/két và các giới hạn được bàn giao trong
  [F6/F7](docsforfe/f6-f7-handoff.md) cần tiếp tục đối chiếu với Hải.
