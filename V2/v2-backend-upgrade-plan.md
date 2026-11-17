# Kế hoạch Nâng cấp & Sửa lỗi Backend — V2 (`RestaurantFoodManagerSystem`)

> **Phạm vi tuyệt đối:** Chỉ chỉnh sửa/nâng cấp ở **Backend** (`RestaurantFoodManagerSystem/` và `RestaurantFoodManagerSystem.Tests/`). **Tuyệt đối không sửa `frontend/`**.  
> **Trạng thái:** Bản thiết kế & kế hoạch tổ chức phòng ban thực thi (chưa chỉnh sửa code).

---

## Phần 1: Sửa lỗi Bảo mật, Public API & Hiệu năng

### 1.1. Đưa khóa ký JWT ra khỏi `appsettings.json` vào `.env` / Biến môi trường
- **Hiện trạng:** `RestaurantFoodManagerSystem/appsettings.json` đang hardcode `"Authentication:Jwt:Key": "key-is-secret-key-is-secret-key-is-secret-key-is-secret"`. Dù `.gitignore` đã ignore `.env`, việc để khóa mẫu trong `appsettings.json` là lỗ hổng leo thang đặc quyền nếu deploy quên ghi đè.
- **Thiết kế V2:**
  1. Xóa trống `"Key": ""` trong `RestaurantFoodManagerSystem/appsettings.json`.
  2. Nạp khóa qua biến môi trường `Authentication__Jwt__Key` (hoặc `dotnet user-secrets`), đồng thời hỗ trợ đọc file `.env` nhẹ ở đầu `Program.Main` khi chạy local.
  3. `Program.cs` tiếp tục chặn khởi động nếu `Auth:Enforce=true` mà thiếu khóa hoặc khóa < 32 byte UTF-8.

### 1.2. Thu hồi JWT ngay lập tức khi khóa tài khoản / đổi mật khẩu / đổi vai trò
- **Hiện trạng:** `TokenService.CreateToken` phát hành JWT sống 8 giờ; `AddJwtBearer` trong `Program.cs` chưa kiểm tra trạng thái sống của user sau khi cấp token.
- **Thiết kế V2 (`TokenVersion` + `OnTokenValidated`):**
  1. Thêm `TokenVersion` (`int`, mặc định `1`) vào entity `Users` (+ EF Core Migration) và `UserDto`.
  2. Tự động tăng `user.TokenVersion++` và xóa cache khi gọi: `UserService.SetActiveAsync`, `AuthService.ChangePasswordAsync`, `AuthService.ResetPasswordAsync`, `AuthService.AssignRoleToUser`, `AuthService.RemoveRoleFromUserAsync`.
  3. `TokenService.CreateToken` nhúng claim `"tv": user.TokenVersion`.
  4. `JwtBearerEvents.OnTokenValidated` trong `Program.cs` đối chiếu `(IsActive, TokenVersion)` qua `IMemoryCache` (TTL 30–60s, fallback `AppDbContext.Users.AsNoTracking()`); nếu `!IsActive` hoặc lệch `tv` -> `context.Fail(...)` trả `401 Unauthorized` ngay lập tức.

### 1.3. Chống trùng lặp `Idempotency-Key` — Kiến trúc Hybrid (Phương án B + Bảng DB `IdempotencyKeys`)
- **Vì sao cần kết hợp cả 2?**
  - **Tầng 1 — In-Memory Singleflight (`ConcurrentDictionary<string, Lazy<Task<Entry>>>` — Phương án B):** Xử lý cực nhanh cho 1 server: các `Idempotency-Key` khác nhau (các bàn khác nhau) chạy **song song 100%** không chặn chéo nhau; 20 người bấm trùng cùng 1 key tại cùng 1 thời khắc được gom vào đúng **1 `Task` duy nhất**.
  - **Tầng 2 — Bảng DB `IdempotencyKeys`:** Khắc phục nhược điểm mất trạng thái nếu server bị sập/restart giữa chừng hoặc sau khi vừa thu tiền/tạo đơn xong.
