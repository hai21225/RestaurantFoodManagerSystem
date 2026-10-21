# HỢP ĐỒNG API — tài liệu dành riêng cho FRONTEND

> **Đây là file FE nên mở đầu tiên.** Tự chứa đủ để code: endpoint, body, status code,
> lỗi và các luật nghiệp vụ mà UI phải tôn trọng.
>
> **Cập nhật:** 2026-10-21 · **Backend:** `RestaurantFoodManagerSystem` — 128 action, 30 bảng, 14 migration
> **Đã kiểm chứng:** 670/670 test tự động + **100/100 case E2E trên API thật** (`scripts\e2e-smoke.ps1`)

---

## 0. TL;DR — backend sẵn sàng chưa?

**CÓ.** 18 nhóm màn hình FE đều có API. Chỉ **2 mục** chưa có:

| Mục chưa có | Endpoint cần | Ảnh hưởng |
|---|---|---|
| **Chia / gộp bill** (FE-19) | `POST /api/orders/{id}/split` | Không làm được màn hình "chia tiền cho 2 khách" |
| **Ca làm việc / két tiền** (FE-20) | `GET/POST /api/management/shifts/*` | Không làm được màn hình "mở ca / đối soát két" |

Ngoài ra, **5 báo cáo vận hành** (`revenue-by-method`, `top-dishes`, `wastage`, `stock-movement`,
`by-staff`) là cho **chủ quán** — không chặn màn hình báo cáo của FE (chỉ cần `revenue` + CSV, đã có).

### 3 điều kiện trước khi bật phân quyền thật

1. **FE phải gửi `Authorization: Bearer <token>`** (task F0-1). Backend đã có JWT + 5 role + policy
   đầy đủ, nhưng `Auth:Enforce` đang là `false` (chế độ dev). Bật `true` chỉ khi FE đã gửi token.
2. **Quirk `GET /api/restaurant-tables/{id}`** — endpoint này **ghi DB** (cập nhật trạng thái bàn),
   còn `GET /api/restaurant-tables` thì không. **Đừng gọi `{id}` chỉ để xem bàn.**
3. **Tiền tệ:** mọi số tiền là `decimal` (JSON number), làm tròn 2 chữ số. **Đơn vị tiền: VND** (số nguyên).

---

## 1. Chạy thử

```powershell
# Backend
dotnet run --project RestaurantFoodManagerSystem
#   → http://localhost:5225/api   ·  Swagger: http://localhost:5225/swagger
#   → tự apply migration + seed role/khu vực lúc khởi động
```

| | |
|---|---|
| Base URL | `http://localhost:5225/api` (FE đọc `localStorage.restaurant.apiBaseUrl`) |
| Lần đầu hệ thống | `GET /api/setup/status` → `requiresSetup: true` ⇒ `POST /api/setup/first-admin` |
| Tài khoản test | `e2e-runner` / `E2E@123456` (đã có sẵn trong DB dev) |
| CORS | đã bật cho FE (`FrontendPolicy`) |

---

## 2. Quy ước chung

### 2.1 Xác thực

| Header | Bắt buộc |
|---|---|
| `Authorization: Bearer <token>` | Sau khi bật `Auth:Enforce=true` (**đang là `false` ⇒ hiện vẫn chạy được không token**) |
| `Content-Type: application/json` | mọi `POST`/`PUT`/`PATCH` có body |
| `Idempotency-Key: <chuỗi tùy ý ≤128 ký tự>` | tuỳ chọn, cho `POST /api/orders`, `POST /api/orders/{id}/items`, `PATCH /api/orders/{id}/items/{itemId}/quantity`, `POST /api/payments` |

> ⚠️ **Ngoại lệ duy nhất: `GET`/`PUT /api/users/me` LUÔN cần token**, kể cả khi `Auth:Enforce=false` —
> vì "tôi" là ai phải biết từ token. Còn lại mọi endpoint chạy được không token ở chế độ dev.
> (Đã kiểm chứng: không token → `401`; có token → `200` + hồ sơ.)

### 2.2 Lỗi — **luôn** là JSON `{ "message": "..." }`

