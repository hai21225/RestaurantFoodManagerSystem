# GĐ0 — Baseline hành vi API & lưới an toàn

Mục đích: khóa lại **hành vi hiện tại** của backend trước khi refactor kiến trúc.
Mọi giai đoạn sau (GĐ1–GĐ8) phải giữ nguyên các hợp đồng dưới đây trừ khi có phê duyệt riêng.

- Ngày ghi baseline: 2026-10-19
- Nhánh: `hai`
- Solution: `RestaurantFoodManagerSystem.sln` (1 project web + 1 project test)
- Build baseline: `Build succeeded` — **16 warnings CS8618, 0 errors**
  (toàn bộ là non-nullable property trong entity/DTO, không phải lỗi)
- Test baseline: **105/105 pass** *(tại thời điểm ghi baseline; hiện tại **241/241**, sau D2+D3)*

## Cách chạy lưới an toàn

```powershell
dotnet build RestaurantFoodManagerSystem.sln --no-incremental
dotnet test RestaurantFoodManagerSystem.Tests\RestaurantFoodManagerSystem.Tests.csproj
```

Test dùng **SQLite in-memory** (`EnsureCreated`, không dùng migrations) nên:

- không kết nối SQL Server thật,
- không tạo/không sửa database thật,
- không đổi schema, không chạm `Migrations/`.

SQLite được chọn thay vì EF InMemory vì `OrderService`, `PurchaseOrderService`, `RecipeService`
(trước đây cả `ManagementService`) mở transaction tường minh (InMemory provider không hỗ trợ transaction).

## Phạm vi đã pin bằng test (105 test lúc ghi baseline, hiện 241)

| Nhóm | File | Nội dung được khóa |
|---|---|---|
| Auth | `AuthTests.cs` | login đúng/sai/inactive/blank, register hash BCrypt, trùng username, confirm password, assign role |
| Food catalog | `FoodCatalogTests.cs` | Food/Category/FoodSize/Topping/FoodTopping CRUD, soft delete, default size, trùng tên, lỗi unique index sau soft delete |
| Orders | `OrdersFlowTests.cs` | snapshot giá, recipe, trừ kho, transaction, kitchen ticket, trạng thái bàn, status history, promotion |
| Payments | `PaymentsFlowTests.cs` | amount lấy từ server, chặn thanh toán trùng, refresh trạng thái bàn |
| Kitchen | `KitchenFlowTests.cs` | allow-list trạng thái, timestamp, lan truyền trạng thái order/item |
| Inventory | `InventoryFlowTests.cs` | ingredient/inventory/supplier/purchase/promotion/recipe, adjustment âm, receive purchase |
| Tables | `RestaurantTableTests.cs` | trùng số bàn, tính trạng thái occupied, side-effect đọc, maintenance, soft delete |
| Menu | `MenuTests.cs` | lọc category/food/size/topping, thứ tự category |
| Khởi động/host | `DatabaseInitializerTests.cs`, `ApiHostSmokeTests.cs` | seed 5 role idempotent, cờ `Database:MigrateOnStartup`/`SeedOnStartup`, host dựng bằng `WebApplicationFactory` trên SQLite in-memory |
| Auth HTTP | `AuthHttpTests.cs`, `AuthDisabledHttpTests.cs` | 401 thiếu/sai token, 403 sai role, 200 đúng role (đủ 6 policy + Admin), `[AllowAnonymous]` vẫn 200; baseline enforce=false vẫn anonymous |
| Management DTO (C4) | `ManagementDtoHttpTests.cs`, `DtoJsonContractTests.cs` | `/api/management/*` trả DTO; `inventory` có `quanlity`, không có `rowVersion`; request/response cũ vẫn dùng JSON `quantity`; `KitchenItemResponse` phát cả `quanlity` + `quantity` |

---

# 1. Endpoint inventory (hợp đồng hiện tại)

Quy ước cột:

- **Body lỗi `{message}`** = object JSON `{"message": "..."}`.
- **Body lỗi `"..."`** = **JSON string trần** (ví dụ `"Table not found"`), không phải object.
- **Entity** = trả thẳng entity EF (không qua DTO).