- **Luồng xử lý chi tiết:**
  1. Bảng `IdempotencyKeys`: `(Id, Scope, IdempotencyKey, ActorId, RequestFingerprint, Status [InProgress|Completed], ResponseJson, StatusCode, CreatedAt, ExpiresAt)` với `UNIQUE INDEX(Scope, IdempotencyKey)`.
  2. Khi nhận request có `Idempotency-Key`:
     - **Bước 1 (Singleflight trong RAM):** Nếu cùng key đang chạy in-flight trên RAM, các request trùng cùng `await` chung `Lazy<Task<Entry>>`.
     - **Bước 2 (Kiểm tra DB trước khi chạy `create`):**
       - Nếu DB đã có dòng `Completed` (còn hạn): kiểm tra `RequestFingerprint` (khác payload -> `409 Conflict`), giống payload -> trả lại `ResponseJson` đã lưu mà không chạy lại nghiệp vụ.
       - Nếu DB có dòng `InProgress`: nếu chưa quá lease timeout (ví dụ 30s) -> trả `409 Conflict` (đang xử lý); nếu quá lease timeout (dấu hiệu server từng sập giữa chừng) -> cho phép thu hồi/chạy lại an toàn.
       - Nếu chưa có: `INSERT` dòng `InProgress` vào DB -> chạy `await create(ct)` -> cập nhật `Status = Completed`, `ResponseJson` -> trả kết quả. Nếu `create(ct)` ném lỗi nghiệp vụ/hủy, xóa dòng `InProgress` và dọn `Lazy` khỏi RAM để client thử lại được.
  3. Job nền `IHostedService` định kỳ dọn các dòng `IdempotencyKeys` hết hạn (sau 24h) để bảng không phình to.

### 1.4. Mở API Public (Xem trước không cần đăng nhập) & Siết đăng nhập khi Đặt bàn / Gọi món
- **Hiện trạng:** `MenuController` (`GET /api/menu`), `FoodsController` (`GET /api/foods`, `GET /api/foods/{id}`), `CategoriesController` (`GET /api/categories`, `GET /api/categories/{id}`) đang gắn `[Authorize(Policy = AuthPolicies.Authenticated)]`; `RestaurantTablesController` (`GET /api/restaurant-tables`) và `TableAreasController` (`GET /api/table-areas`) đang khóa `PosOrCashier`. Người dùng chưa đăng nhập không xem trước được thực đơn hay khu vực/bàn.
- **Thiết kế V2:**
  - **Mở `[AllowAnonymous]` (chỉ đọc, dữ liệu công khai an toàn):**
    - `GET /api/menu`
    - `GET /api/foods`, `GET /api/foods/{id}` (ẩn các món `IsActive == false` khi caller chưa đăng nhập hoặc không phải `WarehouseDesk`)
    - `GET /api/categories`, `GET /api/categories/{id}`
    - `GET /api/public/table-areas` & `GET /api/public/tables` (hoặc mở `GET /api/table-areas`, `GET /api/restaurant-tables` với DTO rút gọn không lộ thông tin đơn/khách đang ngồi, chỉ trả `TableId`, `TableNumber`, `Capacity`, `AreaName`, `Status` để khách xem sơ đồ đặt bàn).
    - `GET /api/public/promotions` (chỉ trả các khuyến mãi đang `IsActive == true` và còn hiệu lực thời gian).
  - **Bắt buộc đăng nhập (`[Authorize]`):**
    - Khi khách muốn **đặt bàn (`POST /api/reservations`)** hoặc **gọi món (`POST /api/orders`, `POST /api/orders/{id}/items`)**, bắt buộc phải có JWT hợp lệ (`CustomerOrPos`).

### 1.5. Tối ưu truy vấn & Quyền khuyến mãi
- **`ReportService.GetDashboardAsync`:** Lọc bỏ `Completed`/`Cancelled` và `GroupBy` ngay dưới SQL thay vì kéo toàn bộ lịch sử `KitchenOrders` lên RAM.
- **Mở quyền đọc khuyến mãi đang hoạt động:** Cho phép `PosOrCashier` (bao gồm `Waiter`) đọc danh sách khuyến mãi đang hoạt động thay vì phải nhớ ID số.

