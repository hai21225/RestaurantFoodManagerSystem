# F6 phần 2 — Nhân viên và vai trò

Cập nhật 2026-11-01 trên `dong`. Đã hoàn thành FE-13/FE-14 trong phạm vi frontend;
chưa hoàn thành toàn bộ F6. Đã đọc AGENTS, contract, work plan, payloads, reference và đối chiếu
UsersController, RolesController, UserService, AuthService, RoleService và DTO thực tế.
Không sửa backend, migration, SQL/appsettings hoặc lấy source từ backup.

## Cách dùng

1. Đăng nhập **Admin**, vào **Quản trị → Nhân viên**. Tìm theo tên/tài khoản/email/điện thoại,
   lọc theo vai trò hoặc trạng thái; mỗi nhân viên có thể giữ nhiều vai trò.
2. **Thêm nhân viên**: nhập họ tên, tên đăng nhập, email, mật khẩu và xác nhận; vai trò ban đầu
   lấy từ API, mặc định **Chưa gán vai trò**. Mật khẩu yêu cầu 6–72 **byte UTF-8**, không phải số ký tự.
3. Chọn **Xem / Quản lý**. Trên màn nhỏ trang chuyển tới chi tiết và có nút **Về danh sách**.
   **Sửa thông tin** chỉ sửa họ tên/email/điện thoại; tên đăng nhập giữ nguyên.
4. **Khóa/Mở khóa** có xác nhận đúng người. Chặn tự khóa và khóa Admin đang hoạt động cuối cùng;
   Admin đã bị khóa không được tính là người quản trị dự phòng.
5. **Gán thêm vai trò** hoặc **Thu hồi** từng vai trò, kiểm tra hộp xác nhận rồi thực hiện.
   Các vai trò còn lại giữ nguyên. Không cho thu hồi Admin cuối cùng; server vẫn quyết định cuối cùng.
6. **Đặt lại mật khẩu** dùng mật khẩu mới + xác nhận, không hỏi mật khẩu hiện tại của nhân viên.
   Các trường mật khẩu được xóa sau thao tác/đóng dialog; không lưu vào localStorage/sessionStorage.
7. **Quản trị → Phân quyền** đọc 5 vai trò/mô tả từ `GET /roles`. Có liên kết sang danh sách nhân viên
   đã lọc theo từng vai trò. Không còn ma trận checkbox và nút Lưu giả; backend không có CRUD role/ma trận quyền.

## API thực tế và xử lý lỗi

- Nối đủ 9 route Admin: GET/POST `/users`, GET/PUT `/users/{id}`, PATCH active,
  POST reset-password, POST/DELETE role của user và GET `/roles`.
- `GET /users` trả **mảng**, không phân trang; tìm/lọc/thống kê trên toàn bộ dữ liệu đã tải.
- Khóa/mở dùng **`PATCH /users/{id}/active?active=false|true`**, không gửi body isActive.
  Tất cả thao tác thành công trong nhóm này trả 200. Tạo user trả message, phải đọc lại danh sách để lấy ID.
- Email trùng khi tạo trả **409**, email trùng khi sửa và vi phạm Admin cuối trả **400**;
  gán trùng role trả **409**, role/user không tồn tại trả **404**. Hiện nguyên message của server.
- `StaffSession` kiểm tra Admin trước HTTP, validation/DTO sạch, snapshot chi tiết trước ghi,
  chặn bấm kép và phản hồi cũ đến trễ. Không tự sửa trạng thái/role trong dữ liệu cục bộ để giả thành công.
- Chỉ cập nhật hồ sơ liên hệ qua PUT; không gửi kèm username/roles/isActive/password hoặc actor.
  Các tên/dữ liệu API được escape khi render; người không phải Admin thấy thông báo thiếu quyền và không thấy menu hai màn này.
- Lưu intent **không chứa mật khẩu/body ghi** vào sessionStorage trước lệnh. Không tự retry khi mất ACK/5xx.
  Nếu nhận 2xx nhưng đọc lại lỗi, review chỉ đọc. Nếu chưa rõ ACK, phải tải mới, đối chiếu rồi xác nhận mở khóa.
  Mật khẩu không có API đọc lại; không dùng UpdatedAt hoặc dữ liệu giống nhau để suy thao tác đã thành công.
- Intent gắn user, xóa khi logout/401; đổi user giữa preflight bị chặn. Đóng tab/xóa bộ nhớ có thể làm mất intent.
  Sửa vai trò hoặc đặt lại mật khẩu của chính mình yêu cầu đăng nhập lại trên ứng dụng sau khi đối chiếu thành công.

## Giới hạn backend đã xác minh

