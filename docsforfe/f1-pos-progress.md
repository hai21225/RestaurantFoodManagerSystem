# Bàn giao POS — các luồng chính của F1

Ngày 2026-10-29, thực hiện trực tiếp trên `dong`, sau `bf1131e` (F0).
Chỉ sửa frontend và tài liệu. Không sửa backend, migrations, cấu hình SQL hay dữ liệu nghiệp vụ hiện có.
Commit POS: `3827af9`. Nhánh nhận bàn giao: [origin/dong](https://github.com/hai21225/RestaurantFoodManagerSystem/tree/dong).

## Đã nối API

| Chức năng | Hành vi |
|---|---|
| Mở đơn theo bàn | Đọc danh sách bàn và các trang `GET /orders?tableId=...`; bỏ đơn Paid/Cancelled, giữ Completed là đơn còn phục vụ. Nếu có nhiều đơn mở thì chặn để Admin kiểm tra. |
| Gửi bếp / gọi thêm | Bàn trống dùng `POST /orders`; bàn có đơn dùng `POST /orders/{id}/items`, không thay toàn bộ món cũ. Giỏ món chưa gửi tách khỏi phần đã gửi. |
| Đổi số lượng | `PATCH /orders/{id}/items/{itemId}/quantity`; số lượng là số nguyên, áp cho tổng phần còn phục vụ trong cùng lượt gọi (`orderItemGroupId`), kể cả khi bếp đã tách dòng. |
| Hủy từng dòng món | `POST /orders/{id}/items/{itemId}/cancel`, bắt buộc lý do, hiển thị lại lý do máy chủ trả về. |
| Hủy toàn bộ đơn | Nút riêng cho Admin, `PATCH /orders/{id}/status`, có lý do và mã quyết định hoàn kho/hao hụt. |
| Lịch sử | Đọc cả `/history` và `/item-history`, hiển thị người thực hiện, thời gian, trạng thái và lý do. |

Đã bổ sung lựa chọn cỡ bất kỳ từ menu thật, topping được gán cho món, ghi chú từng món và ghi chú hóa đơn khi tạo đơn.
Không dùng dữ liệu danh mục giả hoặc trường bán theo kg của bản cũ: API Hải hiện nhận `quantity` nguyên.
Giỏ chưa gửi không tự tính tiền. Tiền hàng/giảm giá/phí/thuế/tổng chỉ hiển thị từ chi tiết đơn do server trả.

## Khóa theo nghiệp vụ

- Waiter chỉ hủy dòng Pending, và chỉ giảm lượt gọi khi mọi phần còn hoạt động đều Pending.
  Món Preparing/Ready/Completed vẫn gọi tăng được; muốn giảm/hủy phần đã nhận thì cần Admin.
- Admin có thể xử lý món đã nhận. Giảm xuống dưới số phần đã xong cần xác nhận `allowWastage`.
  API quyết định kho; frontend không cộng/trừ tồn hay tự đổi trạng thái.
- Cashier chỉ xem POS; Chef dùng màn bếp. Controller/backend vẫn là nơi quyết định quyền cuối cùng.
- Đọc trạng thái lại trước khi mở hộp thoại; 403/409 giữ thông báo backend và tải lại đơn.
  Tự làm mới 5 giây khi trang đang mở, không cắt ngang nhập liệu hoặc hộp thoại; không vẽ lại dòng món khi dữ liệu không đổi.
- Đơn vừa Paid/Cancelled vẫn được giữ để xem kết quả; cần chọn lại bàn để mở lượt mới, tránh gửi giỏ cũ vào một đơn mới ngoài ý muốn.
- Xem bàn dùng `GET /restaurant-tables`, không gọi GET theo id vì route đó có ghi DB.

## Gửi lại khi mất kết nối

- Khóa các nút ghi khi đang gửi. Các lệnh tạo/gọi thêm/đổi số lượng giữ cùng body và `Idempotency-Key` khi thử lại.
- Lưu yêu cầu chưa xác nhận ở `sessionStorage.restaurant.posPending`; tải lại trang vẫn có thể thử xác nhận đúng yêu cầu cũ.
  Nếu đã nhận ACK nhưng GET chi tiết lỗi thì lần thử lại chỉ đọc, không gửi món thêm lần nữa.
- Lệnh hủy không có idempotency của backend: đọc trạng thái trước khi thử lại, không hủy lại dòng/đơn đã Cancelled.
- Logout xóa ngữ cảnh yêu cầu. Khi đang có yêu cầu chưa xác nhận, chặn lệnh mới; chỉ thử lại hoặc quay về đúng bàn đó.
- **Giới hạn backend hiện tại:** idempotency lưu trong RAM, TTL 24 giờ, mất khi tiến trình backend khởi động lại.
  Frontend giữ key không tạo ra bảo đảm chống trùng xuyên lần khởi động backend. Yêu cầu cũ sau restart/TTL cần đối chiếu đơn/lịch sử trước khi thử lại.

## Kiểm tra đã chạy

- **36/36 test frontend**: 18 test F0 và 18 test POS, chạy bằng Node built-in test runner.
  Bao gồm phân trang qua hơn 100 đơn cũ, nhóm món tách dòng, đúng route/payload, 403/409,
  mất ACK/reload/retry, chặn bấm đúp, đơn đã kết thúc và đổi bàn sau khi nhận ACK.
- **9/9 kiểm tra API thật** dùng chính `api.js` + `order-session.js` với JWT Waiter/Admin/Chef trên SQL Server:
  tạo đơn, giảm/tăng Pending, gọi thêm cùng đơn và ticket mới, hủy Pending, bếp nhận đúng lúc POS đang sửa (403),
  Admin hủy Preparing không hoàn kho, Admin hủy cả đơn Pending có hoàn kho, sửa tổng nhóm sau khi bếp tách phần đã làm.
- **Trình duyệt**: đăng nhập Waiter → bàn trống → thêm 2 phần → gửi đơn → giảm còn 1 → gọi thêm vào cùng mã đơn
  → hủy dòng gọi thêm với lý do → xem lịch sử. Sau Chef nhận món còn lại, nút hủy ẩn và thông báo khóa hiện đúng.
- Kiểm tra desktop và breakpoint điện thoại 390 × 844, mở/đóng giỏ điện thoại.
- Cú pháp JavaScript, ID/đường dẫn asset và `git diff --check` đạt.

Dữ liệu thật được giữ nguyên. Toàn bộ kiểm tra ghi chỉ vào database riêng **`RestaurantFoodManager_Integration_20261029`**,
các bàn/món/nguyên liệu đều gắn nhãn `F1 TEST`. Không thử thanh toán tiền thật hay MoMo.
Log, ảnh và script tạo fixture nằm ngoài repo trong `_codex_reports/20261029` và `_codex_runtime` của workspace cha.
Baseline backend tại F0: 518 đạt / 179 lỗi, đã có trên Hải; không chạy lại bộ backend trong F1 vì không sửa backend.

## Còn lại

- **F1-5 áp/gỡ khuyến mãi** chưa nối giao diện. Danh sách promotions hiện là AdminOnly; cần thống nhất cách phục vụ chọn khuyến mãi
  trước khi làm UI cho Waiter. Route áp/gỡ đã có, không cần tự tính giảm giá trên frontend.
- Sửa ghi chú hóa đơn sau tạo, chuyển bàn thuộc F7; xác nhận/hao hụt từng món ở màn bếp thuộc F2, thu ngân thuộc F3.
- Không khẳng định toàn bộ F1–F8 hay bộ test backend đã hoàn tất.
