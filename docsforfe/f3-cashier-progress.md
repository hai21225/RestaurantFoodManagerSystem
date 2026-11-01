# Bàn giao F3 — Thu ngân

Cập nhật 2026-10-31, làm trên nhánh `dong`. Đã fetch và kiểm tra `origin/hai` (`dd258d2`) nằm trong lịch sử của `dong`.
Đọc `AGENTS.md`, `api-contract.md`, mục F3 và các phụ lục thanh toán/bill trong work-plan, payloads, API reference;
đối chiếu controller, DTO, PaymentService, trạng thái và idempotency store thực tế. Nhánh Hải không có file `SKILL.md` riêng.
Không sửa backend, migrations, SQL/appsettings hoặc source từ backup.

## Chức năng đã nối

- Chỉ Cashier/Admin vào màn Thu ngân. JWT/401/403 vẫn đi qua auth và `window.restaurantApi` chung.
- Danh sách đơn đang mở: `Ready`, `Completed`, `Pending`, `SentToKitchen`, `Preparing`; đọc hết trang API,
  tìm theo bàn/mã đơn và lọc đơn đã xong/chờ bếp. Chọn đơn qua `?orderId=` hoặc `?tableId=`.
- Đọc chi tiết món, topping, ghi chú, món hủy/lý do; hiển thị trực tiếp `subtotal`, `discountAmount`,
  `taxAmount`, `serviceChargeAmount`, `totalAmount`. Không tự cộng tiền, tính VAT hay cộng topping lần nữa.
  Order detail chưa có lineTotal nên phiếu đơn hiện đơn giá/số lượng, không tự nhân thành tiền dòng.
- Thu trực tiếp: Cash, Card, Bank, EWallet, Transfer. Đây là ghi nhận khoản đã nhận/đã đối chiếu bên ngoài;
  không tích hợp cổng thẻ/ngân hàng. Hộp xác nhận hiện đúng đơn, phương thức và số tiền vừa xem.
- Trước khi ghi, tải lại chi tiết/phiếu thu, so snapshot và chặn đơn chưa Ready/Completed, món chưa xong,
  đơn 0đ/không còn món, đã Paid/Cancelled hoặc có giao dịch Pending. Sau khi ghi luôn đọc trạng thái server.
- Lịch sử phiếu của đơn và danh sách phiếu có phân trang, lọc trạng thái/phương thức/mã đơn/ngày.
  Bộ lọc ngày dùng đủ ngày Việt Nam, chuyển sang UTC vì PaymentService thực tế lọc PaidAt UTC;
  phiếu Pending/Failed không có PaidAt sẽ không xuất hiện khi lọc ngày thu.
- Thu xong tải lại bàn từ `GET /restaurant-tables` (không gọi GET bàn/{id} có tác dụng ghi),
  lịch sử và queue. Đơn Paid rời queue nhưng chi tiết/phiếu thu vẫn xem được.
- Hoàn toàn bộ với lý do 1–500 ký tự. Server trả Refunded, đơn Ready, bàn Occupied. UI không tự đặt trạng thái.
  Đọc lại đơn đang mở của cùng bàn trước khi hoàn; có lượt khách mới thì chặn, tránh mở chồng đơn cũ (FE-C14).
- Xem trước phiếu đơn/tạm tính bàn, CSS in riêng và nút `window.print()` cho trình duyệt hỗ trợ in/lưu PDF.
  Tạm tính bàn dùng `/orders/table/{tableId}/bill`, không cộng thủ công và không thu gộp nhiều đơn một lần.
  Bỏ dữ liệu mẫu payment.data.js và ô ghi chú thanh toán không có API lưu cho Cashier.

## MoMo

- Tạo/lấy lại intent: `POST /payments/momo`; đọc kết quả qua `/payments/order/{orderId}` khoảng 3 giây/lần.
  Tạo intent vẫn Pending, đơn chưa đóng. Đơn/queue thông thường làm mới khoảng 10 giây khi trang hiện.
- Chỉ hiện nút giả lập khi response `isMock=true`, nhãn rõ không có tiền thật/QR thật.
  Thành công/thất bại đi qua `/payments/momo/mock/{paymentId}?success=...`, sau đó GET lại kết quả.
- Pending chặn thu trực tiếp. Số tiền intent khác đơn mới nhất thì chặn giả lập thành công.
  Không tự hủy intent, không tự đổi phương thức trong khi provider còn chờ.
- Có render QR/link HTTP(S) của provider nếu backend trả về; **chưa kiểm thử MoMo thật** vì môi trường hiện là Mock.
  FE không gọi IPN `/payments/momo/notify`, không chứa khóa provider.

## Chống ghi trùng / mất phản hồi

- Mỗi khoản thu trực tiếp dùng một `Idempotency-Key`; mã người dùng nhập được giữ nguyên.
  Nếu bỏ trống, FE tạo `FE-<UUID>` làm transactionCode ổn định để tìm được phiếu sau mất ACK/restart cache.
  Backend vẫn kiểm tra unique mã và quyết định toàn bộ số tiền. Client khác bỏ trống vẫn được server sinh mã PAY như cũ.
- Lưu `restaurant.paymentPending` trong sessionStorage trước lệnh, khóa lệnh mới và bấm kép.
  Không tự retry thu/hoàn/giả lập. Có ACK mà GET lỗi thì đối chiếu chỉ đọc, không POST lại.
- Mất ACK thu trực tiếp: tìm phiếu bằng mã giao dịch, kể cả phiếu đã Refunded. Mất ACK hoàn: tìm paymentId Refunded.
  Chưa xác định được thì tải lịch sử và yêu cầu người dùng xác nhận đã đối chiếu trước khi tạo lệnh mới.
