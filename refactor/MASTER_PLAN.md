# Kế Hoạch Refactor Backend (Đợt 2) — Seed DB & Hoàn Thiện Nghiệp Vụ

> **Nguyên tắc thực thi:**
> 1. **Không chạy trên Test**: Chạy và kiểm chứng trực tiếp trên SQL Server DB thật (`DefaultConnection`).
> 2. **Tạo Seed dữ liệu cơ bản trước (Phase 1)** để có sẵn dữ liệu chuẩn trên DB phục vụ kiểm tra các Phase sau.
> 3. Thực hiện **từng Phase**: Sửa code $\rightarrow$ `dotnet build --nologo -v q` + chạy thử trên DB $\rightarrow$ cập nhật `[x]` vào file này $\rightarrow$ `git commit` ngay.
> 4. Quy tắc chi tiết xem tại [`.antigravity/SKILL.md`](../.antigravity/SKILL.md).

---

## Bảng Theo Dõi Tiến Độ

| Phase | Nội dung chính | Mức ưu tiên | Trạng thái | Commit |
| :--- | :--- | :--- | :--- | :--- |
| **Baseline** | Mốc sau đợt refactor 1 | Mốc an toàn | `[x] Done` | `7bc197d` |
| **Phase 1** | Tạo file Seed dữ liệu cơ bản & áp trực tiếp lên DB | **Must Do First** | `[x] Done` | `b12382b` |
| **Phase 2** | Sửa 4 lỗi nghiệp vụ cốt lõi (MoMo, Nhập kho, Hoàn tiền, Menu POS) | **Must Fix** | `[x] Done` | `pending` |
| **Phase 3** | Chuẩn hóa Exception Controller & ràng buộc xóa mềm `IsActive` | **Should Fix** | `[ ] Todo` | - |
| **Phase 4** | Gom code trùng lặp & thu gọn `OrderService` / `OrderCancellation` | **Should Fix / Clean** | `[ ] Todo` | - |

---

## Chi Tiết Từng Phase

### Phase 1: Tạo File Seed Dữ Liệu Cơ Bản Trên DB Thật
**Mục đích:** Tạo dữ liệu nền đầy đủ (idempotent — chỉ thêm khi chưa có) để chạy và test trực tiếp mọi luồng POS, Bếp, Kho, Thu ngân trên DB.

- [x] **1.1. Tạo `BasicDataSeeder.cs` và gọi trong `DatabaseInitializer.InitializeAsync`**
  - **File:** `Infrastructure/Persistence/BasicDataSeeder.cs` (mới), `Infrastructure/Persistence/DatabaseInitializer.cs`
  - **Dữ liệu seed cơ bản:**
    1. **Cấu hình (`RestaurantSettings`):** VAT 8%, phí phục vụ 0%, tên & địa chỉ nhà hàng.
    2. **Tài khoản mẫu (`Users` + `UserRoles`):** 5 tài khoản ứng với 5 Role (`admin`, `waiter`, `chef`, `cashier`, `warehouse` — mật khẩu mặc định `123456` băm BCrypt).
    3. **Bàn ăn (`RestaurantTables`):** Seed các bàn mẫu (`B01`–`B06`) phân bổ vào 3 khu vực (`Sảnh chính`, `Lầu 2`, `Ngoài trời`).
    4. **Kho & Nhà cung cấp (`Suppliers`, `Ingredients`, `Inventories`):** 2 nhà cung cấp, 6–8 nguyên liệu cơ bản (thịt bò, gà, mì/bánh phở, rau, phô mai, trà, sữa...) kèm tồn kho ban đầu dồi dào.
    5. **Thực đơn (`Categories`, `Foods`, `FoodSizes`, `Toppings`, `FoodToppings`):** 3 danh mục, 4–5 món ăn kèm Size (`S`, `M`, `L`) và Topping mẫu.
    6. **Công thức (`Recipes`, `RecipeIngredients`):** Tạo công thức `IsActive = true` cho toàn bộ `FoodSizes` đã seed để POS đặt món trừ kho chạy được ngay.
- **Kiểm tra & Commit:** Build + chạy app để tự động seed vào DB $\rightarrow$ `git commit -m "feat(phase-1): add basic database seeder for direct db execution"`

---

### Phase 2: Sửa 4 Lỗi Nghiệp Vụ Cốt Lõi (Must Fix)
**Mục đích:** Khóa chặt các lỗ hổng thanh toán MoMo, cảnh báo tồn kho sau nhập hàng, hoàn tiền đè bàn, và lọc thực đơn POS.

- [x] **2.1. Đồng bộ điều kiện & Transaction cho MoMo**
  - **File:** `Application/Services/Payment/PaymentService.cs`
  - **Việc cần làm:**
    - `CreateMomoIntentAsync`: kiểm tra mọi món active phải `Ready`/`Completed` và `order.TotalAmount > 0` (giống `CreateAsync`).
    - `ApplyMomoNotificationAsync`: bọc `BeginTransactionAsync`, chuyển các `Payment` `Pending` khác của đơn sang `Cancelled`, bắt `DbUpdateException` (unique index `[Status] = 'Paid'`) để trả kết quả idempotent.
- [x] **2.2. Reset `Ingredient.StockStatus` khi nhận hàng nhập kho**
  - **File:** `Application/Services/Suplier/PurchaseOrderService.cs` (`ReceiveAsync`)
  - **Việc cần làm:** Khi cộng tồn kho mà `stock.Quanlity > 0` và `ingredient.StockStatus` đang là `OutOfStock` hoặc `Spoiled`, tự động cập nhật về `Available`.
