# Bàn giao F5 — Tổng quan, báo cáo doanh thu và CSV

Cập nhật 2026-11-01, nhánh `dong`. Đã đọc AGENTS, contract, kế hoạch F5, payloads và API reference;
đối chiếu ReportsController, ReportService, VietnamTime và DTO thực tế.
Chỉ thay frontend và tài liệu; không sửa backend, migration, SQL/appsettings hay lấy source từ backup.

## Đã nối

- Tổng quan gọi `GET /reports/dashboard`: doanh thu/số đơn đã thu hôm nay, số đơn mở,
  tồn dưới ngưỡng, tình trạng bàn, phiếu bếp và tối đa 10 đơn đang mở mới nhất.
  Hiện ngày kinh doanh/mốc cập nhật theo giờ Việt Nam; có nút Làm mới tổng quan.
- Sơ đồ bàn đọc `GET /restaurant-tables`, không gọi `GET /restaurant-tables/{id}` vì route đó ghi DB.
  Bàn bảo trì/ngừng dùng không mở POS; các bàn còn lại liên kết đúng `tableId`.
  Đơn sẵn sàng có liên kết Thu ngân theo `orderId`; các nút lọc chỉ lọc danh sách gần nhất đang hiển thị.
- Bỏ file dữ liệu tĩnh `dashboard.data.js`. Khi tải lỗi không thay số liệu bằng 0 giả;
  lỗi sơ đồ bàn vẫn giữ phần tổng quan đã tải thành công và hiện thông báo để thử lại.
- Báo cáo gọi `GET /reports/revenue?period=day|week|month&date=yyyy-MM-dd`.
  Có kỳ/ngày, tổng doanh thu, số đơn, trung bình mỗi đơn, bảng nhóm và thanh biểu diễn mức doanh thu.
  Mọi số tiền/tổng/trung bình lấy nguyên từ server, không cộng hay chia lại trên frontend.
- Kỳ theo giờ Việt Nam: ngày có 24 khung giờ, tuần thứ Hai–Chủ nhật, tháng theo lịch.
  Gửi nguyên ngày chọn, không chuyển ngày query sang UTC. Kỳ hiển thị lấy `from` bao gồm / `to` loại trừ của server.
  Để trống ngày dùng mặc định server; sau khi tải, cố định ngày của kỳ để không trôi kỳ khi xuất qua nửa đêm.
- CSV đi qua `restaurantApi.download()` bằng Blob và JWT, đúng kỳ/ngày **đã tải thành công**.
  Đổi bộ lọc phải nhấn Xem báo cáo trước khi xuất; khóa bấm kép và đổi kỳ trong lúc đang tải/xuất.
  Tên file `doanh-thu-{day|week|month}-{ngày-bắt-đầu-kỳ-VN}.csv`.
  URL lưu kỳ/ngày để có thể mở lại báo cáo hoặc chia sẻ liên kết trong nhóm.
- Dashboard, báo cáo và CSV chỉ dành cho Admin theo policy backend.
  Vai trò khác ở trang chủ có lối vào nghiệp vụ tương ứng; mở thẳng Báo cáo hiện thiếu quyền và không gọi API báo cáo.

## Cách dùng

1. Đăng nhập Admin, vào **Tổng quan** để xem tình hình hiện tại; bấm **Làm mới tổng quan** sau khi nghiệp vụ đổi.
2. Mở **Quản trị → Báo cáo**, chọn Ngày/Tuần/Tháng và một ngày nằm trong kỳ cần xem.
3. Nhấn **Xem báo cáo**, kiểm tra khoảng kỳ hiển thị, rồi nhấn **Tải CSV**.
4. File CSV có UTF-8 BOM, dấu phẩy phân cột, dấu chấm thập phân và dòng cuối `TOTAL` từ server.
   Nếu Excel trên máy không tự tách cột, dùng nhập dữ liệu từ Text/CSV, chọn UTF-8 và dấu phân cách dấu phẩy.

Số 0 của ngày chưa thu tiền là kết quả hợp lệ, khác với lỗi tải dữ liệu có thông báo.
Ngày 31/10/2026 trong **database kiểm thử riêng** hiện có 120.000đ / 2 đơn / trung bình 60.000đ;
ngày 01/11/2026 tại thời điểm kiểm tra chưa có khoản đã thu. Đây là dữ liệu thử, không phải doanh thu thật của quán.

