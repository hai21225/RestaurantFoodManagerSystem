# Database gốc và hiển thị sau khi xóa

Cập nhật 2026-11-01, nhánh `dong`. Người dùng yêu cầu kiểm tra database gốc, bổ sung phần thiếu,
chạy chương trình và sửa tình trạng bấm Xóa nhưng mục vẫn còn trên giao diện.
Người dùng xác nhận **đặt lại tài khoản Admin của Đông**, giữ các tài khoản hiện có.

## Runtime đang chạy trên máy Đông

- Frontend: `http://localhost:5500`.
- Backend: `http://localhost:5225/api`, kết nối **RestaurantFoodManager** trên `localhost\SQLEXPRESS`.
- `Auth:Enforce=true`, payment provider Mock. Key riêng cho database gốc, không dùng phiên/key Integration.
- Đã xác minh database bằng session SQL Server của PID backend và đăng nhập/xem giao diện bằng Admin của Đông.
- Hai tài khoản cũ được giữ, chỉ reset mật khẩu tài khoản Đông theo yêu cầu; không tạo Admin mới.
  Thông tin đăng nhập được lưu trong file riêng có ACL giới hạn trên máy, ngoài Git; không ghi mật khẩu vào tài liệu này.
- Chạy lại bằng helper local ngoài repository: `D:\Hoc_Tap\quanliduan1111\_codex_runtime\start-original-database.ps1`.
  Helper chỉ khởi động khi cổng chưa có dịch vụ, dùng môi trường riêng; không sửa appsettings được theo dõi bởi Git.

**Các ca kiểm thử có ghi dữ liệu tiếp theo phải chạy trên database Integration riêng.**
Không chạy script F1–F6 mặc định vào cổng 5225 rồi giả định đó vẫn là database test.

## Database trước và sau

Trước thay đổi, database gốc có 6 món, 6 kích cỡ, 4 danh mục, 10 bàn, 2 user/5 role;
chưa có nguyên liệu, tồn kho, công thức, NCC, đơn hàng hoặc thanh toán.

- Đã tạo SQL backup COPY_ONLY + CHECKSUM và chạy RESTORE VERIFYONLY thành công trước khi ghi.
  Bản sao lưu nằm ngoài Git; đường dẫn được ghi trong file local `_codex_runtime/original-db-backup.json`.
- Áp dụng migration có sẵn bằng backend hiện tại, không sửa C#/migration source.
  Lịch sử migration từ 3 lên 15 bản ghi; có migration legacy `AddFlexibleFoodSaleUnits` còn trong DB từ trước,
  không tự xóa lịch sử/cột cũ để khớp số lượng file migration.
- Giữ 6 món, 10 bàn và 2 user cũ. Bổ sung **17 nguyên liệu, 17 dòng tồn, 17 ledger tồn ban đầu mẫu,
  6 công thức đang hoạt động, 1 kích cỡ còn thiếu của Lẩu Cua Đồng và 2 NCC mẫu**.
- Công thức cho cơm chiên Dương Châu (Nhỏ/Vừa/Lớn), cơm trắng (Vừa/Lớn), Lẩu Cua Đồng (Vừa).
  Các dòng dùng đúng đơn vị kho: gạo, trứng, rau củ, dầu/nước mắm, cua, xương, đậu hũ và bún.
- Tồn ban đầu và định lượng là **mẫu để vận hành thử**, không phải kết quả kiểm kê hay định lượng đã được quán xác nhận.
  Ledger có ghi rõ nguồn mẫu; NCC dùng liên hệ mẫu/email `.invalid`, cần cập nhật thông tin thực tế.
- Không tạo đơn hoặc thu tiền để làm đẹp báo cáo: database gốc vẫn **0 đơn, 0 thanh toán** sau chuẩn bị.
- Món ID 5 trùng tên cơm chiên Dương Châu, giá **30.000.000 VND**, chưa có kích cỡ/công thức.
  Giữ nguyên để chủ dự án rà soát; không tự đoán giá hoặc xóa bản ghi cũ.

