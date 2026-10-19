# Audit backend ↔ frontend — ghi chú kiểm tra (read-only)

Ngày: 2026-10-19 · Nhánh: `hai` · HEAD lúc audit: `811055a` (local, chưa push)
Phạm vi: đọc để **ghi chú**, **không sửa code**. Không đụng `frontend/`, `wwwroot/`, `Migrations/`.

Mục đích: trả lời 4 câu hỏi của chủ dự án

1. Các service có hoạt động "trơn tru" không?
2. API đã đủ cho frontend chưa?
3. Cần bổ sung/chỉnh sửa gì?
4. (Sau khi note) nên làm gì tiếp?

Tài liệu liên quan: `endpoint-baseline.md` (hợp đồng API + quirk), `data-access.md` (GĐ5), `refactor-plan.md` (lộ trình GĐ1–GĐ8).

---

## ⚠️ Trạng thái sau audit

**Nhóm A (6 endpoint thiếu + CORS) đã được xử lý ở commit `afa4664`** — xem mục 3.1 và mục 5.

| Mục trong tài liệu này | Trạng thái |
|---|---|
| 3.1 — 6 endpoint frontend gọi mà backend thiếu | ✅ **Đã bù** (`afa4664`) |
| 5.1 — CORS `FrontendPolicy` vô hiệu | ✅ **Đã bật `app.UseCors`** (`afa4664`) |
| 4 — field hiển thị thiếu (Nhóm C/GĐ7) | ⏳ Chưa làm |
| 5 — frontend không gửi `Authorization` | ⏳ Chưa (việc phía frontend) |
| 6 — defect nghiệp vụ (Nhóm B) | ⏳ Chưa làm |

Các số liệu ở mục 1 (156 test) là **tại thời điểm audit**; sau `afa4664` là **176 test**.

---

## 0. Bối cảnh: "bộ" spec gốc đã tìm thấy

| Nguồn | Nội dung | Ghi chú |
|---|---|---|
| `..\Entity.pdf` | Danh sách **class entity C#** đầy đủ (Category, Food, Order, Payment, Promotion, Recipe, Inventory, Users…) | Trích xuất được bằng script (CMap): bản sao tại `D:\Temp\opencode\entity-pdf.txt` |
| `..\Quản lý đặt dự án nhà hàng.docx` | Đặc tả **bảng/cột snake_case** | Chỉ 789 ký tự text; phần lớn là ảnh ERD |

**Kết luận đối chiếu spec ↔ code:** `Domain\Entity\**` khớp gần như **1:1** với `Entity.pdf`, *kể cả các typo* (`Quanlity`, `KitchenOrderItem.Note`…). Khác biệt duy nhất:

- Spec ghi `RestaurantTable.TableNumer` (sai chính tả) → code dùng `TableNumber`, và migration `20260926024608_UpdateDatabase` là để rename cột đó. **Cố ý sửa typo** — OK.
- **Mâu thuẫn giữa 2 spec**: docx ghi `promotion_foods(promotion_id, food_id)`, nhưng `Entity.pdf` **và** code đều là `PromotionFood.SizeId`. Cần chủ dự án xác nhận cái nào đúng.

Entity **không có navigation property** (không `ICollection<OrderItem>`), nên toàn bộ join đều làm thủ công → đây là gốc rễ của phần lớn "field thiếu" ở mục 4.

---

## 1. Lưới an toàn hiện tại (đã chạy lại, khớp baseline)

| Cổng | Kết quả | Baseline trong docs |
|---|---|---|
| `dotnet build --no-incremental` | **14 warning / 0 error** | 14 / 0 ✅ |
| `dotnet test` | **156/156 pass** | 156/156 ✅ |
| `has-pending-model-changes` | **No changes** | No changes ✅ |

Test: 128 attribute `[Fact]/[Theory]` → 156 case khi chạy. Vẫn là test **tầng service + DbContext** (SQLite in-memory), **chưa có test tầng HTTP**.

---

## 2. Kiến trúc hiện tại (nhìn từ code, không suy từ tên thư mục)

| Tầng | Thực tế |
|---|---|
| `Domain\Entity` | Entity thuần, không phụ thuộc EF Core ✅ |
| `Application/Services` | **20 service** dùng trực tiếp `AppDbContext` |
| `Application/Features/Auth` | `AuthService`, `TokenService`, `JwtOptions`, `AuthPolicies` |
| `Application/DTO` | Request/Response DTO cho food catalog + auth; các nhóm khác trả **entity thô** |
| `Infrastructure/Persistence` | `AppDbContext` (duy nhất) |
| `Infrastructure/Auth` | JWT wiring, policy provider, `ICurrentUser` |
| `Controllers` | 13 controller |

