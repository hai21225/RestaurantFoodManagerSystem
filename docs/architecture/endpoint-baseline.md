# GĐ0 — Baseline hành vi API & lưới an toànMục đích: khóa lại **hành vi hiện tại** của backend trước khi refactor kiến trúc.Mọi giai đoạn sau (GĐ1–GĐ8) phải giữ nguyên các hợp đồng dưới đây trừ khi có phê duyệt riêng.- Ngày ghi baseline: 2026-10-19- Nhánh: `hai`- Solution: `RestaurantFoodManagerSystem.sln` (1 project web + 1 project test)- Build baseline: `Build succeeded` — **16 warnings CS8618, 0 errors**  (toàn bộ là non-nullable property trong entity/DTO, không phải lỗi)- Test baseline: **105/105 pass** *(tại thời điểm ghi baseline; hiện tại **376/376**, sau Chặng 3)*## Cách chạy lưới an toàn```powershelldotnet build RestaurantFoodManagerSystem.sln --no-incrementaldotnet test RestaurantFoodManagerSystem.Tests\RestaurantFoodManagerSystem.Tests.csproj```Test dùng **SQLite in-memory** (`EnsureCreated`, không dùng migrations) nên:- không kết nối SQL Server thật,- không tạo/không sửa database thật,- không đổi schema, không chạm `Migrations/`.SQLite được chọn thay vì EF InMemory vì `OrderService`, `PurchaseOrderService`, `RecipeService`(trước đây cả `ManagementService`) mở transaction tường minh (InMemory provider không hỗ trợ transaction).## Phạm vi đã pin bằng test (105 test lúc ghi baseline, hiện 376)| Nhóm | File | Nội dung được khóa ||---|---|---|| Auth | `AuthTests.cs` | login đúng/sai/inactive/blank, register hash BCrypt, trùng username, confirm password, assign role || Food catalog | `FoodCatalogTests.cs` | Food/Category/FoodSize/Topping/FoodTopping CRUD, soft delete, default size, trùng tên, lỗi unique index sau soft delete || Orders | `OrdersFlowTests.cs` | snapshot giá, recipe, trừ kho, transaction (serializable + thứ tự trước khi đọc kho), kitchen ticket, trạng thái bàn, status history, promotion, idempotency, sửa món || Payments | `PaymentsFlowTests.cs` | amount lấy từ server, chặn thanh toán trùng, refresh trạng thái bàn || Kitchen | `KitchenFlowTests.cs` | allow-list trạng thái, timestamp, lan truyền trạng thái order/item || Inventory | `InventoryFlowTests.cs` | ingredient/inventory/supplier/purchase/promotion/recipe, adjustment âm, receive purchase || Tables | `RestaurantTableTests.cs` | trùng số bàn, tính trạng thái occupied, side-effect đọc, maintenance, soft delete || Menu | `MenuTests.cs` | lọc category/food/size/topping, thứ tự category || Khởi động/host | `DatabaseInitializerTests.cs`, `ApiHostSmokeTests.cs` | seed 5 role idempotent, cờ `Database:MigrateOnStartup`/`SeedOnStartup`, host dựng bằng `WebApplicationFactory` trên SQLite in-memory || Auth HTTP | `AuthHttpTests.cs`, `AuthDisabledHttpTests.cs` | 401 thiếu/sai token, 403 sai role, 200 đúng role (đủ 6 policy + Admin), `[AllowAnonymous]` vẫn 200; baseline enforce=false vẫn anonymous || Management DTO (C4) | `ManagementDtoHttpTests.cs`, `DtoJsonContractTests.cs` | `/api/management/*` trả DTO; `inventory` có `quanlity`, không có `rowVersion`; request/response cũ vẫn dùng JSON `quantity`; `KitchenItemResponse` phát cả `quanlity` + `quantity` || Tài khoản (P4) | `UserAccountTests.cs`, `UserAccountHttpTests.cs`, `DtoJsonContractTests.cs` | hồ sơ `GET/PUT /users/me` (không lộ hash, không đổi được `roles`/`isActive`), đổi mật khẩu (sai mật khẩu hiện tại → 400, hash cũ ngừng đăng nhập được), `GET /roles` chỉ đọc đúng 5 role, thu hồi role + chặn xoá Admin cuối cùng |---# 1. Endpoint inventory (hợp đồng hiện tại)Quy ước cột:- **Body lỗi `{message}`** = object JSON `{"message": "..."}`.- **Body lỗi `"..."`** = **JSON string trần** (ví dụ `"Table not found"`), không phải object.- **Entity** = trả thẳng entity EF (không qua DTO).## 1.1 Auth & Users| Verb | Route | Request | Success | Lỗi ||---|---|---|---|---|| GET | `/api/setup/status` | – | `200` `{requiresSetup: bool}` | – || POST | `/api/setup/first-admin` | `RegisterRequest` | `200` `{message}` | `409` `{message}` (đã khởi tạo), `400` `{message}` || POST | `/api/auth/login` | `LoginRequest` | `200` `{message, data: LoginResponse}` | `401` `{message}` || POST | `/api/auth/register` | `RegisterRequest` | `200` `{message:"Register successful"}` | `400` `{message}` (mismatch / `"Register failed"` / `ex.Message`) || GET | `/api/users` | – | `200` `UserListItemResponse[]` | – || POST | `/api/users` | `CreateUserRequest` | `200` `{message}` | `400` `{message}` || POST | `/api/users/{userId:int}/roles` | `AssignRoleRequest` | `200` `{message}` | `400` `{message}` || DELETE | `/api/users/{userId:int}/roles/{roleName}` | – | `200` `{message}` | `400` `{message}`, `404` `{message}` || GET | `/api/roles` | – | `200` `RoleResponse[]` | – || GET | `/api/users/me` | – | `200` `UserProfileResponse` | `401` `{message}`, `404` `{message}` || PUT | `/api/users/me` | `UpdateProfileRequest` | `200` `UserProfileResponse` | `401` `{message}`, `400` `{message}`, `404` `{message}` || POST | `/api/auth/change-password` | `ChangePasswordRequest` | `200` `{message:"Password changed successfully"}` | `401` `{message}`, `400` `{message}`, `404` `{message}` || GET | `/api/users/{id:int}` | – | `200` `UserProfileResponse` | `404` `{message}` || PUT | `/api/users/{id:int}` | `UpdateUserRequest` | `200` `UserProfileResponse` | `400` `{message}`, `404` `{message}` || PATCH | `/api/users/{id:int}/active?active` | – | `200` `UserProfileResponse` | `400` `{message}`, `404` `{message}` || POST | `/api/users/{id:int}/reset-password` | `ResetUserPasswordRequest` | `200` `{message:"Password reset successfully"}` | `400` `{message}`, `404` `{message}` |Hai endpoint `/api/setup/*` và `GET/POST /api/users` được **thêm ở GĐ6-A (`afa4664`)**.`POST /api/setup/first-admin` là `[AllowAnonymous]` nhưng **chỉ thành công khi bảng `Users` rỗng**(trong transaction) ⇒ không thể tự phong Admin sau khi hệ thống đã có user.`POST /api/users` tạo user + gán `roleName` **trong một transaction** (`AuthService.CreateUserWithRoleAsync`).**P4 — tài khoản tự phục vụ:**- `GET /api/roles` là **chỉ đọc** (AdminOnly): 5 role cố định seed sẵn, **không CRUD** và **không trả ma trận quyền**  (ma trận là policy trong code — `AuthPolicies`).- `GET/PUT /api/users/me` + `POST /api/auth/change-password` yêu cầu **Authenticated** (cả 5 role).  `PUT` chỉ sửa `fullName/email/phone`; gửi thêm `roles`/`isActive` bị bỏ qua (chống mass assignment).  Khi `Auth:Enforce=false` mà request không có token, các endpoint này vẫn trả **401** vì cần biết "tôi là ai".- `DELETE /api/users/{id}/roles/{roleName}` (AdminOnly) chặn xoá **Admin cuối cùng** ⇒ `400`  `"Cannot remove the last Admin role"`.- Đổi mật khẩu dùng BCrypt, dài 6–72 ký tự; **không thu hồi token cũ** (JWT đang còn hạn vẫn dùng được tới khi hết hạn).`LoginResponse` = `{UserId, UserName, Email, FullName, Roles, Token}`. Hai field `Roles`/`Token` được **thêm ở GĐ6 (lát 6a)** — chỉ thêm, không phá JSON cũ. `Token` có thể là `null` khi chưa cấu hình khóa ký (`Authentication:Jwt:Key`).Controller cho `/api/users` là `UsersController` (file `UsersController.cs`) — tên class đã sửa ở GĐ1, giữ nguyên route.**Batch A — Admin quản lý nhân viên:** `GET/PUT /api/users/{id}`, `PATCH /api/users/{id}/active`và `POST /api/users/{id}/reset-password` đều **AdminOnly**. `PUT` chỉ sửa `fullName/email/phone`(không nhận `userName`/`roles`/`isActive`). `PATCH active=false` chặn **tự khoá chính mình**(`400 "You cannot lock your own account"`) và chặn khoá **Admin đang hoạt động cuối cùng**(`400 "Cannot lock the last active Admin account"`). Reset mật khẩu dùng BCrypt, 6–72 ký tự,không cần mật khẩu hiện tại; token JWT cũ vẫn có hiệu lực tới khi hết hạn (không lưu token).## 1.2 Food catalog| Verb | Route | Request | Success | Lỗi ||---|---|---|---|---|| GET | `/api/foods?categoryId&includeInactive` | – | `200` `FoodResponseDto[]` | – || GET | `/api/foods/{id:int}` | – | `200` `FoodResponseDto` | `404` `{message}` || POST | `/api/foods` | `CreateFoodRequest` | `201` + `Location` + `FoodResponseDto` | `400` `{message}` || PUT | `/api/foods/{id:int}` | `UpdateFoodRequest` | `200` `FoodResponseDto` | `404` `{message}`, `400` `{message}` || PATCH | `/api/foods/{id:int}/availability` | `UpdateFoodAvailabilityRequest` | `200` `{message}` | `404` `{message}` || PATCH | `/api/foods/{id:int}/active` | `UpdateFoodActiveRequest` | `200` `{message}` | `404` `{message}` || DELETE | `/api/foods/{id:int}` | – | `200` `{message}` | `404` `{message}` || GET | `/api/categories?name` | – | `200` `CategoryDTO[]` | – || GET | `/api/categories/{id:int}` | – | `200` `CategoryDTO` | `404` `{message}` || POST | `/api/categories` | `CategoryDTO` | `200` `{message}` | `400` `{message}` || PUT | `/api/categories/{id:int}` | `CategoryDTO` (Id ghi đè từ route) | `200` `{message}` | `400` `{message}`, `404` `{message}` || DELETE | `/api/categories/{id:int}` | – | `200` `{message}` | `400` `{message}`, `404` `{message}` || GET | `/api/foods/{foodId:int}/sizes` | – | `200` `FoodSizeResponseDto[]` | `404` `{message}` || GET | `/api/foods/{foodId:int}/sizes/{sizeId:int}` | – | `200` `FoodSizeResponseDto` | `404` `{message}` || POST | `/api/foods/{foodId:int}/sizes` | `CreateFoodSizeRequest` | `201` + `Location` | `400` `{message}` || PUT | `/api/foods/{foodId:int}/sizes/{sizeId:int}` | `UpdateFoodSizeRequest` | `200` `FoodSizeResponseDto` | `404` `{message}`, `400` `{message}` || PATCH | `/api/foods/{foodId:int}/sizes/{sizeId:int}/default` | – | `200` `{message}` | `404` `{message}` || DELETE | `/api/foods/{foodId:int}/sizes/{sizeId:int}` | – | `200` `{message}` | `404` `{message}` || GET | `/api/toppings?includeInactive` | – | `200` `ToppingResponseDto[]` | – || GET | `/api/toppings/{id:int}` | – | `200` `ToppingResponseDto` | `404` `{message}` || POST | `/api/toppings` | `CreateToppingRequest` | `201` + `Location` | `400` `{message}` || PUT | `/api/toppings/{id:int}` | `UpdateToppingRequest` | `200` `ToppingResponseDto` | `404` `{message}`, `400` `{message}` || PATCH | `/api/toppings/{id:int}/availability` | `UpdateToppingAvailabilityRequest` | `200` `{message}` | `404` `{message}` || DELETE | `/api/toppings/{id:int}` | – | `200` `{message}` | `404` `{message}` || GET | `/api/foods/{foodId:int}/toppings` | – | `200` `ToppingResponseDto[]` | `404` `{message}` || POST | `/api/foods/{foodId:int}/toppings` | `AssignFoodToppingRequest` | `200` `{message}` | `400` `{message}` || DELETE | `/api/foods/{foodId:int}/toppings/{toppingId:int}` | – | `200` `{message}` | `404` `{message}` |## 1.3 Restaurant tables| Verb | Route | Request | Success | Lỗi ||---|---|---|---|---|| GET | `/api/restaurant-tables` | – | `200` `RestaurantTableDto[]` | – || GET | `/api/restaurant-tables/{id:int}` | – | `200` `RestaurantTableDto` (policy `PosKitchenRead`: Admin/Waiter/Cashier/Chef) | `404` (body rỗng) hoặc `404` `{message}` || POST | `/api/restaurant-tables` | `RestaurantTableDto` | `200` `{message, data}` | `400` `{message}`, `409` `{message}` (xung đột ghi) || PUT | `/api/restaurant-tables/{id:int}` | `RestaurantTableDto` | `200` `{message}` | `400` `{message}`, `404` `{message}`, `409` `{message}` (trùng số bàn active hoặc xung đột ghi) || DELETE | `/api/restaurant-tables/{id:int}` | - | `200` `{message}` | `400` `{message}`, `404`, `409` `{message}` (bàn có đơn mở hoặc xung đột ghi) || PATCH | `/api/restaurant-tables/{id:int}/maintenance?enabled` | - | `204` | `404` (body rỗng), `409` `{message}` (bàn có đơn mở hoặc xung đột ghi) |## 1.4 Orders, payments, menu| Verb | Route | Request | Success | Lỗi ||---|---|---|---|---|| GET | `/api/orders` | query `status`,`tableId`,`from`,`to`,`page`,`pageSize` | `200` **`PagedResponse<OrderListItemResponse>`** = `{page,pageSize,total,items[]}` (`items[]` có `tableNumber`, `itemCount`; không có `rowVersion`) | `400` `{message}` (`page` < 1, `pageSize` ngoài 1..100) || GET | `/api/orders/{id:int}/history` | – | `200` **`OrderStatusHistoryResponse[]`** (thời gian tăng dần; có `changedByName`) | `404` (body rỗng) || GET | `/api/orders/{id:int}` | – | `200` **`OrderDetailResponse`** (mọi field `Order` cũ + `items[]`: `foodName`/`sizeName`/`quantity`/`unitPrice`/`note`/`status`/`cancelReason`/`cancelledAt`/`cancelledBy` + `toppings[]`: `toppingId`/`toppingName`/`quantity`/`unitPrice`). Policy `PosKitchenRead` cho Chef đọc phiếu bếp | `404` (body rỗng) || POST | `/api/orders` | `CreateOrderRequest`; header tuỳ chọn `Idempotency-Key` (≤128 ký tự) | `200` **`OrderCreatedResponse`** (mọi field `Order` cũ, không có `rowVersion`). Gửi lại cùng key + cùng payload trả lại đúng response cũ, không tạo đơn/trừ kho lần hai | `400` `"..."` (bàn/món/topping không hợp lệ, topping không thuộc món, key quá dài), `409` `"..."` (bàn đang bảo trì/đã có đơn mở, thiếu kho; cùng key nhưng payload khác) || PUT | `/api/orders/{id:int}/items` | – | `410 Gone` `{message}` (route đã bị chặn từ C-09 — thay toàn bộ dòng món) | `410` `"..."` (dùng `POST /orders/{id}/items` để gọi thêm, `PATCH /orders/{id}/items/{itemId}/quantity` để đổi số lượng, `POST /orders/{id}/items/{itemId}/cancel` để huỷ món) || POST | `/api/orders/{id:int}/items` | `AddOrderItemsRequest` (`changedBy`, `items[]` cùng shape với `POST /orders`) | `200` **`OrderDetailResponse`** sau khi gọi thêm. Luôn tạo **round/ticket bếp mới**, không đụng món cũ; order `Completed`/`Ready` được mở lại, bàn vẫn `Occupied` | `400` `"..."` (size/topping không hợp lệ), `404` (đơn không tồn tại), `409` `"..."` (đơn `Paid`/`Cancelled`, bàn đã trả, thiếu kho) || PATCH | `/api/orders/{orderId:int}/items/{itemId:int}/quantity` | `AdjustOrderItemQuantityRequest` (`quantity` **hoặc** `delta`, `changedBy`, `reason` (bắt buộc khi về 0), `reasonCode` (`CustomerChange`/`OutOfStock`/`PhysicalShortage`/`KitchenError`), `allowWastage`) | `200` **`OrderDetailResponse`** sau khi đổi số lượng; tách dòng theo nhóm, hoàn kho phần chưa nấu, ghi wastage khi giảm dưới phần đã nấu | `400` `"..."` (thiếu `quantity`/`delta`, cả hai cùng lúc, số lượng âm, thiếu lý do khi về 0), `403` `{message}` (giảm dưới phần đã nấu mà không phải Admin), `404` (không thấy đơn/món), `409` `"..."` (giảm dưới phần đã nấu mà chưa bật `allowWastage`, đơn đã đóng) || GET | `/api/orders/{id:int}/item-history` | – | `200` **`OrderItemHistoryResponse[]`** (audit từng dòng món: `oldStatus`/`newStatus`/`reason`/`reasonCode`/`changedByName`/`createdAt`) | `404` (đơn không tồn tại) || PATCH | `/api/orders/{id:int}/status` | `UpdateOrderStatusRequest`: `status`, `changedBy`, `note` (bắt buộc khi `Cancelled`, ≤500 ký tự), `reasonCode` (`CustomerChange` (mặc định) / `OutOfStock` / `PhysicalShortage` / `KitchenError` — quyết định hoàn kho hay ghi wastage khi huỷ) | `204` | `400` `"..."` (status rỗng/không hợp lệ, thiếu lý do hủy), `403` (`{message}` khi đã có món `Ready`/`Completed` mà không phải Admin), `404`, `409` `"..."` (chuyển sai ma trận hoặc đơn đã `Paid`/`Cancelled`) || POST | `/api/orders/{orderId:int}/items/{itemId:int}/cancel` | `CancelOrderItemRequest`: `reason` (bắt buộc, ≤500 ký tự), `reasonCode` (`CustomerChange` (mặc định) = hoàn kho phần `Pending`; `OutOfStock`/`PhysicalShortage`/`KitchenError` = ghi `Wastage`, không hoàn kho), `changedBy` | `200` **`OrderDetailResponse`** sau khi hủy món | `400` `"..."` (thiếu lý do/quá dài), `403` `{message}` (role không được hủy ở trạng thái món hiện tại), `404` (không thấy đơn/món), `409` `"..."` (đơn `Paid`/`Cancelled`, món đã hủy, xung đột) || PATCH | `/api/orders/{id:int}/promotion/{promotionId:int}` | – | `204` | `400` `"Order or promotion is invalid"` || POST | `/api/payments` | `CreatePaymentRequest` | `200` **`PaymentResponse`** | `400` `{message}` (payment method ngoài allow-list), `404` (body rỗng), `409` `"..."` (đơn đã `Paid`/`Cancelled`, tổng ≤ 0, chưa `Ready`/`Completed`, còn món active chưa xong, hoặc không còn món payable) || GET | `/api/payments` | query `status`,`method`,`orderId`,`from`,`to`,`page`,`pageSize` | `200` **`PagedResponse<PaymentResponse>`** = `{page,pageSize,total,items[]}` | `400` `{message}` (`page` < 1, `pageSize` ngoài 1..100) || GET | `/api/payments/order/{orderId:int}` | – | `200` **`PaymentResponse[]`** | – || GET | `/api/menu` | – | `200` anonymous projection | – |Error body của `OrdersController` / `PaymentsController` phần lớn là **JSON string trần** (`BadRequest(e.Message)`), không phải `{message}`. Riêng lỗi payment method ngoài allow-list trả `400 {message}` (thêm ở Chặng 1).**Idempotency `POST /orders` (Chặng 2):** store in-memory (singleton, TTL 24h, scope theo actor + key). Cùng key + cùng payload → trả snapshot `OrderCreatedResponse` cũ; cùng key + payload khác → `409 "Idempotency key was already used with a different order payload"`. Không gửi header → hành vi cũ. Giới hạn: cache mất khi restart hoặc khi chạy nhiều instance; muốn bền vững cần thêm bảng/cột idempotency vào DB (đổi schema, chưa làm).**Sửa món trong đơn mở (Chặng 2):** `PUT /orders/{id}/items` là **replace-all** — dòng không gửi lên bị xoá. Chỉ nhận đơn `Pending`/`SentToKitchen` và kitchen order + mọi kitchen item còn `Pending`; nếu bếp đã bắt đầu → `409`. Tồn kho được cộng/trừ theo chênh lệch giữa đơn cũ (đọc từ `InventoryTransactions` của chính đơn) và đơn mới; dòng trả kho ghi type `AdjustmentIn`. Toàn bộ thao tác nằm trong transaction `Serializable`; kitchen ticket được tạo lại để khớp dòng món mới.## 1.5 Management (inventory / purchase / promotion / recipe / kitchen)| Verb | Route | Request | Success | Lỗi ||---|---|---|---|---|| GET | `/api/management/ingredients` | – | `200` **`IngredientResponse[]`** | – || POST | `/api/management/ingredients` | `IngredientRequest` | `200` **`IngredientResponse`** | – || PUT | `/api/management/ingredients/{id:int}` | `IngredientRequest` | `204` | `404` || DELETE | `/api/management/ingredients/{id:int}` | – | `204` | `404` || GET | `/api/management/suppliers` | – | `200` **`SupplierResponse[]`** | – || POST | `/api/management/suppliers` | `SupplierRequest` | `200` **`SupplierResponse`** | – || PUT | `/api/management/suppliers/{id:int}` | `SupplierRequest` | `204` | `404` || DELETE | `/api/management/suppliers/{id:int}` | – | `204` | `404` || GET | `/api/management/inventory` | – | `200` **`InventoryResponse[]`** (`quanlity`; không có `rowVersion`) | – || GET | `/api/management/inventory-transactions` | query `ingredientId`,`type`,`referenceType`,`referenceId`,`from`,`to`,`page`,`pageSize` | `200` **`PagedResponse<InventoryTransactionResponse>`** = `{page,pageSize,total,items[]}` (mới nhất trước; có `ingredientName`) | `400` `{message}` (`page` < 1, `pageSize` ngoài 1..100) || POST | `/api/management/inventory/{ingredientId:int}/adjust` | `InventoryAdjustmentRequest` | `204` | `404`, `400` `"..."`, `409` `"..."` (xung đột ghi) || GET | `/api/management/purchase-orders` | – | `200` **`PurchaseOrderResponse[]`** | – || GET | `/api/management/purchase-orders/{id:int}` | – | `200` **`PurchaseOrderDetailResponse`** (`supplierName` + `items[]`: `ingredientName`/`unit`/`quantity`/`unitPrice`/`totalPrice`) | `404` || POST | `/api/management/purchase-orders` | `PurchaseOrderRequest` | `200` **`PurchaseOrderResponse`** | `400` `"..."` || POST | `/api/management/purchase-orders/{id:int}/receive` | `ReceivePurchaseRequest` | `204` | `404`, `409` `"..."` (đã huỷ, thiếu dòng tồn kho hoặc xung đột ghi) || POST | `/api/management/purchase-orders/{id:int}/cancel` | – | `204` (idempotent khi đã huỷ) | `404`, `409` `"..."` (đã nhận hàng) || GET | `/api/management/promotions` | – | `200` **`PromotionResponse[]`** | – || POST | `/api/management/promotions` | `PromotionRequest` | `200` **`PromotionResponse`** | `400` `"..."` || PUT | `/api/management/promotions/{id:int}` | `PromotionRequest` | `204` | `400` `"..."` (loại/giá trị/khảng ngày sai), `404` || PATCH | `/api/management/promotions/{id:int}/active?active` | – | `204` | `404` || DELETE | `/api/management/promotions/{id:int}` | – | `204` | `404` || GET | `/api/management/recipes` | – | `200` **`RecipeSummaryResponse[]`** (mọi field `Recipe` cũ + `ingredientCount`) | – || GET | `/api/management/recipes/{id:int}` | – | `200` `RecipeResponse` (thêm `foodName`/`sizeName`) | `404` || POST | `/api/management/recipes` | `RecipeRequest` | `200` **`RecipeSummaryResponse`** | `400` `"..."`, `409` `"..."` (size đã có công thức active) || PUT | `/api/management/recipes/{id:int}` | `RecipeRequest` | `204` | `404`, `400` `"..."`, `409` `"..."` (size đã có công thức active) || DELETE | `/api/management/recipes/{id:int}` | – | `204` | `404` || GET | `/api/management/kitchen/orders` | – | `200` **`KitchenOrderResponse[]`** | – || GET | `/api/management/kitchen/orders/{id:int}/items` | – | `200` **`KitchenItemResponse[]`** (mọi field `KitchenOrderItem` cũ kể cả `quanlity` + `foodName`/`sizeName`/`quantity`) | – || PATCH | `/api/management/kitchen/orders/{id:int}/status` | `KitchenStatusRequest`: `status`, `reason` (tùy chọn, lưu khi hủy), `reasonCode` (`CustomerChange` (mặc định) / `OutOfStock` / `PhysicalShortage` / `KitchenError`) | `204` | `404`, `400` `"..."`, `403` `{message}` (Chef/Waiter hủy đơn đã có món `Ready`/`Completed`), `409` `"..."` (chuyển sai ma trận, ticket đã `Completed`/`Cancelled` hoặc xung đột ghi) || PATCH | `/api/management/kitchen/items/{id:int}/status` | `KitchenItemStatusRequest`: `status`, `reason` (tùy chọn, lưu khi hủy), `reasonCode` (`CustomerChange` (mặc định) / `OutOfStock` / `PhysicalShortage` / `KitchenError`). **`Accepted` không hợp lệ ở cấp món** (C-11) | `204` | `404`, `400` `"..."`, `403` `{message}` (Chef hủy món `Ready`/`Completed`), `409` `"..."` (chuyển sai ma trận, ticket cha đã kết thúc hoặc xung đột ghi) || POST | `/api/management/kitchen/items/{id:int}/reopen` | `ReopenKitchenItemRequest`: `reason` (tùy chọn, ≤500 ký tự) | `204` — mở lại món `Ready`/`Completed`: món/ticket/order quay về `Preparing` (C-06) | `404` (món không tồn tại), `400` `"..."` (lý do quá dài), `409` `"..."` (order `Paid`/`Cancelled`, ticket `Completed`/`Cancelled`, món chưa `Ready`/`Completed`) || PATCH | `/api/management/kitchen/items/{id:int}/prepared-quantity` | `PreparedQuantityRequest`: `preparedQuantity` (≥1), `changedBy`, `reason` (tùy chọn) | `204` — bếp báo đã nấu bao nhiêu phần; dòng được tách thành phần `Completed` + phần còn lại | `404`, `400` `"..."` (số phần không hợp lệ/vượt số lượng món), `409` `"..."` (ticket/đơn đã kết thúc, món đã nấu xong) || POST | `/api/management/kitchen/items/{id:int}/shortage` | `ReportShortageRequest`: `preparedQuantity` (0..số lượng hiện tại), `reasonCode` (`OutOfStock`/`PhysicalShortage`), `changedBy`, `reason` (tùy chọn) | `204` — giữ phần làm được, huỷ phần thiếu, ghi `Wastage`, không hard-block bếp | `404`, `400` `"..."` (reason code/số phần không hợp lệ), `409` `"..."` (ticket/đơn đã kết thúc, món đã nấu xong) |---# 2. Hành vi xuyên suốt đang được coi là baselineCác mục này **phải giữ nguyên** trong refactor thuần (GĐ1–GĐ4). Nếu muốn đổi → phải là giai đoạn gated (GĐ5+).1. **Phân quyền hiện đang tạm tắt** (cập nhật 2026-10-20): `Auth:Enforce=false` sau khi đã smoke test   `true`; JWT key dev vẫn cấu hình nên login vẫn sinh token. Khi bật `true`, endpoint có `[Authorize]`   trả **401** nếu thiếu/sai token và **403** nếu sai role; `[AllowAnonymous]` vẫn chạy.   **Lưu ý:** frontend hiện chưa gửi `Authorization: Bearer`, nên chỉ bật lại sau khi FE cập nhật.   **Đã pin ở mức HTTP (GĐ6e):** `AuthDisabledHttpTests` giữ baseline anonymous khi tắt enforce; `AuthHttpTests`   xác nhận 401/403/200 khi enforce bật.2. **Actor** (`CreatedBy`, `ChangedBy`, `ReceivedBy`): GĐ6c bước 1 — khi có token thì lấy từ claim `sub` (bỏ qua body, log cảnh báo nếu lệch); khi chưa xác thực (`Auth:Enforce=false`, dev) thì vẫn dùng giá trị body. Field vẫn còn trong DTO. Hiện tại enforce đang tắt nên actor fallback về body.3. **CORS**: policy `FrontendPolicy` **đã được áp** vào pipeline ở GĐ6-A (`afa4664`): `app.UseCors("FrontendPolicy")`, origin `http://localhost:5500` / `5501` / `3000`. Trước GĐ6-A policy đăng ký nhưng vô hiệu.4. **Thời gian**: `UserService/AuthService` dùng `DateTime.Now`; các service khác dùng `DateTime.UtcNow`.5. **Hai kiểu trả lỗi**: `{message}` (controller cũ) và JSON string trần (controller mới).6. **Hai kiểu inject**: constructor (8 controller) và `[FromServices]` (4 controller).7. **Trả entity trực tiếp**: `POST /api/orders`, `POST /api/payments`, `GET /api/payments/order/{orderId}`.   **Đã DTO hoá**: `GET /orders/{id}` → `OrderDetailResponse` (GĐ7-C `46a18e9`); toàn bộ `/api/management/*`   → DTO ở C4 (`c8a2f26`): `IngredientResponse`, `SupplierResponse`, `InventoryResponse`,   `PurchaseOrderResponse`, `PromotionResponse`, `RecipeSummaryResponse` (GET list + POST),   `RecipeResponse` (GET detail), `KitchenOrderResponse`, `KitchenItemResponse`.   `GET /management/inventory` bỏ `rowVersion` nội bộ; các field frontend đang đọc giữ nguyên.8. **Order status GIỜ ĐÃ validate** (Nhóm B2 `29c01d1`): `PATCH /api/orders/{id}/status` chỉ nhận   allow-list `Pending/SentToKitchen/Preparing/Ready/Completed/Cancelled` (không nhận `Paid`), từ chối   chuỗi rỗng/không hợp lệ (`400`) và không cho sửa khi đơn đã ở trạng thái cuối `Paid`/`Cancelled` (`409`).   (Đã pin bằng `UpdateStatus_RejectsStatusOutsideAllowList` / `_RejectsBlankStatus` / `_RejectsChangeFromTerminalState`.)9. **Kitchen status có validate** (allow-list) ở `KitchenService`. Bản trùng `ManagementService.UpdateKitchen`   (không validate) **đã xoá ở D2 `8fa8daa`**, nên từ nay chỉ còn một hành vi bếp duy nhất.   (Test `ManagementKitchen_UpdateSetsTimestamps_ButDoesNotValidateStatus` đã bỏ cùng dead code.)10. **GET bàn theo id có side-effect ghi DB** (tracked entity + `SaveChanges`);    **GET all bàn thì không ghi DB** (do repository dùng `AsNoTracking`).    (Đã pin bằng 2 test `GetById_ComputesOccupied_AndPersistsIt` và `GetAll_ComputesStatus_ButDoesNotPersistIt`.)11. **Soft delete + unique index**: trước đây `toppings.ToppingName` (và category/size/ingredient/table) unique    **không** lọc `IsActive` nên không tạo lại được cùng tên sau soft delete. **Đã sửa ở Nhóm B6 (`62f5d44`)**:    index có filter `[IsActive] = 1`. **Ngoại lệ:** `users.UserName`/`UserEmail` vẫn unique toàn cục (định danh đăng nhập).12. **`Category.DisplayOrder` là `bool`** (entity + DTO) → không thể biểu diễn thứ tự hiển thị thật.    Đổi sang `int` là **thay đổi schema**, cần phê duyệt riêng.13. **`GET /api/menu` trả anonymous types** (`Task<object>`), không có DTO.14. **Khởi động app tự `Migrate()` + seed Roles** (Admin, WarehouseManager, Chef, Cashier, Waiter) — GĐ8 đã tách    sang `DatabaseInitializer`; hành vi mặc định không đổi (`Database:MigrateOnStartup`/`SeedOnStartup` = `true`).15. **`RoleEnum`** (Admin/Manager/Staff/Customer) là dead code và **lệch** với role seed thật.### Thay đổi hành vi có chủ ý (Nhóm B, `29c01d1`) — KHÔNG còn là baseline cũ16. **Ánh xạ trạng thái bếp (B1)**: `Completed`/`Ready` → order `Ready`; `Preparing` → `Preparing`;    `Pending`/`Accepted` → `SentToKitchen`; **`Cancelled` → order `Cancelled`** (trước đây `Cancelled` bị đưa về `SentToKitchen`).17. **Thanh toán (B4/B)**: không thanh toán đơn `Cancelled` (`409`); `payment` + cập nhật `order.Status` là **nguyên tử** (transaction).18. **Tạo ingredient / purchase order (B4)**: chèn header + dòng là **nguyên tử** (transaction).19. **Dọn dữ liệu (B8)**: `Order.Items` null → `400` (không NRE); `.Trim()` null-safe ở recipe/promotion; `UserService`/`AuthService` dùng `UtcNow`.### Thay đổi hành vi có chủ ý (Nhóm B — phần schema, `62f5d44`)20. **Thanh toán trùng (B3)**: unique index lọc trên `payments.OrderId` với `[Status] = 'Paid'` ⇒ mỗi đơn chỉ    có **một** payment `Paid`; nhiều payment `Failed`/chưa thanh toán vẫn hợp lệ. `POST /api/payments` trả    `409 "Order is already paid"` khi vi phạm. Đơn `TotalAmount <= 0` bị chặn (`409 "Order total must be greater than zero"`).21. **Công thức (B5)**: mỗi `SizeId` chỉ có **một** recipe `IsActive=1` (unique index lọc + kiểm tra trong    `CreateAsync`/`UpdateAsync`) ⇒ `POST`/`PUT /api/management/recipes` trả `409 "This size already has an active recipe"`.    Công thức đã soft-delete không chiếm size, nên tạo lại được.22. **Đồng thời (B7)**: `orders`/`inventory`/`restaurant_tables` có `RowVersion` (`rowversion` trên SQL Server).    Request ghi dựa trên dữ liệu cũ ⇒ `409 "Dữ liệu vừa bị thay đổi bởi thao tác khác..."` thay vì ghi đè im lặng.    `RefreshStatusForTableAsync` (side-effect sau khi transaction chính commit) **retry** tối đa 2 lần thay vì trả 409.23. **Quy ước `Quanlity` (C4 `c8a2f26`)**: entity + cột DB đã dùng `Quanlity`; C# DTO/request/service cũng    được chuẩn hoá về `Quanlity`. Các endpoint cũ vẫn phát/nhận JSON `quantity` bằng `[JsonPropertyName]`    để frontend hiện tại không vỡ; riêng `GET /management/inventory` phát `quanlity` (frontend `inventory.js`    đang đọc). **Không có migration** vì DB thật đã là `Quanlity`.---## 1.6 Reports (P3)| Verb | Route | Request | Success | Lỗi ||---|---|---|---|---|| GET | `/api/reports/revenue?period&date` | `period` = `day` (mặc định) \| `week` \| `month`; `date` = ngày bất kỳ trong kỳ (giờ VN, mặc định hôm nay) | `200` **`RevenueReportResponse`** = `{period, timeZone, from, to, totalRevenue, orderCount, averagePerOrder, groups[]}` (`from` bao gồm, `to` loại trừ, UTC; `groups` đủ bucket: 24 giờ / 7 ngày / số ngày trong tháng; `label` = `HH:00` hoặc `dd/MM`) | `400` `{message}` (`period` không thuộc day/week/month) || GET | `/api/reports/revenue/export.csv?period&date` | như trên | `200` `text/csv; charset=utf-8` (UTF-8 **BOM**, `\r\n`): header cố định `period,groupStartVn,groupEndVn,revenue,orderCount` + dòng `TOTAL` cuối; số dùng dấu chấm; thời gian là giờ VN | `400` `{message}` || GET | `/api/reports/dashboard` | – | `200` **`DashboardSummaryResponse`** = `{generatedAt, timeZone, businessDate, from, to, revenueToday, paidOrderCountToday, openOrderCount, lowStockCount, tables{total,available,occupied,maintenance}, kitchen{open,pending,preparing,ready}, recentOrders[]}` | – |Chỉ tính **payment `Paid`** (money đã thu) — không tính giá trị đơn chưa thanh toán. Cả 2 endpoint đều `AdminOnly`.---# 3. Cách dùng baseline để gate refactorVới mỗi giai đoạn:1. Chạy `dotnet build` → không được tăng số warning/error.2. Chạy `dotnet test` → **376/376 phải pass** (105 lúc ghi baseline + test các giai đoạn sau; đã bỏ 1 test dead code).3. Với thay đổi có chạm controller/response shape: so sánh route + verb + status code + JSON   với bảng ở mục 1 (baseline này).4. Với thay đổi có chạm schema: **dừng lại**, xin phê duyệt (xem mục 2, điểm 12).## Giới hạn đã biết của lưới an toàn hiện tại- Test phần lớn chạy ở **tầng service + DbContext**. Từ **GĐ8** đã có host test  (`WebApplicationFactory` + `RestaurantApiFactory`, SQLite in-memory); **GĐ6e** dùng nó để khoá 401/403/200  cho toàn bộ ma trận policy (kể cả baseline `Auth:Enforce=false`).- **Status code nghiệp vụ** (400/404/409…) vẫn được khóa chủ yếu bằng tài liệu (mục 1); tầng HTTP mới phủ auth +  smoke `GET /api/setup/status`.- Muốn mở rộng test HTTP: dùng `RestaurantApiFactory` / `EnforcedRestaurantApiFactory`  (`Database:MigrateOnStartup=false`) rồi `CreateClient()`.## Ghi chú rà soát bổ sung — 2026-10-20- Phiên rà soát **không đổi route/JSON/schema**; baseline **376/376** vẫn giữ nguyên.- Tại thời điểm rà soát, các lỗi về concurrency, kitchen terminal, hủy đơn/hoàn kho, promotion,  validation, setup race và auth chưa sửa — xem `backend-frontend-audit.md` mục 15. Sau đó P1c/P1d  (ma trận trạng thái, hủy món/đơn, hoàn kho, thanh toán) đã được triển khai — xem mục cuối file.- Khi sửa các mục đó, cập nhật bảng endpoint nếu có thay đổi status code/body; các thay đổi chỉ thêm validate/transaction thì không đổi contract.---## P1c/P1d — ĐÃ TRIỂN KHAI (2026-10-20)Tài liệu chi tiết: `docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md`.Các thay đổi dưới đây **đã có trong code** và **là baseline hiện tại**. Bảng endpoint phía trên đãđược cập nhật tương ứng.### Trạng thái- **Đơn:** `Pending → SentToKitchen/Preparing/Ready/Cancelled`; `SentToKitchen → Preparing/Ready/Cancelled`;  `Preparing → Ready/Completed/Cancelled`; `Ready → Completed/Cancelled`; `Completed → Cancelled`.  `Paid` chỉ do `PaymentService` đặt; `Paid`/`Cancelled` là trạng thái cuối. Chặn `Ready → Pending`,  `Pending → Completed` → `409`.- **Món trong đơn / món bếp:** chỉ đi tiến; `Pending → Preparing/Ready/Completed/Cancelled`;  `Preparing → Ready/Completed/Cancelled`; `Ready → Completed/Cancelled`; `Completed`/`Cancelled` đóng  với route đổi trạng thái thường. Riêng route hủy món, Admin được hủy cả món `Ready`/`Completed`.- **Phiếu bếp:** `Pending → Accepted/Preparing/Ready/Completed/Cancelled`;  `Accepted → Preparing/Ready/Completed/Cancelled`; `Preparing → Ready/Completed/Cancelled`;  `Ready → Completed/Cancelled`; `Completed`/`Cancelled` đóng.- **Gộp phiếu bếp:** bỏ món `Cancelled`; tất cả món bị hủy → phiếu `Cancelled`; mọi món active  `Ready`/`Completed` → `Ready`; có món `Preparing` → `Preparing`; còn lại giữ trạng thái hiện tại.### Phân quyền hủy| Hành động | Waiter | Chef | Admin ||---|---|---|---|| Hủy món `Pending` | ✅ | ✅ | ✅ || Hủy món `Preparing` | ❌ | ✅ | ✅ || Hủy món `Ready`/`Completed` | ❌ | ❌ | ✅ || Hủy đơn khi chưa có món `Ready`/`Completed` | ✅ | ✅ | ✅ || Hủy đơn khi đã có món `Ready`/`Completed` | ❌ | ❌ | ✅ || Hủy đơn đã `Paid` | ❌ | ❌ | ❌ |- Route hủy món dùng policy `OrderCancellation` (Admin/Waiter/Chef); `PATCH /orders/{id}/status`  dùng `PosOperator` (Admin/Waiter); kitchen dùng `KitchenDesk` (Admin/Chef).- Khi request không có role (test cũ/không xác thực), service bỏ qua kiểm tra role.- Vi phạm quyền → `403` `{message}`.### Hủy món / hủy đơn- Hủy đơn bắt buộc `note`; hủy món bắt buộc `reason`; tối đa 500 ký tự.- Chỉ hoàn kho phần món còn `Pending` (cả order item và kitchen item). `Preparing/Ready/Completed`  coi như đã tiêu hao, không hoàn. Đơn legacy chưa từng trừ kho → không cộng tồn kho ảo.- Hủy món giảm `Subtotal/TotalAmount`, reset `DiscountAmount = 0`; món cuối bị hủy → đơn `Cancelled`  và trả bàn. Hủy cả đơn giữ nguyên tiền lịch sử.- Hủy đơn/món đã `Paid` → `409`, không hoàn kho.- Ledger: tạo đơn ghi `OrderItemId` theo từng món; `PUT /orders/{id}/items` ghi cấp đơn  (`OrderItemId = null`); hủy món fallback ước tính từ công thức nhưng chặn bởi lượng tiêu hao còn lại.- Migration `20261020093304_AddOrderItemCancellationAndItemLedger` thêm  `order_items.CancelReason/CancelledAt/CancelledBy` và `inventory_transactions.OrderItemId`  (FK `SET NULL`). **Chưa apply** DB thật.### Thanh toán- Đơn `Cancelled` → `409`; đơn đã có payment `Paid` → `409`.- Đơn có dòng món: mọi món active phải `Ready`/`Completed`, đơn phải `Ready`/`Completed`; không còn  món active → `409 "Order has no payable items"`.- Đơn không có dòng món (legacy) giữ hành vi cũ, bỏ qua kiểm tra readiness.- Bàn chỉ được trả sau khi đơn `Paid` (hoặc `Cancelled`) và không còn đơn mở khác.### Test- `OrderCancellationTests.cs` (13 test) phủ ma trận trạng thái, quyền Chef/Waiter/Admin, hoàn kho  theo món, tự hủy đơn và điều kiện thanh toán.- Tổng: **466/466 pass**, build **0 warning / 0 error**.---## Rà soát hồi quy — 2026-10-20 (đợt 2)Baseline **đã cập nhật** theo code mới. Chi tiết lý do ở`docs/architecture/progress-summary-2026-10-20.md` mục 1.9.### Thay đổi status code (cần biết khi tích hợp)| Endpoint | Trước | Nay | Lý do ||---|---|---|---|| `POST /api/restaurant-tables` | `400` khi trùng số bàn | **`409`** `{message}` | Trùng số bàn là xung đột, khớp `PUT` || `POST /api/auth/register` | `400` "Username already exists" | **`409`** `{message}` | Trùng định danh là xung đột || `POST /api/users` | `400` "Username/Email already exists" | **`409`** `{message}` | Như trên (khớp `/api/management/ingredients`) || `POST /api/users/{userId}/roles` | `400` "Role not found" | **`404`** `{message}` | Không tìm thấy role/user || `POST /api/users/{userId}/roles` (gán trùng) | `400` (lỗi SQL) | **`409`** `{message}` | Role đã có ⇒ xung đột || `PATCH /api/management/promotions/{id}/active` (thiếu `?active=`) | `204` + tắt nhầm | **`400`** `{message}` | `bool` không nullable mặc định `false` |### Thay đổi hành vi- **Actor trong body phải là user thật**: `createdBy`/`changedBy` trỏ tới id không tồn tại ⇒  `400` ("... does not reference an existing user") thay vì lỗi FK ⇒ `500`.- **Actor rỗng được lùi về người tạo đơn**: `PATCH /orders/{id}/status` và  `POST /orders/{id}/items/{itemId}/cancel` không gửi `changedBy` ⇒ dùng `orders.CreatedBy`  (trước đây ghi id `0` ⇒ lỗi FK ⇒ `500`).- **`GET /api/management/inventory-transactions?referenceId=N`** bắt buộc kèm `referenceType`  (`Order`/`PurchaseOrder`) ⇒ thiếu thì `400`; trước đây trộn hai không gian id.- **`POST /api/management/inventory/{ingredientId}/adjust`** với số lượng `0` ⇒ `400` (trước đây  ghi 1 dòng ledger rỗng). Số lượng được làm tròn 3 chữ số (khớp `decimal(18,3)`).- **`GET /api/reports/revenue*`**: `from`/`to`/`groups[].from|to` **luôn** là UTC và có đuôi `Z`  (trước đây `Kind` không nhất quán nên `period=month` hoặc khi có `?date=` trả về chuỗi không  có `Z` ⇒ client JS parse sai múi giờ). `?date=` không kèm múi giờ được hiểu là **giờ VN**.- **Tên file CSV** dùng giờ VN (`revenue-<period>-<yyyyMMddHHmm giờ VN>.csv`).- **Mật khẩu 6–72 BYTE UTF-8** (không phải 6–72 ký tự): BCrypt chỉ dùng 72 byte đầu ⇒ mật khẩu  tiếng Việt dài bị cắt âm thầm trước đây. Mật khẩu toàn khoảng trắng bị từ chối.- **Cột `decimal(18,2)` được làm tròn trước khi ghi**: `promotions.DiscountValue /  MinimumOrderAmount / MaxDiscountAmount`, `purchase_orders.TotalAmount` và  `purchase_order_items.TotalPrice` ⇒ giá trị API trả về luôn bằng giá trị thực sự lưu/áp dụng.- **Khuyến mãi không tính món đã huỷ**: `order_items.Status = Cancelled` bị loại khỏi cơ sở tính  giảm giá.- **`PUT /api/management/recipes/{id}`** cập nhật dòng nguyên liệu theo chênh lệch (không xoá rồi  chèn lại cùng khóa); định lượng ≤ 0 ⇒ `400`.- **`POST /api/management/recipes`** khi trùng công thức active của cùng size (race) ⇒ `409` thay  vì `500`.- **Request `/api/management/*`** có `MaxLength` khớp cột DB ⇒ chuỗi quá dài trả `400` thay vì lỗi  "truncated" (`500`).### Không đổi- Route, verb, tên field JSON, và toàn bộ hợp đồng cũ ngoài những dòng trên.- Không đổi schema: vẫn 5 migration, `has-pending-model-changes` = **No changes**.
---

