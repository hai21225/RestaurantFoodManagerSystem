# Kế hoạch Nâng cấp & Đồng bộ Frontend — V2 (`frontend/`)

> **Phạm vi tuyệt đối:** Chỉ chỉnh sửa/bổ sung trong **`frontend/`**. **Không sửa Backend** (`RestaurantFoodManagerSystem/` đã hoàn tất và pass 715/715 test).  
> **Ràng buộc giao diện:** Tuân thủ **100% theme Sahara hiện tại** (`frontend/SAHARA_UI_HANDOFF.md`, `css/base.css`, `css/layout.css`, `css/components.css`, `css/pages/service-workspace.css`, `css/pages/management-sahara.css`), font local **Manrope** + **EB Garamond**, nền kem, chữ nâu đậm, nút chính cam đất `#a94f1c`, vùng chạm tối thiểu 44px, không dùng CSS framework ngoài.  
> **Trạng thái:** Bản thiết kế & kế hoạch thực thi (chưa sửa code).

---

## Phần 1: Các lỗi & Điểm cần sửa ở Frontend hiện tại

### 1.1. Ẩn nút "Đặt món" khơi khơi trên thanh điều hướng
- **Hiện trạng:** Mọi trang HTML (`index.html` và `pages/*.html`) đều có `<button class="nav-item" data-page="...order.html">Đặt món</button>` hiển thị thường trực trên sidebar và mobile bottom-nav (`js/common/navigation.js`).
- **Yêu cầu V2:**
  - **Ẩn hoàn toàn nút `Đặt món` khỏi thanh điều hướng** (ẩn trong `navigation.js` + loại khỏi nhóm menu chính, kể cả trên mobile).
  - Người dùng chỉ vào được màn hình gọi món khi **chọn bàn tại `pages/tables.html` -> bấm "Vào bàn / Đặt món"** (chuyển sang `pages/order.html?tableId=...`), hoặc đi từ liên kết đơn trên Dashboard / `order-tools.html`, hoặc khách quét mã QR tại bàn (`?qrToken=...`).
  - Nếu mở trực tiếp `order.html` không có `tableId` và không có `qrToken`: không hiển thị form đặt món, chỉ hiện thông báo hướng dẫn quay về `tables.html` (đối với nhân viên) hoặc màn hình quét QR (đối với `Customer`).

### 1.2. Fix các lỗi từ bản review V1
1. **Lỗi kẹt ở `index.html` khi chưa đăng nhập (`js/common/api.js`):**
   - Khi `!bootstrapToken` trên trang private (`!auth.isPublicPage()`), `auth.ready` hiện trả về `Promise.resolve(null)` mà không gọi `auth.redirectToLogin()`, khiến `index.html` dừng ở `#dashboardAccess` trống trơn.
   - **Fix:** Nếu `!auth.isPublicPage() && !bootstrapToken`, gọi `auth.redirectToLogin()` ngay.
2. **Danh sách route Public trong `api.js`:**
   - Cập nhật hàm `anonymous(path, method)` để cho phép gọi không cần token tới các endpoint Public V2: `/auth/customer/register`, `/menu`, `/foods`, `/categories`, `/public/table-areas`, `/public/tables`, `/public/promotions`, `/reservations/deposit/momo/notify`.
3. **Dọn code chết:**
   - Xóa 3 file mock data không còn dùng: `js/data/kitchen.data.js`, `js/data/order.data.js`, `js/data/payment.data.js`.

---

## Phần 2: Tích hợp đầy đủ các tính năng Backend V2 vào Frontend

