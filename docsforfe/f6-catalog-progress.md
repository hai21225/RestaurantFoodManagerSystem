# F6 phần 1 — Nguyên liệu và công thức

Cập nhật 2026-11-01, nhánh `dong`. Đây là phần FE-10 của F6, chưa phải toàn bộ Quản trị.
Đã đọc AGENTS, API contract, kế hoạch, payloads, reference và đối chiếu controller/service/DTO thực tế.
Chỉ sửa frontend và tài liệu; không sửa backend, migration, SQL/appsettings hoặc lấy source từ backup.

## Cách dùng

1. Đăng nhập **Admin hoặc Nhân viên kho**, mở **Quản trị → Nguyên liệu / Công thức**.
   Kho và Nhập hàng cũng có liên kết **Danh mục nguyên liệu**.
2. Tab **Nguyên liệu**: thêm tên, đơn vị kho và mức tồn tối thiểu. Tạo mới có tồn bằng 0;
   nhập số lượng bằng **Nhập hàng** hoặc kiểm kê tại **Kho**, không nhập tồn trong danh mục.
3. Có thể sửa tên/mức tối thiểu, ngừng dùng và khôi phục. Đơn vị chỉ sửa khi chưa từng xuất hiện
   trong công thức (kể cả phiên bản cũ), biến động kho hoặc phiếu mua (kể cả nháp/hủy).
   Nếu đơn vị đã khóa, tạo nguyên liệu mới cho đơn vị khác; không đổi số lượng cũ bằng cách đổi đơn vị.
4. **Tình trạng** đánh dấu Bình thường/Hết hàng/Hỏng để Kho theo dõi, không điều chỉnh số lượng
   hoặc tự đổi khả năng bán của món. Hỏng/thiếu thực tế xử lý số lượng tại Kho hoặc luồng Bếp phù hợp.
5. Tab **Công thức**: chọn món/kích cỡ, tên và định lượng cho **1 phần** theo đơn vị kho của từng nguyên liệu.
   Ví dụ nguyên liệu dùng kg thì nhập 0,2 cho 200g. Tối đa 3 số lẻ, ít nhất 0,001; không lặp nguyên liệu.
6. Mỗi kích cỡ có một công thức đang hoạt động. Sửa bản đang dùng sẽ tạo `recipeId` mới;
   để trống phiên bản để server tự tăng. Sau lưu luôn đọc lại danh sách, chọn bản mới.
7. **Xem / Phiên bản** để đọc định lượng và lịch sử. Có thể ngừng/khôi phục;
   trước khi khôi phục bản cũ phải ngừng bản hiện tại và bảo đảm kích cỡ/nguyên liệu còn hoạt động.
   Các phiên bản cũ và định lượng cũ được giữ lại.

## Đã nối và quy tắc giao diện

- Đủ 7 route nguyên liệu: list/detail/create/update/delete/active/status;
  đủ 6 route công thức: list/detail/create/update/delete/active.
  `DELETE` là ngừng dùng có thể khôi phục; không xóa lịch sử.
- Tìm/lọc theo tên, hoạt động, tình trạng kho; công thức theo món, tên/phiên bản và kích cỡ chưa thiết lập.
  Đọc cả lịch sử món/nguyên liệu ngừng dùng, không âm thầm bỏ nguyên liệu cũ khi sửa.
- Giữ tên kích cỡ tùy ý từ backend trong màn Công thức, không giới hạn vào Nhỏ/Vừa/Lớn.
  Đây chưa phải xác minh mọi tên kích cỡ qua giao diện Món ăn/POS.
- `restaurantCatalog.CatalogSession` xử lý validation, preflight, phiên bản và yêu cầu chưa rõ kết quả;
  mọi HTTP qua `restaurantApi`, dùng quyền JWT thật, hiện lỗi server và escape dữ liệu khi render HTML.
- Kiểm tra đơn vị qua lịch sử kho → toàn bộ công thức → toàn bộ phiếu mua;
  chỉ mở ô đơn vị sau khi đọc đủ. Lỗi lookup giữ đơn vị khóa, vẫn cho sửa tên/mức tối thiểu.
  Server vẫn quyết định cuối cùng; lỗi unit `409` khóa lại ô và giữ dữ liệu form khác.
- Đọc lại bản ghi, so snapshot trước ghi để phát hiện thay đổi đã xảy ra khi form đang mở.
  Chặn bấm kép và phản hồi chi tiết đến trễ. Không coi preflight là bảo đảm nguyên tử giữa hai người dùng.
- Các route danh mục chưa có idempotency/receipt. Ghi intent vào `sessionStorage` trước khi gửi;
  mất kết nối/5xx giữ khóa, không tự gửi lại. Nếu đã nhận 2xx nhưng tải sau đó lỗi, chỉ đọc lại để hoàn tất.
  Nếu chưa rõ ACK, tải dữ liệu, đối chiếu thủ công rồi xác nhận mở khóa; không suy thành công từ tên/định lượng giống nhau.
  Intent gắn user, xóa khi đăng xuất/401; đóng tab hoặc xóa bộ nhớ làm mất dấu theo dõi cục bộ.