## C-13 + Đợt sửa P0 (2026-10-21) — thay đổi hợp đồng API

> Baseline cũ ở trên **giữ nguyên**. Phần này ghi lại **thay đổi** so với baseline.
> Build **0 warning / 0 error** · test **550/550 pass** · migration `20261021025953_AddIngredientStockStatusAndFoodInactiveReason` (đã apply).
> Luật nghiệp vụ đầy đủ: `docs/architecture/kitchen-stock-rule-2026-10-21.md`.

### C-13.1 Endpoint mới (1)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| PATCH | `/api/management/ingredients/{id:int}/status` | `IngredientStatusRequest`: `stockStatus` (`Available`/`OutOfStock`/`Spoiled`), `note` (tuỳ chọn) | `204` | `400` `{message}` (status không hợp lệ), `404` |

### C-13.2 Query param mới (1)

| Route | Query | Ý nghĩa |
|---|---|---|
| `GET /api/management/kitchen/orders` | `status=open` | chỉ trả phiếu **đang mở** (loại `Completed`/`Cancelled`) — dùng để poll màn hình bếp 3–5s |

### C-13.3 Response thêm field

| DTO | Field mới |
|---|---|
| `KitchenOrderResponse` | `tableNumber` (string), `waiterName` (string?), `itemCount` (int) |
| `IngredientResponse` | `stockStatus` (string: `Available`/`OutOfStock`/`Spoiled`) |