---

## Phần 2: Các tính năng Nghiệp vụ Mới (Chỉ Backend)

### 2.1. Vai trò `Customer`, Đăng ký công khai & Admin quản lý cả Nhân viên lẫn Khách hàng
1. **Vai trò & Policy (`AuthPolicies.cs`, `AuthorizationServiceCollectionExtensions.cs`):**
   - Thêm `Customer` vào `AuthPolicies.Roles` và seed vào bảng `Roles`.
   - Thêm policy `CustomerOnly` (`Customer`) và `CustomerOrPos` (`Admin`, `Waiter`, `Customer`).
2. **Mở đăng ký công khai cho người dùng (`AuthController.cs`, `AuthService.cs`):**
   - Thêm `[AllowAnonymous] POST /api/auth/customer/register` nhận `CustomerRegisterRequest` (**không** cho client truyền role; backend ép gán duy nhất role `Customer` trong cùng 1 transaction tạo user).
   - Giữ `POST /api/auth/register` (`AdminOnly`) cho luồng Admin tạo tài khoản nhân viên nội bộ.
   - `POST /api/auth/login` dùng chung cho cả nhân viên và khách hàng.
3. **Admin quản lý cả Nhân viên và Khách hàng (`UsersController.cs`, `UserService.cs`):**
   - Mở rộng `GET /api/users` hỗ trợ filter: `?userType=all|staff|customer`, `?role=...`, `?isActive=...`, `?search=...` (kèm phân trang).
   - Giữ nguyên quyền Admin khóa/mở khóa (`PATCH /api/users/{id}/active`), đặt lại mật khẩu (`POST /api/users/{id}/reset-password`), sửa thông tin (`PUT /api/users/{id}`).
   - Bổ sung `GET /api/users/{id}/customer-summary` (AdminOnly) xem nhanh lịch sử đơn hàng & đặt bàn của một khách hàng.

### 2.2. Nâng cấp Quản lý Bàn cho Admin & QR Code gọi món
- **Hiện trạng:** `RestaurantTablesController` đã có `POST /api/restaurant-tables`, `PUT`, `DELETE` (`AdminOnly`) và `TableAreasController` đã có CRUD khu vực, nhưng bàn mẫu chủ yếu đang dựa vào seeder và chưa có `QrToken` để khách tự quét gọi món.
- **Nâng cấp V2 (`RestaurantTablesController.cs`, `RestaurantTableService.cs`):**
  - Thêm cột `QrToken` (`string?`, unique index) vào `RestaurantTable`: tự động sinh khi `POST /api/restaurant-tables` tạo bàn mới (và backfill cho bàn cũ).
  - Thêm `POST /api/restaurant-tables/{id:int}/qr-token` (`AdminOnly`): tạo lại `QrToken` mới nếu bị lộ/hỏng mã QR dán trên bàn.
  - Thêm `POST /api/restaurant-tables/bulk` (`AdminOnly`): cho phép Admin tạo nhanh nhiều bàn cùng lúc (ví dụ tiền tố `"B-"`, từ số `1` đến `10`, cùng `AreaId` và `Capacity`) thay vì phải tạo từng cái hoặc phụ thuộc seeder.

