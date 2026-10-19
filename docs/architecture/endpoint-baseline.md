# GĐ0 — Baseline hành vi API & lưới an toàn

Mục đích: khóa lại **hành vi hiện tại** của backend trước khi refactor kiến trúc.
Mọi giai đoạn sau (GĐ1–GĐ8) phải giữ nguyên các hợp đồng dưới đây trừ khi có phê duyệt riêng.

- Ngày ghi baseline: 2026-10-19
- Nhánh: `hai`
- Solution: `RestaurantFoodManagerSystem.sln` (1 project web + 1 project test)
- Build baseline: `Build succeeded` — **16 warnings CS8618, 0 errors**
  (toàn bộ là non-nullable property trong entity/DTO, không phải lỗi)
- Test baseline: **105/105 pass**

## Cách chạy lưới an toàn

```powershell
dotnet build RestaurantFoodManagerSystem.sln --no-incremental
dotnet test RestaurantFoodManagerSystem.Tests\RestaurantFoodManagerSystem.Tests.csproj
```

Test dùng **SQLite in-memory** (`EnsureCreated`, không dùng migrations) nên:

- không kết nối SQL Server thật,
- không tạo/không sửa database thật,
- không đổi schema, không chạm `Migrations/`.

SQLite được chọn thay vì EF InMemory vì `OrderService` và `ManagementService` mở transaction tường minh
(InMemory provider không hỗ trợ transaction).

## Phạm vi đã pin bằng test (105 test)

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

---

# 1. Endpoint inventory (hợp đồng hiện tại)

Quy ước cột:

- **Body lỗi `{message}`** = object JSON `{"message": "..."}`.
- **Body lỗi `"..."`** = **JSON string trần** (ví dụ `"Table not found"`), không phải object.
- **Entity** = trả thẳng entity EF (không qua DTO).

## 1.1 Auth & Users

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| POST | `/api/auth/login` | `LoginRequest` | `200` `{message, data: LoginResponse}` | `401` `{message}` |
| POST | `/api/auth/register` | `RegisterRequest` | `200` `{message:"Register successful"}` | `400` `{message}` (mismatch / `"Register failed"` / `ex.Message`) |
| POST | `/api/users/{userId:int}/roles` | `AssignRoleRequest` | `200` `{message}` | `400` `{message}` |

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
| POST | `/api/restaurant-tables` | `RestaurantTableDto` | `200` `{message, data}` | `400` `{message}` |
| PUT | `/api/restaurant-tables/{id:int}` | `RestaurantTableDto` | `200` `{message}` | `400` `{message}`, `404` `{message}` |
| DELETE | `/api/restaurant-tables/{id:int}` | – | `200` `{message}` | `400` `{message}`, `404` `{message}` |
| PATCH | `/api/restaurant-tables/{id:int}/maintenance?enabled` | – | `204` | `404` (body rỗng) |

## 1.4 Orders, payments, menu

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/orders/{id:int}` | – | `200` **`Order` entity** | `404` (body rỗng) |
| POST | `/api/orders` | `CreateOrderRequest` | `200` **`Order` entity** | `400` `"..."` (KeyNotFound), `409` `"..."` (InvalidOperation) |
| PATCH | `/api/orders/{id:int}/status` | `UpdateOrderStatusRequest` | `204` | `404` (body rỗng) |
| PATCH | `/api/orders/{id:int}/promotion/{promotionId:int}` | – | `204` | `400` `"Order or promotion is invalid"` |
| POST | `/api/payments` | `CreatePaymentRequest` | `200` **`Payment` entity** | `404` (body rỗng), `409` `"..."` |
| GET | `/api/payments/order/{orderId:int}` | – | `200` **`Payment[]` entity** | – |
| GET | `/api/menu` | – | `200` anonymous projection | – |

Error body của `OrdersController` / `PaymentsController` là **JSON string trần** (`BadRequest(e.Message)`), không phải `{message}`.

## 1.5 Management (inventory / purchase / promotion / recipe / kitchen)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/management/ingredients` | – | `200` **`Ingredient[]` entity** | – |
| POST | `/api/management/ingredients` | `IngredientRequest` | `200` **`Ingredient` entity** | – |
| PUT | `/api/management/ingredients/{id:int}` | `IngredientRequest` | `204` | `404` |
| DELETE | `/api/management/ingredients/{id:int}` | – | `204` | `404` |
| GET | `/api/management/suppliers` | – | `200` **`Supplier[]` entity** | – |
| POST | `/api/management/suppliers` | `SupplierRequest` | `200` **`Supplier` entity** | – |
| PUT | `/api/management/suppliers/{id:int}` | `SupplierRequest` | `204` | `404` |
| DELETE | `/api/management/suppliers/{id:int}` | – | `204` | `404` |
| GET | `/api/management/inventory` | – | `200` **`Inventory[]` entity** | – |
| POST | `/api/management/inventory/{ingredientId:int}/adjust` | `InventoryAdjustmentRequest` | `204` | `404`, `400` `"..."` |
| GET | `/api/management/purchase-orders` | – | `200` **`PurchaseOrder[]` entity** | – |
| POST | `/api/management/purchase-orders` | `PurchaseOrderRequest` | `200` **`PurchaseOrder` entity** | `400` `"..."` |
| POST | `/api/management/purchase-orders/{id:int}/receive` | `ReceivePurchaseRequest` | `204` | `404` |
| GET | `/api/management/promotions` | – | `200` **`Promotion[]` entity** | – |
| POST | `/api/management/promotions` | `PromotionRequest` | `200` **`Promotion` entity** | `400` `"..."` |
| PUT | `/api/management/promotions/{id:int}` | `PromotionRequest` | `204` | `404` |
| PATCH | `/api/management/promotions/{id:int}/active?active` | – | `204` | `404` |
| DELETE | `/api/management/promotions/{id:int}` | – | `204` | `404` |
| GET | `/api/management/recipes` | – | `200` **`Recipe[]` entity** | – |
| GET | `/api/management/recipes/{id:int}` | – | `200` `RecipeResponse` | `404` |
| POST | `/api/management/recipes` | `RecipeRequest` | `200` **`Recipe` entity** | `400` `"..."` |
| DELETE | `/api/management/recipes/{id:int}` | – | `204` | `404` |
| GET | `/api/management/kitchen/orders` | – | `200` **`KitchenOrder[]` entity** | – |
| GET | `/api/management/kitchen/orders/{id:int}/items` | – | `200` **`KitchenOrderItem[]` entity** | – |
| PATCH | `/api/management/kitchen/orders/{id:int}/status` | `KitchenStatusRequest` | `204` | `404`, `400` `"..."` |
| PATCH | `/api/management/kitchen/items/{id:int}/status` | `KitchenItemStatusRequest` | `204` | `404`, `400` `"..."` |