### C-13.4 Request mở rộng (4)

| Request (route) | Field mới |
|---|---|
| `ReportShortageRequest` — `POST /api/management/kitchen/items/{id}/shortage` | `ingredientId` (int?), `deactivateDish` (bool), `inactiveReason` (string?) |
| `KitchenStatusRequest` — `PATCH /api/management/kitchen/orders/{id}/status` | `changedBy` (int) |
| `KitchenItemStatusRequest` — `PATCH /api/management/kitchen/items/{id}/status` | `changedBy` (int) |
| `ReopenKitchenItemRequest` — `POST /api/management/kitchen/items/{id}/reopen` | `changedBy` (int) |

### C-13.5 Hành vi mới (quan trọng — đọc kèm luật bếp)

| Trường hợp | Hành vi mới |
|---|---|
| Món `Preparing` (bếp đã bấm) | **KHÔNG hoàn kho** — ghi `Wastage`. Món này **khóa với POS** (không huỷ/giảm được); chỉ Chef/Admin |
| `PATCH /api/orders/{orderId}/items/{itemId}/quantity` giảm món `Preparing` bởi Waiter | **`403`** `{message}` (trước: cho qua) |
| `GET /api/orders/{id}/history` | thêm dòng **"đơn được tạo"** (`Pending → SentToKitchen`) + dòng **`→ Paid`** |
| `GET /api/orders/{id}/item-history` | thêm dòng **"dòng món được tạo"** (`"" → Pending`) |
| Huỷ đơn | phiếu bếp đã `Completed` **giữ nguyên** (trước: bị đánh dấu `Cancelled`) |
| `POST /api/management/kitchen/items/{id}/shortage` với `deactivateDish=true` | món bị `IsActive=false` (biến mất khỏi menu POS) + lưu `InactiveReason`/`InactiveAt`; **nguyên liệu giữ nguyên** |

