# Restaurant Food Manager System

**Tác giả: Đông & Hải**

Ứng dụng quản lý nhà hàng, hỗ trợ các bộ phận phục vụ, bếp, thu ngân và kho
phối hợp từ gọi món đến thanh toán. Backend dùng **ASP.NET Core 8, Entity Framework Core
và SQL Server**; frontend dùng **HTML, CSS và JavaScript**, không cần npm hay bước build.

Chức năng chính: quản lý bàn và đặt bàn trước, gọi món tại bàn/QR, xử lý và in phiếu
bếp, thu tiền và ca thu ngân, kho–nhập hàng, thực đơn–công thức, nhân viên–phân quyền,
báo cáo và xuất CSV. Khách hàng có thể xem thực đơn và đăng ký đặt bàn.

## Chuẩn bị

Cài **.NET SDK 8**, **Python 3** và **SQL Server/SQL Express**; bảo đảm SQL Server đang chạy.

```powershell
git clone --branch dong https://github.com/hai21225/RestaurantFoodManagerSystem.git
cd RestaurantFoodManagerSystem
```

Backend dùng kết nối `ConnectionStrings:DefaultConnection` đang cấu hình.
Nếu máy dùng SQL instance khác, tạo file `RestaurantFoodManagerSystem/.env`:

```ini
ConnectionStrings__DefaultConnection=Server=localhost\SQLEXPRESS;Database=RestaurantFoodManager;Integrated Security=True;TrustServerCertificate=True
```

Đổi `localhost\SQLEXPRESS` theo máy của bạn. File `.env` được Git bỏ qua.
Tài khoản kết nối SQL Server cần quyền tạo database nếu chưa có và cập nhật schema.

## Chạy dự án

Bấm đúp **[start-backend.bat](start-backend.bat)** ở thư mục gốc, hoặc chạy:

```powershell
.\start-backend.bat
```

BAT mở hai cửa sổ Backend và Frontend. Mỗi lần backend khởi động từ BAT sẽ:

1. Dùng khóa JWT từ biến môi trường nếu đã có; nếu chưa, tự tạo một lần và dùng lại
   khóa tại `%LOCALAPPDATA%\RestaurantFoodManagerSystem\jwt-signing-key.txt`.
2. Áp dụng migration còn thiếu trên database đang cấu hình.
3. Chạy [BasicDataSeeder](RestaurantFoodManagerSystem/Infrastructure/Persistence/BasicDataSeeder.cs)
   để bổ sung tài khoản, bàn, nguyên liệu, món và công thức khởi tạo.

Đợi backend khởi động xong rồi mở:

- **Website:** [http://localhost:5500/pages/login.html](http://localhost:5500/pages/login.html)
- **Swagger/API:** [http://localhost:5225/swagger/index.html](http://localhost:5225/swagger/index.html)

Nhấn **Ctrl+C** ở từng cửa sổ để dừng. Nếu cổng đã có tiến trình chạy, BAT không mở thêm;
hãy dừng backend cũ rồi chạy lại BAT để áp dụng code hoặc seed mới.

```powershell
.\start-backend.bat --check     # Kiểm tra công cụ; không chạy ứng dụng, migration hay seed
.\start-backend.bat --backend   # Chỉ chạy backend, migration và seed
.\start-backend.bat --frontend  # Chỉ chạy frontend
```

## Tài khoản theo vai trò

Seeder tạo những tài khoản sau khi chưa tồn tại:

| Vai trò | Tên đăng nhập | Mật khẩu khởi tạo | Công việc chính |
|---|---|---|---|
| Quản trị viên | `admin` | `123456` | Quản trị hệ thống, xem báo cáo |
| Phục vụ | `waiter` | `123456` | Chọn bàn, gửi và theo dõi món |
| Bếp | `chef` | `123456` | Nhận món, báo tiến độ, thiếu/hỏng, in phiếu |
| Thu ngân | `cashier` | `123456` | Thu tiền, phiếu thu, quản lý ca |
| Quản lý kho | `warehouse` | `123456` | Nguyên liệu, công thức, tồn kho, nhập hàng |

**Khách hàng** tự tạo tài khoản tại [trang đăng ký](http://localhost:5500/pages/register.html);
không có tài khoản khách hàng được seed sẵn. Menu hiển thị theo vai trò được cấp.

Mật khẩu trong bảng chỉ áp dụng cho tài khoản mới được seed. Nếu tên đăng nhập hoặc
email đã tồn tại, seeder giữ mật khẩu hiện có. Các tài khoản cũ của nhóm vẫn được giữ lại.

## Lưu ý

- Seed không xóa toàn bộ database hoặc lịch sử đơn; mục khởi tạo đã ngừng dùng có thể được tạo lại.
- Sao lưu database đang sử dụng trước khi áp dụng migration mới. Clone Git không tải kèm dữ liệu riêng của máy khác.
- MoMo có chế độ mô phỏng; thanh toán thật cần cấu hình nhà cung cấp.
- Không đưa khóa JWT, file `.env` hoặc thông tin kết nối riêng lên Git; đổi mật khẩu khởi tạo khi dùng ngoài môi trường học tập.
- Nếu chưa có tài khoản hoặc backend báo lỗi, xem cửa sổ Backend và kiểm tra đúng database.
  Nếu giao diện còn bản cũ, tải lại bằng **Ctrl+Shift+R**.
