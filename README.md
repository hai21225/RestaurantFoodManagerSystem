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
node --test frontend/tests/api-auth.test.cjs frontend/tests/order-session.test.cjs frontend/tests/kitchen-session.test.cjs frontend/tests/payment-session.test.cjs frontend/tests/warehouse-session.test.cjs frontend/tests/reporting-session.test.cjs
dotnet test RestaurantFoodManagerSystem.Tests
```

F0 đã có lưu/gửi JWT, phân biệt 401/403, đăng xuất và helper tải CSV/header tuỳ chọn.
POS đã nối mở đơn theo bàn, gọi thêm, đổi số lượng, hủy món, hủy đơn Admin và lịch sử:
xem [bàn giao F1](docsforfe/f1-pos-progress.md). Áp/gỡ khuyến mãi của F1 và các màn F6–F8 còn lại
được theo dõi trong [kế hoạch frontend](docsforfe/fe-work-plan.md).
Bếp F2 đã có nhận từng món, báo số phần, thiếu/hỏng có lý do và mở lại: xem [bàn giao F2](docsforfe/f2-kitchen-progress.md).
Thu ngân F3 đã có thu trực tiếp, lịch sử/lọc, xem trước bill, hoàn tiền, MoMo intent/poll và nút thử Mock:
xem [bàn giao F3](docsforfe/f3-cashier-progress.md). In giấy/PDF và MoMo provider thật chưa được xác minh.
MoMo đang dùng provider `Mock`; chưa phải thanh toán thật.
Kho/Nhập hàng F4 đã nối điều chỉnh có lý do, lịch sử/tồn cần xử lý, phiếu nhập và nhà cung cấp:
xem [bàn giao F4](docsforfe/f4-warehouse-progress.md). Danh mục nguyên liệu/công thức đầy đủ vẫn thuộc F6.
Tổng quan/Báo cáo F5 đã nối API thật, chọn kỳ ngày/tuần/tháng và tải CSV dành cho Admin:
xem [bàn giao F5](docsforfe/f5-reporting-progress.md). CSV đọc số liệu mới của kỳ tại lúc xuất.

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