### 2.3. Đặt bàn trước + Thanh toán cọc (`TableReservation`) & Quy tắc Gọi món tại bàn (QR / Nhân viên)
1. **Đặt bàn trước & Thanh toán cọc tại nhà (`ReservationsController` — `api/reservations`):**
   - **Nguyên tắc chống phá / bom đơn:** Khách ở trên web (từ xa) **chỉ được đặt bàn trước và thanh toán tiền cọc 1 phần**, **tuyệt đối không được tạo đơn hay gọi món từ xa**.
   - Entity `TableReservation`: `ReservationId`, `TableId`, `CustomerUserId` (`int?`), `CustomerName`, `CustomerPhone`, `PartySize`, `ReservedFrom`, `ReservedTo`, `Status` (`Pending`, `Confirmed`, `Seated`, `Cancelled`, `NoShow`, `Completed`), `DepositAmount` (`decimal(18,2)`), `DepositStatus` (`None`, `Pending`, `Paid`, `AppliedToOrder`, `Refunded`, `Forfeited`), `DepositTransactionCode` (`string?`), `OrderId` (`int?`), `Note`, `CancelReason`, `RowVersion`, audit fields.
   - **Thanh toán tiền cọc:**
     - Hỗ trợ tạo giao dịch cọc qua MoMo (`POST /api/reservations/{id}/deposit/momo` + IPN callback tái sử dụng `MomoPaymentService`) hoặc thu ngân xác nhận cọc (`POST /api/reservations/{id}/deposit/confirm` — `CashierDesk`).
     - Khi khách đến ăn (`Seated` -> tạo `Order` -> `PaymentService` thu tiền), số tiền cọc trạng thái `Paid` sẽ được **khấu trừ tự động** vào số tiền cần thu còn lại trên đơn (`AmountDue = Order.TotalAmount - DepositAmount`) và chuyển `DepositStatus = AppliedToOrder`.
     - Hủy hợp lệ trước giờ quy định -> `Refunded`; khách bùng bàn (`NoShow`) -> `Forfeited`.
   - **Chống trùng lịch:** Trong transaction `Serializable`, kiểm tra bàn hoạt động, không bảo trì, `PartySize <= Capacity`, và không giao nhau với các đặt bàn `Pending`/`Confirmed`/`Seated` (`existing.ReservedFrom < ReservedTo && existing.ReservedTo > ReservedFrom`).
   - **Tương tác `OrderService` & `PaymentService`:**
     - Đơn vãng lai (walk-in) bị chặn nếu bàn sắp đến giờ có khách đã `Confirmed`/`Seated`.
     - Khi nhân viên check-in khách đặt bàn (`POST /api/reservations/{id}/seat`) hoặc tạo đơn cho bàn đặt, gắn `ReservationId` / `OrderId` và chuyển lịch đặt sang `Seated`. Khi đơn `Paid`, tự động chuyển `TableReservation` liên kết sang `Completed`.
   - **Endpoints:** `POST /api/reservations` (`CustomerOrPos`), `GET /api/reservations/my` (`CustomerOnly`), `GET /api/reservations` (`PosOperator`), `GET /api/reservations/{id}`, `POST .../deposit/momo`, `POST .../cancel`, `.../confirm`, `.../seat`, `.../no-show`.
2. **Quy tắc Gọi món — Chỉ Nhân viên hoặc Quét QR tại bàn (`OrdersController`, `OrderService`):**
   - **Chỉ 2 chủ thể được gọi món (`POST /api/orders`, `POST /api/orders/{id}/items`):**
     1. **Nhân viên (`Admin`, `Waiter`):** Tạo đơn / thêm món trực tiếp tại POS.
     2. **Khách hàng (`Customer`) quét mã QR tại bàn:** Bắt buộc request từ `Customer` phải kèm **`TableQrToken` hợp lệ** khớp với `RestaurantTable.QrToken` của bàn đang mở phục vụ (`IsActive == true`, không `Maintenance`).
   - **Chặn order từ xa:** Nếu caller mang role `Customer` gọi `POST /api/orders` hoặc `POST /api/orders/{id}/items` mà **thiếu hoặc sai `TableQrToken`** (kể cả khi truyền `ReservationId` hoặc `TableId` trực tiếp) -> **từ chối ngay (`403 Forbidden`)**.
   - Mở policy `CustomerOrPos` cho `POST /api/orders`, `POST /api/orders/{id}/items`, `GET /api/orders/{id}` (kiểm tra `order.CustomerUserId == _currentUser.UserId` nếu là `Customer`) và thêm `GET /api/orders/my` (`CustomerOnly`).
   - Giữ nguyên quyền sửa trạng thái, hủy món, chuyển bàn, áp khuyến mãi cho `PosOperator`/`OrderCancellation`.