### Đợt sửa P0 (2026-10-21) — thay đổi hợp đồng HTTP

| Endpoint | Trước | Nay |
|---|---|---|
| `POST /api/payments` | `500` khi xung đột ghi | **`409`** `{message}` |
| `POST /api/orders` | `500` khi xung đột ghi | **`409`** `{message}` |
| `POST /api/orders/{id}/items` | `500` khi xung đột ghi | **`409`** `{message}` |
| `PATCH /api/orders/{orderId}/items/{itemId}/quantity` | `500` khi xung đột ghi | **`409`** `{message}` |
| `POST /api/orders/{orderId}/items/{itemId}/cancel` | `500` khi xung đột ghi | **`409`** `{message}` |

> **Không** đổi route, **không** đổi tên field JSON cũ, **không** đổi schema ngoài migration C-13.
> `PUT /api/orders/{id}/items` vẫn trả **`410 Gone`** như cũ.

---

## C-14 — "Tiền đúng" (2026-10-21) — thay đổi hợp đồng API

> Build **0 warning / 0 error** · test **569/569 pass** ·
> migration `20261021041728_AddSettingsTotalsPromotionSnapshot` (**đã apply trên DB thật**).
> Chi tiết luật: `docs/architecture/progress-summary-2026-10-21.md` §C-14.