## 1.1 Auth & Users

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/setup/status` | – | `200` `{requiresSetup: bool}` | – |
| POST | `/api/setup/first-admin` | `RegisterRequest` | `200` `{message}` | `409` `{message}` (đã khởi tạo), `400` `{message}` |
| POST | `/api/auth/login` | `LoginRequest` | `200` `{message, data: LoginResponse}` | `401` `{message}` |
| POST | `/api/auth/register` | `RegisterRequest` | `200` `{message:"Register successful"}` | `400` `{message}` (mismatch / `"Register failed"` / `ex.Message`) |
| GET | `/api/users` | – | `200` `UserListItemResponse[]` | – |
| POST | `/api/users` | `CreateUserRequest` | `200` `{message}` | `400` `{message}` |
| POST | `/api/users/{userId:int}/roles` | `AssignRoleRequest` | `200` `{message}` | `400` `{message}` |

Hai endpoint `/api/setup/*` và `GET/POST /api/users` được **thêm ở GĐ6-A (`afa4664`)**.
`POST /api/setup/first-admin` là `[AllowAnonymous]` nhưng **chỉ thành công khi bảng `Users` rỗng**
(trong transaction) ⇒ không thể tự phong Admin sau khi hệ thống đã có user.
`POST /api/users` tạo user + gán `roleName` **trong một transaction** (`AuthService.CreateUserWithRoleAsync`).

`LoginResponse` = `{UserId, UserName, Email, FullName, Roles, Token}`. Hai field `Roles`/`Token` được **thêm ở GĐ6 (lát 6a)** — chỉ thêm, không phá JSON cũ. `Token` có thể là `null` khi chưa cấu hình khóa ký (`Authentication:Jwt:Key`).
Controller cho `/api/users` là `UsersController` (file `UsersController.cs`) — tên class đã sửa ở GĐ1, giữ nguyên route.

## 1.2 Food catalog

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/foods?categoryId&includeInactive` | – | `200` `FoodResponseDto[]` | – |
| GET | `/api/foods/{id:int}` | – | `200` `FoodResponseDto` | `404` `{message}` |
| POST | `/api/foods` | `CreateFoodRequest` | `201` + `Location` + `FoodResponseDto` | `400` `{message}` |
| PUT | `/api/foods/{id:int}` | `UpdateFoodRequest` | `200` `FoodResponseDto` | `404` `{message}`, `400` `{message}` |
| PATCH | `/api/foods/{id:int}/availability` | `UpdateFoodAvailabilityRequest` | `200` `{message}` | `404` `{message}` |
| PATCH | `/api/foods/{id:int}/active` | `UpdateFoodActiveRequest` | `200` `{message}` | `404` `{message}` |
| DELETE | `/api/foods/{id:int}` | – | `200` `{message}` | `404` `{message}` |
| GET | `/api/categories?name` | – | `200` `CategoryDTO[]` | – |
| GET | `/api/categories/{id:int}` | – | `200` `CategoryDTO` | `404` `{message}` |
| POST | `/api/categories` | `CategoryDTO` | `200` `{message}` | `400` `{message}` |
| PUT | `/api/categories/{id:int}` | `CategoryDTO` (Id ghi đè từ route) | `200` `{message}` | `400` `{message}`, `404` `{message}` |
| DELETE | `/api/categories/{id:int}` | – | `200` `{message}` | `400` `{message}`, `404` `{message}` |
| GET | `/api/foods/{foodId:int}/sizes` | – | `200` `FoodSizeResponseDto[]` | `404` `{message}` |
| GET | `/api/foods/{foodId:int}/sizes/{sizeId:int}` | – | `200` `FoodSizeResponseDto` | `404` `{message}` |
| POST | `/api/foods/{foodId:int}/sizes` | `CreateFoodSizeRequest` | `201` + `Location` | `400` `{message}` |
| PUT | `/api/foods/{foodId:int}/sizes/{sizeId:int}` | `UpdateFoodSizeRequest` | `200` `FoodSizeResponseDto` | `404` `{message}`, `400` `{message}` |
| PATCH | `/api/foods/{foodId:int}/sizes/{sizeId:int}/default` | – | `200` `{message}` | `404` `{message}` |
| DELETE | `/api/foods/{foodId:int}/sizes/{sizeId:int}` | – | `200` `{message}` | `404` `{message}` |
| GET | `/api/toppings?includeInactive` | – | `200` `ToppingResponseDto[]` | – |
| GET | `/api/toppings/{id:int}` | – | `200` `ToppingResponseDto` | `404` `{message}` |
| POST | `/api/toppings` | `CreateToppingRequest` | `201` + `Location` | `400` `{message}` |
| PUT | `/api/toppings/{id:int}` | `UpdateToppingRequest` | `200` `ToppingResponseDto` | `404` `{message}`, `400` `{message}` |
| PATCH | `/api/toppings/{id:int}/availability` | `UpdateToppingAvailabilityRequest` | `200` `{message}` | `404` `{message}` |
| DELETE | `/api/toppings/{id:int}` | – | `200` `{message}` | `404` `{message}` |
| GET | `/api/foods/{foodId:int}/toppings` | – | `200` `ToppingResponseDto[]` | `404` `{message}` |
| POST | `/api/foods/{foodId:int}/toppings` | `AssignFoodToppingRequest` | `200` `{message}` | `400` `{message}` |
| DELETE | `/api/foods/{foodId:int}/toppings/{toppingId:int}` | – | `200` `{message}` | `404` `{message}` |

## 1.3 Restaurant tables

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/restaurant-tables` | – | `200` `RestaurantTableDto[]` | – |
| GET | `/api/restaurant-tables/{id:int}` | – | `200` `RestaurantTableDto` | `404` (body rỗng) hoặc `404` `{message}` |
| POST | `/api/restaurant-tables` | `RestaurantTableDto` | `200` `{message, data}` | `400` `{message}`, `409` `{message}` (xung đột ghi) |
| PUT | `/api/restaurant-tables/{id:int}` | `RestaurantTableDto` | `200` `{message}` | `400` `{message}`, `404` `{message}`, `409` `{message}` (xung đột ghi) |
| DELETE | `/api/restaurant-tables/{id:int}` | – | `200` `{message}` | `400` `{message}`, `404` `{message}`, `409` `{message}` (xung đột ghi) |
| PATCH | `/api/restaurant-tables/{id:int}/maintenance?enabled` | – | `204` | `404` (body rỗng), `409` `{message}` (xung đột ghi) |

## 1.4 Orders, payments, menu

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/orders/{id:int}` | – | `200` **`OrderDetailResponse`** (mọi field `Order` cũ + `items[]`: `foodName`/`sizeName`/`quantity`/`unitPrice`/`note`/`status`) | `404` (body rỗng) |
| POST | `/api/orders` | `CreateOrderRequest` | `200` **`Order` entity** | `400` `"..."` (KeyNotFound), `409` `"..."` (InvalidOperation) |
| PATCH | `/api/orders/{id:int}/status` | `UpdateOrderStatusRequest` | `204` | `400` `"..."` (rỗng/không hợp lệ), `404`, `409` `"..."` (đã Paid/Cancelled) |
| PATCH | `/api/orders/{id:int}/promotion/{promotionId:int}` | – | `204` | `400` `"Order or promotion is invalid"` |
| POST | `/api/payments` | `CreatePaymentRequest` | `200` **`Payment` entity** | `404` (body rỗng), `409` `"..."` (đơn đã Paid/Cancelled hoặc tổng ≤ 0) |
| GET | `/api/payments/order/{orderId:int}` | – | `200` **`Payment[]` entity** | – |
| GET | `/api/menu` | – | `200` anonymous projection | – |

Error body của `OrdersController` / `PaymentsController` là **JSON string trần** (`BadRequest(e.Message)`), không phải `{message}`.

## 1.5 Management (inventory / purchase / promotion / recipe / kitchen)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/management/ingredients` | – | `200` **`IngredientResponse[]`** | – |
| POST | `/api/management/ingredients` | `IngredientRequest` | `200` **`IngredientResponse`** | – |
| PUT | `/api/management/ingredients/{id:int}` | `IngredientRequest` | `204` | `404` |
| DELETE | `/api/management/ingredients/{id:int}` | – | `204` | `404` |
| GET | `/api/management/suppliers` | – | `200` **`SupplierResponse[]`** | – |
| POST | `/api/management/suppliers` | `SupplierRequest` | `200` **`SupplierResponse`** | – |
| PUT | `/api/management/suppliers/{id:int}` | `SupplierRequest` | `204` | `404` |
| DELETE | `/api/management/suppliers/{id:int}` | – | `204` | `404` |
| GET | `/api/management/inventory` | – | `200` **`InventoryResponse[]`** (`quanlity`; không có `rowVersion`) | – |
| POST | `/api/management/inventory/{ingredientId:int}/adjust` | `InventoryAdjustmentRequest` | `204` | `404`, `400` `"..."`, `409` `"..."` (xung đột ghi) |
| GET | `/api/management/purchase-orders` | – | `200` **`PurchaseOrderResponse[]`** | – |
| POST | `/api/management/purchase-orders` | `PurchaseOrderRequest` | `200` **`PurchaseOrderResponse`** | `400` `"..."` |
| POST | `/api/management/purchase-orders/{id:int}/receive` | `ReceivePurchaseRequest` | `204` | `404`, `409` `"..."` (xung đột ghi) |
| GET | `/api/management/promotions` | – | `200` **`PromotionResponse[]`** | – |
| POST | `/api/management/promotions` | `PromotionRequest` | `200` **`PromotionResponse`** | `400` `"..."` |
| PUT | `/api/management/promotions/{id:int}` | `PromotionRequest` | `204` | `404` |
| PATCH | `/api/management/promotions/{id:int}/active?active` | – | `204` | `404` |
| DELETE | `/api/management/promotions/{id:int}` | – | `204` | `404` |
| GET | `/api/management/recipes` | – | `200` **`RecipeSummaryResponse[]`** (mọi field `Recipe` cũ + `ingredientCount`) | – |
| GET | `/api/management/recipes/{id:int}` | – | `200` `RecipeResponse` (thêm `foodName`/`sizeName`) | `404` |
| POST | `/api/management/recipes` | `RecipeRequest` | `200` **`RecipeSummaryResponse`** | `400` `"..."`, `409` `"..."` (size đã có công thức active) |
| PUT | `/api/management/recipes/{id:int}` | `RecipeRequest` | `204` | `404`, `400` `"..."`, `409` `"..."` (size đã có công thức active) |
| DELETE | `/api/management/recipes/{id:int}` | – | `204` | `404` |
| GET | `/api/management/kitchen/orders` | – | `200` **`KitchenOrderResponse[]`** | – |
| GET | `/api/management/kitchen/orders/{id:int}/items` | – | `200` **`KitchenItemResponse[]`** (mọi field `KitchenOrderItem` cũ kể cả `quanlity` + `foodName`/`sizeName`/`quantity`) | – |
| PATCH | `/api/management/kitchen/orders/{id:int}/status` | `KitchenStatusRequest` | `204` | `404`, `400` `"..."`, `409` `"..."` (xung đột ghi) |
| PATCH | `/api/management/kitchen/items/{id:int}/status` | `KitchenItemStatusRequest` | `204` | `404`, `400` `"..."`, `409` `"..."` (xung đột ghi) |

---

# 2. Hành vi xuyên suốt đang được coi là baseline

Các mục này **phải giữ nguyên** trong refactor thuần (GĐ1–GĐ4). Nếu muốn đổi → phải là giai đoạn gated (GĐ5+).

1. **Phân quyền chưa được enforce ở dev** (GĐ6b): đã có `AddAuthentication` (JwtBearer) +
   `AddAuthorization` + `[Authorize]` theo ma trận, `app.UseAuthentication()` + `app.UseAuthorization()`.
   Nhưng `Auth:Enforce=false` (mặc định) ⇒ `EnforceableAuthorizationPolicyProvider` trả policy cho-qua
   nên **toàn bộ API vẫn anonymous**. Chỉ khi bật `Auth:Enforce=true` mới có 401/403 (thay đổi hành vi, đã duyệt).
   **Đã pin ở mức HTTP (GĐ6e):** `AuthDisabledHttpTests` giữ baseline anonymous; `AuthHttpTests` xác nhận
   401/403/200 khi enforce bật.
2. **Actor** (`CreatedBy`, `ChangedBy`, `ReceivedBy`): GĐ6c bước 1 — khi có token thì lấy từ claim `sub` (bỏ qua body, log cảnh báo nếu lệch); khi chưa xác thực (`Auth:Enforce=false`, dev) thì vẫn dùng giá trị body. Field vẫn còn trong DTO.
3. **CORS**: policy `FrontendPolicy` **đã được áp** vào pipeline ở GĐ6-A (`afa4664`): `app.UseCors("FrontendPolicy")`, origin `http://localhost:5500` / `5501` / `3000`. Trước GĐ6-A policy đăng ký nhưng vô hiệu.
4. **Thời gian**: `UserService/AuthService` dùng `DateTime.Now`; các service khác dùng `DateTime.UtcNow`.
5. **Hai kiểu trả lỗi**: `{message}` (controller cũ) và JSON string trần (controller mới).
6. **Hai kiểu inject**: constructor (8 controller) và `[FromServices]` (4 controller).
7. **Trả entity trực tiếp**: `POST /api/orders`, `POST /api/payments`, `GET /api/payments/order/{orderId}`.
   **Đã DTO hoá**: `GET /orders/{id}` → `OrderDetailResponse` (GĐ7-C `46a18e9`); toàn bộ `/api/management/*`
   → DTO ở C4 (`c8a2f26`): `IngredientResponse`, `SupplierResponse`, `InventoryResponse`,
   `PurchaseOrderResponse`, `PromotionResponse`, `RecipeSummaryResponse` (GET list + POST),
   `RecipeResponse` (GET detail), `KitchenOrderResponse`, `KitchenItemResponse`.
   `GET /management/inventory` bỏ `rowVersion` nội bộ; các field frontend đang đọc giữ nguyên.
8. **Order status GIỜ ĐÃ validate** (Nhóm B2 `29c01d1`): `PATCH /api/orders/{id}/status` chỉ nhận
   allow-list `Pending/SentToKitchen/Preparing/Ready/Completed/Cancelled` (không nhận `Paid`), từ chối
   chuỗi rỗng/không hợp lệ (`400`) và không cho sửa khi đơn đã ở trạng thái cuối `Paid`/`Cancelled` (`409`).
   (Đã pin bằng `UpdateStatus_RejectsStatusOutsideAllowList` / `_RejectsBlankStatus` / `_RejectsChangeFromTerminalState`.)
9. **Kitchen status có validate** (allow-list) ở `KitchenService`. Bản trùng `ManagementService.UpdateKitchen`
   (không validate) **đã xoá ở D2 `8fa8daa`**, nên từ nay chỉ còn một hành vi bếp duy nhất.
   (Test `ManagementKitchen_UpdateSetsTimestamps_ButDoesNotValidateStatus` đã bỏ cùng dead code.)
10. **GET bàn theo id có side-effect ghi DB** (tracked entity + `SaveChanges`);
    **GET all bàn thì không ghi DB** (do repository dùng `AsNoTracking`).
    (Đã pin bằng 2 test `GetById_ComputesOccupied_AndPersistsIt` và `GetAll_ComputesStatus_ButDoesNotPersistIt`.)
11. **Soft delete + unique index**: trước đây `toppings.ToppingName` (và category/size/ingredient/table) unique
    **không** lọc `IsActive` nên không tạo lại được cùng tên sau soft delete. **Đã sửa ở Nhóm B6 (`62f5d44`)**:
    index có filter `[IsActive] = 1`. **Ngoại lệ:** `users.UserName`/`UserEmail` vẫn unique toàn cục (định danh đăng nhập).
12. **`Category.DisplayOrder` là `bool`** (entity + DTO) → không thể biểu diễn thứ tự hiển thị thật.
    Đổi sang `int` là **thay đổi schema**, cần phê duyệt riêng.
13. **`GET /api/menu` trả anonymous types** (`Task<object>`), không có DTO.
14. **Khởi động app tự `Migrate()` + seed Roles** (Admin, WarehouseManager, Chef, Cashier, Waiter) — GĐ8 đã tách
    sang `DatabaseInitializer`; hành vi mặc định không đổi (`Database:MigrateOnStartup`/`SeedOnStartup` = `true`).
15. **`RoleEnum`** (Admin/Manager/Staff/Customer) là dead code và **lệch** với role seed thật.

### Thay đổi hành vi có chủ ý (Nhóm B, `29c01d1`) — KHÔNG còn là baseline cũ

16. **Ánh xạ trạng thái bếp (B1)**: `Completed`/`Ready` → order `Ready`; `Preparing` → `Preparing`;
    `Pending`/`Accepted` → `SentToKitchen`; **`Cancelled` → order `Cancelled`** (trước đây `Cancelled` bị đưa về `SentToKitchen`).
17. **Thanh toán (B4/B)**: không thanh toán đơn `Cancelled` (`409`); `payment` + cập nhật `order.Status` là **nguyên tử** (transaction).
18. **Tạo ingredient / purchase order (B4)**: chèn header + dòng là **nguyên tử** (transaction).
19. **Dọn dữ liệu (B8)**: `Order.Items` null → `400` (không NRE); `.Trim()` null-safe ở recipe/promotion; `UserService`/`AuthService` dùng `UtcNow`.

### Thay đổi hành vi có chủ ý (Nhóm B — phần schema, `62f5d44`)

20. **Thanh toán trùng (B3)**: unique index lọc trên `payments.OrderId` với `[Status] = 'Paid'` ⇒ mỗi đơn chỉ
    có **một** payment `Paid`; nhiều payment `Failed`/chưa thanh toán vẫn hợp lệ. `POST /api/payments` trả
    `409 "Order is already paid"` khi vi phạm. Đơn `TotalAmount <= 0` bị chặn (`409 "Order total must be greater than zero"`).
21. **Công thức (B5)**: mỗi `SizeId` chỉ có **một** recipe `IsActive=1` (unique index lọc + kiểm tra trong
    `CreateAsync`/`UpdateAsync`) ⇒ `POST`/`PUT /api/management/recipes` trả `409 "This size already has an active recipe"`.
    Công thức đã soft-delete không chiếm size, nên tạo lại được.
22. **Đồng thời (B7)**: `orders`/`inventory`/`restaurant_tables` có `RowVersion` (`rowversion` trên SQL Server).
    Request ghi dựa trên dữ liệu cũ ⇒ `409 "Dữ liệu vừa bị thay đổi bởi thao tác khác..."` thay vì ghi đè im lặng.
    `RefreshStatusForTableAsync` (side-effect sau khi transaction chính commit) **retry** tối đa 2 lần thay vì trả 409.
23. **Quy ước `Quanlity` (C4 `c8a2f26`)**: entity + cột DB đã dùng `Quanlity`; C# DTO/request/service cũng
    được chuẩn hoá về `Quanlity`. Các endpoint cũ vẫn phát/nhận JSON `quantity` bằng `[JsonPropertyName]`
    để frontend hiện tại không vỡ; riêng `GET /management/inventory` phát `quanlity` (frontend `inventory.js`
    đang đọc). **Không có migration** vì DB thật đã là `Quanlity`.

---

# 3. Cách dùng baseline để gate refactor

Với mỗi giai đoạn:

1. Chạy `dotnet build` → không được tăng số warning/error.
2. Chạy `dotnet test` → **241/241 phải pass** (105 lúc ghi baseline + test các giai đoạn sau; đã bỏ 1 test dead code).
3. Với thay đổi có chạm controller/response shape: so sánh route + verb + status code + JSON
   với bảng ở mục 1 (baseline này).
4. Với thay đổi có chạm schema: **dừng lại**, xin phê duyệt (xem mục 2, điểm 12).

## Giới hạn đã biết của lưới an toàn hiện tại

- Test phần lớn chạy ở **tầng service + DbContext**. Từ **GĐ8** đã có host test
  (`WebApplicationFactory` + `RestaurantApiFactory`, SQLite in-memory); **GĐ6e** dùng nó để khoá 401/403/200
  cho toàn bộ ma trận policy (kể cả baseline `Auth:Enforce=false`).
- **Status code nghiệp vụ** (400/404/409…) vẫn được khóa chủ yếu bằng tài liệu (mục 1); tầng HTTP mới phủ auth +
  smoke `GET /api/setup/status`.
- Muốn mở rộng test HTTP: dùng `RestaurantApiFactory` / `EnforcedRestaurantApiFactory`
  (`Database:MigrateOnStartup=false`) rồi `CreateClient()`.