### "Các repository" — không còn tồn tại (có chủ đích)

GĐ5 đã **bỏ** `IRepository`/`IUnitOfWork`/`Repository<T>`/`UnitOfWork`; cả 9 service (nay 20) dùng `AppDbContext` trực tiếp, giữ nguyên semantics tracking. Đây là quyết định đã ghi trong `data-access.md`, **không phải lỗi**. Đánh đổi cần nhớ:

- Không có lớp chặn truy vấn dùng chung ⇒ mỗi service tự lo `AsNoTracking`, tự lọc `IsActive`.
- Không có `rowversion`/concurrency token ⇒ mọi read-modify-write là last-writer-wins (xem mục 6).
- Không có global query filter ⇒ soft-deleted rows lộ ra nếu service quên lọc.

---

## 3. Tổng quan API

Khoảng **~65 endpoint** qua 13 controller. Frontend thực hiện **44 lời gọi** → **37 khớp**, **7 không có ở backend**.

- JSON casing: MVC dùng `JsonSerializerDefaults.Web` ⇒ **camelCase** (không có `AddJsonOptions` nào). Frontend đọc camelCase ⇒ **khớp casing toàn bộ**, không có mismatch kiểu chữ.
- Không có **request-body mismatch** nào (field thừa như `saleUnit` bị System.Text.Json bỏ qua im lặng).

### 3.1 Bảy endpoint frontend gọi mà backend KHÔNG có (chặn chức năng)

| # | Frontend gọi | File:line | Hệ quả |
|---|---|---|---|
| 1 | `GET /setup/status` | `login.js:40` | Trang login **luôn** rơi vào nhánh "server chưa kết nối" |
| 2 | `GET /setup/status` | `setup.js:34` | Trang setup **không chạy được** (báo không kết nối được máy chủ) |
| 3 | `POST /setup/first-admin` | `setup.js:81` | **Không thể tạo Admin đầu tiên** từ UI |
| 4 | `GET /users` | `employees.js:98` | Trang **nhân viên** không tải được danh sách |
| 5 | `POST /users` (+ `roleName`) | `employees.js:142` | **Không thể tạo nhân viên** từ UI |
| 6 | `PATCH /foods/{id}/active` | `foods.js:609` | Nút "kích hoạt lại món" lỗi (backend là `/availability`) |
| 7 | `PUT /management/recipes/{id}` | `recipes.js:356` | Nút **Sửa công thức** lỗi (backend chỉ có GET/POST/DELETE) |

backend **không có** `SetupController`/`SetupService` nào (grep toàn solution: 0 match "setup").
`UsersController` chỉ có **đúng 1** route: `POST /api/users/{userId}/roles`.

Lưu ý #5: `POST /api/auth/register` **có** tồn tại (AdminOnly) nhưng **khác path** và **không nhận `roleName`** → không thay thế được `/users`.

### 3.2 Endpoint backend không frontend nào dùng (bề mặt chết)

| Controller | Không dùng |
|---|---|
| `AuthController` | `POST /register` |
| `UsersController` | `POST /{userId}/roles` — *(endpoint duy nhất của controller này)* |
| `FoodsController` | `GET /{id}` |
| `CategoriesController` | `GET /{id}`, `PUT /{id}`, `DELETE /{id}` |
| `FoodSizesController` | `GET .../sizes/{id}`, `PUT`, `PATCH .../default`, `DELETE` |
| `ToppingsController` | **toàn bộ 6 route** |
| `FoodToppingsController` | **toàn bộ 3 route** |
| `OrdersController` | `PATCH /{id}/status`, `PATCH /{id}/promotion/{promotionId}` |
| `PaymentsController` | `GET /order/{orderId}` |
| `ManagementController` | suppliers (4), `inventory/adjust`, `purchase-orders` POST + receive, **promotions (5)**, `ingredients` PUT/DELETE |
| `RestaurantTablesController` | `PUT /{id}` |

⇒ **Không có trang promotions nào** ở frontend dù backend đã đủ API (5 route) và `OrderService` đã hỗ trợ áp khuyến mãi.

---

