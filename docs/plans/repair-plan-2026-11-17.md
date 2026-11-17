# Kế hoạch kiểm tra & tu sửa hệ thống (Backend + Frontend)

Ngày lập: 2026-11-17  
Phạm vi: Toàn bộ Backend (`RestaurantFoodManagerSystem/`) và Frontend (`frontend/`).

---

## 1. Giải thích các State của Đặt bàn (Reservations)

Hệ thống Backend hiện có **2 nhóm state độc lập** cho mỗi đơn đặt bàn (`TableReservation`):

### 1.1. Trạng thái lượt đặt bàn (`TableReservationStatuses`)
| State | Nhãn tiếng Việt | Ý nghĩa & Quyền chuyển trạng thái |
|---|---|---|
| `Pending` | **Chờ xác nhận** | Khách (hoặc nhân viên) vừa tạo đơn đặt bàn. Bàn bắt đầu bị khóa giữ chỗ nếu nằm trong khung giờ đặt. |
| `Confirmed` | **Đã xác nhận giữ bàn** | Nhân viên xác nhận đơn đặt (`POST /reservations/{id}/confirm`). Bàn bị khóa, không nhận khách lẻ. |
| `Seated` | **Khách đã tới (Đang dùng bàn)** | Nhân viên ấn **"Xác nhận khách tới"** (`POST /reservations/{id}/seat`). Bàn chuyển sang luồng order bình thường. |
| `Completed` | **Hoàn tất** | Tự động chuyển khi Order của bàn đặt được thanh toán xong. |
| `NoShow` | **Quá giờ / Khách không đến** | Nhân viên xác nhận trả bàn thủ công **chỉ sau khi đã quá giờ hẹn**. Nếu đã đặt cọc `Paid`, tiền cọc chuyển sang `Forfeited`. |
| `Cancelled` | **Đã hủy** | Hủy đơn đặt bàn (`POST /reservations/{id}/cancel`). Nếu đã đặt cọc `Paid`, tiền cọc chuyển sang `Refunded`. |

### 1.2. Trạng thái tiền cọc (`DepositStatuses`)
| State | Nhãn tiếng Việt | Ý nghĩa |
|---|---|---|
| `None` | Không yêu cầu cọc | `DepositAmount == 0` hoặc hủy khi chưa thanh toán cọc. |
| `Pending` | Chờ thanh toán cọc | Có tiền cọc nhưng chưa thu được. |
| `Paid` | Đã đặt cọc | Đã xác nhận thu tiền cọc (MoMo hoặc thu ngân). |
| `AppliedToOrder` | Đã trừ vào hóa đơn | Tiền cọc được khấu trừ khi thanh toán Order của bàn. |
| `Refunded` | Đã hoàn cọc | Hoàn cọc khi đơn đặt bị hủy hợp lệ. |
| `Forfeited` | Mất cọc (Quá giờ không đến) | Khách không đến (`NoShow`) sau khi đã đặt cọc. |

---

## 2. Quy tắc nghiệp vụ khóa bàn & đồng bộ Đặt bàn (Theo yêu cầu mới)

1. **Khóa bàn khi có khách đặt trước:**
   - Khi bàn có đơn đặt ở trạng thái `Pending` hoặc `Confirmed` trong khung giờ hiệu lực / sắp tới, bàn trên sơ đồ nhân viên (`tables.html`) và `dashboard.html` phải hiển thị trạng thái **Đang có khách giữ (`Reserved`)** và **bị khóa**.
   - **Không ai được phép mở order mới / nhận khách lẻ** vào bàn đang ở trạng thái khóa `Reserved`.
2. **Không cho phép trả bàn sớm khi đang trong giờ giữ:**
   - Khi bàn đang ở trạng thái khóa (`Pending` / `Confirmed`) và **chưa quá giờ hẹn**, nhân viên **không thể chủ động bấm trả bàn / giải phóng bàn**.
3. **Xác nhận khách tới:**
   - Khi khách đến, nhân viên bấm **"Xác nhận khách tới"** (`seat`) → chuyển đơn đặt sang `Seated` và cho phép bàn chuyển sang trạng thái **bàn order bình thường** (`Occupied` khi tạo/gắn Order).
4. **Quá giờ hẹn không đến → Trả bàn thủ công (không tự động):**
   - **Không tự động trả bàn** khi quá giờ (tránh trường hợp khách bận đến trễ và đã báo nhân viên vẫn giữ bàn).
   - Chỉ khi **đã quá giờ hẹn** (so sánh thời gian hiện tại với mốc `ReservedFrom + thời gian ân hạn` hoặc `ReservedTo`), hệ thống mới mở khóa nút **"Quá giờ / Xác nhận trả bàn"** (`no-show`) để nhân viên **tự xác nhận trả bàn thủ công**, đưa bàn về `Available`.