**Khóa tài khoản, thu quyền và đặt lại mật khẩu chưa thu hồi JWT cũ.** API thật đã xác nhận:
khóa chặn đăng nhập mới, nhưng token cũ còn đọc được API; thu Chef chặn phiên đăng nhập mới vào Bếp,
nhưng JWT Chef đã cấp trước đó vẫn được phép. Gán thêm role cũng cần đăng nhập mới để có claim mới.
UI hiển thị giới hạn này và tự logout khi thay quyền/mật khẩu của chính mình, không thể đăng xuất mọi thiết bị thay server.
Đề xuất Hải xử lý ở [**FE-C21 — ghi chú bàn giao chi tiết**](fe-c21-jwt-session-revocation.md), không coi chức năng khóa/thu quyền là thu hồi phiên tức thời.

Snapshot và kiểm tra Admin cuối trên FE chỉ là preflight, chưa bảo đảm nguyên tử khi nhiều Admin ghi cùng lúc.
Chưa có idempotency/receipt hoặc concurrency token cho nhóm nhân viên (FE-C22).
Chưa có API audit lịch sử thay đổi quyền/nhân viên; không hiển thị lịch sử giả.

## Kiểm tra đã chạy

Chỉ dùng database `RestaurantFoodManager_Integration_20261029`, `Auth:Enforce=true`.
Không ghi vào database `RestaurantFoodManager`; không sửa tài khoản nghiệp vụ thật.

| Kiểm tra | Kết quả |
|---|---|
| Cú pháp/HTML/asset/diff | 34 JS, 15 HTML, 318 đường dẫn tài nguyên và git diff --check đạt; file ghi chú local giữ nguyên hash |
| Bộ test frontend | **183/183 đạt**, gồm 28 ca StaffSession mới, không bỏ qua |
| API thật qua restaurantApi/StaffSession | **13/13 đạt** |
| Quyền API | Admin được phép; Waiter/Chef/Cashier/WarehouseManager nhận 403 cho các route đọc và sửa đã thử |
| Tài khoản và liên hệ | Tạo kèm role nguyên tử, trùng tên/email và dữ liệu sai không tạo user dở dang; PUT giữ username và các vai trò |
| Khóa/role/password | Khóa/mở, multi-role, thu từng role, reset, bảo vệ tự khóa/Admin cuối; login mới và JWT cũ được kiểm tra riêng |
| Mất ACK | Sau API thật đã tạo, proxy bỏ phản hồi; chỉ tạo một lần, intent không chứa mật khẩu, review không POST lại |
| Giao diện Admin | Lưu hồ sơ user thử, xem multi-role, guard tự khóa/Admin cuối, form tạo/reset và hộp xác nhận gán role mở/hủy đúng người |
| Giao diện Phục vụ | Đăng nhập trên origin kiểm thử riêng; mở thẳng Nhân viên và Phân quyền đều hiện thiếu quyền, hai menu được ẩn |
| Vai trò/liên kết | Đọc đúng 5 role/mô tả server; link Chef sang danh sách đã chọn filter Chef |
| Responsive | Rộng CSS 390px: form tạo/sửa và danh mục vai trò không tràn ngang; chọn nhân viên đưa tới chi tiết |

Các thao tác nhạy cảm tạo/khóa/mở/gán/thu/reset đã chạy qua API trên user giả;
giao diện xác nhận được mở/hủy, không mô tả là đã bấm thực hiện mọi thao tác nhạy cảm qua UI.
Tự đổi quyền/mật khẩu rồi logout được kiểm tra ở lớp session và wiring frontend; chưa đổi quyền/mật khẩu Admin chính qua UI.

Fixture: user #6 `F6 STAFF TEST Nhân viên giao diện` (Waiter + Chef, active), #7 Admin dự phòng (locked),
#9 Mất ACK (active, chưa gán role). Admin kiểm thử chính #1 giữ nguyên mật khẩu/quyền/trạng thái.
Mật khẩu thử ngẫu nhiên và script nằm ngoài Git tại `_codex_runtime`; báo cáo/ảnh ở
`D:\Hoc_Tap\quanliduan1111\_codex_reports\20261101\f6-staff-*`.
Kiểm tra cú pháp JS, ID/asset HTML, liên kết tài liệu, diff và hash file ghi chú local ghi trong `f6-staff-static-checks.json`.

Không sửa/rerun test backend: baseline F0 **518 đạt / 179 lỗi / 697 tổng** vẫn còn.
Tiếp theo: **Tài khoản cá nhân** (FE-03), rồi món/topping/size/category, khu vực bàn và khuyến mãi còn lại của F6.