## 4. Field backend trả THIẾU so với frontend đọc

Đây là nhóm lỗi "âm thầm" — không 500, chỉ hiển thị sai/trống.

| # | Field frontend đọc | Nơi | Backend thực tế | Hệ quả quan sát được |
|---|---|---|---|---|
| 1 | `item.foodName`, `item.sizeName`, `item.quantity`, `item.saleUnit` | `kitchen.js:77-80` | `KitchenOrderItem` chỉ có `quanlity` (typo) + không join food/size | Board bếp hiện **"Món #<id>"**, không cỡ, **số lượng 0** |
| 2 | `order.items[]` (kèm `foodName`, `sizeName`, `quantity`, `unitPrice`) | `payment.js:56-64` | `Order` không có `Items`; `GET /orders/{id}` trả entity `Order` | Hoá đơn thu về **1 dòng tổng** thay vì chi tiết món |
| 3 | `recipe.ingredientCount` | `recipes.js:207` | `Recipe` entity không có field này | Chi tiết hiện **"undefined nguyên liệu"** |
| 4 | `details.foodName`, `details.sizeName` | `recipes.js:213` | `RecipeResponse` chỉ có `sizeId`, không join tên | Tiêu đề chi tiết công thức hiện **"undefined"** |
| 5 | `food.saleUnit`, `food.allowsFractionalQuantity` | `order.js:83-85`, `foods.js:85` | Không tồn tại trong entity `Food` | **Hiện KHÔNG gây lỗi** (xem 4.1) |

### 4.1 Vì sao #5 hiện vô hại (đã kiểm chứng)

`frontend/js/common/food-units.js:2` đặt `weightSalesEnabled = false`, nên:

- `normalize(unit)` **luôn** trả `"portion"` bất kể đầu vào ⇒ thiếu `saleUnit` không đổi hiển thị.
- `allowsFractionalQuantity(unit)` **luôn** `false` ⇒ bước tăng/giảm số lượng luôn là `1`.

⇒ Rủi ro "gửi số lượng thập phân vào `int Quantity`" là **dormant** (không thể chạm tới ở cấu hình hiện tại), **không phải lỗi đang hoạt động**. Nếu sau này bật cân bán thì đây thành lỗi thật.

### 4.2 Vấn đề hiệu năng ở frontend (không phải lỗi backend, nhưng đáng ghi)

- `foods.js:67` và `recipes.js:78` gọi **N+1 HTTP**: 1 request `/foods`, rồi 1 request `/foods/{id}/sizes` **cho từng món**. Trang thực đơn/công thức sẽ chậm dần theo số món.
- `kitchen.js:53-61`: mỗi ticket thêm 2-3 request (items + order + table) → **N+1 HTTP** trên board bếp.

---

## 5. Chuỗi Auth chưa khép kín (vấn đề ưu tiên cao nhất)

GĐ6 đã làm xong phần backend (token, policy, actor), nhưng **end-to-end chưa nối**:

| Mắt xích | Trạng thái |
|---|---|
| Backend sinh token khi login | ✅ `AuthController.cs:45`, `TokenService` |
| Frontend lưu token | ✅ `login.js:132` (`result.data` vào `sessionStorage`) |
| Frontend **gửi** token | ❌ `api.js:5-11` **không bao giờ** set `Authorization` |
| Backend enforce | ⏸ `Auth:Enforce=false` (mặc định) |
| Frontend chặn route theo role | ❌ `navigation.js` không có role-gating/auth-guard |

Hệ quả trực tiếp:

- Bật `Auth:Enforce=true` **ngay bây giờ sẽ làm sập toàn bộ frontend** (mọi request 401).
- `kitchen.js:8` đọc `localStorage["restaurant.currentRole"]`, nhưng khoá này **không nơi nào được ghi** (login ghi `sessionStorage["restaurant.currentUser"]`) ⇒ `canUpdateKitchen` **luôn** nhận `"staff"` ⇒ nút cập nhật bếp luôn bị disable. Dù backend đã trả `roles[]` trong login, frontend không dùng.
- Frontend không có màn hình đăng xuất/xoá session.

### 5.1 CORS — cần xác minh runtime

- `Program.cs:14-23` đăng ký policy `FrontendPolicy` cho `http://localhost:5500`, nhưng **không có `app.UseCors(...)`** ở pipeline (`Program.cs:104-116`) ⇒ policy vô hiệu.
- `Program.cs:111` có `app.UseHttpsRedirection()`.
- Frontend mặc định gọi `http://localhost:5225/api` (`api.js:3`).