### 2.1. Cổng Public (Chưa đăng nhập) & Đăng ký Khách hàng (`Customer`)
1. **Trang Thực đơn & Xem bàn công khai (`pages/public-menu.html` + `js/pages/public-menu.js`):**
   - Cho phép khách vãng lai (chưa đăng nhập) xem:
     - Thực đơn theo danh mục (`GET /api/menu` hoặc `GET /api/categories` + `GET /api/foods`), tìm kiếm không dấu.
     - Khuyến mãi đang áp dụng (`GET /api/public/promotions`).
     - Tình trạng khu vực & bàn trống để tham khảo trước (`GET /api/public/table-areas`, `GET /api/public/tables`).
   - Có nút **"Đăng nhập"** và **"Đăng ký đặt bàn"** nổi bật (theo theme Sahara); nhấn **"Đặt bàn"** khi chưa đăng nhập sẽ điều hướng sang `login.html` / `register.html` kèm `returnUrl`.
   - Thêm liên kết *"Xem thực đơn & bàn trống"* và *"Đăng ký tài khoản khách hàng"* ngay trên `pages/login.html`.
2. **Trang Đăng ký Khách hàng (`pages/register.html` + `js/pages/register.js`):**
   - Form theo layout `login.html` (Sahara card): `fullName`, `userName`, `email`, `phone`, `password`, `confirmPassword`.
   - Gọi `POST /api/auth/customer/register`, nhận JWT + thông tin user role `Customer`, lưu session qua `auth.setSession(data)` và chuyển sang `pages/reservations.html`.
3. **Cập nhật Phân quyền & Điều hướng (`js/common/auth.js`, `js/common/navigation.js`):**
   - Thêm role `Customer` (`Khách hàng`) vào `pageRoles` và `roleNames`.
   - Trang chủ mặc định của `Customer` (`getHomeUrl()`): `pages/reservations.html`.
   - `Customer` chỉ thấy các mục của mình: **Đặt bàn (`pages/reservations.html`)**, **Gọi món QR / Đơn của tôi (`pages/qr-order.html` hoặc `order.html?qrToken=...`)**, **Thực đơn (`pages/public-menu.html`)**, **Tài khoản (`pages/account.html`)**. Ẩn hoàn toàn các mục nội bộ (`tables`, `kitchen`, `payment`, `inventory`, `employees`, `reports`, `administration`).

### 2.2. Đặt bàn trước + Thanh toán cọc & Gọi món QR tại bàn
1. **Trang Đặt bàn (`pages/reservations.html` + `js/pages/reservations.js`):**
   - **Giao diện cho `Customer`:**
     - Chọn khu vực/bàn, ngày giờ (`ReservedFrom`, `ReservedTo`), số khách (`PartySize <= Capacity`), ghi chú, số tiền cọc (`DepositAmount`).
     - Danh sách **"Lịch đặt bàn của tôi"** (`GET /api/reservations/my`): hiển thị trạng thái (`Pending`, `Confirmed`, `Seated`, `Cancelled`, `NoShow`, `Completed`) và trạng thái cọc (`None`, `Pending`, `Paid`, `AppliedToOrder`, `Refunded`, `Forfeited`).
     - Hỗ trợ thanh toán cọc qua MoMo (`POST /api/reservations/{id}/deposit/momo` + mở link/QR + polling/nút mô phỏng khi `isMock`, giống cơ chế ở `payment.js`) và nút **Hủy đặt bàn** (`POST /api/reservations/{id}/cancel`).
     - **Tuyệt đối không** cho `Customer` đặt món từ trang này (chống khách phá / bom đơn từ xa).
   - **Giao diện cho `Admin` / `Waiter` / `Cashier` (thêm tab/mục "Đặt bàn" trên thanh điều hướng cho `Admin`/`Waiter`):**
     - Xem toàn bộ lịch đặt (`GET /api/reservations` lọc theo ngày, trạng thái, bàn).
     - Các nút nghiệp vụ: **Xác nhận (`confirm`)**, **Nhận bàn (`seat`)**, **Khách không đến (`no-show`)**; `Cashier`/`Admin` có nút **Xác nhận đã thu cọc (`deposit/confirm`)**.