### C-14.1 Endpoint mới (3)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/management/settings` | — | `200` cấu hình quán | `401`/`403` (Admin) |
| PUT | `/api/management/settings` | `RestaurantSettingsRequest` | `200` cấu hình đã lưu | `400` `{message}` (tỉ lệ ngoài 0–100), `401`/`403` |
| DELETE | `/api/orders/{id:int}/promotion/{promotionId:int}` | — | `204` | `400` `{message}` (đơn không đang áp khuyến mãi đó), `409` (đơn đã `Paid`/`Cancelled`) |

### C-14.2 Header mới `Idempotency-Key` (3 route)

| Route | Tác dụng |
|---|---|
| `POST /api/orders/{id}/items` | gọi thêm món — bấm 2 lần chỉ tạo **1** round bếp và trừ kho **1** lần |
| `PATCH /api/orders/{id}/items/{itemId}/quantity` | đổi số lượng — không trừ/trả kho 2 lần |
| `POST /api/payments` | thu tiền — không tạo 2 bản ghi payment, đơn chỉ chuyển `Paid` 1 lần |

- Cùng key + cùng payload ⇒ trả lại **kết quả cũ** (không thực thi lại).
- Cùng key + **payload khác** ⇒ **`409`** `{message}` (`... different request payload`).
- **Không** gửi header ⇒ hành vi cũ (không chống trùng).
- Key tối đa **128 ký tự**; key được gộp theo người dùng + route nên 2 route khác nhau không đụng nhau.

### C-14.3 Response thêm / đổi kiểu field

| DTO | Thay đổi |
|---|---|
| `OrderDetailResponse` | **+** `serviceChargeAmount` (decimal), `promotionId` (int?), `promotionDiscountType` (string?), `promotionDiscountValue` (decimal?) |
| `OrderListItemResponse` | **+** `serviceChargeAmount` (decimal) |
| `OrderCreatedResponse` | **+** `serviceChargeAmount`, `promotionId`, `promotionDiscountType`, `promotionDiscountValue` |
| `OrderStatusHistoryResponse` | **+** `reasonCode` (string?) — `CustomerChange`/`OutOfStock`/`PhysicalShortage`/`KitchenError` |
| `PromotionResponse` | **+** `startAtUtc`, `endAtUtc`; `startAt`/`endAt` nay trả theo **giờ VN** (xem Đợt 2.5) |
| `CategoryDTO` | `displayOrder` đổi kiểu **`bool` → `int`** (sắp xếp danh mục được) |

### C-14.4 Công thức tiền (đọc trước khi hiển thị hoá đơn)

Mọi luồng ghi tiền giờ đi qua **một** hàm `OrderTotalsCalculator`:

```
base = Subtotal − Discount
TaxAmount        = TaxIncluded ? base × rate / (100 + rate)   : base × rate / 100
ServiceCharge    = base × serviceRate / 100
TotalAmount      = TaxIncluded ? base + ServiceCharge         : base + TaxAmount + ServiceCharge
```

- `TaxIncluded = true` (mặc định, chủ dự án chốt 2026-10-21): **giá menu đã bao gồm VAT** ⇒ VAT chỉ **tách ra để in**, **không** cộng thêm vào tổng.
- `TaxIncluded = false`: giá menu chưa gồm VAT ⇒ VAT **cộng thêm** vào tổng.
- **Phí dịch vụ luôn cộng thêm** và tính trên **tiền hàng sau giảm giá**.
- Mặc định (`taxRate = 0`, `serviceChargeRate = 0`) ⇒ tổng = tiền hàng − giảm giá, **đúng hành vi hệ thống cũ**.

> ⚠️ `Payment.Amount` = `order.TotalAmount` ⇒ khi bật VAT/phí DV, **tiền thu** và **doanh thu báo cáo**
> đều là **tiền khách trả** (đã gồm VAT + phí DV). Báo cáo "doanh thu thuần" là việc của Đợt 4.

### C-14.5 Quy ước múi giờ (P0-05)