| HTTP | Khi nào | FE làm gì |
|---|---|---|
| `400` | Dữ liệu vào sai (thiếu field, sai kiểu, quá độ dài, `pageSize` ngoài 1..100) | hiện `message` |
| `401` | Chưa đăng nhập / token sai / hết hạn | **xóa token → về trang đăng nhập** |
| `403` | Đã đăng nhập nhưng không đủ vai trò | hiện "bạn không có quyền làm việc này" |
| `404` | Không tìm thấy món/danh mục/bàn/đơn/nguyên liệu/target | hiện "không tìm thấy" |
| `409` | Xung đột nghiệp vụ (đã trả tiền, trùng tên, trùng mã giao dịch, bàn có khách…) | hiện `message` |
| `410` | Route đã bị bỏ (`PUT /api/orders/{id}/items`) | báo "luồng này đã đổi, dùng route mới" |
| `500` | Lỗi hệ thống | hiện "lỗi hệ thống, thử lại" — **đừng** hiện message kỹ thuật |

> Trước đây ~50 lỗi trả **text thuần** ⇒ `payload.message` là `undefined`. Nay **mọi** lỗi đều có message.

### 2.3 Status code thành công — **không đồng nhất, kiểm tra đúng**

| Loại | Code | Ví dụ |
|---|---|---|
| `GET` đọc | `200` | mọi `GET` |
| `POST` tạo mới (catalog) | **`201`** | `POST /api/foods`, `/api/toppings`, `/api/foods/{id}/sizes` |
| `POST`/`PATCH`/`PUT` sửa (các module khác) | `200` | `POST /api/orders`, `POST /api/payments`, `POST /api/management/promotions`… |
| `PATCH`/`PUT` cập nhật trạng thái + 2 lệnh kho | **`204`** (không body) | `PATCH .../kitchen/*/status`, `PATCH /api/orders/{id}/note`, `PATCH /api/orders/{id}/table`, `PUT /api/management/promotions/{id}/targets`, `PUT /api/management/recipes/{id}`, `POST /api/management/inventory/{id}/adjust`, `POST /api/management/purchase-orders/{id}/receive` |

> `api.js` hiện đã xử lý `204` (`response.status === 204 ? null : ...`) ✔

### 2.4 Phân trang

```json
{ "page": 1, "pageSize": 20, "total": 42, "items": [ ... ] }
```

Áp dụng cho: `GET /api/orders` · `GET /api/payments` · `GET /api/management/inventory-transactions`.
`page` từ 1 · `pageSize` 1..100 (mặc định 20) · sai ⇒ `400`.

### 2.5 Thời gian & múi giờ

| Chỗ | Quy ước |
|---|---|
| Mọi mốc thời gian trong DB | **UTC** |
| `promotions.startAt` / `endAt` (**khi gửi lên**) | **giờ Việt Nam (UTC+7)**, không kèm offset cũng hiểu là giờ VN |
| `promotions.startAt` / `endAt` (**khi nhận về**) | `startAt`/`endAt` = giờ VN · `startAtUtc`/`endAtUtc` = UTC thật |
| `from`/`to` của báo cáo & thanh toán | `yyyy-MM-dd` (ngày theo giờ VN) |

### 2.6 Tên field — giữ nguyên lỗi gõ cũ

| Nơi | Tên lượng |
|---|---|
| Đơn, bếp, topping | `quantity` ✔ (có alias JSON chuẩn) |
| **Kho, ledger kho** | **`quanlity`** (giữ lỗi gõ cũ — đọc đúng tên này) |

---

## 3. Phân quyền — 5 vai trò