---

## 3. Các lỗi đã phát hiện qua quét Backend + Frontend

### 3.1. Lỗi FE không gọi được Nguyên liệu, Công thức, Khuyến mãi, Kích cỡ món, Topping món
1. **`frontend/js/common/api.js` (dòng 15-28, 64):**
   - Hàm `anonymous()` cắt query string và coi mọi `GET /foods/...` và `GET /categories/...` là anonymous nên **không gửi header `Authorization: Bearer`**.
   - Hậu quả: `GET /foods/{id}/sizes` (`FoodSizesController`) và `GET /foods/{id}/toppings` (`FoodToppingsController`) yêu cầu `[Authorize]` bị trả **401 Unauthorized**, làm lỗi toàn bộ trang **Công thức (`recipes.html`)**, **Kích cỡ / Topping (`foods.html`, `administration.html`)**, và phần chọn phạm vi **Khuyến mãi**.
2. **`frontend/js/common/catalog-session.js`:**
   - `CatalogSession.load()` dùng `Promise.all` gọi `/foods/{id}/sizes?includeInactive=true` cho tất cả món; chỉ 1 request lỗi là sập toàn bộ màn hình Công thức & Nguyên liệu.
3. **`frontend/js/pages/foods.js` (dòng 495):**
   - Tạo danh mục (`POST /categories`) gửi `displayOrder: false` (boolean) thay vì số nguyên `0`, gây lỗi 400.
4. **`frontend/js/pages/public-menu.js` (dòng 64):**
   - Kiểm tra `discountType === "Percentage"` thay vì `"Percent"`, làm khuyến mãi phần trăm hiển thị sai thành `đ`.
5. **`frontend/js/pages/order-tools.js` (dòng 23):**
   - Còn thông báo cũ hạn chế vai trò `Waiter` chọn khuyến mãi dù `order-extras-session.js` đã hỗ trợ tải `GET /management/promotions/active`.

### 3.2. Lỗi không đồng bộ Đặt bàn khách hàng ↔ Sơ đồ bàn nhân viên
1. **Backend (`RestaurantTableService.cs` & `RestaurantTableDto`):**
   - `RestaurantTableStatuses` chỉ có `Available`, `Occupied`, `Maintenance` — thiếu `Reserved`.
   - `RestaurantTableService` chỉ kiểm tra `Orders` mà không kiểm tra `TableReservations`, nên bàn đã đặt vẫn trả về `Available`.
   - `OrderService.CreateAsync` cần chặn chặt mọi trường hợp tạo order walk-in vào bàn đang bị khóa bởi đơn đặt `Pending` hoặc `Confirmed`.
   - `ReservationService.NoShowAsync` cần kiểm tra **chỉ cho phép thực hiện khi đã quá giờ hẹn** (không cho trả bàn thủ công trước giờ hẹn).
   - `ReservationsController.cs` thiếu endpoint `POST /api/reservations/{id}/deposit/momo/mock` dù `ReservationService.CreateMomoDepositAsync` (dòng 288) có trả về URL này (404).
2. **Frontend (`tables.js`, `tables.data.js`, `dashboard.js`, `reservations.js`):**
   - `tables.js` (`normalizeStatus`) và `dashboard.js` không xử lý `Reserved` nên bàn bị gán nhãn `"Trống"`.
   - `tables.js` không tải thông tin đặt bàn và không lắng nghe sự kiện SignalR của đặt bàn.
   - `reservations.js` thiếu nhãn cho `AppliedToOrder` và `Forfeited` trong `depositLabels`.

### 3.3. Tách trang Nhân viên & Khách hàng / Người dùng
- `frontend/pages/employees.html` + `employees.js` đang gộp chung cả nhân viên và khách hàng thông qua `#employeeTypeFilter`.
- `frontend/pages/roles.html` (dòng 18) vẫn ghi `"5 vai trò cố định"` trong khi thực tế có **6 vai trò** (thêm `Customer`).