- **Đầu vào** `promotions.startAt` / `endAt`: **giờ Việt Nam (UTC+7)**. Giá trị **không kèm múi giờ** được hiểu là giờ VN; nếu client gửi kèm múi giờ (ISO-8601 có `Z`/offset) thì giữ nguyên mốc đó.
- **Lưu trong DB**: **UTC** (cột `datetime2` không có timezone ⇒ phải quy đổi).
- **Đầu ra** `PromotionResponse`: `startAt`/`endAt` = **giờ VN** (đúng quy ước đầu vào ⇒ FE gửi lại nguyên vẹn khi sửa); `startAtUtc`/`endAtUtc` = mốc UTC thật.
- Migration đã **trừ 7 giờ** cho các dòng `promotions` cũ (cửa sổ khuyến mãi theo giờ VN giữ nguyên).

### C-14.6 Hành vi mới khác

| Trường hợp | Hành vi mới |
|---|---|
| Áp khuyến mãi | đơn lưu `promotionId` + snapshot `promotionDiscountType`/`promotionDiscountValue`; ghi 1 dòng audit "Applied promotion N" |
| Gỡ khuyến mãi | `DELETE /api/orders/{id}/promotion/{promotionId}` ⇒ `discountAmount = 0`, `promotionId = null`, tính lại tổng |
| Sửa món / gọi thêm / huỷ món | discount và `promotionId` bị **bỏ** (cơ sở khuyến mãi đã đổi) ⇒ tổng tính lại |
| Huỷ đơn | dòng lịch sử đơn mang `reasonCode` (`OrderStatusHistoryResponse.reasonCode`) |
| Danh mục | `displayOrder` là **số** ⇒ menu sắp theo `displayOrder` rồi tên |

> **Không** đổi route cũ, **không** đổi tên field JSON cũ.
> MoMo: backend **không** đụng provider/thanh toán tích hợp — `Payment` entity / `Payment.Status` / cột `payments` **không đổi**.

---

## C-15 — Chuyển bàn + thanh toán MoMo (2026-10-21) — thay đổi hợp đồng API

> Build **0 warning / 0 error** · test **594/594 pass** ·
> migration `20261021052642_AddMomoPaymentProvider` (**đã apply trên DB thật**).

### C-15.1 Endpoint mới (4)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| PATCH | `/api/orders/{id:int}/table` | `ChangeOrderTableRequest`: `tableId`, `changedBy` | `204` | `400` `{message}` (bàn không tồn tại / bảo trì / trùng bàn cũ), `409` `{message}` (bàn đích có đơn mở, hoặc đơn đã `Paid`/`Cancelled`) |
| POST | `/api/payments/momo` | `CreateMomoPaymentRequest`: `orderId`, `createdBy` | `200` `MomoIntentResponse` | `400` `{message}`, `409` `{message}` (đơn chưa sẵn sàng / đã thu / đã huỷ) |
| POST | `/api/payments/momo/notify` | `MomoNotificationRequest` (**MoMo gọi, không cần token**) | `200` `{ accepted, applied, paymentId, message }` | — (IPN sai chữ ký vẫn trả 200 nhưng `accepted = false`) |
| POST | `/api/payments/momo/mock/{paymentId:int}?success=true` | — | `200` `{ accepted, applied, paymentId, message }` | `400` `{message}`, `409` `{message}` (chỉ chạy khi `Payments:Provider = Mock`) |
| POST | `/api/payments/{paymentId:int}/refund` | `RefundPaymentRequest`: `reason`, `createdBy` | `200` `PaymentResponse` (`status = Refunded`) | `400` `{message}` (thiếu lý do), `409` `{message}` (giao dịch không ở trạng thái `Paid`) |

> `POST /api/payments/momo/notify` có `[AllowAnonymous]` — MoMo không đăng nhập được.
> An toàn nằm ở **chữ ký HMAC + kiểm tra số tiền**, không nằm ở xác thực.

### C-15.2 Chuyển bàn — luật đã chốt

| Tình huốp | Hành vi |
|---|---|
| Bàn đích **đang có đơn mở** | **`409`** — ngoài bàn đó đang có người nên không thể cho vào |
| Bàn đích bảo trì / đã xoá | `400` |
| Chuyển sang chính bàn cũ | `400` |
| Đơn đã `Paid`/`Cancelled` | `409` |
| Thành công | đơn đổi `tableId` · bàn cũ → `Available` · bàn mới → `Occupied` · lịch sử đơn có dòng "Moved from table X to table Y" |
| Quyền | `PosOperator` = **Waiter + Admin** |

### C-15.3 Thanh toán MoMo — luồng

```
POST /api/payments/momo          → tạo intent (Payment.Status = Pending), trả redirectUrl + qrCodeUrl
   khách quét QR / mở redirectUrl
POST /api/payments/momo/notify   → MoMo gọi về: verify chữ ký → kiểm tra số tiền → Payment = Paid, đơn = Paid
```

- `Payment.Status` nay có thêm: `Pending` · `Failed` · `Refunded` (cũ chỉ có `Paid`).
- `Payment.PaidAt` **nullable** (intent chưa thu thì chưa có thời điểm thu).
- **Số tiền luôn lấy từ đơn**, không tin giá trị client gửi lên; IPN sai số tiền ⇒ từ chối.
- **IPN gửi lại nhiều lần là idempotent** (lần sau trả `applied = false`, không ghi 2 lần).
- Bấm "tạo giao dịch" 2 lần ⇒ **trả lại cùng intent** (không tạo 2 QR, khách không quét nhầm).

### C-15.4 Chế độ giả lập (môi trường test) — không tốn tiền thật

Cấu hình `Payments:Provider`:
- `"Mock"` (mặc định) — **không gọi ra ngoài**. Backend tự sinh payload IPN **đã ký chữ ký đúng**
  bằng đúng khoá mà lúc verify dùng ⇒ chạy **đúng đường kiểm tra thật**.
  `redirectUrl` trỏ về `POST /api/payments/momo/mock/{paymentId}?success=true` — gọi nó là đóng tiền thành công.
- `"MoMo"` — gọi thật ra `api.momo.vn`. Cần đặt 3 khoá (không commit vào repo):
  `Payments__MoMo__PartnerCode`, `Payments__MoMo__AccessKey`, `Payments__MoMo__SecretKey`.

### C-15.5 Hoàn tiền

- Chỉ hoàn **toàn bộ** (chưa cần hoàn từng phần).
- `Payment.Status` → `Refunded`, `RefundedAt` được ghi.
- **Đơn mở lại về `Ready`** (đã phục vụ xong, chờ thu lại) + lịch sử đơn có dòng `Paid → Ready`.
- Bàn trở lại `Occupied` (đơn lại đang mở).
- Giao dịch MoMo thật ⇒ gọi API refund của MoMo **trước khi** ghi nhận; MoMo lỗi ⇒ không hoàn.

### C-15.6 Response thêm / đổi field

| DTO | Thay đổi |
|---|---|
| `PaymentResponse` | **+** `provider`, `requestId`, `providerTransactionId`, `note`, `providerResponse`, `createdAt`, `refundedAt`; `paidAt` **→ nullable** |
| `MomoIntentResponse` (mới) | `paymentId`, `orderId`, `amount`, `status`, `provider`, `requestId`, `redirectUrl`, `qrCodeUrl`, `isMock` |

### C-15.7 Lưu ý nghiệp vụ

- `Payment.Amount` = `order.TotalAmount` ⇒ **tiền thu** và **doanh thu báo cáo** đều là *tiền khách trả*
  (đã gồm VAT + phí DV nếu bật). Báo cáo "doanh thu thuần" vẫn là việc của Đợt 4.
- Cột `payments.Provider` mặc định `Direct` (thu tiền mặt/thẻ nội bộ), `MoMo` cho luồng provider.

---

## C-16 — Hợp đồng lỗi thống nhất (2026-10-21)

> Build **0 warning / 0 error** · test **602/602 pass** ·
> migration `20261021055037_AddPaymentsStatusCheckConstraint` (**đã apply trên DB thật**).

### C-16.1 Ánh xạ exception → HTTP

Mọi exception lọt ra khỏi controller đều do `ApiExceptionFilter` (`Infrastructure/Errors/`)
đổi thành lỗi HTTP. Body lỗi **luôn** là `{ "message": "..." }`.

| Exception | HTTP | Nghĩa |
|---|---|---|
| `KeyNotFoundException` | **`404`** | Không tìm thấy món/danh mục/bàn/đơn |
| `ArgumentException` | **`400`** | Dữ liệu đầu vào sai |
| `InvalidOperationException` | **`409`** | Xung đột nghiệp vụ (trùng tên, vi phạm luật, xung đột ghi) |
| còn lại | **`500`** | Lỗi hệ thống — message chung, chi tiết **chỉ ghi log server-side** |

### C-16.2 Thay đổi hành vi

| Trước | Nay |
|---|---|
| 5 controller danh mục `catch (Exception ex)` ⇒ **lỗi SQL / mất kết nối cũng trả `400`/`404`** kèm `ex.Message` thô | Lỗi hệ thống ⇒ **`500`** + message chung; log đầy đủ server-side |
| 18 chỗ `throw new Exception("...")` trong 5 service | Exception có ngữ nghĩa (`KeyNotFound` / `InvalidOperation`) |
| **50** chỗ trả lỗi dạng **text thuần** (`Conflict(e.Message)`) | **JSON** `{ "message": "..." }` — đúng hợp đồng `api.js` đang đọc |
| 7 chỗ `KeyNotFoundException` trả `400` | **`404`** (khớp với `ApiExceptionFilter` và bảng lỗi của FE) |
| `payments.Status` không có ràng buộc ở tầng DB | **CHECK constraint `CK_payments_Status`** (`Pending`/`Paid`/`Failed`/`Refunded`/`Cancelled`) |

### C-16.3 Endpoint không đổi

> **Không** đổi route, **không** đổi tên field JSON, **không** đổi body của request.
> Chỉ thay **status code** và **dạng body lỗi** — FE đọc `payload.message` nên nay mọi
> lỗi đều có message đọc được (trước đây lỗi dạng text khiến `payload.message` là `undefined`).

---

## C-17 — Ledger kho có đơn vị + khoá đơn vị nguyên liệu (2026-10-21)

> Build **0 warning / 0 error** · test **618/618 pass** ·
> migration `20261021061510_AddInventoryTransactionUnit` (**đã apply trên DB thật**).

### C-17.1 Ledger ghi kèm đơn vị tính

`inventory_transactions.unit` (nvarchar(50), **nullable**) — **snapshot** đơn vị tại thời điểm
ghi dòng. Nhờ vậy một dòng ledger "2" luôn đọc được là 2 kg hay 2 g.

| Nơi ghi dòng ledger | `unit` lấy từ |
|---|---|
| `Sale` (trừ kho khi gọi món) | `Ingredients.Unit` lúc trừ kho |
| `Purchase` (nhận kho) | `Ingredients.Unit` lúc nhận |
| `AdjustmentIn` / `AdjustmentOut` (điều chỉnh tay) | `Ingredients.Unit` lúc điều chỉnh |
| `AdjustmentIn` (hoàn kho: huỷ đơn / huỷ món / giảm số lượng) | `Ingredients.Unit` lúc hoàn |
| `Wastage` (hao hụt) | `Ingredients.Unit` lúc ghi |
| Dòng **tách** khi tách dòng món | **giữ nguyên** đơn vị của dòng gốc |