- Metadata MoMo đang xem lưu trong `restaurant.paymentMomo`; logout/401/đổi phiên xóa cả hai key.
  Đóng phiên trình duyệt có thể mất ngữ cảnh: cần đối chiếu lịch sử thu trước khi thao tác lại.
  Idempotency backend là RAM TTL 24h; không mô tả đây là bảo đảm exactly-once bền vững/multi-instance.

## Kiểm tra đã thực chạy

- 80/80 test Node: 18 auth/API + 18 POS + 18 Bếp + 26 Thu ngân. Không fail/skipped.
- 13/13 kiểm tra API SQL Server thật: Cashier JWT và quyền; chặn thu khi chờ bếp; món hoàn tất/thiếu bị hủy;
  tiền mặt và trả bàn; cùng key không tạo phiếu mới; lọc ngày VN; hoàn/mở lại; trùng mã;
  MoMo Mock Pending/reuse/Failed/Paid/refund; bốn phương thức trực tiếp còn lại;
  mất ACK sau commit SQL chỉ đối chiếu; chặn hoàn đơn cũ khi bàn có đơn mới.
- Trình duyệt với Cashier: thu tiền mặt, phiếu và bàn Trống; hoàn tiền bắt buộc lý do, bàn Có khách;
  MoMo Mock Pending và thất bại; tìm/lọc theo đơn; xem trước phiếu. Kiểm tra bố cục desktop và breakpoint điện thoại,
  hộp xác nhận vừa màn hình, không tràn ngang toàn trang. Không có lỗi JS trong console ở các luồng đã thử.
- Chỉ ghi dữ liệu giả gắn nhãn F3 TEST vào `RestaurantFoodManager_Integration_20261029`.
  Không thu/hoàn tiền thật; không dùng database nghiệp vụ. Fixture, tài khoản test, log và ảnh nằm ngoài repository.

## Giới hạn / việc tiếp theo

- Chưa xác minh in giấy hoặc file PDF: bản xem trước đã kiểm tra, nhưng thao tác in trong trình duyệt tích hợp
  timeout công cụ và không có bằng chứng file/máy in. Cần thử In/Lưu PDF trên Chrome/Edge có hộp thoại in.
- Hoàn tiền và mở lại đơn cũ cần BE bảo đảm nguyên tử với việc tạo đơn mới trên cùng bàn (FE-C14).
  Guard FE giảm rủi ro theo snapshot, không thay được kiểm tra/lock server khi hai máy thao tác đồng thời.
- Backend chưa có lệnh hủy intent MoMo để đổi phương thức an toàn; không tự thu tiền mặt khi còn Pending (FE-C15).
- Chưa gọi MoMo provider thật. Chia/gộp bill, ca/két, tên/MST cấu hình trên phiếu là phần riêng.
- Không chạy lại bộ backend trong F3 vì không sửa C#. Baseline cũ vẫn 518/697 đạt, 179 lỗi có sẵn trên Hải;
  không coi các số test cũ 100/100 hoặc 670/670 trong tài liệu là kết quả kiểm thử lại của phiên này.
- Áp/gỡ khuyến mãi F1 và các màn F4–F8 còn lại tiếp tục theo work-plan.

## Cập nhật 2026-11-01 — bố cục Thanh toán trên điện thoại

- Người dùng báo bộ lọc phiếu thu bị dồn chữ, bảng khó đọc trên điện thoại.
  Bản tải mới trong trình duyệt đã nhận CSS; ảnh người dùng có nhiều thành phần mang kiểu mặc định.
  Thêm phiên bản CSS/JS trong payment.html để trình duyệt tải đồng bộ bản mới.
- Bộ lọc thành một cột ở màn hình tối đa 480 px; ô nhập/select và nút lọc cao ít nhất 44 px trên mobile,
  font ô nhập 16 px. Phân trang có dòng trạng thái riêng và hai nút nằm bên dưới.
- Dưới 700 px, mỗi phiếu là một thẻ có nhãn, số tiền/mã giao dịch/thời điểm thu nằm trên dòng đủ rộng.
  Giữ một bảng dữ liệu và ngữ nghĩa table/row/cell; desktop vẫn hiển thị bảng.
  Khi không có dữ liệu chỉ hiện thông báo rỗng, không ép bảy tiêu đề vào màn hình hẹp.
- Đã kiểm tra trình duyệt ở 320/390/768/1280 px: không tràn ngang toàn trang, các ô lọc nằm trong màn hình.
  Thử lọc Đã thu + Tiền mặt trên runtime thật; không tạo khoản thu/hoàn tiền hay sửa dữ liệu nghiệp vụ.
- Kiểm tra mã giao dịch dài và số tiền lớn ở 320 px bằng bản xem thử tĩnh ngoài Git,
  sử dụng markup từ renderLedger hiện tại; nội dung nằm gọn trong thẻ. Đây là kiểm tra bố cục với dữ liệu mẫu,
  không phải khoản thu trong DB gốc hay bằng chứng thanh toán thực tế.
- Kiểm tra cú pháp payment.js và **26/26 test payment-session đạt**; console trình duyệt không có lỗi JS.
  Ảnh kiểm chứng nằm ngoài Git tại `_codex_reports/20261101/payment-mobile-fixed-390.png`
  và `payment-mobile-long-code-320.png`.
- Lỗi đọc đơn trên database gốc **FE-C23 vẫn chưa xử lý**; xem
  [ghi chú database gốc](original-database-and-delete-progress.md#fe-c23--lỗi-đọc-đơn-và-phiếu-bếp-trên-database-gốc).
  Bản sửa này chỉ thay đổi giao diện frontend, không thay đổi backend hoặc database.