| Policy | Ai được | Dùng cho |
|---|---|---|
| `Authenticated` | mọi role đã đăng nhập | `GET /api/menu` |
| `AdminOnly` | Admin | quản trị danh mục/món, khuyến mãi, bàn, nhân viên, cấu hình quán |
| `PosOperator` | Admin + Waiter | tạo đơn, gọi thêm, đổi SL, huỷ món, áp khuyến mãi, ghi chú hoá đơn, chuyển bàn |
| `PosOrCashier` | Admin + Waiter + Cashier | đọc đơn, lịch sử, **hoá đơn theo bàn** |
| `PosKitchenRead` | + Chef | bếp đọc đơn/bàn (không sửa) |
| `CashierDesk` | Admin + Cashier | thanh toán, hoàn tiền |
| `KitchenDesk` | Admin + Chef | bảng bếp, đổi trạng thái món/phiếu |
| `WarehouseDesk` | Admin + WarehouseManager | kho, nguyên liệu, công thức, phiếu mua, nhà cung cấp |
| `OrderCancellation` | Admin + Waiter + Chef | huỷ món (món `Preparing` chỉ Admin) |

---

## 4. Danh mục endpoint theo màn hình FE

> ✅ = FE đã gọi · ⬜ = FE chưa gọi. Tất cả đều **đã có** trừ 2 mục ghi rõ ở mục 0.

### 4.1 Hạ tầng / đăng nhập

| | Method | Path | Policy | Ghi chú |
|---|---|---|---|---|
|⬜| GET | `/api/setup/status` | — | `{ requiresSetup }` |
|⬜| POST | `/api/setup/first-admin` | — | chỉ dùng được khi bảng `users` còn rỗng |
|✅| POST | `/api/auth/login` | — | trả `{ message, data: { userId, userName, email, fullName, roles[], token } }` |
|⬜| GET | `/api/users/me` | Authenticated | ⚠️ **luôn cần token** (xem 2.1) |
|⬜| PUT | `/api/users/me` | Authenticated | ⚠️ **luôn cần token** · sửa `fullName`/`email`/`phone` |
|⬜| POST | `/api/auth/change-password` | Authenticated | `{ oldPassword?, newPassword, confirmPassword }` |

### 4.2 Menu (POS dùng để hiển thị)

| | Method | Path | Policy |
|---|---|---|---|
|✅| GET | `/api/menu` | Authenticated |

### 4.3 Danh mục · món · cỡ · topping

| | Method | Path | Policy | Ghi chú |
|---|---|---|---|---|
|✅| GET | `/api/categories` | Authenticated | |
|⬜| POST | `/api/categories` | AdminOnly | trả `{ message }` — **không có id** ⇒ `GET /categories` rồi tìm theo tên |
|⬜| GET | `/api/categories/{id}` | Authenticated | |
|⬜| PUT | `/api/categories/{id}` | AdminOnly | |
|⬜| PATCH | `/api/categories/{id}/active` | AdminOnly | `{ isActive }` |
|⬜| DELETE | `/api/categories/{id}` | AdminOnly | soft-delete |
|⬜| GET | `/api/foods` | Authenticated | `?includeInactive=true` |
|⬜| GET | `/api/foods/{id}` | Authenticated | |
|⬜| POST | `/api/foods` | AdminOnly | `{ categoryId, foodName, foodDescription?, foodImageUrl?, foodPrice, isAvailable? }` → **201** |
|⬜| PUT | `/api/foods/{id}` | AdminOnly | |
|⬜| PATCH | `/api/foods/{id}/availability` | AdminOnly | `{ isAvailable }` |
|⬜| PATCH | `/api/foods/{id}/active` | AdminOnly | `{ isActive }` |
|⬜| DELETE | `/api/foods/{id}` | AdminOnly | soft-delete |
|⬜| GET | `/api/foods/{foodId}/sizes` | Authenticated | `?includeInactive=true` |
|⬜| POST | `/api/foods/{foodId}/sizes` | AdminOnly | `{ sizeName, sizePrice, isDefault? }` → **201** |
|⬜| GET | `/api/foods/{foodId}/sizes/{sizeId}` | Authenticated | |
|⬜| PUT | `/api/foods/{foodId}/sizes/{sizeId}` | AdminOnly | |
|⬜| PATCH | `/api/foods/{foodId}/sizes/{sizeId}/default` | AdminOnly | đặt cỡ mặc định |
|⬜| PATCH | `/api/foods/{foodId}/sizes/{sizeId}/active` | AdminOnly | `{ isActive }` |
|⬜| DELETE | `/api/foods/{foodId}/sizes/{sizeId}` | AdminOnly | soft-delete |
|⬜| GET | `/api/toppings` | Authenticated | |
|⬜| POST | `/api/toppings` | AdminOnly | `{ toppingName, toppingPrice, isAvailable? }` → **201** |
|⬜| PUT | `/api/toppings/{id}` | AdminOnly | |
|⬜| PATCH | `/api/toppings/{id}/availability` · `/active` | AdminOnly | |
|⬜| DELETE | `/api/toppings/{id}` | AdminOnly | soft-delete |
|⬜| GET | `/api/foods/{foodId}/toppings` | Authenticated | |
|⬜| POST | `/api/foods/{foodId}/toppings` | AdminOnly | `{ toppingId }` — gắn topping cho món |
|⬜| DELETE | `/api/foods/{foodId}/toppings/{toppingId}` | AdminOnly | gỡ topping |