Migration **backfill** đơn vị cho toàn bộ dòng ledger cũ (theo đơn vị hiện tại của nguyên liệu).

### C-17.2 Response thêm field (1)

`GET /api/management/inventory-transactions` → mỗi item có thêm **`unit`** (string, có thể `null`).

```json
{ "id": 12, "ingredientId": 3, "ingredientName": "Thịt bò", "type": "Sale",
  "quanlity": 2.0, "quanlityBefore": 10.0, "quanlityAfter": 8.0,
  "unit": "kg", "referenceType": "Order", "referenceId": 42, "note": null,
  "createdAt": "...", "createdBy": 1 }
```

> Lưu ý: endpoint này dùng tên field **`quanlity`** (giữ nguyên lỗi gõ cũ của entity) — khác với
> `POST /api/management/inventory/{ingredientId}/adjust` nhận `quantity`.

### C-17.3 Chặn sửa đơn vị nguyên liệu

`PUT /api/management/ingredients/{id}` — nếu `unit` **khác** giá trị đang lưu (so **không phân biệt
hoa/thường**) và nguyên liệu đã có **công thức** / **ledger kho** / **phiếu mua** ⇒ **`409`**.

```json
{ "message": "Cannot change unit from 'kg' to 'g' because this ingredient is already used in recipes, stock history. Create a new ingredient instead." }
```

| Tình huống | Kết quả |
|---|---|
| Nguyên liệu **chưa** dùng ở đâu | Đổi đơn vị **được** (chữa typo lúc mới tạo) |
| Nguyên liệu đã có công thức / ledger / phiếu mua | **`409`** — message nêu rõ đã dùng ở đâu |
| Gửi lại **đúng** đơn vị đang lưu | **`204`** (kể cả khác hoa/thường ⇒ coi như không đổi) |
| Gửi đơn vị khác **hoa/thường** | Giữ nguyên chuỗi đã lưu (không ghi đè) — ledger và nguyên liệu luôn khớp |

> **Vì sao chặn:** cùng một con số lượng nhưng khác đơn vị là khác hàng nghĩa. Đổi `kg → g` sau khi
> đã dùng sẽ làm **âm thầm sai** mọi công thức, ledger và báo cáo cũ. Không cần bảng quy đổi đơn vị:
> muốn đổi đơn vị thì tạo nguyên liệu mới.

### C-17.4 Endpoint không đổi

> **Không** đổi route, **không** đổi tên field JSON của request.
> `unit` trong `IngredientRequest` **vẫn bắt buộc** như trước — chỉ thêm quy tắc khi nào được đổi.

---

## C-18 — Target khuyến mãi (2026-10-21)

> Build **0 warning / 0 error** · test **633/633 pass** · **không** có migration
> (2 bảng `promotion_foods`/`promotion_categories` đã có sẵn từ trước).

### C-18.1 Vì sao cần

`OrderService.ResolveDiscountBaseAsync` đã đọc `promotion_foods` + `promotion_categories` để
giới hạn phạm vi giảm giá — nhưng **không API nào ghi** vào 2 bảng đó ⇒ chúng **luôn rỗng**
⇒ **mọi khuyến mãi âm thầm giảm trên toàn bộ hoá đơn**. Nay đã có đường ghi ⇒ cơ chế targeting
thực sự chạy được.

### C-18.2 Endpoint mới (1)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| PUT | `/api/management/promotions/{id:int}/targets` | `PromotionTargetsRequest`: `sizeIds` (int[]), `categoryIds` (int[]) | `204` | `404` `{message}` (khuyến mãi không tồn tại, hoặc size/danh mục không tồn tại) |

**Semantics: thay toàn bộ** (idempotent) — gửi danh sách mới là danh sách sau khi sửa.

| Body | Nghĩa |
|---|---|
| `{ "sizeIds": [1,2], "categoryIds": [3] }` | khuyến mãi chỉ áp cho size 1, 2 và các món thuộc danh mục 3 |
| `{ "sizeIds": [], "categoryIds": [] }` | **bỏ giới hạn** ⇒ khuyến mãi áp cho toàn bộ hoá đơn (hành vi mặc định) |

> Gửi trùng id trong mảng ⇒ tự bỏ trùng, không lỗi. `sizeIds`/`categoryIds` thiếu ⇒ coi như rỗng.

### C-18.3 Response thêm field (2)

`PromotionResponse` += `sizeIds` (int[]), `categoryIds` (int[]) — có trong **cả**
`GET /api/management/promotions` và `GET /api/management/promotions/{id}` (đọc 1 lần cho cả danh sách, không N+1).

### C-18.4 Ảnh hưởng tới đơn đang mở

Thao tác này **không** tính lại các đơn đang mở đã áp khuyến mãi — số tiền giảm đã ghi trên
đơn giữ nguyên. Đơn sửa món sẽ tính lại theo target mới.

---

## C-19 — Mã giao dịch + phiên bản công thức (2026-10-21)

> Build **0 warning / 0 error** · test **643/643 pass** ·
> migration `20261021064129_AddPaymentTransactionCodeUnique` (**đã apply trên DB thật**).

### C-19.1 Mã giao dịch phải luôn có và luôn duy nhất

Trước đây `Payment.TransactionCode` lấy thẳng giá trị client gửi, thiếu thì để `""` ⇒ nhiều
dòng trùng nhau, mà index thì **không** unique ⇒ không phát hiện được thu trùng.

| Tình huống | Nay |
|---|---|
| Client **không** gửi `transactionCode` | Server **sinh mã nội bộ**: `PAY{yyyyMMddHHmmss}-{8 hex}` (26 ký tự) |
| Client gửi mã | Giữ nguyên (bỏ khoảng trắng thừa) |
| Client gửi mã **đã tồn tại** | **`409`** `{ "message": "Transaction code '...' already exists" }` |
| Intent MoMo (`POST /api/payments/momo`) | Cũng sinh mã riêng (mã của MoMo nằm ở `providerTransactionId`, ghi khi IPN về) |

**Schema:** index `IX_payments_TransactionCode` giờ là **unique** với filter
`WHERE TransactionCode <> ''`. Migration **backfill** mã cho dòng cũ đang rỗng và ghi hậu tố
riêng cho mã đang trùng, trước khi tạo index.

### C-19.2 Sửa công thức = tạo phiên bản mới (không ghi đè bản cũ)

`PUT /api/management/recipes/{id}` — **route không đổi**, nhưng hành vi đổi:

| | Trước | Nay |
|---|---|---|
| Bản cũ | **bị ghi đè** ⇒ mất định lượng cũ | chuyển sang `IsActive = false`, **giữ nguyên** định lượng (minh chứng lịch sử) |
| Bản mới | — | tạo mới, `IsActive = true`; unique index `(SizeId) WHERE IsActive = 1` bảo đảm chỉ 1 bản đang chạy |
| `recipeId` | không đổi | **đổi** — bản mới có `recipeId` riêng |
| Version | lấy đúng giá trị client gửi | client gửi **rỗng** hoặc **đúng bằng** version đang chạy ⇒ server **tự tăng** (`1` → `2`); khác thì giữ nguyên |
| Sửa lại bản đã bị thay thế | sửa tiếp bản cũ | **`409`** `{ "message": "This recipe version is no longer active; ..." }` |

> **FE:** sau khi sửa công thức phải **gọi lại** `GET /api/management/recipes` (id đã đổi).
> Form FE hiện gọi lại danh sách sau khi lưu ⇒ **không cần sửa gì**.
> Lưu ý: đơn đã tạo trước đó dùng công thức cũ; sửa công thức **không** đổi lịch sử đơn cũ
> (định lượng đã trừ kho rồi), chỉ ảnh hưởng các đơn tạo sau này.

---

## Đợt 3 — "Đủ vận hành" (2026-10-21)

> Build **0 warning / 0 error** · test **670/670 pass** ·
> migration `20261021070215_AddOrderBillNote` (**đã apply trên DB thật**).

### Đợt 3.1 — B2: hoá đơn gộp theo bàn

| Verb | Route | Response | Lỗi |
|---|---|---|---|
| GET | `/api/orders/table/{tableId:int}/bill` | `TableBillResponse` | `404` (bàn không có đơn đang mở, hoặc bàn không tồn tại) |

```json
{ "tableId": 5, "tableNumber": "05", "orderIds": [11, 12], "orderCount": 2,
  "subtotal": 250000, "discountAmount": 0, "taxAmount": 22727,
  "serviceChargeAmount": 12500, "totalAmount": 262500,
  "items": [ { "sizeId": 3, "foodName": "Phở", "sizeName": "L", "quantity": 2,
               "unitPrice": 50000, "lineTotal": 100000, "note": "bỏ hành" } ],
  "notes": [ "Xuất hoá đơn công ty ABC" ],
  "orders": [ { "orderId": 11, "status": "Ready", "subtotal": 100000,
                "discountAmount": 0, "totalAmount": 100000,
                "promotionId": null, "note": null, "createdAt": "..." } ] }
```

**Luật:**
- Gộp **mọi đơn đang mở** của bàn (không `Paid`, không `Cancelled`).
- Tổng tiền = **tổng của từng đơn** (mỗi đơn đã chốt tiền riêng theo khuyến mãi riêng) ⇒ không bao giờ lệch với số tiền đã ghi trên từng đơn.
- Dòng món đã huỷ **không** lên hoá đơn.
- Dòng món được gộp khi **cùng món + cùng đơn giá + cùng ghi chú**; khác ghi chú thì tách dòng.
- **Phạm vi:** endpoint này chỉ **đọc/gộp để hiển thị**. Thanh toán vẫn làm **từng đơn**
  (`POST /api/payments` nhận `orderId`); FE dùng `orderIds` để thanh toán lần lượt.

> **Lưu ý luật nghiệp vụ:** hiện tại `POST /api/orders` **chặn** tạo đơn thứ 2 trên cùng bàn
> ("Table already has an open order") ⇒ bàn thường chỉ có **1** đơn đang mở. Endpoint vẫn gộp
> đúng nhiều đơn nếu có (dữ liệu cũ, hoặc sau này đổi luật cho nhiều "round" mỗi là 1 đơn).

### Đợt 3.2 — B3: ghi chú hoá đơn

| | Nội dung |
|---|---|
| Schema | `orders.Note` (nvarchar(500), **nullable**) — ghi chú cho **cả hoá đơn** (khác `OrderItem.Note` là ghi chú cho từng món) |
| Endpoint mới | `PATCH /api/orders/{id:int}/note` — body `{ "note": "...", "changedBy": 0 }`; `note` rỗng ⇒ **xoá** ghi chú |
| Request mở rộng | `POST /api/orders` nhận thêm `note` (tuỳ chọn) |
| Response thêm field | `OrderDetailResponse` / `OrderListItemResponse` / `OrderCreatedResponse` += `note` |
| Audit | Mỗi lần sửa ghi 1 dòng `order_status_histories` ("Updated bill note: …" / "Cleared bill note") |
| Lỗi | `404` (đơn không tồn tại), `400` (`changedBy` không trỏ tới user nào tồn tại) |

`changedBy` = 0 ⇒ lấy actor từ token; nếu không có token ⇒ lùi về **người tạo đơn**.

### Đợt 3.3 — B4: danh sách tồn kho thấp

| Verb | Route | Response |
|---|---|---|
| GET | `/api/management/inventory/low-stock` (WarehouseDesk) | `LowStockItemResponse[]` |