- Menu vẫn nằm trong **Quản trị**; Báo cáo giữ nguyên vị trí Quản trị và quyền Admin.
  Cập nhật phiên bản asset chung để trình duyệt tải menu và xử lý đăng xuất mới.

## Kết quả thực chạy

Chỉ kiểm thử ghi trên database riêng `RestaurantFoodManager_Integration_20261029`, `Auth:Enforce=true`.
Không ghi vào database `RestaurantFoodManager`; thông tin đăng nhập, key, script và log ngoài Git.

| Kiểm tra | Kết quả |
|---|---|
| Toàn bộ test frontend | **155/155 đạt**, không bỏ qua; gồm 27 ca catalog mới |
| API thật kết hợp `CatalogSession` | **15/15 đạt** |
| Quyền API | Admin/WarehouseManager được phép; Waiter/Chef/Cashier nhận 403 với nguyên liệu và công thức |
| API liên thông | Tạo nguyên liệu → công thức → nhận phiếu nhập → đặt món → bếp nhận/hoàn thành; tồn cuối đúng 2,25 kg và 0,85 kg |
| Phiên bản/lịch sử | PUT 204 sinh ID/version mới, bản cũ giữ định lượng; chặn sửa bản cũ/khôi phục xung đột |
| Mất ACK | Proxy bỏ phản hồi sau khi API thật đã tạo; reload/đối chiếu không gửi POST lần hai |
| Giao diện Admin | Thêm/sửa nguyên liệu, đơn vị chưa dùng sửa được/đã dùng bị khóa; đổi tình trạng, ngừng/khôi phục; tạo/sửa công thức, xem bản cũ, ngừng/khôi phục bản mới |
| Liên kết F4 | Nguyên liệu mới hiện trong dropdown Tạo phiếu nhập; link Danh mục nguyên liệu quay đúng trang |
| Responsive | Kiểm tra ở rộng CSS 390px, danh sách dạng thẻ và hai form không tràn ngang; menu dài xuống dòng |
| Console trình duyệt | Không có error/warn trong các thao tác catalog đã kiểm tra |

API fixture có tiền tố `F6 TEST`: nguyên liệu #7–#13, món #5, size #5/#6, công thức API #5/#6,
công thức giao diện #7/#8, phiếu #7 hủy/#8 đã nhận và đơn #12 Ready chưa thu tiền.
Giao diện giữ công thức #8 đang dùng, #7 ngừng dùng; chỉ dữ liệu kiểm thử được thêm trong lượt này.
Fixture không được dùng để kết luận dữ liệu/nghiệp vụ thật đã được nghiệm thu.

Bằng chứng cục bộ nằm ngoài repository tại `D:\Hoc_Tap\quanliduan1111\_codex_reports\20261101\`:
`f6-frontend-tests.tap`, `f6-live-checks.json`, `f6-static-checks.json`, `f6-*-desktop.png`, `f6-ingredients-mobile.png`.
Các kiểm tra cú pháp JS, đường dẫn tài nguyên/ID HTML và `git diff --check` được ghi trong static-checks.

## Giới hạn và việc tiếp theo

- Backend hiện không tự ngừng công thức khi ngừng nguyên liệu. Luồng tiêu hao tồn chưa kiểm tra
  `Ingredient.IsActive/StockStatus`, nên ngừng danh mục/đánh dấu Hỏng không phải khóa bán an toàn.
  Cần nhóm chốt quy tắc backend nếu muốn cơ chế đó; xem FE-C20 trong kế hoạch.
- API khôi phục công thức chỉ kiểm tra xung đột bản active, chưa kiểm tra size/nguyên liệu active.
  FE kiểm tra trước khi khôi phục; chưa thay thế kiểm tra bắt buộc tại server (FE-C19).
- `note` của route tình trạng nguyên liệu chưa được service lưu. UI không hỏi lý do rồi báo đã lưu lý do;
  nếu cần audit phải bổ sung backend. Đây không phải luồng ghi hao hụt Bếp.
- Chưa có `unitLocked`/usage metadata, phải đọc nhiều detail (FE-C17). Chưa có receipt và kiểm soát
  đồng thời nguyên tử cho catalog (FE-C18). Mất ACK cần người dùng đối chiếu, không tự retry.
- Chưa kiểm thử UI theo từng vai trò ngoài Admin; quyền các vai trò còn lại đã kiểm tra qua API thật.
  Chưa chạy lại bộ test C#: baseline F0 **518 đạt / 179 lỗi / 697 tổng** vẫn còn; không sửa backend trong F6.
- F6 còn nhân viên/vai trò/tài khoản, món/topping/size/category, khu vực bàn và khuyến mãi.
  Bước tiếp theo phù hợp là **nhân viên + vai trò**, rồi tài khoản; không coi phần này là hoàn tất toàn bộ F6.