⇒ [Cần xác minh runtime] Nếu frontend được serve từ `localhost:5500` (Live Server) và backend chỉ bind `http://localhost:5225`, trình duyệt sẽ **chặn cross-origin**. Hiện chưa rõ frontend chạy được là nhờ đâu (có thể chủ dự án đang chạy frontend bằng cách khác, hoặc chưa từng test cross-origin). **Đây là câu hỏi cần chủ dự án trả lời trước khi bàn tiếp.**

---

## 6. Chất lượng service — defect đã kiểm chứng

Chú thích: **[C]** = tôi đọc code và xác nhận; **[P]** = đúng theo code nhưng phụ thuộc điều kiện runtime (đồng thời/DB) nên **chưa kiểm chứng bằng cách chạy**.

### 6.1 Nghiêm trọng (đúng đắn nghiệp vụ / dữ liệu)

| # | Mức | Vị trí | Vấn đề |
|---|---|---|---|
| 1 | **Cao** | `KitchenService.cs:34-36` | `[C]` Ánh xạ trạng thái sai: `Completed`→order `"Ready"`, và **mọi trạng thái khác** (gồm `Cancelled`, `Pending`, `Accepted`) rơi vào `"SentToKitchen"` ⇒ **huỷ ticket bếp lại mở lại đơn**. |
| 2 | **Cao** | `PaymentService.cs:22-25` | `[C]` Check-then-insert, **không transaction**, và `AppDbContext.cs:669` là index **non-unique** trên `Payment.OrderId` ⇒ [P] hai lần submit đồng thời tạo **2 payment "Paid"**. |
| 3 | **Cao** | `OrderService.cs:47-66` vs `:68` | `[C]` Kiểm tra tồn kho **trước khi mở transaction**, và `:123-125` trừ kho **không kiểm lại** ⇒ [P] hai đơn đồng thời cùng qua kiểm tra ⇒ **tồn kho âm / mất update**. |
| 4 | **Cao** | `InventoryService.cs:25-28` | `[P]` Cùng kiểu check-then-mutate, không lock ⇒ tồn kho âm khi điều chỉnh đồng thời. |
| 5 | **Cao** | `PurchaseOrderService.cs:59-91` | `[P]` `Receive` không khoá ⇒ nhận hàng đồng thời **cộng kho 2 lần**. |
| 6 | **Cao** | `MenuService.cs:19` | `[C]` `_db.FoodToppings.Any(...)` nằm **trong projection lazy** → truy vấn DB được thực thi **lúc serialize response**, N+1, và truy cập DB ngoài phạm vi action. |
| 7 | TB | `OrderService.cs:30-34`, `RecipeService.cs:13-52` | `[C]` Recipe khớp chỉ theo `SizeId && IsActive`; index `Recipe.SizeId` **non-unique** ⇒ nhiều công thức active cho 1 size bị **cộng dồn nguyên liệu**. `RecipeService` không chặn tạo trùng. |
| 8 | TB | `OrderService.cs:148-155` | `[C]` `UpdateStatusAsync` nhận **mọi chuỗi** trạng thái: không allow-list, không kiểm transition, không guard rỗng/null. |
| 9 | TB | `PaymentService.cs:20-25` | `[C]` Không kiểm trạng thái đơn ⇒ **đơn `Cancelled` vẫn thanh toán được**; `Amount = order.TotalAmount` có thể = 0. |

### 6.2 Trung bình