### 4.4 POS — đơn (F1)

| | Method | Path | Policy | Ghi chú |
|---|---|---|---|---|
|⬜| GET | `/api/orders` | PosOrCashier | `?status=&tableId=&from=&to=&page=&pageSize=` |
|⬜| GET | `/api/orders/{id}` | PosKitchenRead | chi tiết + món + topping |
|✅| POST | `/api/orders` | PosOperator | tạo đơn (hỗ trợ `Idempotency-Key`) |
|⬜| GET | `/api/orders/table/{tableId}/bill` | PosOrCashier | **hoá đơn gộp theo bàn** (mục 4.9) |
|⬜| POST | `/api/orders/{id}/items` | PosOperator | **gọi thêm** — luôn tạo vòng bếp mới |
|⬜| PATCH | `/api/orders/{id}/items/{itemId}/quantity` | PosOperator | đổi SL (gửi **đúng một** trong `quantity` / `delta`) |
|⬜| POST | `/api/orders/{id}/items/{itemId}/cancel` | OrderCancellation | huỷ 1 món + lý do |
|⬜| PATCH | `/api/orders/{id}/status` | PosOperator | đổi trạng thái đơn (huỷ đơn) |
|⬜| PATCH | `/api/orders/{id}/promotion/{promotionId}` | PosOperator | áp khuyến mãi (body rỗng) |
|⬜| DELETE | `/api/orders/{id}/promotion/{promotionId}` | PosOperator | **gỡ** khuyến mãi |
|⬜| PATCH | `/api/orders/{id}/note` | PosOperator | **ghi chú hoá đơn** (mục 4.9) |
|⬜| PATCH | `/api/orders/{id}/table` | PosOperator | **chuyển bàn** |
|⬜| GET | `/api/orders/{id}/history` | PosOrCashier | lịch sử trạng thái **đơn** |
|⬜| GET | `/api/orders/{id}/item-history` | PosOrCashier | lịch sử trạng thái **từng món** |
|⛔| PUT | `/api/orders/{id}/items` | — | **410 Gone** — không dùng |

### 4.5 Bếp (F2)

| | Method | Path | Policy | Ghi chú |
|---|---|---|---|---|
|✅| GET | `/api/management/kitchen/orders` | KitchenDesk | `?status=open` · `?since=` · `?page`/`?pageSize` — **response vẫn là mảng** |
|✅| GET | `/api/management/kitchen/orders/{id}/items` | KitchenDesk | món của phiếu |
|✅| PATCH | `/api/management/kitchen/orders/{id}/status` | KitchenDesk | `{ status, changedBy, reason?, reasonCode? }` |
|✅| PATCH | `/api/management/kitchen/items/{id}/status` | KitchenDesk | `{ status, changedBy, reason?, reasonCode? }` |
|⬜| PATCH | `/api/management/kitchen/items/{id}/prepared-quantity` | KitchenDesk | *"nấu được mấy phần"* |
|⬜| POST | `/api/management/kitchen/items/{id}/shortage` | KitchenDesk | báo thiếu nguyên liệu |
|⬜| POST | `/api/management/kitchen/items/{id}/reopen` | KitchenDesk | mở lại món đã xong |