---

# 2. Hành vi xuyên suốt đang được coi là baseline

Các mục này **phải giữ nguyên** trong refactor thuần (GĐ1–GĐ4). Nếu muốn đổi → phải là giai đoạn gated (GĐ5+).

1. **Phân quyền chưa được enforce ở dev** (GĐ6b): đã có `AddAuthentication` (JwtBearer) +
   `AddAuthorization` + `[Authorize]` theo ma trận, `app.UseAuthentication()` + `app.UseAuthorization()`.
   Nhưng `Auth:Enforce=false` (mặc định) ⇒ `EnforceableAuthorizationPolicyProvider` trả policy cho-qua
   nên **toàn bộ API vẫn anonymous**. Chỉ khi bật `Auth:Enforce=true` mới có 401/403 (thay đổi hành vi, đã duyệt).
2. **Actor do client gửi**: `CreatedBy`, `ChangedBy`, `ReceivedBy` lấy từ body.
3. **CORS**: policy `FrontendPolicy` được đăng ký nhưng **không** `app.UseCors(...)` → policy vô hiệu.
4. **Thời gian**: `UserService/AuthService` dùng `DateTime.Now`; các service khác dùng `DateTime.UtcNow`.
5. **Hai kiểu trả lỗi**: `{message}` (controller cũ) và JSON string trần (controller mới).
6. **Hai kiểu inject**: constructor (8 controller) và `[FromServices]` (4 controller).
7. **Trả entity trực tiếp**: orders, payments, toàn bộ `/api/management/*` (trừ recipe details).
8. **Order status không validate**: `PATCH /api/orders/{id}/status` nhận mọi chuỗi.
   (Đã pin bằng test `UpdateStatus_AcceptsAnyStatusString_CurrentlyUnvalidated`.)
9. **Kitchen status có validate** (allow-list) ở `KitchenService`, nhưng
   `ManagementService.UpdateKitchen` **không validate** → hai hành vi cho cùng nghiệp vụ.
   (Đã pin bằng test `ManagementKitchen_UpdateSetsTimestamps_ButDoesNotValidateStatus`.)
10. **GET bàn theo id có side-effect ghi DB** (tracked entity + `SaveChanges`);
    **GET all bàn thì không ghi DB** (do repository dùng `AsNoTracking`).
    (Đã pin bằng 2 test `GetById_ComputesOccupied_AndPersistsIt` và `GetAll_ComputesStatus_ButDoesNotPersistIt`.)
11. **Soft delete + unique index**: `toppings.ToppingName` có unique index không lọc `IsActive`,
    nên không thể tạo lại topping cùng tên sau khi soft delete. (Đã pin bằng test.)
12. **`Category.DisplayOrder` là `bool`** (entity + DTO) → không thể biểu diễn thứ tự hiển thị thật.
    Đổi sang `int` là **thay đổi schema**, cần phê duyệt riêng.
13. **`GET /api/menu` trả anonymous types** (`Task<object>`), không có DTO.
14. **Khởi động app tự `Migrate()` + seed Roles** (Admin, WarehouseManager, Chef, Cashier, Waiter).
15. **`RoleEnum`** (Admin/Manager/Staff/Customer) là dead code và **lệch** với role seed thật.

---

# 3. Cách dùng baseline để gate refactor

Với mỗi giai đoạn:

1. Chạy `dotnet build` → không được tăng số warning/error.
2. Chạy `dotnet test` → **105/105 phải pass**.
3. Với thay đổi có chạm controller/response shape: so sánh route + verb + status code + JSON
   với bảng ở mục 1 (baseline này).
4. Với thay đổi có chạm schema: **dừng lại**, xin phê duyệt (xem mục 2, điểm 12).

## Giới hạn đã biết của lưới an toàn hiện tại

- Test chạy ở **tầng service + DbContext**, chưa phải tầng HTTP
  (`WebApplicationFactory` chưa dùng vì `Program.cs` gọi `Database.Migrate()` vào SQL Server thật
  ngay khi host được build).
- Vì vậy **status code HTTP và error body shape** được khóa bằng tài liệu (mục 1), chưa tự động hóa.
- Việc bổ sung test HTTP-level cần một host test riêng (tách seed/migrate khỏi `Program.cs`) → đề xuất ở GĐ8.
