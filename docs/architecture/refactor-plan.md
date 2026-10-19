# Kế hoạch refactor backend — trạng thái & đầu việc tiếp theo

Cập nhật: 2026-10-19 · Nhánh: **`hai`** · Commit cuối: **`62f5d44`** (local, chưa push)

Tài liệu liên quan:

- `docs/architecture/endpoint-baseline.md` — hợp đồng API hiện tại (route/verb/status/body lỗi) + 15 hành vi phải giữ.
- `docs/architecture/data-access.md` — quy tắc truy cập dữ liệu sau GĐ5 (bảng ánh xạ tracking).
- `docs/architecture/backend-frontend-audit.md` — audit đối chiếu backend ↔ frontend (7 lời gọi thiếu, field thiếu, 32 defect, dead code).

## 1. Lưới an toàn (gate cho MỌI giai đoạn)

```powershell
dotnet build RestaurantFoodManagerSystem.sln --no-incremental   # hiện: 14 warning, 0 error
dotnet test  RestaurantFoodManagerSystem.sln                    # hiện: 206/206 pass
dotnet ef migrations has-pending-model-changes --project RestaurantFoodManagerSystem\RestaurantFoodManagerSystem.csproj
```

- Test dùng **SQLite in-memory** + `EnsureCreated` ⇒ không chạm SQL Server, không chạm `Migrations/`.
- Warning baseline: **14** (toàn bộ CS8618 trong entity/DTO). Không được tăng.
- Test baseline: **206/206** (105 gốc + 7 GĐ6a + 33 GĐ6b + 11 GĐ6c + 20 GĐ6-A + 2 GĐ7-C + 7 GĐ7-B + 14 Nhóm B schema + 5 GĐ8 initializer + 2 GĐ8 host). Nếu giảm số lượng test thì phải nói rõ lý do.

**Quy tắc bất di bất dịch:** không đụng `frontend/`, `wwwroot/`, không sửa/xoá migration, không đổi schema khi chưa được duyệt, không đổi route/JSON mà không duyệt.

## 2. Đã làm