### 4.6 Thu ngân (F3)

| | Method | Path | Policy | Ghi chú |
|---|---|---|---|---|
|✅| POST | `/api/payments` | CashierDesk | `{ orderId, paymentMethod, transactionCode?, createdBy }` |
|⬜| GET | `/api/payments` | CashierDesk | `?status=&method=&orderId=&from=&to=&page=&pageSize=` |
|⬜| GET | `/api/payments/order/{orderId}` | CashierDesk | lịch sử thu của 1 đơn (**dùng để poll trạng thái MoMo**) |
|⬜| POST | `/api/payments/momo` | CashierDesk | tạo intent MoMo → `{ paymentId, status, isMock, … }` |
|⬜| POST | `/api/payments/{id}/refund` | CashierDesk | hoàn tiền toàn bộ |

> **Poll trạng thái MoMo:** `POST /api/payments/momo` → lấy `paymentId` → poll
> `GET /api/payments/order/{orderId}` (2–3 giây/lần) → đơn chuyển `Paid` là thành công.
> **Không** có endpoint `GET /api/payments/momo/{id}`.

> `POST /api/payments/momo/notify` là **điều khoản của MoMo**, không phải của FE.

### 4.7 Kho & mua hàng (F4)

| | Method | Path | Policy | Ghi chú |
|---|---|---|---|---|
|✅| GET | `/api/management/inventory` | WarehouseDesk | tồn hiện tại (`quanlity`) |
|⬜| POST | `/api/management/inventory/{ingredientId}/adjust` | WarehouseDesk | điều chỉnh tồn (âm = giảm) → **204**  · ⚠️ **`note` BẮT BUỘC** (C-21, thiếu ⇒ `400`) |
|⬜| GET | `/api/management/inventory-transactions` | WarehouseDesk | biến động kho (có `unit`) |
|⬜| GET | `/api/management/inventory/low-stock` | WarehouseDesk | **nguyên liệu cần kho xử lý** · có `stockStatus` (C-21) |
|⬜| GET | `/api/management/ingredients` | WarehouseDesk | |
|⬜| GET | `/api/management/ingredients/{id}` | WarehouseDesk | |
|⬜| POST | `/api/management/ingredients` | WarehouseDesk | `{ name, unit, minimumStock? }` |
|⬜| PUT | `/api/management/ingredients/{id}` | WarehouseDesk | ⚠️ **đổi `unit` của nguyên liệu đã dùng ⇒ `409`** (mục 5.4) |
|⬜| DELETE | `/api/management/ingredients/{id}` | WarehouseDesk | soft-delete |
|⬜| PATCH | `/api/management/ingredients/{id}/active` | WarehouseDesk | `{ isActive }` |
|⬜| PATCH | `/api/management/ingredients/{id}/status` | WarehouseDesk | `{ stockStatus: Available\|OutOfStock\|Spoiled, note? }` |
|⬜| GET | `/api/management/suppliers` | WarehouseDesk | |
|⬜| POST/PUT/DELETE | `/api/management/suppliers[/{id}]` | WarehouseDesk | |
|⬜| PATCH | `/api/management/suppliers/{id}/active` | WarehouseDesk | `{ isActive }` |
|⬜| GET | `/api/management/purchase-orders` | WarehouseDesk | |
|⬜| POST | `/api/management/purchase-orders` | WarehouseDesk | tạo phiếu mua |
|⬜| GET | `/api/management/purchase-orders/{id}` | WarehouseDesk | |
|⬜| POST | `/api/management/purchase-orders/{id}/receive` | WarehouseDesk | nhập hàng (cộng kho) → **204** |
|⬜| POST | `/api/management/purchase-orders/{id}/cancel` | WarehouseDesk | huỷ phiếu (đã nhận ⇒ `409`) |

### 4.8 Công thức · khuyến mãi · cấu hình (F6/F7)