*(Bỏ qua tính năng tách hóa đơn / thanh toán nhiều lần theo yêu cầu.)*

### 2.4. Nghiệp vụ Kết ca & Đối soát Két tiền (`CashierShift`)
- Entity `CashierShift` (`ShiftId`, `CashierUserId`, `OpenedAt`, `ClosedAt`, `OpeningCash`, `ExpectedCash`, `ActualCash`, `Variance`, `Status [Open|Closed]`, `CashTotal`, `CardTotal`, `MomoTotal`, `OtherTotal`, `RefundTotal`, `Note`, `RowVersion`) + `ShiftId` (`int?`) trên `Payment`.
- `PaymentService.CreateAsync`: bắt buộc thu ngân phải có ca `Open`, tự gắn `ShiftId`. Hoàn tiền mặt trừ vào `ExpectedCash`; MoMo IPN giữ nguyên `ShiftId` từ lúc tạo intent.
- Endpoints (`api/cashier-shifts`, `CashierDesk`): `POST /open`, `GET /current`, `POST /{shiftId}/close` (trong `Serializable` tx), `GET /`, `GET /{shiftId}`.

### 2.5. Báo cáo Top Món bán chạy & Giá vốn / Lợi nhuận (COGS)
- Tính trên `OrderItem` không bị hủy thuộc `Order` đã có `Payment.Status == Paid` trong kỳ (`VietnamTime.Resolve`).
- Đơn giá vốn nguyên liệu lấy bình quân gia quyền từ `PurchaseOrderItems` của các phiếu nhập đã nhận (`ReceivedAt != null`); giá vốn món tính theo `Recipe` (`IsActive == true`) -> `RecipeIngredient`. Gắn cờ `HasMissingRecipe` / `HasMissingCost` nếu thiếu dữ liệu.
- Endpoints (`ReportsController`, `AdminOnly`): `GET /api/reports/top-dishes`, `GET /api/reports/profitability`, và `export.csv` dùng `ReportService.EscapeCsvCell` + UTF-8 BOM.

### 2.6. Real-time với ASP.NET Core SignalR (`RestaurantHub`)
- `Program.cs`: `AddSignalR()`, `MapHub<RestaurantHub>("/hubs/restaurant")`, đọc `access_token` query string trong `OnMessageReceived`, bật `.AllowCredentials()` cho CORS.
- Groups: `role:Kitchen`, `role:Cashier`, `role:Waiter`, `customer:{userId}`, `order:{id}`.
- Phát sự kiện **sau khi `tx.CommitAsync` thành công** từ `OrderService`, `KitchenService`, `PaymentService`.

---

## Phần 3: Kế hoạch Tổ chức Phòng ban Subagent & Quy trình Thực thi

Khi bắt đầu bước code V2, hệ thống sẽ điều phối một **Phòng ban Kỹ thuật Backend (chỉ sửa trong `RestaurantFoodManagerSystem/` và `RestaurantFoodManagerSystem.Tests/`, tuyệt đối không đụng `frontend/`)**, phân công rõ vai trò và quy tắc dùng skill như sau:

### 3.1. Sơ đồ nhân sự & Phân công Subagent