### 3.4. Các lỗi / điểm cần gia cố khác (Backend + Frontend)
- **Backend `BasicDataSeeder.cs`:** Bàn seed (`B01`–`B06`) chưa có `QrToken` mặc định, khiến quét QR bị lỗi đến khi Admin tạo thủ công.
- **Backend `Program.cs`:** `app.UseCors("FrontendPolicy")` chỉ bật trong `IsDevelopment()`, cần cấu hình hợp lệ cho cả Staging/Production.
- **Backend `PublicController.cs`:** `GET /api/public/promotions` trả về raw entity `Promotion` thay vì `PromotionResponse`.
- **Frontend HTML điều hướng cũ:** Một số file HTML quản trị còn nút tĩnh `data-page="order.html"` bị `navigation.js` xóa lúc runtime; `foods.js` còn gắn trùng sự kiện `[data-page]`.

---

## 4. Lộ trình tu sửa chi tiết

### Giai đoạn 1: Sửa lỗi gọi API & dữ liệu danh mục
1. **Sửa `frontend/js/common/api.js`:**
   - Không xếp `/foods/*/sizes`, `/foods/*/toppings`, hoặc các request quản trị (`?includeInactive=true`) vào nhóm `anonymous`; luôn gửi kèm `Authorization: Bearer` khi đã có token.
2. **Sửa `frontend/js/common/catalog-session.js` & `admin-session.js`:**
   - Đảm bảo `CatalogSession.load()` xử lý an toàn khi tải sizes theo món, kiểm tra lại toàn bộ luồng tải & thao tác cho **Nguyên liệu**, **Kho**, **Công thức**, **Kích cỡ món**, **Topping món**, **Khuyến mãi**.
3. **Sửa lỗi contract FE:**
   - `foods.js`: đổi `displayOrder: false` → `0`.
   - `public-menu.js`: đổi `"Percentage"` → `"Percent"`.
   - `order-tools.js`: cập nhật thông báo phù hợp cho `Waiter`.

### Giai đoạn 2: Đồng bộ Đặt bàn & Khóa bàn theo quy tắc mới
1. **Backend:**
   - Cập nhật `RestaurantTableStatuses` thêm `Reserved` và cập nhật `RestaurantTableService` + `RestaurantTableDto` trả về trạng thái `Reserved` cùng thông tin tóm tắt đơn đặt (`reservationId`, `customerName`, `customerPhone`, `reservedFrom`, `reservedTo`, `reservationStatus`, `depositStatus`, `isOverdue`) khi bàn đang có đơn `Pending` hoặc `Confirmed`.
   - Cập nhật `OrderService.CreateAsync`: chặn mở bàn/tạo order walk-in nếu bàn đang có đơn `Pending` hoặc `Confirmed` giữ chỗ (chỉ cho mở khi nhân viên đã xác nhận khách tới / gắn `reservationId`).
   - Cập nhật `ReservationService.NoShowAsync`: bắt buộc kiểm tra đã quá giờ hẹn (ví dụ `now > reservedFrom + graceMinutes` hoặc `now > reservedTo`) mới cho phép nhân viên xác nhận trả bàn thủ công; nếu chưa quá giờ thì trả lỗi nghiệp vụ rõ ràng (không cho trả bàn sớm).
   - Phát sự kiện SignalR khi có thay đổi đặt bàn (`ReservationCreated`, `ReservationConfirmed`, `ReservationSeated`, `ReservationCancelled`, `ReservationNoShow`).
   - Bổ sung `POST /api/reservations/{id}/deposit/momo/mock` trong `ReservationsController.cs`.
2. **Frontend:**
   - `tables.js` & `tables.data.js`: thêm trạng thái **`reserved` ("Đang có khách giữ / Đã khóa")**, hiển thị thông tin khách đặt, giờ hẹn, trạng thái quá giờ; khóa nút mở bàn/tạo order walk-in trên bàn đang `Reserved`; chỉ hiện nút **"Xác nhận khách tới"** và (khi đã quá giờ) nút **"Quá giờ — Trả bàn thủ công"**.
   - `tables.js`: đăng ký lắng nghe các sự kiện SignalR đặt bàn để tự động đồng bộ real-time.
   - `dashboard.js`: hiển thị đúng nhãn `Đang có khách giữ` cho bàn `Reserved`.
   - `reservations.js`: bổ sung đầy đủ nhãn tiếng Việt cho 6 trạng thái đặt bàn & 6 trạng thái tiền cọc, kèm giải thích ngắn gọn trạng thái và khóa nút `NoShow` khi chưa quá giờ hẹn.

### Giai đoạn 3: Tách riêng trang Nhân viên & Khách hàng
1. **Trang Nhân viên (`frontend/pages/employees.html` + `frontend/js/pages/employees.js`):**
   - Chỉ hiển thị và quản lý tài khoản nhân viên (`userType=staff`), bỏ bộ lọc gộp khách hàng.