| | Method | Path | Policy | Ghi chú |
|---|---|---|---|---|
|⬜| GET | `/api/management/recipes` | WarehouseDesk | `?includeInactive=true` |
|⬜| GET | `/api/management/recipes/{id}` | WarehouseDesk | chi tiết + định lượng |
|⬜| POST | `/api/management/recipes` | WarehouseDesk | tạo công thức |
|⬜| PUT | `/api/management/recipes/{id}` | WarehouseDesk | ⚠️ **tạo phiên bản mới**, `recipeId` **đổi** (mục 5.5) |
|⬜| DELETE | `/api/management/recipes/{id}` | WarehouseDesk | soft-delete |
|⬜| PATCH | `/api/management/recipes/{id}/active` | WarehouseDesk | `{ isActive }` |
|⬜| GET | `/api/management/promotions` | AdminOnly | có `sizeIds` / `categoryIds` |
|⬜| GET | `/api/management/promotions/{id}` | AdminOnly | |
|⬜| POST | `/api/management/promotions` | AdminOnly | `startAt`/`endAt` = **giờ VN** |
|⬜| PUT | `/api/management/promotions/{id}` | AdminOnly | |
|⬜| PUT | `/api/management/promotions/{id}/targets` | AdminOnly | **gắn món/danh mục được giảm** → **204** |
|⬜| PATCH | `/api/management/promotions/{id}/active` | AdminOnly | `{ isActive }` |
|⬜| DELETE | `/api/management/promotions/{id}` | AdminOnly | soft-delete |
|⬜| GET | `/api/management/settings` | AdminOnly | cấu hình quán (VAT, phí DV) |
|⬜| PUT | `/api/management/settings` | AdminOnly | `{ restaurantName, address, phone, taxCode, currency, taxRate, taxIncluded, serviceChargeRate }` |

### 4.9 Bàn · khu vực · báo cáo · nhân viên

| | Method | Path | Policy | Ghi chú |
|---|---|---|---|---|
|✅| GET | `/api/restaurant-tables` | Authenticated | ⚠️ endpoint này **không** ghi DB |
|⬜| GET | `/api/restaurant-tables/{id}` | Authenticated | ⚠️ endpoint này **CÓ ghi DB** |
|⬜| POST | `/api/restaurant-tables` | AdminOnly | → `{ message, data: { tableId, … } }` |
|⬜| PUT | `/api/restaurant-tables/{id}` | AdminOnly | |
|⬜| DELETE | `/api/restaurant-tables/{id}` | AdminOnly | soft-delete |
|⬜| PATCH | `/api/restaurant-tables/{id}/maintenance` | AdminOnly | `{ isMaintenance }` |
|⬜| GET/POST/PUT/DELETE | `/api/table-areas[/{id}]` | AdminOnly | `PATCH /api/table-areas/{id}/active` |
|⬜| GET | `/api/reports/dashboard` | AdminOnly | tổng quan (doanh thu, bàn, bếp, tồn thấp) |
|⬜| GET | `/api/reports/revenue` | AdminOnly | `?period=day\|week\|month&date=yyyy-MM-dd` |
|⬜| GET | `/api/reports/revenue/export.csv` | AdminOnly | tải CSV (cần `blob` trong `api.js`) |
|⬜| GET | `/api/users` · `/api/users/{id}` | AdminOnly | |
|⬜| POST | `/api/users` | AdminOnly | `{ userName, email, password, confirmPassword, fullName?, phone?, roleName? }` |
|⬜| PUT | `/api/users/{id}` | AdminOnly | |
|⬜| PATCH | `/api/users/{id}/active` | AdminOnly | `{ isActive }` |
|⬜| POST | `/api/users/{id}/reset-password` | AdminOnly | |
|⬜| POST | `/api/users/{userId}/roles` | AdminOnly | `{ roleName }` |
|⬜| DELETE | `/api/users/{userId}/roles/{roleName}` | AdminOnly | thu role |
|⬜| GET | `/api/roles` | AdminOnly | 5 vai trò |

---

## 5. Luật nghiệp vụ FE phải tôn trọng

> Đây là phần **quan trọng nhất** — hiển thị sai thì tiền/kho lệch.