| # | Vị trí | Vấn đề |
|---|---|---|
| 10 | `RestaurantTableService.cs:10,11` | `[C]` **Ghi DB trong GET**; ở `GetAll` entity là `AsNoTracking` nên `SaveChanges` là **no-op im lặng** nhưng vẫn tốn round-trip. (Đúng như quirk #10 baseline.) |
| 11 | `RestaurantTableService.cs:11,17` | `[C]` N+1 (`Orders.AnyAsync` mỗi bàn), danh sách không phân trang. |
| 12 | `RestaurantTableService.cs:10-16` | `[C]` **Không method nào có `CancellationToken`**, dù `OrderService`/`PaymentService` gọi vào. |
| 13 | `IngredientService.cs:22-31` | `[C]` Chèn ingredient + inventory bằng **2 `SaveChanges` không transaction** ⇒ có thể có nguyên liệu không có dòng tồn kho. |
| 14 | `PurchaseOrderService.cs:20-56` | `[C]` Không kiểm `Items` rỗng (tạo PO tổng 0đ) + 2 `SaveChanges` không transaction. |
| 15 | `UserService.cs:72-73`, `AuthService.cs:59-60` | `[C]` Dùng **`DateTime.Now`** trong khi các service khác dùng `DateTime.UtcNow` ⇒ timestamp lệch múi giờ. |
| 16 | `ToppingService.cs:57-64,100-108` | `[C]` Kiểm trùng có lọc `IsActive` nhưng unique index `ToppingName` **không lọc** ⇒ tạo lại tên topping đã soft-delete → `DbUpdateException` **không xử lý (500)**. |
| 17 | `AppDbContext.cs:138,141,331,798` | `[C]` Các unique index (`UserName`, `UserEmail`, `ToppingName`, `Ingredient.Name`) **không `HasFilter`** ⇒ va chạm với bản ghi đã soft-delete → 500. |
| 18 | `OrderService.cs:162` | `[C]` Giảm giá % không làm tròn trước khi lưu `decimal(18,2)` ⇒ giá trị lưu khác giá trị trả về. |
| 19 | `OrderService.cs:87` | `[C]` Topping của món **không** kiểm có thuộc `FoodToppings` của món đó. |
| 20 | `OrderService.cs:23` | `[C]` Bàn chỉ kiểm `IsActive`, không kiểm bàn đang bảo trì/đã có khách. |
| 21 | `OrderService.cs:85-86` | `[C]` Giá topping gộp vào `OrderItem.UnitPrice` đã lưu **và** cộng vào `Subtotal` ⇒ `UnitPrice` đã lưu không còn nghĩa "đơn giá" (ai nhân lại sẽ tính thừa). |

### 6.3 Thấp / vệ sinh code

| # | Vị trí | Vấn đề |
|---|---|---|
| 22 | `UserService.cs:15` | `[C]` `.Trim()` **trước** khi kiểm null/rỗng ⇒ NRE nếu `username` null. |
| 23 | `RecipeService.cs:33`, `PromotionService.cs:20,41` | `[C]` `.Trim()` trên field **không `[Required]`** ⇒ NRE khi JSON gửi `null`. |
| 24 | `OrderService.cs:28` | `[C]` `request.Items` không guard (JSON `null` lọt qua `[MinLength]`) ⇒ NRE. |
| 25 | `KitchenService.cs:57` | `[C]` `FirstAsync` ném unhandled nếu thiếu kitchen order cha (các nhánh khác trả `false`). |
| 26 | `PaymentService.cs:24`, `PurchaseOrderService.cs:69` | `[C]` `PaymentMethod` chỉ Trim, không validate; `FirstAsync` inventory ném unhandled nếu nguyên liệu thiếu dòng tồn. |
| 27 | `PurchaseOrderService.cs:64` | `[C]` Chỉ chặn `"Received"`; PO `"Cancelled"` vẫn nhận hàng được. |
| 28 | 8 file (xem dưới) | `[C]` **Không có `CancellationToken`**: `FoodService`, `CategoryService`, `FoodSizeService`, `ToppingService`, `FoodToppingService`, `UserService`, `UserRoleService`, `RoleService`, `RestaurantTableService`. |
| 29 | Nhiều file | `[C]` Dùng `Exception`/`ArgumentException` chung cho lỗi nghiệp vụ, thông điệp dạng chuỗi. |
| 30 | Nhiều file | `[C]` Truy vấn danh sách **không phân trang** (foods, categories, toppings, menu, orders, inventory, purchase-orders, promotions, recipes, suppliers, ingredients, tables). |
| 31 | `FoodSizeService.cs:120-125,172-177,…` | `[C]` `AsNoTracking()` rồi `Update()` để attach lại ⇒ ghi đè thay đổi đồng thời (last-writer-wins). |
| 32 | `FoodSizeService` (clear default + set default) | `[C]` Cùng 1 `SaveChanges` nên đúng trong 1 request, nhưng [P] không có guard độc quyền khi đồng thời ⇒ có thể có 2 size default. |

### 6.4 Dead code & trùng lặp (đã xác minh bằng grep toàn solution)

| Ký hiệu | Vị trí | Bằng chứng |
|---|---|---|
| **Toàn bộ `ManagementService`** | `Application/Services/ManagementService.cs` | Không controller/service nào gọi. Chỉ `Program.cs:89` (DI) và test (`TestBase.cs:92`, `InventoryFlowTests.cs:475/478/481` — chỉ dùng `Kitchen`/`UpdateKitchen`). 22 method bất khả truy cập từ app. |
| `UserRoleService.GetUserRoleByIdAsync` | `UserRoleService.cs:13` | Chỉ có định nghĩa, không nơi nào gọi. |
| `UserRoleService.GetAllUserRolesAsync` | `UserRoleService.cs:28` | Chỉ có định nghĩa, không nơi nào gọi. |

Trùng lặp thật: `ManagementService.Kitchen`/`UpdateKitchen` (`:87-100`) trùng `KitchenService` nhưng **thiếu allow-list** và **không đồng bộ `Order.Status`**. Test đang **pin hành vi lệch** này (`InventoryFlowTests.cs:467-481`) — tức là có chủ ý, nhưng gây 2 hành vi cho cùng nghiệp vụ.

### 6.5 Điểm tốt (nên giữ)

- Unique index bao phủ hầu hết quy tắc nghiệp vụ quan trọng; FK có `OnDelete` tường minh.
- `OrderService` snapshot `UnitPrice`; `PaymentService.Amount` lấy từ server (không tin client) — đúng như yêu cầu skill.
- `TokenService` dùng `DateTime.UtcNow`, trả `null` an toàn khi chưa cấu hình khoá.
- Soft delete nhất quán (`IsActive=false`), không hard-delete lịch sử.
- `RecipeService` có transaction + validate nguyên liệu trùng/rỗng.

---

## 7. Tổng hợp theo trang frontend (cái gì chạy được?)

| Trang | Trạng thái thực tế |
|---|---|
| `index.html` (dashboard) | Dữ liệu **tĩnh** (`dashboard.data.js`), không gọi API |
| `login.html` | Login API khớp, nhưng `GET /setup/status` **thiếu** ⇒ luôn hiện cảnh báo "chưa kết nối" |
| `setup.html` | **Không chạy được** (thiếu cả 2 endpoint setup) |
| `foods.html` | Chạy phần lớn; nút "kích hoạt lại" **lỗi** (`/active` vs `/availability`); không quản lý toppings |
| `tables.html` | Chạy được |
| `order.html` | Chạy được (menu + tạo đơn) |
| `kitchen.html` | Tải được nhưng **hiển thị sai** (Món #id, SL 0); nút cập nhật luôn bị disable do role |
| `payment.html` | Tính tiền đúng nhưng **không có chi tiết món** (chỉ 1 dòng tổng) |
| `recipes.html` | Tải/xoá/tạo được; **Sửa lỗi** (thiếu PUT); hiển thị "undefined" |
| `inventory.html` | Chỉ **hiển thị** danh sách; các nút nghiệp vụ là placeholder |
| `purchases.html` | Chỉ **hiển thị** danh sách; không tạo/nhận phiếu từ UI |
| `employees.html` | **Không chạy được** (thiếu GET/POST `/users`) |
| `roles.html` | **Placeholder tĩnh** (chỉ `navigation.js` + `management.js`) |
| `reports.html` | **Placeholder tĩnh** (chưa có số liệu) |
| `account.html` | **Placeholder tĩnh** |
| *promotions* | **Không có trang** |

---

## 8. Gợi ý thứ tự xử lý (chưa code — chờ bạn chốt)

Xếp theo "giá trị / rủi ro", tách rõ nhóm **không đổi hành vi** và nhóm **đổi hành vi/schema**.

### Nhóm A — chặn chức năng, cần làm sớm (đổi hành vi, cần bạn duyệt)

- A1. `GET /setup/status` + `POST /setup/first-admin` (SetupController/Service). Cần chốt luật: chỉ cho tạo khi **chưa có user nào**; không public sau đó.
- A2. `GET /users` + `POST /users` (kèm gán `roleName`) — hoặc thống nhất dùng `/auth/register` + `/users/{id}/roles` và **sửa frontend** (cần bạn quyết bên nào đổi).
- A3. `PUT /management/recipes/{id}`.
- A4. `PATCH /foods/{id}/active` **hoặc** đổi frontend sang `/availability` (nhỏ hơn nhiều).

### Nhóm B — chất lượng dữ liệu/nghiệp vụ (đổi hành vi, cần duyệt)

- B1. `KitchenService.cs:34-36` — sửa ánh xạ `Cancelled`/`Pending`/`Accepted`.
- B2. Allow-list + transition cho `OrderService.UpdateStatusAsync`.
- B3. Chặn đơn `Cancelled`/`Paid` thanh toán sai; thêm **unique index** `Payment.OrderId` (⇒ **đổi schema**).
- B4. Bọc transaction cho `PaymentService`, `IngredientService`, `PurchaseOrderService.CreateAsync`.
- B5. Chặn nhiều recipe active / 1 size (`RecipeService`) + cân nhắc unique index (**đổi schema**).
- B6. `HasFilter` cho unique index soft-delete (**đổi schema**).
- B7. Concurrency token/`rowversion` cho `Inventory`/`RestaurantTable`/`Order` (**đổi schema**) — quyết định lớn.
- B8. `DateTime.Now` → `UtcNow`; guard null cho các `.Trim()`.

### Nhóm C — đổi hợp đồng JSON (cần phối hợp frontend) = GĐ7

- C1. `Order.items[]` (kèm tên món/cỡ) cho `GET /orders/{id}` và hoá đơn.
- C2. `KitchenOrderItem`: trả `foodName`/`sizeName`/`quantity` (đúng chính tả) thay `quanlity`.
- C3. `Recipe` list: thêm `ingredientCount`; detail: thêm `foodName`/`sizeName`.
- C4. Bỏ trả entity thô (`Ingredient`, `Supplier`, `Inventory`, `PurchaseOrder`, `Promotion`, `Recipe`, `KitchenOrder*`) → DTO.

### Nhóm D — không đổi hành vi (an toàn, làm bất cứ lúc nào)

- D1. **GĐ8**: tách seed/`Migrate()` khỏi `Program.cs` → mở đường test HTTP (điều kiện cho 6e).
- D2. Xoá `ManagementService` + `UserRoleService.GetUserRoleByIdAsync`/`GetAllUserRolesAsync` (dead code) — **nhưng** phải chuyển 2 test `InventoryFlowTests.cs:475-481` sang `KitchenService` (đổi test).
- D3. Thêm `CancellationToken` cho 9 service còn thiếu.
- D4. N+1 trong `MenuService` + N+1 HTTP ở frontend (sửa frontend cần duyệt riêng).
- D5. Bỏ ghi DB trong GET (`RestaurantTableService`) — **đổi hành vi** (quirk #10) nên cần duyệt.

---

## 9. Điều tôi CHƯA xác minh (cần bạn hoặc cần runtime)

1. **CORS/HTTPS thực tế**: frontend hiện có thực sự gọi được `localhost:5225` không, và bằng cách nào? (chưa chạy app vì `Program.cs` tự `Migrate()` vào SQL Server thật).
2. Toàn bộ kết luận `[P]` về **đồng thời** (double payment, tồn kho âm) — đúng theo code nhưng chưa test bằng request song song.
3. Có phải SQL Server `HAI2\SQLEXPRESS` đang sẵn dùng? (connection string trong `appsettings.json`).
4. `promotion_foods` đúng là `SizeId` hay `FoodId` (2 spec mâu thuẫn).
5. Chủ dự án muốn **frontend hay backend** đổi ở các điểm lệch path (`/active`, `/users`) — vì cả hai cách đều hợp lệ.

---

## 10. Kết luận ngắn

- **Backend "chạy trơn tru" ở tầng service cơ bản, nhưng KHÔNG khép kín với frontend**: 7 endpoint thiếu, ~5 nhóm field thiếu, auth chưa nối, CORS có nghi vấn.
- **Nghiệp vụ có lỗ hổng thật**: ánh xạ trạng thái bếp sai (`Cancelled`), double-payment khả thi, trừ kho không khoá, recipe cộng dồn.
- **Code vệ sinh còn nợ**: `ManagementService` chết, 9 service thiếu `CancellationToken`, nhiều truy vấn không phân trang, `DateTime.Now` lẫn lộn.
- **Điểm mạnh**: entity khớp spec gần tuyệt đối; unique index & FK phủ tốt; snapshot giá đúng; soft delete nhất quán; test 156/156 xanh.

Ghi chú: **không file code nào bị sửa** trong phiên audit này; chỉ tạo tài liệu này (và script trích PDF đặt ngoài repo ở `D:\Temp\opencode\`).