| Vai trò | Model | Dùng skill `ponytail`? | Phạm vi sở hữu & Nhiệm vụ |
|---|---|---|---|
| **Tech Lead (Architect)** | Hydrogen Medium (`inherit`) | **KHÔNG** *(cần độ chính xác kiến trúc & review nghiêm ngặt)* | Chốt DB Schema & EF Core Migrations, Interface/DTO contracts; điều phối merge; review toàn bộ diff cuối cùng; ký duyệt trước khi bàn giao. |
| **Backend Dev 1** *(Auth, Public API & Admin)* | Hydrogen Medium (`inherit`) | **CÓ** *(bắt buộc đọc & tuân thủ `ponytail/SKILL.md`)* | JWT `.env`, `TokenVersion` revocation + `OnTokenValidated`, Public Read APIs (`[AllowAnonymous]`), `Customer` register/policy, Admin quản lý Staff & Customer, nâng cấp `RestaurantTables` (`QrToken` + bulk create). |
| **Backend Dev 2** *(Idempotency & SignalR)* | Hydrogen Medium (`inherit`) | **CÓ** *(bắt buộc đọc & tuân thủ `ponytail/SKILL.md`)* | Hybrid `OrderIdempotencyStore` (Singleflight + bảng DB `IdempotencyKeys` + cleanup job), `RestaurantHub` + gắn emit sau `CommitAsync` tại `OrderService`, `KitchenService`, `PaymentService`. |
| **Backend Dev 3** *(Reservation, Order & Shift)* | Hydrogen Medium (`inherit`) | **CÓ** *(bắt buộc đọc & tuân thủ `ponytail/SKILL.md`)* | `TableReservation` + conflict detection, Customer Self-Order (`ReservationId` / `QrToken`), `CashierShift` + tích hợp `PaymentService`. |
| **Backend Dev 4** *(Reporting & Optimization)* | Hydrogen Medium (`inherit`) | **CÓ** *(bắt buộc đọc & tuân thủ `ponytail/SKILL.md`)* | `ReportService` Top Dishes & COGS/Profitability + CSV export, tối ưu `GetDashboardAsync`, mở danh sách khuyến mãi active cho `PosOrCashier`. |
| **QA Engineer (Concurrency & Test)** | Hydrogen Medium (`inherit`) | **KHÔNG** *(cần độ chính xác, phủ đầy đủ edge-case & concurrency)* | Xây dựng hạ tầng test HTTP song song (SQLite WAL file hoặc SQL Server/LocalDB), viết & chạy toàn bộ test giả lập đa người dùng đồng thời + test hồi quy toàn hệ thống. |

> **Quy tắc bắt buộc cho Dev 1–4 (`ponytail`):** Thay đổi tối thiểu nhưng giải quyết triệt để bài toán; không trùng lặp logic; không tạo abstraction/layer thừa khi chỉ có 1 implementation; giữ nguyên toàn bộ comment/docstring không liên quan; **tuyệt đối không sửa `frontend/`**.

### 3.2. Quy trình phối hợp (Workflow 5 bước)
1. **Bước 1 — Chốt Contract & Migration (Tech Lead):** Tạo các Entity mới (`Users.TokenVersion`, `IdempotencyKeys`, `RestaurantTable.QrToken`, `TableReservation`, `Order.CustomerUserId/ReservationId`, `CashierShift`, `Payment.ShiftId`) và 1–2 EF Core Migration sạch, thống nhất DTO/Interface để Dev 1–4 không giẫm chân lên nhau.
2. **Bước 2 — Triển khai song song (Dev 1, Dev 2, Dev 3, Dev 4):** Mỗi Dev làm việc trong workspace riêng (`branch` hoặc `share` hoặc phân tách file rõ ràng), tuân thủ skill `ponytail`.
3. **Bước 3 — Code Review nội bộ (Tech Lead):** Tech Lead kiểm tra chéo các điểm chạm (`Program.cs`, `AppDbContext.cs`, `OrderService.cs`, `PaymentService.cs`), đảm bảo không lọt thay đổi sang `frontend/`, không còn khóa toàn cục hay lỗ hổng phân quyền.
4. **Bước 4 — Kiểm thử & Giả lập Concurrency (QA Engineer):** Chạy toàn bộ test cũ + bộ test V2 mới (xem Phần 4).
5. **Bước 5 — Sửa lỗi & Nghiệm thu (Dev + Tech Lead + QA):** Dev fix mọi bug QA/Lead phát hiện -> QA chạy lại toàn bộ `dotnet test` -> Tech Lead xác nhận pass 100% trước khi kết thúc.

---

