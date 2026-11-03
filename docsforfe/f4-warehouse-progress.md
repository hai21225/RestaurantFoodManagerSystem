# Bàn giao F4 — Kho & Nhập hàng

Cập nhật 2026-10-31, nhánh `dong`. Đọc hướng dẫn `AGENTS.md`, contract, mục F4/các phụ lục C-17/C-21,
payloads và API reference; đối chiếu ManagementController, InventoryService, PurchaseOrderService, SupplierService, DTO.
Không sửa C#, migrations, SQL/appsettings hoặc khôi phục source từ backup.

## Đã nối

- WarehouseManager/Admin được truy cập hai màn Kho/Nhập hàng. Dùng JWT/401/403 và lớp HTTP chung `restaurantApi`.
- Kho hiển thị tên/đơn vị/trạng thái/ngưỡng từ nguyên liệu và lượng `quanlity`/mốc cập nhật từ tồn kho.
  Danh sách **Cần kho xử lý** đọc trực tiếp `/management/inventory/low-stock`;
  giữ `Spoiled`/`OutOfStock` dù tồn ghi sổ còn nhiều, không tự suy thành “sắp hết”.
  Thẻ Hết theo sổ/Báo hỏng đếm từ danh sách cần xử lý của nguyên liệu đang hoạt động.
- Tìm/lọc tồn; điều chỉnh phần tăng hoặc giảm, tối đa 3 số lẻ và ±1.000.000, lý do bắt buộc 1–500 ký tự.
  Tải lại tồn trước lệnh, chặn snapshot đổi và giảm quá tồn. Không giả cộng/trừ trên state để báo thành công.
- Lịch sử kho có tên, đơn vị, lượng trước/sau, nguồn/mã chứng từ, lý do và người tạo; lọc và phân trang server.
  Mã chứng từ bắt buộc kèm Order/PurchaseOrder; ManualAdjustment không có mã chứng từ.
  Ngày giao diện là ngày Việt Nam, đổi sang mốc ISO UTC đủ cả ngày vì service lọc CreatedAt UTC.
- Nhập hàng: tìm theo mã/NCC, lọc Draft/Received/Cancelled và tháng tạo theo giờ Việt Nam; xem chi tiết từng dòng.
  Giữ bố cục form chọn NCC/thêm nhiều nguyên liệu và thêm NCC ngay trong phiếu.
- Tạo Draft chỉ gửi NCC/actor/dòng hàng; chỉ chọn NCC và nguyên liệu đang hoạt động, nguyên liệu không trùng,
  số lượng ít nhất 0,001 và tối đa 3 số lẻ, đơn giá không âm/tối đa 2 số lẻ.
  Không tính tổng tiền ở FE: form báo tổng sẽ chốt khi lưu, chi tiết hiện `totalPrice`/`totalAmount` server trả.
  Bỏ thẻ “Tổng chi tháng” tự cộng vì danh sách phiếu chưa phải sổ thanh toán nhà cung cấp.
- Nhận/hủy có hộp xác nhận; tải lại chi tiết và so snapshot trước khi gửi. Chỉ thao tác Draft.
  Nhận trả Received và ghi kho; hủy trả Cancelled, không cộng kho. Nút bị khóa khi phiếu đã kết thúc.
  Bỏ trạng thái/bước “duyệt” không có trong API. Phiếu Received có liên kết sang đúng biến động kho của phiếu.
- NCC: thêm, sửa, ngừng hợp tác bằng soft delete và khôi phục bằng PATCH active. Phiếu cũ vẫn giữ NCC/lịch sử.
  Địa chỉ tối đa 500 ký tự khớp DTO; trước khi sửa/ngừng/khôi phục, đọc lại và so snapshot tránh ghi đè dữ liệu vừa thấy đổi.

## Mất phản hồi / bấm trùng

- `warehouse-session.js` dùng chung hai màn, lưu `restaurant.warehousePending` trước mọi lệnh ghi.
  Khóa bấm kép và lệnh tiếp theo; chuyển Kho ↔ Nhập hàng vẫn giữ yêu cầu chưa rõ kết quả trong cùng tab.