Database là dữ liệu local, không nằm trong commit frontend và không được push backup/thông tin đăng nhập lên Git.

## Vì sao bấm Xóa vẫn thấy

Backend dùng **xóa mềm** để giữ lịch sử. Trước đây các màn mặc định tải/hiện cả inactive;
riêng bàn bị gộp nhãn với Bảo trì và chi tiết công thức tiếp tục chọn bản vừa ngừng dùng.

Đã sửa frontend:

- **Món ăn:** mặc định Đang sử dụng, xóa xong tải lại API, chuyển về danh sách đang dùng và đóng chi tiết cũ.
  Bộ lọc Đã xóa/Ngừng bán cho phép tìm và khôi phục. Món đã xóa cũng không hiện trong lựa chọn thêm kích cỡ.
- **Bàn:** mặc định Đang dùng; tách Đã xóa khỏi Bảo trì. Xóa xong ẩn khỏi sơ đồ đang dùng,
  xem/khôi phục ở bộ lọc Đã xóa; bàn bảo trì còn active vẫn được hiển thị và có thể kết thúc bảo trì.
- **Nguyên liệu:** mặc định Đang dùng; sau ngừng/khôi phục trở về bộ lọc Đang dùng.
  Chọn Ngừng dùng để xem/khôi phục, không xóa số lượng tồn hay ledger.
- **Công thức:** mặc định Đã có công thức, chỉ hiện món/kích cỡ còn active với recipe active.
  Ngừng công thức thì bỏ chi tiết bản vừa ngừng; xem lại qua Tất cả kích cỡ → Xem/Phiên bản.
- Mỗi màn bỏ chọn bản ghi đã khuất khỏi bộ lọc; không để chi tiết cũ khiến người dùng nghĩ xóa chưa thành công.
- Thông báo xác nhận nói rõ nơi xem lại; thao tác ngừng/xóa/khôi phục đọc lại API.
  Giá vốn/thời gian chế biến chưa có từ API được ghi Chưa có dữ liệu thay cho số 0 tự đặt.

Đây là ẩn khỏi danh sách đang dùng, **không thêm API xóa vĩnh viễn**. Lịch sử và khả năng khôi phục vẫn giữ.
Các giới hạn backend như FE-C20/FE-C21 vẫn theo kế hoạch, không được coi là đã sửa trong lượt này.

## Kiểm tra đã thực hiện

| Kiểm tra | Kết quả |
|---|---|
| Backup trước cập nhật | COPY_ONLY/CHECKSUM + VERIFYONLY đạt |
| SQL session runtime | PID backend trên 5225 đang kết nối RestaurantFoodManager |
| Đăng nhập sau reset | Admin Đông đăng nhập API và trình duyệt thành công |
| Dữ liệu gốc | 6 món, 10 bàn, 2 user được giữ; 17 nguyên liệu/tồn dương, 6 recipe đọc detail đủ đơn vị/quantity |
| Nghiệp vụ trên DB gốc | Không tạo đơn/thu tiền; orders=0, payments=0 |
| Xóa/khôi phục trên UI | Món, bàn, nguyên liệu, công thức: ẩn mặc định và khôi phục từ bộ lọc đúng; thử trên fixture của DB Integration |
| Sau thử UI | Chỉ các fixture vừa tạo được ngừng dùng qua API; không dùng bản ghi gốc để kiểm thử xóa |
| Frontend tests | **183/183 đạt**, không bỏ qua |
| Cú pháp/HTML/asset/diff | 34 JS, 15 HTML, 318 đường dẫn tài nguyên và git diff --check đạt |

Script, kết quả và ảnh thực chạy nằm ngoài Git tại `_codex_runtime` và `_codex_reports/20261101` của workspace máy Đông.
Không sửa hoặc chạy lại bộ test C#; baseline lỗi backend cũ không thay đổi.