### 5.1 1 bàn = 1 đơn đang mở

`POST /api/orders` **trả `409`** nếu bàn đã có đơn chưa `Paid`/`Cancelled`.
⇒ Muốn gọi thêm món cho bàn đó thì dùng `POST /api/orders/{id}/items`, **không** tạo đơn mới.

### 5.2 Gọi thêm ⇒ đơn quay lại `SentToKitchen`

Sau `POST /api/orders/{id}/items`: đơn về `SentToKitchen` và **tạo phiếu bếp mới**.
⇒ **Thu ngân không thu được tiền** (`409 Order is not ready for payment`) cho tới khi bếp xong vòng mới.
UI thu ngân nên hiện "đang chờ bếp" thay vì báo lỗi.

### 5.3 Tiền luôn do server tính

`GET /api/orders/{id}` trả `subtotal` / `discountAmount` / `taxAmount` / `serviceChargeAmount` / `totalAmount`.
⇒ **FE không tự tính tổng, không tự áp VAT/phí DV.** Công thức:
`base = subtotal − discountAmount` → phí DV = `base × rate%` → VAT = `base × rate/(100+rate)` nếu giá đã gồm VAT.

### 5.4 Đơn vị của nguyên liệu bị khoá

`PUT /api/management/ingredients/{id}` — đổi `unit` khi nguyên liệu **đã dùng** (có trong công thức /
đã phát sinh biến động kho / đã có trong phiếu mua) ⇒ **`409`**:
`"Cannot change unit from 'kg' to 'g' because this ingredient is already used in recipes, stock history. Create a new ingredient instead."`
⇒ Form nên **disable ô "Đơn vị"** và báo *"Nguyên liệu đã dùng nên không đổi được đơn vị — hãy tạo nguyên liệu mới"*.

### 5.5 Sửa công thức ⇒ `recipeId` đổi

`PUT /api/management/recipes/{id}` tạo **phiên bản mới**; bản cũ chuyển `isActive=false` (giữ lịch sử).
- FE **phải gọi lại** `GET /api/management/recipes` sau khi lưu (id đã đổi).
- Gửi `version` rỗng hoặc **đúng bằng** version đang chạy ⇒ server **tự tăng** (`1` → `2`).
- Sửa lại bản đã bị thay thế ⇒ **`409`**.

### 5.6 Khuyến mãi — 3 mức

| | |
|---|---|
| **Áp** | `PATCH /api/orders/{id}/promotion/{promotionId}` — BE tự kiểm tra thời hạn, đơn tối thiểu, **phạm vi target** |
| **Gỡ** | `DELETE /api/orders/{id}/promotion/{promotionId}` |
| **Phạm vi** | `PUT /api/management/promotions/{id}/targets` — có target ⇒ chỉ giảm trên dòng món thuộc target; **không** có target ⇒ giảm toàn hoá đơn |

### 5.7 Mã giao dịch

`POST /api/payments` — `transactionCode` **tuỳ chọn**:
- không gửi ⇒ **server sinh** mã `PAY{yyyyMMddHHmmss}-{8 hex}` ⇒ `response.transactionCode` luôn có giá trị.
- gửi mã **đã tồn tại** ⇒ **`409`** ⇒ cho thu ngân nhập mã ngân hàng, hiện "mã đã tồn tại".

### 5.8 Hoá đơn theo bàn — chỉ để hiển thị

`GET /api/orders/table/{tableId}/bill` gom mọi đơn **đang mở** của bàn (tổng tiền, dòng món đã gộp, ghi chú).
⇒ **Thanh toán vẫn làm từng đơn** (`POST /api/payments` nhận `orderId`); dùng `orderIds` trong response để lần lượt.
⇒ Bàn không có đơn mở ⇒ `404`.

### 5.9 Trạng thái (để vẽ UI đúng)

**Đơn:** `Pending → SentToKitchen → Preparing → Ready → Completed → Paid` · `Cancelled`
**Món bếp:** `Pending → Preparing → Ready` (+ `Completed`, `Cancelled`)
**Phiếu bếp:** `Pending → Accepted → Preparing → Ready → Completed` (+ `Cancelled`)