2. **Trang Khách hàng (`frontend/pages/customers.html` + `frontend/js/pages/customers.js`):**
   - Trang riêng hiển thị danh sách khách hàng (`userType=customer`), chi tiết thống kê (`customer-summary`: số đơn, tổng chi tiêu, số lượt đặt bàn), khóa/mở tài khoản, đặt lại mật khẩu.
3. **Điều hướng & phân quyền:**
   - Cập nhật `auth.js` (`pageRoles`), `navigation.js` (thêm mục `Khách hàng` cho `Admin`), và sửa `roles.html` thành **6 vai trò**.

### Giai đoạn 4: Gia cố Backend & Dọn dẹp Frontend
1. Sinh sẵn `QrToken` cho các bàn seed trong `BasicDataSeeder.SeedTablesAsync`.
2. Cho phép `UseCors("FrontendPolicy")` hoạt động theo cấu hình ở cả môi trường non-Development.
3. Chuẩn hóa `PublicController.GetPromotions` trả về `PromotionResponse`.
4. Dọn bỏ nút `order.html` tĩnh thừa trong HTML quản trị và listener trùng lặp trong `foods.js`.
5. Chạy kiểm thử lại toàn bộ Backend (`dotnet test`) và Frontend (`node --test frontend/tests/*.test.cjs`).

---

## 5. Kế hoạch phân công đội Subagent (Hydrogen Medium)

Toàn bộ công việc được chia cho **4 subagent chạy song song** (cùng model `Gemini 4 Hydrogen (Medium)`), tách biệt hoàn toàn phạm vi file để không xung đột:

| Subagent | Vai trò | Phạm vi file sở hữu (không chồng chéo) | Nhiệm vụ chính |
|---|---|---|---|
| `backend-engineer` | Backend Fixes & Tests | `RestaurantFoodManagerSystem/**`, `RestaurantFoodManagerSystem.Tests/**` | Thêm trạng thái `Reserved` + thông tin đặt bàn vào `RestaurantTableDto`/`RestaurantTableService`; chặn mở bàn walk-in khi đang giữ chỗ; chỉ cho `NoShow` thủ công khi quá giờ hẹn; phát SignalR đặt bàn; thêm `deposit/momo/mock`; seed `QrToken`; bật CORS; chuẩn hóa `PublicController.GetPromotions`; viết & chạy `dotnet test`. |
| `fe-catalog-fixes` | FE API Auth & Catalog | `frontend/js/common/api.js`, `catalog-session.js`, `admin-session.js`, `recipes.js`, `administration.js`, `foods.js`, `public-menu.js`, `order-tools.js` | Sửa `api.js` để `/foods/*/sizes`, `/foods/*/toppings` và `?includeInactive=true` luôn gửi JWT; xử lý an toàn `CatalogSession.load()`; sửa `displayOrder: 0` ở `foods.js`, `"Percent"` ở `public-menu.js`, thông báo Waiter ở `order-tools.js`. |
| `fe-reservation-sync` | FE Tables & Reservations Sync | `frontend/js/data/tables.data.js`, `frontend/js/pages/tables.js`, `dashboard.js`, `order.js`, `reservations.js`, `frontend/pages/tables.html`, `reservations.html` | Đồng bộ trạng thái `Reserved` ("Đang có khách giữ / Đã khóa") trên `tables.html` và `dashboard.js`; khóa mở bàn/order lẻ khi bàn đang giữ; thêm nút "Xác nhận khách tới" và nút trả bàn thủ công `NoShow` chỉ mở khi quá giờ; lắng nghe SignalR đặt bàn; bổ sung đủ nhãn 6 state đặt bàn & 6 state tiền cọc. |
| `fe-users-separation` | FE Employees vs Customers | `frontend/pages/employees.html`, `employees.js`, `customers.html` (mới), `customers.js` (mới), `roles.html`, `administration.html`, `inventory.html`, `recipes.html`, `order-tools.html`, `frontend/js/common/auth.js`, `navigation.js` | Tách riêng trang Nhân viên (`employees.html`, `userType=staff`) và trang Khách hàng (`customers.html`, `userType=customer` + `customer-summary`); cập nhật `auth.js` và `navigation.js`; sửa `roles.html` thành 6 vai trò; dọn nút `order.html` thừa trong HTML quản trị. |

Sau khi cả 4 subagent hoàn tất, Coordinator sẽ rà soát chéo contract BE–FE và chạy kiểm tra tổng thể lần cuối.
