# Kế hoạch refactor backend — trạng thái & đầu việc tiếp theo

Cập nhật: 2026-10-19 · Nhánh: **`hai`** · Commit cuối: **`43b52e7`** (local, chưa push)

Tài liệu liên quan:

- `docs/architecture/endpoint-baseline.md` — hợp đồng API hiện tại (route/verb/status/body lỗi) + 15 hành vi phải giữ.
- `docs/architecture/data-access.md` — quy tắc truy cập dữ liệu sau GĐ5 (bảng ánh xạ tracking).

## 1. Lưới an toàn (gate cho MỌI giai đoạn)

```powershell
dotnet build RestaurantFoodManagerSystem.sln --no-incremental   # hiện: 14 warning, 0 error
dotnet test  RestaurantFoodManagerSystem.sln                    # hiện: 105/105 pass
dotnet ef migrations has-pending-model-changes --project RestaurantFoodManagerSystem\RestaurantFoodManagerSystem.csproj
```

- Test dùng **SQLite in-memory** + `EnsureCreated` ⇒ không chạm SQL Server, không chạm `Migrations/`.
- Warning baseline: **14** (toàn bộ CS8618 trong entity/DTO). Không được tăng.
- Test baseline: **112/112** (105 gốc + 7 test thêm ở GĐ6a). Nếu giảm số lượng test thì phải nói rõ lý do.

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
| GĐ6a | Thêm package `JwtBearer` 8.0.32 + `JwtOptions`/`TokenService`; `AuthService`/`UserRoleService` nạp tên role của user; `LoginResponse` thêm `Roles`+`Token`; login sinh token (trả `null` nếu chưa cấu hình khóa); +7 test | `43b52e7` | Không (chỉ thêm field) |

Kết quả: cả backend chỉ còn **một** phong cách data-access; `Domain` không còn phụ thuộc EF Core.

## 3. Hành vi "lạ" đang được cố ý giữ (đừng vô tình sửa)

Chi tiết đầy đủ ở `endpoint-baseline.md` mục 2. Nổi bật:

1. Không authentication/authorization (sẽ đổi ở GĐ6 — có duyệt).
2. Actor (`CreatedBy`/`ChangedBy`/`ReceivedBy`) lấy từ **body** (sẽ đổi ở GĐ6).
3. CORS policy `FrontendPolicy` đăng ký nhưng **không** `UseCors` ⇒ vô hiệu.
4. `UserService`/`AuthService` dùng `DateTime.Now`; service khác dùng `DateTime.UtcNow`.
5. Hai kiểu body lỗi: `{message}` (controller cũ) và JSON string trần (`Orders`/`Payments`).
6. Hai kiểu inject: constructor (8 controller) và `[FromServices]` (4 controller).
7. Trả entity EF trực tiếp ở orders/payments/`/api/management/*`.
8. `PATCH /api/orders/{id}/status` **không validate** chuỗi trạng thái.
9. Kitchen status: `KitchenService` có allow-list, `ManagementService.UpdateKitchen` thì không.
10. `GET /api/restaurant-tables/{id}` **có** ghi DB; `GET /api/restaurant-tables` **không** ghi (do tracking khác nhau — xem `data-access.md`).
11. Soft delete + unique index `toppings.ToppingName` ⇒ không tạo lại được cùng tên.
12. `Category.DisplayOrder` là `bool` (đổi sang `int` = **đổi schema**, cần duyệt).
13. `GET /api/menu` trả anonymous type.
14. App tự `Migrate()` + seed 5 role khi khởi động.
15. `RoleEnum` (dead) đã xoá ở GĐ1.

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

1. **Bước 1:** giữ field trong DTO nhưng bỏ qua giá trị body, luôn dùng claim (log cảnh báo nếu lệch) ⇒ frontend không vỡ.
2. **Bước 2 (sau GĐ6):** xoá field khỏi DTO khi frontend đã bỏ.

### 4.6 Các lát triển khai

| Lát | Nội dung | Phá vỡ? |
|---|---|---|
| 6a ✅ | Thêm package JWT + config; `TokenService`; `AuthService` nạp role của user; `LoginResponse` thêm `roles`+`token` | Không (chỉ thêm field) |
| 6b | `AddAuthentication`/`AddAuthorization` + policy + `app.UseAuthentication()` + gắn `[Authorize]`, **bật/tắt bằng `Auth:Enforce`** (dev mặc định `false` đến khi frontend xong) | Có, khi bật |
| 6c | Actor lấy từ claim, bỏ qua field body | Không |
| 6d | Siết `register` (AdminOnly), policy mật khẩu tối thiểu, thống nhất error shape, Swagger security schema | Có |
| 6e | Test HTTP-level 401/403 — **phụ thuộc GĐ8** (tách `Migrate()`+seed khỏi `Program.cs`) | Không |

### 4.7 Test cần thêm cho GĐ6

- `TokenService` sinh đúng claim `sub`/`role`.
- Ma trận policy: quét (nhóm endpoint × 5 role) bằng `IAuthorizationService`, khẳng định allow/deny.
- 401 khi thiếu token, 403 khi sai role (ở 6e).
- Actor trong DB đến từ claim chứ không từ body.
- `dotnet ef migrations has-pending-model-changes` phải vẫn "No changes" (GĐ6 **không** đổi schema).

## 5. Chưa làm (deferred, cần duyệt riêng)

- GĐ7: DTO hoá response, bỏ trả entity EF ở orders/payments/`/api/management/*` — **đổi hợp đồng JSON**, phải phối hợp frontend.
- GĐ8: tách seed/`Migrate()` khỏi `Program.cs` để test HTTP-level (`WebApplicationFactory`) — điều kiện cần cho 6e.
- Sửa quirk GET-all-bàn-không-ghi-DB (đổi hành vi).
- `Category.DisplayOrder` bool → int (đổi schema, cần migration).
- Refresh token / revocation, rate-limit login, audit log, đổi/quên mật khẩu.
- Xoá `ManagementService` + cặp `Kitchen/UpdateKitchen` trùng + field actor khỏi DTO — **chờ frontend mock xong** (quyết định của chủ dự án).
- Hợp nhất 2 kiểu body lỗi và 2 kiểu inject controller — chỉ làm khi có duyệt.

## 6. Cách tiếp tục ở phiên sau

1. `git pull` (nhánh `hai`), rồi chạy mục 1 để xác nhận 14 warning / 112 test.
2. **6a đã xong.** Sang **6b** nhưng để `Auth:Enforce=false`; chỉ bật `true` khi frontend đã gửi `Authorization: Bearer`.
3. Mọi thay đổi đụng route/JSON/schema phải hỏi trước.

Cấu hình khóa ký ở dev (không commit): đặt `Authentication:Jwt:Key` qua biến môi trường
`Authentication__Jwt__Key` hoặc `dotnet user-secrets set "Authentication:Jwt:Key" "<>=32 ký tự>"`.
Khi khóa trống, `TokenService` trả `null` và login **vẫn chạy** (chưa enforce) — token chỉ có giá trị khi đã cấu hình khóa.

**Lưu ý phiên này:** không `git push` (theo yêu cầu chủ dự án); commit chỉ ở máy local.

Ghi chú môi trường: `.opencode/skills/restaurant-backend/SKILL.md` có thay đổi line-ending **từ trước phiên refactor**, cố ý không commit.