2. **Trang Khách quét QR gọi món tại bàn (`pages/qr-order.html` + `js/pages/qr-order.js`):**
   - Đọc `qrToken` từ URL (`?qrToken=...`) hoặc cho phép nhập/dán mã QR bàn khi đã ngồi tại quán.
   - Nếu thiếu `qrToken`: chặn hiển thị nút gửi bếp và thông báo rõ *"Vui lòng quét mã QR đặt tại bàn để gọi món"*.
   - Khi có `qrToken` hợp lệ: hiển thị thực đơn (tái sử dụng cấu trúc menu Sahara), cho phép gọi món (`POST /api/orders` kèm `tableQrToken`), gọi thêm món (`POST /api/orders/{id}/items` kèm `tableQrToken`), và theo dõi trạng thái từng món của mình (`GET /api/orders/my`, `GET /api/orders/{id}`).

### 2.3. Nâng cấp các màn hình Nội bộ hiện có
1. **Admin Quản lý Bàn & QR (`pages/administration.html` + `js/pages/administration.js`):**
   - Trong tab `tables`:
     - Hiển thị `QrToken` / đường dẫn QR của từng bàn (`qr-order.html?qrToken=...`), nút **Copy link QR** và nút **Tạo lại mã QR** (`POST /api/restaurant-tables/{id}/qr-token`).
     - Thêm form **Tạo nhanh nhiều bàn** (`POST /api/restaurant-tables/bulk`: `Prefix`, `StartNumber`, `EndNumber`, `AreaId`, `Capacity`).
2. **Admin Quản lý Nhân viên & Khách hàng (`pages/employees.html` + `js/pages/employees.js` & `js/common/staff-session.js`):**
   - Thêm bộ lọc `userType` (`Tất cả` | `Nhân viên` | `Khách hàng`) gọi `GET /api/users?userType=...`.
   - Khi chọn một tài khoản có role `Customer`, gọi thêm `GET /api/users/{id}/customer-summary` để hiển thị thẻ thống kê nhanh: Tổng số đơn, Tổng chi tiêu, Số lượt đặt bàn.
3. **Thu ngân Kết ca & Két tiền (`pages/payment.html` + `js/pages/payment.js` & `js/common/payment-session.js`):**
   - Thêm khối **Quản lý ca thu ngân** trên trang Thu ngân:
     - Nếu chưa mở ca (`GET /api/cashier-shifts/current` trả 404/null): hiển thị form **Mở ca làm việc** (`POST /api/cashier-shifts/open` nhập `OpeningCash` + ghi chú) và nhắc thu ngân mở ca trước khi thu tiền.
     - Khi đang mở ca: hiển thị `OpeningCash`, tổng thu Tiền mặt / Thẻ / MoMo / Hoàn tiền, `ExpectedCash` trực tiếp; nút **Đóng ca & Đối soát** mở modal nhập `ActualCash`, tính và hiển thị chênh lệch `Variance` (`POST /api/cashier-shifts/{shiftId}/close`).
     - Tab/Drawer xem **Lịch sử ca làm việc** (`GET /api/cashier-shifts`).
   - Trên chi tiết thanh toán đơn có đặt bàn trước: hiển thị dòng **"Đã khấu trừ cọc đặt bàn"** nếu đơn liên kết `Reservation` có `DepositStatus == Paid`.
4. **Phục vụ chọn Khuyến mãi từ danh sách (`pages/order-tools.html` + `js/common/order-extras-session.js`):**
   - Đổi nguồn tải khuyến mãi cho `Waiter` sang `GET /api/management/promotions/active` để `Waiter` chọn trực tiếp từ danh sách thay vì phải gõ tay ID số.
5. **Báo cáo Top Món & Giá vốn / Lợi nhuận COGS (`pages/reports.html` + `js/pages/reports.js` & `js/common/reporting-session.js`):**
   - Bổ sung 2 tab/khối báo cáo mới cùng bộ lọc kỳ/ngày và nút xuất CSV:
     - **Top món bán chạy (`GET /api/reports/top-dishes` + `export.csv`)**: bảng xếp hạng theo số lượng/doanh thu/lãi gộp.
     - **Lợi nhuận & Giá vốn COGS (`GET /api/reports/profitability` + `export.csv`)**: tổng Doanh thu, tổng Giá vốn, Lãi gộp, Biên lợi nhuận (%), và cảnh báo nếu có món thiếu công thức (`HasMissingRecipe`) hoặc thiếu giá nhập (`HasMissingCost`).