```json
[ { "ingredientId": 3, "ingredientName": "Cà phê", "unit": "kg",
    "quanlity": 0.2, "minimumStock": 1, "shortage": 0.8, "isOutOfStock": false } ]
```

**Luật:**
- Chỉ gồm nguyên liệu **đang hoạt động** và có `tồn ≤ mức tối thiểu**.
- `shortage` = `minimumStock − quanlity` (0 nếu đã đủ); `isOutOfStock` = tồn ≤ 0.
- Sắp xếp theo `shortage` **giảm dần** (gấp nhất trước), rồi theo tên.

> Trước Đợt 3, dashboard chỉ có `lowStockCount` (một con số) và `GET /management/inventory` không
> có `minimumStock` ⇒ FE phải tự ghép 2 danh sách để biết cái nào sắp hết.

### Đợt 3.4 — B6: bảng bếp

`GET /api/management/kitchen/orders` (KitchenDesk) — **thêm query param**, response **vẫn là mảng** (không đổi shape ⇒ không phá FE).

| Query | Ý nghĩa |
|---|---|
| `status=open` | chỉ phiếu đang mở (đã có từ C-13) |
| `since` (ISO) | chỉ trả phiếu **có thay đổi** kể từ mốc: gửi mới / bắt đầu nấu / hoàn thành |
| `page`, `pageSize` | phân trang **tuỳ chọn**; không truyền ⇒ trả toàn bộ (hành vi cũ) |

**Mẫu dùng đúng (bếp poll 3–5s):**
1. **Lần đầu**: gọi **không** kèm `since` (lấy hết phiếu).
2. **Các lần sau**: gọi kèm `since` = mốc thời gian của lần trước ⇒ chỉ nhận phần thay đổi.

> `since` lọc theo mốc ⇒ nếu bếp mở app lần đầu mà có kèm `since` thì các phiếu cũ chưa xử lý
> sẽ **không** hiện. Vì vậy lần đầu **phải** gọi không kèm `since`.

`page`/`pageSize` sai (vd `pageSize=0`) ⇒ `400` (dùng chung `Paging.ResolveSkip`).

---

## C-20 — Tăng số lượng món: tách dòng `Pending` mới (2026-10-21)

> Luật: `docs/architecture/kitchen-stock-rule-2026-10-21.md` §2.4.1 (chủ dự án chốt).
> Build **0 warning / 0 error** · test **673/673 pass** · E2E **100/100** · **không** đổi schema.

### C-20.1 Hành vi mới của `PATCH /api/orders/{orderId}/items/{itemId}/quantity` (khi TĂNG SL)

| Dòng món đang ở | Trước | Nay |
|---|---|---|
| Còn dòng `Pending` (bếp chưa bắt đầu) | gộp vào dòng Pending | **gộp vào dòng Pending** (không đổi) |
| Đã vào nấu / đã ra đĩa | **cộng thẳng vào dòng `Preparing`** (bếp thấy "5 phần" dù mới có 2) | **TÁCH dòng `Pending` MỚI**, nằm **ngay dưới** dòng cũ, **cùng phiếu bếp** |

**Ví dụ (bếp đang nấu 2 phần gà rán, POS tăng thêm 3):**

```json
// GET /api/management/kitchen/orders/{id}/items  (trước)
[ { "id": 25, "orderItemId": 27, "quanlity": 2, "quantity": 2, "status": "Preparing" } ]

// GET /api/management/kitchen/orders/{id}/items  (nay)
[ { "id": 25, "orderItemId": 27, "quanlity": 2, "quantity": 2, "status": "Preparing" },
  { "id": 26, "orderItemId": 28, "quanlity": 3, "quantity": 3, "status": "Pending"  } ]
```

- Dòng mới có **`OrderItemId` riêng** (dòng `OrderItem` mới, cùng `OrderItemGroupId`) ⇒
  `GET /api/orders/{id}` cũng trả **2 dòng**: `2 [Preparing]` rồi `3 [Pending]`.
- `GET /api/management/kitchen/orders/{id}/items` **có `orderby ki.Id`** ⇒ dòng mới **luôn** nằm dưới.
- Phiếu bếp **không đổi** (vẫn 1 phiếu) nếu phiếu còn mở; nếu phiếu đã `Ready`/đóng ⇒ tạo **phiếu mới** (như cũ).
- Trạng thái phiếu **không bị hạ**: `Aggregate` thấy còn món `Preparing` ⇒ phiếu vẫn `Preparing`.
- Tổng tiền / kho **không đổi** (`Subtotal` = Σ `unitPrice × SL`; ledger ghi thêm dòng `Sale` cho phần mới).

### C-20.2 Lợi ích nghiệp vụ (lý do tách dòng thay vì gộp)

| | Gộp chung vào dòng `Preparing` (cũ) | Tách dòng `Pending` mới (nay) |
|---|---|---|
| Huỷ/giảm sau đó | **cả** 5 phần đều `Preparing` ⇒ ghi `Wastage` ⇒ **mất 5 phần nguyên liệu** | 3 phần mới còn `Pending` ⇒ **hoàn kho** (`AdjustmentIn`) · 2 phần đang nấu ⇒ `Wastage` |
| Ledger | gộp 1 dòng | **ghi theo từng dòng**, `OrderItemId` riêng ⇒ tra cứu được |

Ledger thực tế sau khi giảm 5 → 1:

```
Sale         | 0.002 | orderItemId=27 (dòng gốc, 2 phần)  "trừ kho lúc gọi món"
Sale         | 0.003 | orderItemId=28 (dòng mới, 3 phần)  "trừ kho lúc gọi món"
AdjustmentIn | 0.003 | orderItemId=28 (dòng mới)           "Returned stock when order was cancelled"  ← HOÀN KHO
Wastage      | 0.001 | orderItemId=27 (dòng gốc)          "Wastage (KitchenError)"                 ← HAO HỤT
```

### C-20.3 Ghi chú cho FE

- Màn bếp: vẽ theo thứ tự API trả về (đã sắp xếp `OrderBy(Id)`) ⇒ dòng `Pending` mới **tự nằm dưới**.
- Màn POS (chi tiết đơn): `GET /api/orders/{id}` trả **2 dòng** cho 1 món khi đã tách — FE nên gộp hiển thị
  theo `orderItemGroupId` nếu muốn 1 dòng tổng, hoặc hiển thị từng dòng như bếp.
- **POS vẫn được TĂNG số lượng món đang nấu** (luật "khoá" của C-13 chỉ khoá **huỷ** + **giảm**).

### C-20.4 Test

`Phase2LifecycleTests.cs` — 3 test mới:
`IncreaseQuantity_WhilePreparing_SplitsIntoNewPendingLineInSameTicket` ·
`IncreaseQuantity_WhilePending_MergesIntoExistingPendingLine` ·
`IncreaseQuantity_WhilePreparing_NewPendingLineListedLastInKitchenTicket`

> **Không** đổi route · **không** đổi tên field JSON · **không** đổi schema.

---

## C-21 — Sửa 6 lỗi làm **báo cáo sai** (2026-10-21)

> Đợt sửa **trước** khi làm thêm báo cáo vận hành (B5) — báo cáo trên nền dữ liệu sai thì ra báo cáo sai.
> Build **0 warning / 0 error** · test **690/690 pass** · E2E **100/100** · **không** đổi schema.

| # | Lỗi | Trước | Nay |
|---|---|---|---|
| 1 | `ReasonCode` mất ở cấp **đơn** | huỷ 1 món / bếp huỷ món cuối ⇒ dòng lịch sử **đơn** có `ReasonCode = null` ⇒ báo cáo "đơn huỷ vì hết hàng" **bỏ sót** | `reasonCode` truyền xuống `ApplyOrderStatusAsync` ⇒ dòng lị sử cấp đơn **có** mã lý do |
| 2 | Ledger `Before/After` sai khi tách dòng | dòng gốc đổi `Quanlity` nhưng giữ `Before/After`; dòng tách copy nguyên `Before/After` ⇒ `"Before − After" ≠ Quanlity` ở **cả hai** dòng ⇒ dựng lại tồn kho từ ledger ra số **sai** | dòng gốc: `After = Before − phần còn lại`; dòng tách nối tiếp (`Before = After` dòng gốc, `After = After` gốc) ⇒ **chuỗi liên tục**, tổng tiêu hao không đổi |
| 3 | Ledger không có nguyên nhân | điều chỉnh tay: `ReferenceType/ReferenceId/Note` đều rỗng; nhập phiếu mua: **không** có `Note` ⇒ không trả lời được "tồn giảm 3kg vì lý do gì" | điều chỉnh tay: `ReferenceType = "ManualAdjustment"` + **bắt buộc** có note; nhập phiếu mua: `Note = "Nhập phiếu mua #<id> từ <NCC>"` |
| 4 | Tồn kho thấp không thấy nguyên liệu hư | chỉ lọc `tồn ≤ mức tối thiểu` ⇒ nguyên liệu bếp đánh dấu `Spoiled` mà còn tồn nhiều ⇒ **không hiện** ở đâu cả | thêm field `stockStatus`; **gồm cả** nguyên liệu `OutOfStock`/`Spoiled` dù tồn còn nhiều |
| 5 | `?since=` của bảng bếp bỏ sót đổi trạng thái **món** | chỉ lọc theo 3 mốc của phiếu (Sent/Started/Completed), mỗi mốc chỉ set 1 lần ⇒ món A `Ready` khi món B còn `Preparing` ⇒ phiếu không lọt qua `since` ⇒ **bếp không thấy món vừa xong** | lọc **cả** lịch sử trạng thái món (`order_item_status_histories`) |
| 6 | CSV không chặn formula injection | ô bắt đầu `=`/`+`/`-`/`@` bị Excel hiểu là **công thức** khi mở file | tiền tên dấu nháy `'`; **số âm** (`-5`) là dữ liệu số hợp lệ ⇒ không bị chặn |

### C-21.1 Hợp đồng API đổi

| Route | Thay đổi |
|---|---|
| `POST /api/management/inventory/{ingredientId}/adjust` | **`note` BẮT BUỘC** (lý do điều chỉnh). Thiếu/rỗng ⇒ **`400`** `{message}` |
| `GET /api/management/inventory/low-stock` | Response **+ `stockStatus`** (`Available`/`OutOfStock`/`Spoiled`). Danh sách **gồm cả** nguyên liệu `OutOfStock`/`Spoiled` dù tồn còn nhiều (trước chỉ có nguyên liệu tồn ≤ mức tối thiểu) |
| `GET /api/management/inventory-transactions` | `referenceType` có thêm giá trị **`ManualAdjustment`** (điều chỉnh tay). `referenceId` vẫn **null** cho nhóm này ⇒ lọc theo `referenceType` = `ManualAdjustment` |
| `GET /api/management/kitchen/orders?since=` | Trả về **nhiều hơn** so với trước: thêm phiếu chỉ có thay đổi ở cấp **món** (mốc phiếu không đổi) |

> **Lưu ý FE:** `low-stock` giờ có thể trả về nguyên liệu **không hề sắp hết** (vì bị đánh dấu hư/hết).
> FE nên lọc hiển thị theo `stockStatus` thay vì coi mọi dòng là "sắp hết".

### C-21.2 Test

`Tests/C21ReportIntegrityTests.cs` — 17 test mới, mỗi lỗi 1 test (C-21.1 có 2 test: huỷ từ POS + huỷ từ bếp).