## Phần 4: Kế hoạch QA & Giả lập Nhiều người bấm cùng lúc

### 4.1. Đánh giá phương pháp giả lập: `new Thread` vs `Task.WhenAll` + Cổng đồng bộ
- **Đánh giá:** Không nên dùng `new Thread(...)` thuần vì `HttpClient` và `IOrderIdempotencyStore.ExecuteAsync` đều là `async Task`. Ép chờ đồng bộ dễ gây **ThreadPool starvation** và bị lệch pha vài mili-giây do OS schedule.
- **Cách chuẩn nhất trong ASP.NET Core:** Dùng **`Task.WhenAll(...)` + `TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)`** (hoặc `SemaphoreSlim(0)`) kết hợp `WebApplicationFactory<Program>`. Tất cả `N` request cùng dừng chờ tại `await startGate.Task`, khi gọi `startGate.SetResult()` cả `N` request xuất phát tại **cùng 1 thời khắc**.
- **Hạ tầng DB khi test song song:** `TestBase` hiện tại dùng 1 `SqliteConnection("DataSource=:memory:")` dùng chung nên không chạy song song nhiều HTTP request được. QA sẽ tạo helper dùng SQLite file tạm chế độ `WAL` (mỗi request mở connection riêng) hoặc SQL Server/LocalDB để test transaction `Serializable` thật.

### 4.2. Bộ kịch bản test bắt buộc của QA
1. **Bão bấm đúp (`Same Key + Same Payload`):** 20 request đồng thời cùng `Idempotency-Key` -> `create` chỉ chạy **1 lần**, cả 20 response trả về cùng kết quả, tồn kho chỉ trừ 1 lần.
2. **Thông lượng đa bàn (`Different Keys`):** 10 request với 10 key khác nhau; bên trong delegate `create` đặt `TaskCompletionSource` chờ đủ 10 request cùng vào rồi mới nhả (timeout 2s) -> chứng minh không còn khóa toàn cục.
3. **Phục hồi sau sập server giữa chừng (`Hybrid DB Idempotency`):**
   - Dòng `Completed` trong DB vẫn trả kết quả cũ dù cache RAM đã bị xóa (mô phỏng restart server).
   - Dòng `InProgress` còn hạn -> trả `409 Conflict`; dòng `InProgress` quá lease timeout (mô phỏng server crash giữa chừng) -> cho phép xử lý lại an toàn.
4. **Sai payload cùng key:** Trả `409 Conflict` cả khi chạy tuần tự lẫn đồng thời.
5. **Tranh chấp đặt bàn (`Reservation Overlap Race`):** 2 khách đặt trùng bàn & trùng giờ cùng lúc -> 1 thành công, 1 `409 Conflict`; 2 khung giờ kề nhau (`18:00–19:00` và `19:00–20:00`) đều thành công.
6. **Tranh chấp mở/đóng ca (`Shift Race`):** Mở 2 ca cùng lúc -> 1 thành công, 1 `409 Conflict`; đóng ca kép hoặc đóng ca song song với thu tiền không làm lệch `ExpectedCash`.
7. **Thu hồi JWT tức thì:** Khóa user / đổi role -> JWT cũ ngay lập tức bị trả **`401 Unauthorized`**.
8. **Public API vs Auth Guard & Chặn Order từ xa:** `GET /api/menu`, `GET /api/foods`, `GET /api/categories`, `GET /api/public/tables` trả `200 OK` không cần token; `POST /api/reservations` và `POST /api/orders` không có token phải trả `401 Unauthorized`; `Customer` gọi `POST /api/orders` hoặc `AddItems` mà không có `TableQrToken` hợp lệ (dù truyền `TableId` hay `ReservationId`) phải bị chặn **`403 Forbidden`**; ngược lại `Customer` có `TableQrToken` hợp lệ hoặc nhân viên `Waiter`/`Admin` phải tạo đơn thành công; tiền cọc đặt bàn (`DepositAmount`) phải được khấu trừ đúng khi thanh toán hóa đơn.
