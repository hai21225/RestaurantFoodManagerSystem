# Kế Hoạch Refactor Backend (Đợt 2 & 3) — Seed DB, Nghiệp Vụ & Tối Ưu Kiến Trúc (Mục 1–11)

> **Nguyên tắc thực thi:**
> 1. **Không chạy trên Test**: Chạy và kiểm chứng trực tiếp trên SQL Server DB thật (`DefaultConnection`).
> 2. Thực hiện **từng Phase**: Sửa code $\rightarrow$ `dotnet build --nologo -v q` + chạy thử trên DB $\rightarrow$ cập nhật `[x]` vào file này $\rightarrow$ `git commit` ngay.
> 3. Quy tắc chi tiết xem tại [`.antigravity/SKILL.md`](../.antigravity/SKILL.md).

---

## Bảng Theo Dõi Tiến Độ

| Phase | Nội dung chính | Mục Audit | Trạng thái | Commit |
| :--- | :--- | :--- | :--- | :--- |
| **Baseline** | Mốc sau đợt refactor 1 | — | `[x] Done` | `7bc197d` |
| **Phase 1** | Tạo file Seed dữ liệu cơ bản & áp trực tiếp lên DB | Seed DB | `[x] Done` | `b12382b` |
| **Phase 2** | Sửa 4 lỗi nghiệp vụ cốt lõi (MoMo, Nhập kho, Hoàn tiền, Menu POS) | Mục 1–4 | `[x] Done` | `218300b` |
| **Phase 3** | Chuẩn hóa Exception Controller & ràng buộc xóa mềm `IsActive` | Mục 5–6 | `[x] Done` | `cb2e6b6` |
| **Phase 4** | Gom code trùng lặp & thu gọn `OrderService` / `OrderCancellation` | Mục 8–9 | `[x] Done` | `1a83b36` |
| **Phase 5** | Đồng bộ `includeInactive` danh mục & gom logic User/Auth trùng lặp | Mục 6, 8, 9 | `[x] Done` | `6788af8` |
| **Phase 6** | Giảm Over-engineering (`IsolationLevel.Serializable` & `OrderIdempotencyStore`) | Mục 10 | `[x] Done` | `9d5ba46` |
| **Phase 7** | Khử truy vấn N+1 trong `OrderService` & xóa `.Update()` thừa | Mục 11 | `[x] Done` | `f79120a` |
| **Phase 8** | Thu gọn & tách nhỏ 3 God Class (`OrderService`, `KitchenService`, `AppDbContext`) | Mục 7, 8 | `[x] Done` | `c2e9cd7` |

---

## Chi Tiết Các Phase Đã Hoàn Thành (Phase 1–4)

- [x] **Phase 1 (`b12382b`):** Tạo `BasicDataSeeder.cs` và gọi trong `DatabaseInitializer.InitializeAsync` (seed cấu hình quán, 5 tài khoản theo role, bàn ăn `B01`–`B06`, nhà cung cấp, nguyên liệu + tồn kho, danh mục, món ăn, size, và công thức active).
- [x] **Phase 2 (`218300b`):** Đồng bộ kiểm tra & transaction `Serializable` cho thanh toán MoMo (`PaymentService`); tự động reset `Ingredient.StockStatus = Available` khi nhận hàng nhập kho (`PurchaseOrderService`); chặn hoàn tiền mở lại đơn vào bàn đang có khách (`PaymentService.RefundAsync`); lọc Menu POS chỉ hiển thị size có công thức active (`MenuService`).
- [x] **Phase 3 (`cb2e6b6`):** Xóa `try/catch` thủ công sai chuẩn tại `UsersController`, `AuthController`, `RestaurantTablesController`; bổ sung kiểm tra `IsActive` và chống trùng tên tại `SupplierService`, `FoodService`, `PromotionService`.
- [x] **Phase 4 (`1a83b36`):** Tái sử dụng `ReduceAsync` trong `OrderService.AdjustItemQuantityAsync`; gom validate `IdempotencyKey` vào `OrderIdempotencyStore.RunIfKeyedAsync`; hợp nhất `ComputeNetConsumption` và `ReturnStockForItemAsync`; gom `DeleteAsync(id) => SetActiveAsync(id, false, ct)` ở 9 Service CRUD.

---

## Chi Tiết Các Phase Tiếp Theo (Mục 6–11)