## Kiểm tra thực chạy

- **128/128 test frontend đạt**, không fail/skipped: 106 test cũ + 22 test ReportingSession.
  Có kiểm tra quyền, lỗi tải/partial failure, phản hồi cũ đến muộn, số tiền server,
  ngày không hợp lệ, biên UTC/VN, bộ lọc chưa áp dụng, mặc định ngày và khóa xuất CSV.
- **12/12 kiểm tra API SQL Server thật đạt** trên `RestaurantFoodManager_Integration_20261029`:
  Admin JWT; dashboard so SQL và danh sách bàn thuần đọc; doanh thu ngày/tuần/tháng so tổng, số đơn,
  trung bình trong SQL; loại khoản Refunded có sẵn; ngày không giao dịch; tháng 2 nhuận 29 ngày;
  mặc định ngày; 403 cho Waiter/Chef/Cashier/WarehouseManager ở cả ba route; thiếu JWT 401;
  period/ngày sai 400; lỗi sơ đồ bàn không làm mất dashboard.
- CSV ngày/tuần/tháng kiểm tra BOM, UTF-8, CRLF, header, từng bucket theo giờ VN,
  số liệu/dòng TOTAL khớp JSON và tên file đúng kỳ.
- Trình duyệt Admin: Tổng quan/sơ đồ/bộ lọc đơn; báo cáo ngày 24 nhóm, tuần 7 nhóm, tháng 31 nhóm;
  bộ lọc chưa áp dụng khóa CSV; tải file thật thành công vào Downloads.
  SHA256 file tải từ trình duyệt khớp CSV lấy qua helper API cùng kỳ.
  Phục vụ đăng nhập thấy lối vào POS, mở thẳng Báo cáo bị chặn; không có lỗi console ở các luồng đã thử.
- Breakpoint 390×844: Tổng quan và bộ lọc/thẻ báo cáo không tràn ngang toàn trang;
  bảng số liệu có vùng cuộn riêng. Đã trả viewport về mặc định sau kiểm tra.
- Cú pháp **31 JS**, ID/đường dẫn **15 HTML / 307 tài nguyên và liên kết**, selector hai màn và `git diff --check` đạt.
- F5 chỉ đọc dữ liệu nghiệp vụ có sẵn trong database kiểm thử, không tạo đơn/thu/hoàn tiền để kiểm tra.
  Log, CSV, ảnh và thông tin tài khoản kiểm thử nằm ngoài repository. Ghi chú local riêng được giữ nguyên.

## Giới hạn

- Doanh thu là tổng `Payment.Amount` có trạng thái **Paid**, có `PaidAt` trong kỳ;
  không phải tổng các đơn đang mở hay tổng phiếu nhập hàng. Refunded bị loại khỏi kỳ thu ban đầu.
- Thẻ **Nguyên liệu dưới ngưỡng** của dashboard chỉ đếm nguyên liệu active có lượng tồn ≤ ngưỡng.
  Khác danh sách **Cần kho xử lý** F4, vốn còn gồm cờ hỏng/hết dù tồn cao; không thay hai khái niệm cho nhau.
- CSV đọc dữ liệu mới tại lúc xuất, cùng kỳ nhưng không phải snapshot đóng băng của bảng vừa xem.
  Có thanh toán/hoàn tiền đồng thời thì số liệu có thể đổi; tải lại báo cáo để đối chiếu.
  Dashboard và sơ đồ bàn cũng là hai GET riêng; làm mới thủ công, chưa poll tự động.
- Chưa có API báo cáo giá vốn/lợi nhuận, món bán chạy, theo ca hay phương thức;
  không hiển thị lựa chọn giả cho các báo cáo này. Không xuất `.xlsx` và chưa mở kiểm tra bằng Excel desktop.
- Không chạy lại bộ test C# vì backend không đổi; baseline cũ **518/697 đạt, 179 lỗi có sẵn trên Hải** vẫn còn.

Các phase trước vẫn giữ giới hạn đã ghi trong bàn giao F1–F4. Việc tiếp theo thuộc F6;
có thể ưu tiên danh mục nguyên liệu/công thức để Kho/Nhập hàng có đủ dữ liệu chọn.