| GĐ | Nội dung | Commit | Đổi hành vi? |
|---|---|---|---|
| GĐ0 | Bộ test baseline 105 test (SQLite in-memory) + tài liệu endpoint | `123521c` | Không |
| GĐ1 | Dời `AuthService` → `Application/Features/Auth`; sửa mismatch class `UsersController`; xoá dead code (`RoleEnum`, `RoleDto`, 4 method `UserService` chưa dùng); xoá thư mục rỗng | `85915db` | Không |
| GĐ2 | `AppDbContext` từ `Domain/` → `Infrastructure/Persistence/`, đổi namespace, cập nhật 13 `using` | `ecf9870` | Không |
| GĐ3 | Dọn cây `Controllers/`: `FoodController`→`Food`, `UserController`→`Users`, `RestaurantTableController`→`RestaurantTables`; đổi tên file cho khớp class | `94a6ae1` | Không |
| GĐ4 | Chia logic `ManagementService` về 6 service theo feature (mỗi service nhận `AppDbContext`); `ManagementService` giữ lại làm facade mỏng; giữ cặp `Kitchen/UpdateKitchen` trùng | `e88011c` | Không |
| GĐ5 | Bỏ `IRepository`/`IUnitOfWork`/`Repository<T>`/`UnitOfWork`; 9 service dùng `AppDbContext` trực tiếp, giữ nguyên semantics tracking | `77e5a78` | Không |
| GĐ6a | Thêm package `JwtBearer` 8.0.32 + `JwtOptions`/`TokenService`; `AuthService`/`UserRoleService` nạp tên role của user; `LoginResponse` thêm `Roles`+`Token`; login sinh token (trả `null` nếu chưa cấu hình khóa); +7 test | `e7d7ab6` | Không (chỉ thêm field) |
| GĐ6b | `AddAuthentication`/`AddAuthorization` + 7 policy + `app.UseAuthentication()`; gắn `[Authorize]` theo ma trận mục 4.4; công tắc `Auth:Enforce` (dev mặc định `false`) qua `EnforceableAuthorizationPolicyProvider`; +33 test | `038007c` | Có, **chỉ khi** `Auth:Enforce=true` |
| GĐ6c | Actor (`CreatedBy`/`ChangedBy`/`ReceivedBy`) lấy từ claim `sub` khi có token, fallback body khi chưa xác thực (`ICurrentUser` + `CurrentUserExtensions`); +11 test | `d20d788` | Không (giữ field body, chỉ đổi nguồn khi có token) |
| GĐ6-A | Bù **6 endpoint** frontend cần + bật CORS: `GET /setup/status`, `POST /setup/first-admin`, `GET/POST /users`, `PUT /management/recipes/{id}`, `PATCH /foods/{id}/active`; `app.UseCors("FrontendPolicy")` (thêm origin 5500/5501/3000); +20 test | `afa4664` | **Có** (endpoint mới + CORS thật sự hoạt động) |
| GĐ7-C | Bù **field hiển thị thiếu** frontend đọc (chỉ **bổ sung**, không xoá field): `GET /orders/{id}` thêm `items[]` (foodName/sizeName/quantity/unitPrice/note/status); `GET /management/kitchen/orders/{id}/items` thêm `foodName`/`sizeName`/`quantity` (giữ `quanlity` cũ); `GET /management/recipes` thêm `ingredientCount`; `GET /management/recipes/{id}` thêm `foodName`/`sizeName`; +2 test | `46a18e9` | Có (thêm field JSON; **không** đổi route/schema) |
| GĐ7-B | Sửa lỗi nghiệp vụ **không đổi schema**: B1 ánh xạ trạng thái bếp (`Cancelled`→`Cancelled`, không còn mở lại đơn); B2 allow-list + chặn sửa trạng thái cuối cho order status (controller trả 400/409); B4 transaction cho `PaymentService`/`IngredientService.CreateAsync`/`PurchaseOrderService.CreateAsync`; B8 chặn `Items=null`, `.Trim()` null-safe, `DateTime.Now`→`UtcNow`; chặn thanh toán đơn `Cancelled`; thêm hằng `OrderStatuses`; +7 test (2 test baseline được cập nhật chủ ý) | `29c01d1` | **Có** (thêm validate + transaction) |
| Nhóm B schema | Hoàn tất **B3/B5/B6/B7** (đổi schema, đã duyệt): unique index lọc `payments.OrderId` (`Status='Paid'`) + chặn `TotalAmount<=0`; unique index lọc `recipes.SizeId` (`IsActive=1`) + chặn trùng công thức active; `HasFilter(IsActive=1)` cho `categories.CategoryName`, `food_sizes(FoodId,SizeName)`, `toppings.ToppingName`, `ingredients.Name`, `restaurant_tables.TableNumber` (**users giữ unique toàn cục**); `RowVersion` (`rowversion`) cho `orders`/`inventory`/`restaurant_tables` + map xung đột ghi → 409; migration `20261019090127_NhomB_DataIntegrity` đã áp xuống DB thật; +14 test | `62f5d44` | **Có** (schema + 409 mới) |
| GĐ8 | Tách migrate + seed khỏi `Program.cs` → `Infrastructure/Persistence/DatabaseInitializer.cs`; thêm `DatabaseInitializationOptions` (`MigrateOnStartup`/`SeedOnStartup`, mặc định `true`); `public partial class Program` cho `WebApplicationFactory`; thêm package test `Microsoft.AspNetCore.Mvc.Testing` 8.0.32 + factory SQLite in-memory; +7 test (5 initializer + 2 host) | `1f93733` | Không (mặc định giữ nguyên migrate + seed lúc khởi động) |

Kết quả: cả backend chỉ còn **một** phong cách data-access; `Domain` không còn phụ thuộc EF Core.

## 3. Hành vi "lạ" đang được cố ý giữ (đừng vô tình sửa)

Chi tiết đầy đủ ở `endpoint-baseline.md` mục 2. Nổi bật:

1. Enforcement phân quyền **đang tắt** ở dev: GĐ6a sinh token, GĐ6b gắn `[Authorize]` + policy nhưng `Auth:Enforce=false` mặc định ⇒ API vẫn anonymous; bật `true` mới trả 401/403 (có duyệt).
2. Actor (`CreatedBy`/`ChangedBy`/`ReceivedBy`): **GĐ6c bước 1 đã làm** — nếu có token thì luôn lấy claim `sub` (bỏ qua body, log cảnh báo nếu lệch); nếu chưa xác thực (dev, `Auth:Enforce=false`) thì vẫn dùng body. Bước 2 (xoá field khỏi DTO) để sau.
3. **CORS**: policy `FrontendPolicy` nay **đã được áp** vào pipeline (`app.UseCors` ở GĐ6-A `afa4664`), origin `http://localhost:5500` / `5501` / `3000`. Trước GĐ6-A policy này đăng ký nhưng vô hiệu.
4. `UserService`/`AuthService` dùng `DateTime.Now`; service khác dùng `DateTime.UtcNow`. → **ĐÃ SỬA ở `29c01d1`** (B8: tất cả dùng `UtcNow`).
5. Hai kiểu body lỗi: `{message}` (controller cũ) và JSON string trần (`Orders`/`Payments`).
6. Hai kiểu inject: constructor (8 controller) và `[FromServices]` (4 controller).
7. Trả entity EF trực tiếp ở payments/`/api/management/*` (orders/recipes/kitchen items đã DTO hoá — xem `endpoint-baseline.md` quirk #7).
8. `PATCH /api/orders/{id}/status` **trước đây không validate** chuỗi trạng thái. → **ĐÃ SỬA ở `29c01d1`** (B2: allow-list + chặn trạng thái cuối; controller trả 400/409).
9. Kitchen status: `KitchenService` có allow-list, `ManagementService.UpdateKitchen` thì không.
10. `GET /api/restaurant-tables/{id}` **có** ghi DB; `GET /api/restaurant-tables` **không** ghi (do tracking khác nhau — xem `data-access.md`).
11. Soft delete + unique index: **ĐÃ SỬA ở `62f5d44`** (B6) — `toppings.ToppingName` (và category/size/ingredient/table) nay có filter `IsActive=1` ⇒ tạo lại được cùng tên sau soft delete. **Ngoại lệ:** `users.UserName`/`UserEmail` vẫn unique toàn cục.
12. `Category.DisplayOrder` là `bool` (đổi sang `int` = **đổi schema**, cần duyệt).
13. `GET /api/menu` trả anonymous type.
14. App tự `Migrate()` + seed 5 role khi khởi động.
15. `RoleEnum` (dead) đã xoá ở GĐ1.
16. **Đồng thời (B7, `62f5d44`)**: `orders`/`inventory`/`restaurant_tables` có `RowVersion`; request ghi bằng dữ liệu cũ trả **409** thay vì ghi đè im lặng. Refresh trạng thái bàn (side-effect sau transaction chính) thì **retry** chứ không trả 409.
17. **Unique có lọc (B3/B5, `62f5d44`)**: `payments.OrderId` unique khi `Status='Paid'` (nhiều payment `Failed` vẫn được); `recipes.SizeId` unique khi `IsActive=1`. Đơn `TotalAmount<=0` bị chặn thanh toán.

## 4. GĐ6 — Authentication & Authorization (ĐÃ CHỐT THIẾT KẾ, CHƯA CODE)

### 4.1 Quyết định đã xác nhận

| # | Vấn đề | Quyết định |
|---|---|---|
| 1 | Package `Microsoft.AspNetCore.Authentication.JwtBearer` | **Đồng ý thêm** |
| 2 | `POST /api/auth/register` | **AdminOnly** |
| 3 | Sửa thực đơn (food/category/size/topping/food-topping) | **WarehouseManager + Admin** |
| 4 | Lifetime token | **8 giờ**, chưa làm refresh token |

### 4.2 Token

- HS256, key ở **ngoài repo** (`Authentication:Jwt:Key` qua env/user-secrets). Không commit key.
- Claims: `sub` = `Users.Id` (dùng làm actor), `unique_name`, `name`, `role` (nhiều claim), `jti`, `iat`, `exp`.
- Login response **thêm field** (không phá cái cũ):
  `data = { userId, userName, email, fullName, roles: [...], token }`

### 4.3 Policy

| Policy | Role |
|---|---|
| `Authenticated` | tất cả role hợp lệ |
| `AdminOnly` | Admin |
| `PosOperator` | Admin, Waiter |
| `CashierDesk` | Admin, Cashier |
| `KitchenDesk` | Admin, Chef |
| `WarehouseDesk` | Admin, WarehouseManager |

### 4.4 Ma trận phân quyền (đã cập nhật theo quyết định #3)

| Nhóm endpoint | Policy |
|---|---|
| `POST /api/auth/login` | *public* |
| `POST /api/auth/register` | `AdminOnly` |
| `POST /api/users/{userId}/roles` | `AdminOnly` |
| `GET` foods / categories / toppings / menu | `Authenticated` |
| `POST/PUT/PATCH/DELETE` foods, categories, sizes, toppings, food-toppings | **`WarehouseDesk`** (Admin + WarehouseManager) |
| `GET /api/restaurant-tables*` | `PosOperator` hoặc `CashierDesk` |
| `POST/PUT/DELETE /api/restaurant-tables*`, `PATCH .../maintenance` | `AdminOnly` |
| `GET /api/orders/{id}` | `PosOperator` hoặc `CashierDesk` |
| `POST /api/orders`, `PATCH /api/orders/{id}/status`, `PATCH .../promotion/{id}` | `PosOperator` |
| `POST /api/payments`, `GET /api/payments/order/{id}` | `CashierDesk` |
| `GET /api/management/kitchen/orders*` | `KitchenDesk` |
| `PATCH /api/management/kitchen/orders/{id}/status`, `.../items/{id}/status` | `KitchenDesk` |
| ingredients / suppliers / inventory / purchase-orders (mọi verb) | `WarehouseDesk` |
| recipes (mọi verb) | `WarehouseDesk` |
| promotions (mọi verb) | `AdminOnly` |

### 4.5 Actor lấy từ token (2 bước)

Map: `CreateOrderRequest.CreatedBy`, `OrderStatusHistory.ChangedBy`, `CreatePaymentRequest.CreatedBy`, `PurchaseOrderRequest.CreatedBy`, `ReceivePurchaseRequest.ReceivedBy`, `InventoryAdjustmentRequest.CreatedBy` → claim `sub`.

1. **Bước 1 ✅ (GĐ6c):** giữ field trong DTO nhưng bỏ qua giá trị body, luôn dùng claim (log cảnh báo nếu lệch) ⇒ frontend không vỡ. Thực hiện qua `ICurrentUser` (Application) + `HttpContextCurrentUser` (Infrastructure đọc claim `sub`, fallback `ClaimTypes.NameIdentifier`) và `CurrentUserExtensions.ResolveActorId`; khi chưa có token thì fallback về body.
2. **Bước 2 (sau GĐ6):** xoá field khỏi DTO khi frontend đã bỏ.

### 4.6 Các lát triển khai

| Lát | Nội dung | Phá vỡ? |
|---|---|---|
| 6a ✅ | Thêm package JWT + config; `TokenService`; `AuthService` nạp role của user; `LoginResponse` thêm `roles`+`token` | Không (chỉ thêm field) |
| 6b ✅ | `AddAuthentication`/`AddAuthorization` + policy + `app.UseAuthentication()` + gắn `[Authorize]`, **bật/tắt bằng `Auth:Enforce`** (dev mặc định `false` đến khi frontend xong) | Có, khi bật |
| 6c ✅ | Actor lấy từ claim, bỏ qua field body | Không |
| 6d ⏸ | Siết `register` (AdminOnly), policy mật khẩu tối thiểu, thống nhất error shape, Swagger security schema | Có |
| 6e ⏸ | Test HTTP-level 401/403 — hạ tầng `WebApplicationFactory` đã sẵn từ **GĐ8** | Không |

**GĐ6 tạm chốt ở 6c** theo quyết định của chủ dự án. 6d/6e chuyển sang mục 5 (deferred).

### 4.7 Test cần thêm cho GĐ6

- `TokenService` sinh đúng claim `sub`/`role`.
- Ma trận policy: quét (nhóm endpoint × 5 role) bằng `IAuthorizationService`, khẳng định allow/deny.
- 401 khi thiếu token, 403 khi sai role (ở 6e).
- Actor trong DB đến từ claim chứ không từ body.
- `dotnet ef migrations has-pending-model-changes` phải vẫn "No changes" (GĐ6 **không** đổi schema).

## 5. Chưa làm (deferred, cần duyệt riêng)

> **GĐ6-A (`afa4664`) đã bù xong nhóm "endpoint thiếu" mà frontend gọi** (setup, users, recipe PUT,
> foods/active) và bật CORS. Chi tiết đầy đủ + danh sách defect còn lại: `backend-frontend-audit.md`.

Còn lại (theo thứ tự gợi ý trong `backend-frontend-audit.md` mục 8):

> ✅ **Nhóm B đã hoàn tất**: phần không đổi schema ở `29c01d1` (B1/B2/B4/B8 + chặn thanh toán đơn `Cancelled`)
> và phần đổi schema ở `62f5d44` (B3 unique `payments.OrderId` lọc `Status='Paid'` + chặn `TotalAmount<=0`;
> B5 chặn nhiều recipe active/1 size + unique index lọc `IsActive`; B6 `HasFilter(IsActive=1)` cho unique index
> soft-delete — **trừ users**; B7 `rowversion` cho `Inventory`/`RestaurantTable`/`Order` + 409 khi xung đột ghi).
> Migration `20261019090127_NhomB_DataIntegrity` **đã áp xuống DB thật** `HAI2\SQLEXPRESS / RestaurantFoodManager`.

- **Nhóm C — GĐ7 (đổi hợp đồng JSON)**: ✅ phần **field hiển thị** đã làm ở `46a18e9`
  (`GET /orders/{id}` thêm `items[]`, `KitchenOrderItem` thêm `foodName/sizeName/quantity`,
  `Recipe.ingredientCount` + `foodName/sizeName`) — **chỉ bổ sung**, frontend cũ không vỡ.
  Còn lại: **DTO hoá** response để bỏ trả entity thô (mục tiếp theo).
- GĐ7: DTO hoá response, bỏ trả entity EF ở orders/payments/`/api/management/*` — **đổi hợp đồng JSON**, phải phối hợp frontend.
- GĐ8 ✅: tách seed/`Migrate()` khỏi `Program.cs` để test HTTP-level (`WebApplicationFactory`) — điều kiện cần cho 6e. **Không đổi hành vi** (cờ `Database:MigrateOnStartup`/`SeedOnStartup` mặc định `true`).
- GĐ6d: siết `register` (đã gắn `AdminOnly` ở 6b), policy mật khẩu tối thiểu, thống nhất error shape, Swagger security schema — **đổi hành vi khi enforce**.
- GĐ6e: test HTTP-level 401/403 — hạ tầng `RestaurantApiFactory` (GĐ8) đã cho phép; chỉ còn viết test.
- Sửa quirk GET-all-bàn-không-ghi-DB (đổi hành vi).
- `Category.DisplayOrder` bool → int (đổi schema, cần migration).
- Refresh token / revocation, rate-limit login, audit log, đổi/quên mật khẩu.
- Xoá `ManagementService` + cặp `Kitchen/UpdateKitchen` trùng + field actor khỏi DTO — **chờ frontend mock xong** (quyết định của chủ dự án).
- Hợp nhất 2 kiểu body lỗi và 2 kiểu inject controller — chỉ làm khi có duyệt.

## 6. Cách tiếp tục ở phiên sau

1. `git pull` (nhánh `hai`), rồi chạy mục 1 để xác nhận 14 warning / 206 test.
2. **GĐ6 đã chốt ở 6a + 6b + 6c(bước 1); GĐ6-A (`afa4664`) bù 6 endpoint + bật CORS; GĐ7-C (`46a18e9`) bù field hiển thị; GĐ7-B (`29c01d1`) sửa lỗi nghiệp vụ không đổi schema; Nhóm B schema (`62f5d44`) hoàn tất B3/B5/B6/B7; GĐ8 tách migrate/seed khỏi `Program.cs` (host test `WebApplicationFactory` đã chạy).** Các việc ưu tiên tiếp theo ở mục 5; gợi ý: **GĐ6e** (test 401/403 ở mức HTTP — hạ tầng đã sẵn), rồi **DTO hoá GĐ7** (phần còn lại).
3. Giữ `Auth:Enforce=false` ở dev cho tới khi frontend gửi `Authorization: Bearer`; chỉ bật `true` khi đã cấu hình khóa ký (app sẽ **fail-fast** nếu bật enforce mà thiếu khóa). **Frontend hiện vẫn chưa gửi token** (`frontend/js/common/api.js` không set header `Authorization`) — đây là việc phía frontend, xem `backend-frontend-audit.md` mục 5.
4. Mọi thay đổi đụng route/JSON/schema phải hỏi trước.

Chi tiết 6b đã triển khai:

- 7 policy ở `Infrastructure/Auth/AuthorizationServiceCollectionExtensions.cs` (nguồn chung cho app + test):
  `Authenticated`, `AdminOnly`, `PosOperator`, `CashierDesk`, `KitchenDesk`, `WarehouseDesk`, `PosOrCashier`.
- `EnforceableAuthorizationPolicyProvider` trả policy "cho qua" khi `Auth:Enforce=false`; khi `true` mới uỷ quyền cho provider mặc định ⇒ bật/tắt không cần sửa controller.
- Gắn `[Authorize]` theo ma trận 4.4; `POST /api/auth/login` để `[AllowAnonymous]`.
- `Auth:Enforce` mặc định `false` trong `appsettings.json`.

Chi tiết 6c (bước 1) đã triển khai:

- `Application/Common/ICurrentUser.cs` (abstraction) + `CurrentUserExtensions.ResolveActorId` (chọn claim khi có, fallback body khi chưa).
- `Infrastructure/Auth/HttpContextCurrentUser.cs` đọc claim `sub` (fallback `ClaimTypes.NameIdentifier`).
- 4 service nhận `ICurrentUser` + `ILogger<T>`: `OrderService`, `PaymentService`, `PurchaseOrderService`, `InventoryService`.
- `Program.cs`: `AddHttpContextAccessor()` + `AddScoped<ICurrentUser, HttpContextCurrentUser>()`.
- **Chưa** xoá field actor khỏi DTO (đó là bước 2).

Chi tiết GĐ8 đã triển khai:

- `Infrastructure/Persistence/DatabaseInitializer.cs`: `InitializeAsync(IServiceProvider, options, ct)` tự mở scope
  riêng, migrate nếu bật rồi gọi `SeedRolesAsync` (idempotent, chỉ seed khi `Roles` rỗng); `DefaultRoles` là nguồn
  role dùng chung cho seed + test.
- `DatabaseInitializationOptions` đọc từ section `Database` (`MigrateOnStartup`, `SeedOnStartup`, **mặc định `true`**
  ⇒ production không đổi hành vi).
- `Program.cs` chỉ còn `await DatabaseInitializer.InitializeAsync(...)`; thêm `public partial class Program { }`
  cho `WebApplicationFactory<Program>`.
- Test: `DatabaseInitializerTests` (5) + `ApiHostSmokeTests` (2, dùng `RestaurantApiFactory`: SQLite in-memory,
  `MigrateOnStartup=false` vì migration viết cho SQL Server, `SeedOnStartup=true` để chạy đúng đường khởi động).
  Đây là hạ tầng để làm **GĐ6e** (401/403 mức HTTP) mà không cần SQL Server.
- Đã chạy app thật (`dotnet run --launch-profile http`) trên `HAI2\SQLEXPRESS`: log
  `No migrations were applied. The database is already up to date.`; seed bỏ qua vì bảng `roles` đã có;
  `GET /api/setup/status` → `200 {"requiresSetup":false}`.

Cấu hình khóa ký ở dev (không commit): đặt `Authentication:Jwt:Key` qua biến môi trường
`Authentication__Jwt__Key` hoặc `dotnet user-secrets set "Authentication:Jwt:Key" "<>=32 ký tự>"`.
Khi khóa trống, `TokenService` trả `null` và login **vẫn chạy** (chưa enforce) — token chỉ có giá trị khi đã cấu hình khóa.

**Lưu ý phiên này:** không `git push` (theo yêu cầu chủ dự án); commit chỉ ở máy local.

Ghi chú môi trường: `.opencode/skills/restaurant-backend/SKILL.md` có thay đổi line-ending **từ trước phiên refactor**, cố ý không commit.