### Phase 5 (Mục 6, 8, 9): Đồng Bộ `includeInactive` & Gom Logic User/Auth Trùng Lặp
**Mục đích:** Đồng nhất cơ chế lọc xóa mềm (`includeInactive`) cho toàn bộ danh mục quản trị và xóa code lặp giữa `UserService`, `UserRoleService`, `AuthService`, `UsersController`, `AuthController`.

- [x] **5.1. Chuẩn hóa `GetAllAsync(bool includeInactive = false)` cho Kho & Khuyến mãi**
  - **File:** `Application/Services/Inventory/IngredientService.cs`, `Application/Services/Suplier/SupplierService.cs`, `Application/Services/Promotion/PromotionService.cs`, `Api/Controllers/Management/ManagementController.cs`, `Application/Services/Food/ToppingService.cs`
  - **Việc cần làm:**
    - Mặc định `GetAllAsync(bool includeInactive = false, CancellationToken ct = default)` chỉ trả bản ghi `IsActive = true`; truyền `?includeInactive=true` tại `ManagementController` (`ingredients`, `suppliers`, `promotions`) để Admin/Kho xem cả bản ghi đã ẩn.
    - Chặn bật `IsAvailable = true` cho `Topping` đã xóa mềm (`!topping.IsActive`) trong `ToppingService.UpdateAvailabilityAsync`.
- [x] **5.2. Gom truy vấn Role & logic tạo nhân viên / đặt mật khẩu**
  - **File:** `Application/Services/User/UserService.cs`, `Application/Services/Auth/AuthService.cs`, `Api/Controllers/Users/UsersController.cs`, `Api/Controllers/Users/AuthController.cs`
  - **Việc cần làm:**
    - Gom truy vấn lấy danh sách tên Role của user trong `UserService.GetProfileAsync` dùng chung helper với `UserRoleService`.
    - Gom logic kiểm tra & băm mật khẩu mới giữa `AuthService.ChangePasswordAsync` và `AuthService.ResetPasswordAsync`.
    - Bỏ kiểm tra `Password != ConfirmPassword` lặp lại ở `UsersController.Create` và `AuthController.Register` (để `AuthService.Register` kiểm tra tập trung).
- **Kiểm tra & Commit:** Build + kiểm tra API trên DB $\rightarrow$ `git commit -m "refactor(phase-5): standardize includeInactive filters and deduplicate user auth logic"`

---

### Phase 6 (Mục 10): Giảm Over-Engineering (`IsolationLevel.Serializable` & `OrderIdempotencyStore`)
**Mục đích:** Hạ mức cô lập transaction ở các tác vụ CRUD danh mục để tránh khóa bảng/deadlock không cần thiết và làm gọn bộ nhớ đệm chống gửi trùng.

- [x] **6.1. Hạ `IsolationLevel.Serializable` thừa ở CRUD danh mục về `BeginTransactionAsync(ct)`**
  - **File:** `Application/Services/Food/FoodSizeService.cs`, `Application/Services/Promotion/PromotionService.cs`, `Application/Services/Recipe/RecipeService.cs`, `Application/Services/Restaurant/RestaurantTableService.cs`, `Application/Services/Order/OrderService.cs`
  - **Việc cần làm:** Đổi `BeginTransactionAsync(IsolationLevel.Serializable, ct)` thành `BeginTransactionAsync(ct)` (`ReadCommitted`) tại `FoodSizeService` (`CreateAsync`, `UpdateAsync`, `SetDefaultAsync`), `PromotionService.SetTargetsAsync`, `RecipeService.UpdateAsync`, `RestaurantTableService` (`DeleteRestaurantTableAsync`, `SetMaintenanceAsync`), và `OrderService` (`ApplyPromotionAsync`, `RemovePromotionAsync` — đã có `RowVersion` + filtered unique index bảo vệ).
- [x] **6.2. Đơn giản hóa `InMemoryOrderIdempotencyStore`**
  - **File:** `Application/Common/OrderIdempotencyStore.cs`
  - **Việc cần làm:** Thay mảng 64 `SemaphoreSlim` stripes phức tạp và hàm `RemoveExpired()` không khóa bằng 1 `SemaphoreSlim(1, 1)` + `Dictionary<string, Entry>` gọn nhẹ, an toàn luồng tuyệt đối.
- **Kiểm tra & Commit:** Build + kiểm tra trên DB $\rightarrow$ `git commit -m "refactor(phase-6): downgrade unnecessary serializable transactions and simplify idempotency store"`

---

