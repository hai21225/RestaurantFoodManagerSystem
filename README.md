# Restaurant Food Manager

Backend ASP.NET Core 8 + SQL Server; frontend HTML/CSS/JavaScript thuần, không cần npm.
Nhánh làm việc: `dong`. Tiến độ sau khi gộp `hai`: [ghi chú tích hợp](docsforfe/integration-progress.md).

## Chạy trên máy mới

Cài .NET SDK 8, SQL Server/SQL Express và Python 3. Mở PowerShell tại thư mục repository.
Database dưới đây là database phát triển riêng; không trỏ lần chạy đầu vào database đang có dữ liệu nghiệp vụ.

Terminal 1 — backend:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Server=localhost\SQLEXPRESS;Database=RestaurantFoodManagerDev;Integrated Security=True;TrustServerCertificate=True"
$taskJwtBytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($taskJwtBytes)
$env:Authentication__Jwt__Key = [Convert]::ToBase64String($taskJwtBytes)
dotnet restore RestaurantFoodManagerSystem.sln
dotnet run --project RestaurantFoodManagerSystem --no-launch-profile --urls http://localhost:5225
```

Đổi tên SQL instance nếu máy không dùng `SQLEXPRESS`. Backend tự áp dụng migration và seed vai trò/khu vực bàn.
`Auth:Enforce=true` yêu cầu JWT. Khoá ký không lưu trong Git; muốn giữ phiên qua các lần mở terminal,
lưu khoá bằng .NET user-secrets hoặc cấu hình môi trường riêng. Đổi khoá khiến token cũ hết hiệu lực.
Swagger: <http://localhost:5225/swagger>.

Terminal 2 — frontend, cũng tại thư mục repository:

```powershell
python -m http.server 5500 --bind localhost --directory frontend
```

Mở <http://localhost:5500/pages/login.html>. Database mới sẽ chuyển tới màn hình tạo Admin đầu tiên;
không có tài khoản/mật khẩu mặc định. Sau đó đăng nhập và tạo nhân viên theo vai trò.
Nên dùng `localhost` và cổng 5500/5501 để khớp CORS Development.

## Kiểm tra

Cần Node.js 20+ để chạy test frontend; việc mở/chạy giao diện không cần Node.js.

```powershell
dotnet build RestaurantFoodManagerSystem.sln
node --test frontend/tests/api-auth.test.cjs
dotnet test RestaurantFoodManagerSystem.Tests
```

F0 đã có lưu/gửi JWT, phân biệt 401/403, đăng xuất và helper tải CSV/header tuỳ chọn.
Trang báo cáo, hồ sơ, POS và bếp còn các việc F1–F8: xem [kế hoạch frontend](docsforfe/fe-work-plan.md).
MoMo đang dùng provider `Mock`; chưa phải thanh toán thật.

Kết quả ngày 2026-10-29: build đạt, 18/18 test F0 đạt; backend 518/697 đạt, 179 lỗi đã có trên `hai`.
Chi tiết và giới hạn kiểm thử nằm trong [ghi chú tích hợp](docsforfe/integration-progress.md).
