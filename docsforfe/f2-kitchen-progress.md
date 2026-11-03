# Bàn giao F2 — Màn Bếp

Ngày 2026-10-29. Làm trên `dong`, sau `dee2695`; chỉ sửa frontend và tài liệu.
Ghi chú này đi cùng thay đổi F2. Không sửa backend, migrations, SQL/appsettings hoặc dữ liệu nghiệp vụ hiện có.

## Các luồng đã nối

| Luồng | API / kết quả |
|---|---|
| Nhận riêng từng món | `PATCH /management/kitchen/items/{id}/status` với `Preparing`; món khác vẫn Pending. Không dùng nút nhận cả phiếu. POS khóa giảm/hủy đúng phần đã nhận. |
| Báo số phần vừa làm được | `PATCH .../{id}/prepared-quantity`, số nguyên 1..số phần của dòng hiện tại. Backend tách dòng Completed và dòng còn lại. Tiền/kho không đổi. |
| Thiếu/hỏng thực tế | `POST .../{id}/shortage`, số phần làm được 0..n-1, lý do bắt buộc và mã `OutOfStock`/`PhysicalShortage`. Phần thiếu bị hủy, ghi Wastage, không hoàn kho. |
| Ngừng bán món có nguyên liệu không đảm bảo | Checkbox trong báo thiếu gửi `deactivateDish=true` và lý do. Món biến mất khỏi menu API; nguyên liệu vẫn được giữ. POS thấy thay đổi ở lần tải thực đơn mới. |
| Hủy món | `PATCH .../{id}/status` với Cancelled, có lý do/mã. Chef xử lý Pending/Preparing; Ready chỉ Admin, Completed dùng luồng mở lại hoặc Admin xử lý tại POS. |
| Mở lại món bấm xong nhầm | `POST .../{id}/reopen`, bắt buộc nhập lý do trên UI. Chỉ khi món Ready/Completed, đơn chưa Paid/Cancelled, phiếu chưa Completed/Cancelled. Không PATCH lùi trạng thái. |

## Đồng bộ và hiển thị

- Mọi trạng thái đọc lại từ API sau thao tác, không tự gán trạng thái món/phiếu trong JS.
- Poll snapshot đầy đủ `GET /management/kitchen/orders?status=open` mỗi 5 giây; không dùng `since` để tránh giữ phiếu đã rời hàng đợi.
- Đọc items từng phiếu và chi tiết từng đơn; dùng chung một lần đọc đơn cho nhiều vòng bếp cùng đơn.
  Không gọi GET bàn theo id có ghi DB. Số bàn/tên phục vụ lấy từ metadata phiếu.
- Lý do hủy lấy từ `GET /orders/{id}`. Không gọi item-history bằng Chef vì route đó là PosOrCashier.
- Bộ lọc tìm bàn/món/phục vụ, mức ưu tiên và ba cột dùng dữ liệu thật; bỏ khu vực bếp giả và checkbox tự hoàn thành cũ.
- Có kết quả chỉ đọc cho phiếu vừa bị hủy/đóng, kể cả khi phiếu không còn trong danh sách mở.
- Tải lại trước khi ghi; nếu số phần/trạng thái khác lúc mở hộp thoại thì yêu cầu mở thao tác lại.
  403/409 hiển thị message và làm mới dữ liệu; điều kiện quyền cuối cùng do backend quyết định.

## Mất phản hồi khi ghi

Các route bếp hiện không có idempotency. Không tự gửi lại lệnh báo số phần/thiếu nguyên liệu:

- Lưu ngữ cảnh yêu cầu trong `sessionStorage.restaurant.kitchenPending`, khóa lệnh mới khi chưa xác nhận kết quả.
- Đã nhận ACK nhưng chưa GET được kết quả: nút tải lại chỉ đọc, không POST/PATCH lại.
- Chưa nhận ACK: tải phiếu để đối chiếu số phần/trạng thái, người dùng xác nhận đã đối chiếu rồi mới thao tác tiếp.
- Logout/401 xóa ngữ cảnh cùng các dữ liệu phiên. Không khẳng định frontend có thể đảm bảo exactly-once khi backend chưa hỗ trợ.

## Kiểm tra thực chạy

- **54/54 test frontend**: 18 F0, 18 POS, 18 Bếp; có kiểm tra ghi sai role, món khác giữ Pending,
  số lượng nguyên/giới hạn, phiếu/đơn đóng, stale snapshot, mất ACK, reload/đối chiếu, không ghi lại sau ACK, bấm đúp và receipt hủy.
- **11/11 kiểm tra với API SQL Server thật**, dùng chính `api.js`, `order-session.js`, `kitchen-session.js`:
  nhận từng món/POS khóa đúng món, Waiter bị 403 sau bếp nhận, tách số phần và giữ nguyên tiền/kho,
  mở lại không trừ kho lần nữa, shortage có Wastage/không hoàn, hủy có lý do, tất cả xong về Ready,
  chặn mở lại phiếu Completed, shortage toàn bộ có receipt Cancelled, ngừng bán và giữ nguyên liệu, kiểm quyền Chef/Waiter.
- **Trình duyệt Chef**: nhận món A và giữ B chưa nhận → báo xong 1/3 phần → mở lại phần đã xong → báo làm được 1/2 phần còn lại,
  phần thiếu hiện Cancelled và lý do. Không có lỗi JavaScript được ghi trong lần kiểm tra.
- Kiểm tra desktop và điện thoại 390 × 844: lọc bàn, chuyển cột, mở/đóng hộp thoại; trả viewport về mặc định sau kiểm tra.
- Cú pháp 27 JS, đường dẫn asset/ID của 15 HTML và `git diff --check` đạt.

Tất cả thao tác ghi kiểm thử chỉ vào **`RestaurantFoodManager_Integration_20261029`**, bàn/món/nguyên liệu gắn nhãn F2 TEST.
Giữ nguyên các phiếu khác. Không tạo thanh toán thật. Fixture, khóa đăng nhập, log và ảnh giữ ngoài Git trong workspace cha.
Không chạy lại bộ backend trong F2 vì không sửa backend; baseline tại F0 vẫn là 518 đạt / 179 lỗi đã có trên Hải.

## Giới hạn / trao đổi với Hải

- UI hiện báo thiếu cho cả công thức, chưa chọn `ingredientId` riêng: Chef không được đọc `/management/ingredients` hoặc recipes.
  Nếu cần chọn nguyên liệu, bổ sung lookup dành cho KitchenDesk; không tự mở quyền kho. Đã ghi FE-C12 trong work-plan.
- Cần idempotency/durable request receipt cho lệnh bếp nếu muốn tự retry an toàn; hiện dùng đối chiếu thủ công (FE-C13).
- Phần áp/gỡ khuyến mãi F1 và các màn F3–F8 vẫn là công việc riêng.