### Phase 7 (Mục 11): Khử Truy Vấn N+1 Trong `OrderService` & Xóa `.Update()` Thừa
**Mục đích:** Giảm số câu lệnh SQL trong transaction tạo đơn / gọi thêm món từ $4 \times N$ về 3 truy vấn batch cố định, xóa lệnh `.Update()` thừa cuối cùng.

- [x] **7.1. Batch query cho `PrepareItemsAsync` và `CalculateItemRequirementsAsync`**
  - **File:** `Application/Services/Order/OrderService.cs`, `Application/Common/OrderItemMutations.cs`
  - **Việc cần làm:**
    - `PrepareItemsAsync`: nạp 1 lần tất cả `FoodSizes` + `Foods` theo danh sách `SizeId` và nạp 1 lần tất cả `Toppings` + `FoodToppings` theo danh sách `ToppingId` trước vòng lặp `foreach`.
    - `CalculateItemRequirementsAsync`: nạp 1 lần tất cả `Recipes` active + `RecipeIngredients` cho mọi `SizeId` trong đơn thay vì gọi DB từng món.
    - Bỏ tham số `oldRequirements` luôn rỗng ở `PrepareStockDeltasAsync`.
- [x] **7.2. Xóa `_db.Toppings.Update(topping)` thừa**
  - **File:** `Application/Services/Food/ToppingService.cs`
  - **Việc cần làm:** Xóa lệnh `_db.Toppings.Update(topping)` trên entity đã được `FindAsync` theo dõi.
- **Kiểm tra & Commit:** Build + kiểm tra đặt món/trừ kho trực tiếp trên DB $\rightarrow$ `git commit -m "perf(phase-7): batch order item preparation queries to eliminate N+1 and remove redundant update"`

---

### Phase 8 (Mục 7, 8): Thu Gọn & Tách Nhỏ 3 God Class (`OrderService`, `KitchenService`, `AppDbContext`)
**Mục đích:** Xóa đoạn code tách dòng lặp lại trong `KitchenService` và tách 3 file > 1.000 dòng thành các file `partial` đúng chức năng (dưới 900 dòng/file) mà không đổi DI hay API.

- [x] **8.1. Gom helper trong `KitchenService` & tách `KitchenService.Queries.cs`**
  - **File:** `Application/Services/Kitchen/KitchenService.cs`, `Application/Services/Kitchen/KitchenService.Queries.cs` (mới)
  - **Việc cần làm:**
    - Gom bước nạp & kiểm tra bộ tứ `(KitchenOrderItem, KitchenOrder, Order, OrderItem)` đang lặp ở `ReopenItemAsync`, `UpdatePreparedQuantityAsync`, `ReportShortageAsync` vào helper chung `LoadMutableItemContextAsync`.
    - Gom bước tách dòng món (`CloneLine` + `CopyToppingsAsync` + `RecordItemHistory` + `KitchenOrderItem` + `SplitItemLedgerAsync`) lặp giữa `UpdatePreparedQuantityAsync` và `ReportShortageAsync` vào helper `SplitKitchenOrderItemLineAsync`.
    - Tách các hàm đọc (`GetOrdersAsync`, `GetItemsAsync`) sang `KitchenService.Queries.cs` (`public sealed partial class KitchenService`).
- [x] **8.2. Tách `OrderService.Queries.cs` & gom truy vấn lịch sử đơn**
  - **File:** `Application/Services/Order/OrderService.cs`, `Application/Services/Order/OrderService.Queries.cs` (mới)
  - **Việc cần làm:** Chuyển các hàm đọc (`GetAsync`, `GetTableBillAsync`, `GetListAsync`, `GetHistoryAsync`, `GetItemHistoryAsync`) sang `OrderService.Queries.cs` (`public sealed partial class OrderService`).
- [x] **8.3. Tách `AppDbContext.ModelConfiguration.cs`**
  - **File:** `Infrastructure/Persistence/AppDbContext.cs`, `Infrastructure/Persistence/AppDbContext.ModelConfiguration.cs` (mới)
  - **Việc cần làm:** Tách hàm `OnModelCreating` (~1.250 dòng cấu hình bảng) sang `AppDbContext.ModelConfiguration.cs` (`public partial class AppDbContext`), giữ `AppDbContext.cs` chỉ còn khai báo `DbSet` và `SaveChanges`/`RowVersion` (~180 dòng).
- **Kiểm tra & Commit:** Build + chạy kiểm tra toàn trình trên DB $\rightarrow$ `git commit -m "refactor(phase-8): deduplicate kitchen item splitting and split god classes into focused partial files"`