- **Không tự retry** điều chỉnh, tạo phiếu hay tạo NCC: backend chưa hỗ trợ Idempotency-Key cho các route này.
  Điều chỉnh đã commit nhưng mất ACK không thể được chứng minh chỉ bằng số tồn mới hoặc ghi chú giống nhau.
- Có ACK rồi GET lỗi: tải lại để đối chiếu, không POST lại. Nhận/hủy mất ACK được đọc theo id/status phiếu.
  Tạo phiếu/NCC mất ACK mà chưa biết id: người dùng đối chiếu danh sách và xác nhận thủ công; không ghép đoán bản ghi giống nhau.
- Yêu cầu điều chỉnh được đối chiếu trên màn Kho, các yêu cầu phiếu/NCC trên màn Nhập hàng. Có liên kết chuyển đúng màn.
  Logout/401/đổi phiên xóa pending; đóng tab có thể mất ngữ cảnh, cần đối chiếu chứng từ trước khi tạo lại.

## Kiểm tra thực chạy

- **106/106 test frontend đạt**: 80 test các phase trước + 26 test WarehouseSession; không fail/skipped.
- **14/14 kiểm tra API SQL Server thật đạt**: quyền WarehouseManager và 403 các role khác;
  low-stock giữ Spoiled tồn cao; thiếu lý do/âm kho; delta số lẻ và ledger;
  CRUD/soft delete/restore NCC; Draft không cộng kho; tổng tiền dòng server làm tròn;
  receive có đơn vị/NCC/mã phiếu; chặn receive lần hai/hủy Received;
  cancel không đổi kho; NCC inactive giữ lịch sử; lọc nguồn/ngày VN/phân trang;
  chặn tồn vừa đổi; mất ACK sau commit điều chỉnh và receive không ghi lần hai.
- Trình duyệt bằng WarehouseManager: nhận phiếu #4, mở đúng ledger của phiếu, điều chỉnh −0,125 có lý do;
  thêm NCC #2 ngay trong phiếu, chặn dòng trùng, tạo phiếu #5 tổng 9.000,28đ từ server rồi hủy;
  sửa/ngừng/khôi phục NCC và kiểm tra phiếu cũ vẫn còn. Không có lỗi JS ở các luồng đã thử.
- Kiểm tra breakpoint 390×844: form nhập hàng và bộ lọc Kho dùng được, không tràn ngang toàn trang;
  liên kết `?ingredientId=` phản ánh đúng lựa chọn trên bộ lọc. Đã trả viewport về mặc định sau kiểm tra.
- Cú pháp 30 JS, ID/đường dẫn 15 HTML với 302 tài nguyên/liên kết đều đạt; `git diff --check` đạt.
- Chỉ ghi dữ liệu giả **F4 TEST** vào `RestaurantFoodManager_Integration_20261029`;
  không ghi database nghiệp vụ. Tài khoản thử, fixture, log, ảnh nằm ngoài Git.

## Giới hạn và điểm bàn giao backend

- F4 chưa làm CRUD danh mục nguyên liệu/công thức (F6). Kho đọc cờ Available/OutOfStock/Spoiled;
  nhận hàng/điều chỉnh **không** tự xóa cờ hỏng/hết và không tự bật lại món bị ngừng bán.
- Các route ghi Kho/tạo phiếu/tạo NCC cần idempotency/receipt bền vững nếu muốn tự retry an toàn (FE-C16).
  Guard so snapshot ở FE không thay cho điều kiện cập nhật nguyên tử/ETag khi nhiều máy cùng sửa.
- Hợp đồng thực tế: âm kho bị `AdjustInventory` trả **400** (khác ghi chú cũ nói 409), xung đột ghi trả 409;
  receive lại phiếu Received trả **404**, cancel Received/receive Cancelled trả **409**.
- Chưa có API duyệt phiếu, nhập một phần, trả hàng sau nhận hay thanh toán công nợ NCC;
  F4 không dựng trạng thái/luồng giả cho các nghiệp vụ đó.
- Không chạy lại bộ test C# vì backend không đổi; baseline cũ 518/697 đạt, 179 lỗi có sẵn trên Hải vẫn còn.
  F5 báo cáo/dashboard và F1 áp/gỡ khuyến mãi vẫn theo work-plan.