### 2.4. Real-time SignalR trên các màn hình vận hành
- Tạo module dùng chung `frontend/js/common/realtime.js` (tải `@microsoft/signalr` local bundle hoặc fallback WebSocket/long-polling an toàn mà không làm gãy trang nếu offline).
- Kết nối tới `${apiOrigin}/hubs/restaurant?access_token=${token}`:
  - **`kitchen.js`:** Lắng nghe `OrderCreated`, `OrderItemsAdded`, `KitchenTicketStatusChanged`, `KitchenItemStatusChanged` -> tự động `refresh(true)` bảng Bếp.
  - **`payment.js`:** Lắng nghe `OrderCreated`, `OrderStatusChanged`, `PaymentSucceeded`, `PaymentFailed`, `PaymentRefunded` -> tự động cập nhật hàng đợi thu ngân và kết quả MoMo ngay lập tức.
  - **`tables.js` & `order.js` & `reservations.js` & `qr-order.js`:** Lắng nghe `TableStatusChanged`, `OrderStatusChanged`, `OrderItemsAdded`, `KitchenItemStatusChanged` -> tự động đồng bộ trạng thái bàn/món.

---

## Phần 3: Kế hoạch Đội ngũ Subagent Frontend (Chủ đề Sahara, không đụng Backend)

| Vai trò | Model | Dùng skill `ponytail`? | Nhiệm vụ & File sở hữu |
|---|---|---|---|
| **FE Tech Lead** | Hydrogen Medium (`inherit`) | **KHÔNG** | Rà soát `SAHARA_UI_HANDOFF.md` & CSS tokens; sửa `api.js`, `auth.js`, `navigation.js` (ẩn nút `Đặt món`, thêm route public & role `Customer`); xây dựng `realtime.js` (SignalR) và tải thư viện `@microsoft/signalr` local vào `frontend/assets/vendor/` (nếu cần); review toàn bộ UI/CSS/diff cuối cùng. |
| **FE Dev 1** *(Public, Register, Reservations & QR Order)* | Hydrogen Medium (`inherit`) | **CÓ** | Tạo `pages/public-menu.html` + `js/pages/public-menu.js`, `pages/register.html` + `js/pages/register.js`, `pages/reservations.html` + `js/pages/reservations.js`, `pages/qr-order.html` + `js/pages/qr-order.js`; cập nhật `login.html` (link đăng ký & xem thực đơn), `order.js` (xử lý khi mở không có `tableId`), và cập nhật ẩn nút `Đặt món` trong các trang HTML. |
| **FE Dev 2** *(Shift, Promotions, Admin Tables & Customers)* | Hydrogen Medium (`inherit`) | **CÓ** | Nâng cấp `pages/payment.html` + `payment.js` + `payment-session.js` (Mở/Đóng ca két tiền + hiển thị trừ cọc); `order-extras-session.js` (dropdown khuyến mãi `active` cho Waiter); `administration.html` + `administration.js` (QR bàn + Bulk tạo bàn); `employees.html` + `employees.js` + `staff-session.js` (lọc Staff/Customer + Customer summary). |
| **FE Dev 3** *(Reports Top Dishes/COGS, SignalR Wiring & Cleanup)* | Hydrogen Medium (`inherit`) | **CÓ** | Nâng cấp `pages/reports.html` + `reports.js` + `reporting-session.js` (Top món + Lợi nhuận COGS + CSV); nối `realtime.js` vào `kitchen.js`, `payment.js`, `tables.js`, `order.js`; xóa 3 file mock data thừa (`kitchen.data.js`, `order.data.js`, `payment.data.js`). |
| **FE QA Engineer** | Hydrogen Medium (`inherit`) | **KHÔNG** | Kiểm tra cú pháp toàn bộ JS/CJS, kiểm tra liên kết & ID HTML, viết & chạy unit/smoke test trong `frontend/tests/` cho `navigation` (không còn nút `Đặt món`), `auth` (`Customer` role, redirect khi chưa login), `reservations`, `qr-order`, `payment-shift`, `reporting`. |
