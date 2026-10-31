# Tiến độ tích hợp Hải → Đông

Cập nhật 2026-11-01. Nhánh làm việc **`dong`**; không tạo nhánh làm việc khác.

Tiến độ sau F0: đã nối các luồng POS chính; xem [bàn giao F1](f1-pos-progress.md).
Màn Bếp đã nối F2; xem [bàn giao F2](f2-kitchen-progress.md) và các giới hạn quyền/retry đã ghi rõ.
Thu ngân đã nối F3: thu trực tiếp, lịch sử/lọc, xem trước bill, hoàn tiền và MoMo Mock;
80/80 test frontend + 13/13 API Thu ngân thật đạt. Xem [bàn giao F3](f3-cashier-progress.md),
đặc biệt giới hạn in/PDF, provider thật và hoàn đơn cũ khi bàn đã có lượt khách mới.
Kho/Nhập hàng đã nối F4: điều chỉnh có lý do, lịch sử/tồn cần xử lý, phiếu và NCC;
106/106 test frontend + 14/14 API Kho thật đạt. Xem [bàn giao F4](f4-warehouse-progress.md)
về đối chiếu mất ACK và các nghiệp vụ chưa có API.
Tổng quan/Báo cáo đã nối F5: số liệu thật, kỳ ngày/tuần/tháng theo giờ Việt Nam và CSV cho Admin;
128/128 test frontend + 12/12 kiểm tra API Báo cáo/SQL thật đạt. Xem [bàn giao F5](f5-reporting-progress.md)
về cách dùng, định nghĩa doanh thu và CSV đọc số liệu mới tại thời điểm xuất.
Các kết quả F0 bên dưới là mốc kiểm tra lúc tích hợp, không thay thế tiến độ F1 mới.

## Mã nguồn đã gộp

- Đông: `8d6769c`; Hải: `dd258d2`; merge commit: `c210d87`.
- Giữ toàn bộ backend, tests, migration, tài liệu và giao diện bàn của Hải.
- Giữ nguyên logic/CSS Nhập hàng của Đông. HTML Nhập hàng chỉ bổ sung script auth chung trong F0.
- Hai xung đột: `Program.cs`, `appsettings.json`. Giữ service/JWT của Hải, CORS chỉ chạy ở Development,
  giữ kết nối SQL của Đông và bỏ giá trị khoá ký JWT khỏi cấu hình được commit. Nạp khoá qua môi trường/user-secrets.
- Nhánh nhận bàn giao của nhóm: [origin/dong](https://github.com/hai21225/RestaurantFoodManagerSystem/tree/dong).
- Commit chức năng: `bf1131e` (F0 đăng nhập/API), `3827af9` (các luồng POS); ghi chú được lưu cùng mã nguồn.

## F0 — hạ tầng xác thực/API

Đã làm:

- `auth.js`: lưu token, hồ sơ/vai trò, tương thích `sessionStorage` của các trang cũ; chặn truy cập trang khi chưa đăng nhập;
  kiểm tra hạn dùng và đăng xuất đồng bộ giữa các tab; xoá ngữ cảnh bàn/đơn khi đổi phiên.
- Login lưu JWT và quay lại đúng trang trong ứng dụng. Không chấp nhận URL quay lại thuộc website khác.
- Tất cả 15 trang nạp auth/API. Header hiện danh tính/vai trò thật và có nút Đăng xuất.
- API gửi Bearer; 401 ở API bảo vệ xoá phiên/chuyển login; 401 sai mật khẩu giữ tại form; 403 báo thiếu quyền, giữ phiên.
- Helper HTTP giữ chữ ký gọi cũ, hỗ trợ options/header (`Idempotency-Key`), lỗi nghiệp vụ, problem+json, 204, AbortSignal và tải Blob CSV.
- Bếp đọc vai trò Chef/Admin của phiên; bỏ việc lấy quyền từ `restaurant.currentRole` giả.

Chưa làm trong F0: CRUD hồ sơ/đổi mật khẩu (F6), giao diện báo cáo (F5), POS gọi thêm/huỷ món (F1),
bếp xác nhận/hao hụt theo từng món (F2), thu ngân đầy đủ (F3). Helper CSV đã có, nút báo cáo cũ chưa nối API.

## Kết quả thực chạy

| Kiểm tra | Kết quả |
|---|---|
| `dotnet build RestaurantFoodManagerSystem.sln` | Đạt, 0 warning / 0 error |
| `node --test frontend/tests/api-auth.test.cjs` | 18/18 đạt |
| Cú pháp JS, đường dẫn asset/script HTML | 25 JS, 15 HTML đạt |
| `git diff --check` cho phần F0 | Đạt |
| API SQL Server với `Auth:Enforce=true` | 7/7 đạt: hồ sơ Admin, CSV Admin, thiếu JWT, JWT sai, login sai, 403 sai role, CORS Bearer |
| Trình duyệt | Chưa đăng nhập → login; sai mật khẩu hiện lỗi; Admin đăng nhập → quay lại Nhập hàng, hiện tên/role; đăng xuất → login |
| Toàn bộ tests backend | **518 đạt / 179 lỗi / 697 tổng**, không bỏ qua ca nào |

Chạy API bằng database mới **`RestaurantFoodManager_Integration_20261029`**, áp dụng migration tự động.
Chỉ thêm tài khoản kiểm thử Admin/Waiter/Chef; không thêm đơn hàng, thanh toán hay chứng từ nghiệp vụ.
Không chạy migration hay ghi dữ liệu vào database `RestaurantFoodManager` hiện có.
Thông tin đăng nhập kiểm thử, khoá JWT và log nằm ngoài repository.

## Hải cần xử lý trước khi coi backend đã qua kiểm thử

- Cùng nhánh Hải `dd258d2` trước khi gộp đã có đúng 179 ca lỗi. Không được dùng số 100/100 E2E cũ để kết luận bản hiện tại đã đạt.
- Phần lớn lỗi liên quan `CurrentUserExtensions.ResolveActor`: không có actor trong token thì ném `UnauthorizedAccessException`;
  nhiều test còn giả định được lấy actor từ body. Cần chốt chính sách actor, sửa test/host và chạy lại.
- Merge nguyên mã Hải có whitespace lỗi từ trước ở hai file C# và hai tài liệu; log kiểm tra được giữ ngoài repo.
  Không chỉnh hàng loạt backend/tài liệu chỉ để đổi whitespace.

## Việc tiếp theo

1. Hải chốt JWT/actor và đưa bộ test backend về trạng thái đạt.
2. Đông tiếp tục F6 trên `dong`; ưu tiên danh mục nguyên liệu/công thức để Kho/Nhập hàng có đủ dữ liệu chọn.
3. Theo dõi các phần còn lại của F1 và giới hạn đã ghi trong bàn giao F2–F5; chưa coi các giới hạn đó đã được xử lý.

Mỗi chức năng cập nhật tài liệu và commit riêng; chỉ đánh dấu xong khi đã kiểm thử luồng đó.