| Mã lý do huỷ | Ảnh hưởng kho |
|---|---|
| `CustomerChange` (mặc định) | **hoàn kho** phần chưa nấu |
| `OutOfStock` / `PhysicalShortage` / `KitchenError` | ghi **hao hụt**, **không** hoàn kho |

> Món `Preparing` = nguyên liệu **đã tiêu thật** ⇒ mọi thay đổi từ đó ghi `Wastage`, **không** hoàn kho.
> POS **không** tự huỷ/giảm món `Preparing` (chỉ Chef/Admin).

### 5.10 Hoàn tiền

`POST /api/payments/{id}/refund` ⇒ giao dịch `Refunded`, **đơn mở lại `Ready`** (chờ thu lại), bàn về `Occupied`.
⇒ Đã thu rồi hoan ⇒ **không còn tính vào doanh thu**.

---

## 6. Ma trận trạng thái bếp ↔ kho (rút gọn)

| Tình huống | Kết quả kho |
|---|---|
| POS gọi món | **trừ kho ngay** + ghi ledger `Sale` |
| Bếp bấm `Preparing` | món đó bị **khoá**: mọi thay đổi sau đó ghi `Wastage`, không hoàn kho |
| Huỷ món chưa nấu (`CustomerChange`) | **hoàn kho** phần chưa nấu |
| Huỷ món đang nấu | ghi `Wastage`, **không** hoàn |
| Bếp báo thiếu nguyên liệu | huỷ phần thiếu + ghi `Wastage`; có thể ngừng bán món đó |
| Mở lại món đã xong | món/ticket/đơn về `Preparing` (chỉ khi đơn chưa `Paid`/`Cancelled`) |

Chi tiết: `docs/architecture/kitchen-stock-rule-2026-10-21.md`.

---

## 7. Checklist bàn giao

- [ ] `api.js` đọc token từ `localStorage` và gắn `Authorization: Bearer` cho **mọi** request
- [ ] `401` → xoá token, về trang đăng nhập · `403` → báo không đủ quyền · `409` → hiện `message`
- [ ] `api.js` có `download()` dùng `blob` cho CSV, và cho phép header tuỳ chọn (`Idempotency-Key`)
- [ ] Không còn gọi `PUT /api/orders/{id}/items` (đã `410`)
- [ ] Thu ngân chỉ gọi `POST /api/payments` khi đơn đủ điều kiện (để tránh `409`)
- [ ] POS: dùng `POST /api/orders/{id}/items` để gọi thêm (không tạo đơn mới trên bàn đang có đơn)
- [ ] Bếp poll `GET /api/management/kitchen/orders?status=open` 3–5s; lần đầu **không** kèm `since`
- [ ] Sửa công thức → gọi lại `GET /api/management/recipes`
- [ ] Sửa nguyên liệu đã dùng → bắt `409` và disable ô "Đơn vị"
- [ ] **Không tự tính tiền** — luôn lấy `totalAmount` từ server

---

## 8. File liên quan

| File | Nội dung |
|---|---|
| `docsforfe/README.md` | Quy tắc + mục lục + trạng thái FE |
| `docsforfe/fe-work-plan.md` | Bảng công việc FE theo phase F0–F7 + mục 0 "backend sẵn sàng chưa" |
| `docsforfe/fe-api-reference.md` | Danh mục 128 action + phụ lục thay đổi theo từng đợt |
| `docsforfe/fe-payloads.md` | Body/response mẫu chi tiết |
| `docs/architecture/endpoint-baseline.md` | Hợp đồng API phía backend (nguồn chuẩn) |
| `docs/architecture/kitchen-stock-rule-2026-10-21.md` | Luật bếp ↔ kho ↔ hoàn kho đầy đủ |
| `docs/architecture/backend-capabilities-2026-10-21.md` | Backend hiện đã có gì (năng lực theo module) |
| `docs/architecture/e2e-smoke-test-2026-10-21.md` | 100 case E2E trên API thật + quirks hợp đồng |