- [x] **2.3. Chặn hoàn tiền (`RefundAsync`) mở lại đơn vào bàn đang có khách mới**
  - **File:** `Application/Services/Payment/PaymentService.cs` (`RefundAsync`)
  - **Việc cần làm:** Nếu `order.TableId != null` đã có đơn mở khác (`Status != Paid && Status != Cancelled`), ném `InvalidOperationException` (409) không cho mở lại đơn cũ đè lên bàn.
- [x] **2.4. Chỉ hiển thị món & size có công thức active trên Menu POS**
  - **File:** `Application/Services/Food/MenuService.cs` (`GetMenuAsync`)
  - **Việc cần làm:** Chỉ trả về `FoodSize` đang `IsActive` và có `Recipe` active; ẩn `Food` nếu không có `FoodSize` nào hợp lệ.
- **Kiểm tra & Commit:** Build + chạy kiểm tra trực tiếp trên DB $\rightarrow$ `git commit -m "fix(phase-2): fix momo transaction, stock status reset, refund table check, and pos menu filter"`

---

### Phase 3: Chuẩn Hóa Exception Controller & Ràng Buộc Soft-Delete (`IsActive`)
**Mục đích:** Trả đúng mã HTTP 409 khi xung đột dữ liệu và ngăn gắn dữ liệu mới vào danh mục/nhà cung cấp đã xóa mềm.

- [ ] **3.1. Xóa `try/catch` sai chuẩn còn sót tại Controller**
  - **File:** `Api/Controllers/User/UsersController.cs`, `Api/Controllers/Auth/AuthController.cs`, `Api/Controllers/Table/RestaurantTablesController.cs`
  - **Việc cần làm:** Xóa các khối `catch (InvalidOperationException)` trả nhầm `400 BadRequest` và `catch (DbUpdateException)` bắt lỗi diện rộng; để `ApiExceptionFilter` xử lý đồng nhất.
- [ ] **3.2. Bổ sung kiểm tra `IsActive` & chống trùng tên**
  - **File:** `Application/Services/Inventory/SupplierService.cs`, `Application/Services/Food/FoodService.cs`, `Application/Services/Promotion/PromotionService.cs`
  - **Việc cần làm:**
    - `SupplierService`: lọc `IsActive` ở `GetAllAsync`; kiểm tra trùng tên active ở `CreateAsync`/`UpdateAsync`.
    - `FoodService`: kiểm tra `category.IsActive` khi thêm/sửa món; chặn bật `IsAvailable = true` khi `!food.IsActive`.
    - `PromotionService`: kiểm tra `IsActive` cho `FoodSizes` và `Categories` trong `SetTargetsAsync`.
- **Kiểm tra & Commit:** Build + kiểm tra trên DB $\rightarrow$ `git commit -m "fix(phase-3): remove remaining controller try-catch and enforce active entity checks"`

---

### Phase 4: Gom Code Trùng Lặp & Thu Gọn God Class (~350 dòng)
**Mục đích:** Xóa các đoạn code copy-paste trong `OrderService`, `OrderCancellation`, `OrderItemMutations` và 9 Service CRUD.

- [ ] **4.1. Thu gọn `OrderService.AdjustItemQuantityAsync` & gom validate `IdempotencyKey`**
  - **File:** `Application/Services/Order/OrderService.cs`, `Application/Services/Payment/PaymentService.cs`, `Application/Common/OrderIdempotencyStore.cs`
  - **Việc cần làm:**
    - Tái sử dụng hàm nội bộ `ReduceAsync(lines, allowReturn: false)` cho nhóm `preparedItems` trong `AdjustItemQuantityAsync` (xóa ~65 dòng lặp).
    - Chuyển hàm kiểm tra độ dài & chuẩn hóa `idempotencyKey` vào `OrderIdempotencyStore` (xóa ~60 dòng lặp ở 4 hàm `*IdempotentAsync`).
- [ ] **4.2. Hợp nhất hàm tính `NetConsumption` & hoàn kho nguyên liệu**
  - **File:** `Application/Common/OrderCancellation.cs`, `Application/Common/OrderItemMutations.cs`
  - **Việc cần làm:** Dùng chung `OrderItemMutations.ComputeNetConsumption` tại `OrderCancellation` và gộp logic hoàn kho giữa `ReturnStockForPartialItemAsync` và `ReturnStockForItemAsync`.
- [ ] **4.3. Gom `DeleteAsync` $\rightarrow$ `SetActiveAsync(id, false)` và các helper trùng lặp**
  - **File:** 9 Service (`Food`, `FoodSize`, `Category`, `Topping`, `Ingredient`, `Supplier`, `Recipe`, `Promotion`, `TableArea`), `UserService.cs`, `AuthService.cs`, `AppDbContext.cs`, `FoodToppingService.cs`
  - **Việc cần làm:**
    - Cho `DeleteAsync(id)` gọi `SetActiveAsync(id, false)` ở 9 Service.
    - Gom hàm validate nguyên liệu công thức trong `RecipeService`, hàm kiểm tra Admin cuối cùng, hàm cập nhật `RowVersion` trong `AppDbContext`, và xóa các lệnh `_db.Update(...)` thừa trên entity đang tracked.
- **Kiểm tra & Commit:** Build + kiểm tra trên DB $\rightarrow$ `git commit -m "refactor(phase-4): deduplicate order adjustment, stock return, idempotency, and soft-delete methods"`
