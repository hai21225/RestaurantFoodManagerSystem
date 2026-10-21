# API reference cho FRONTEND (128 action backend)

**Cập nhật:** 2026-10-21 · **Nguồn:** `RestaurantFoodManagerSystem/Controllers/**` (đã quét code)
**Ký hiệu:** ✅ = FE đã gọi · ⬜ = FE **chưa** gọi (xem `fe-work-plan.md`) · 🔒 = cần `Authorization: Bearer`
**Base URL:** `http://localhost:5225/api` (FE đọc `localStorage.restaurant.apiBaseUrl`)

**Tổng:** 128 action — FE hiện dùng **38** (30%), **89** chưa dùng, **1** đã bị bỏ (`PUT /orders/{id}/items` → 410).
**Policy** cần hiểu: `Authenticated` (bất kỳ đã đăng nhập) · `PosOperator` (Admin/Waiter) · `PosOrCashier` (Admin/Waiter/Cashier) · `PosKitchenRead` (Admin/Waiter/Cashier/Chef) · `KitchenDesk` (Admin/Chef) · `WarehouseDesk` (Admin/WarehouseManager) · `CashierDesk` (Admin/Cashier) · `AdminOnly` · `OrderCancellation` (Admin/Waiter/Chef).

> Lưu ý: khi `Auth:Enforce=false` (hiện tại) mọi policy đều cho qua. Khi bật `true`, FE **bắt buộc** gửi Bearer (Phase F0).

---

## 1. Xác thực & khởi tạo (5)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| POST | `/auth/login` | AllowAnonymous | ✅ | Trả token; **FE phải lưu token** |
|⬜| POST | `/auth/change-password` | Authenticated | ⬜ | Cần mật khẩu hiện tại + xác nhận |
|⬜| POST | `/auth/register` | AdminOnly | ⬜ | Dùng `POST /users` thay cho |
|✅| GET | `/setup/status` | AllowAnonymous | ✅ | Có user chưa → cần setup |
|✅| POST | `/setup/first-admin` | AllowAnonymous | ✅ | Chỉ khi bảng user còn rỗng |

## 2. Nhân viên & vai trò (11)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|⬜| GET | `/users/me` | Authenticated | ⬜ | Hồ sơ cá nhân |
|⬜| PUT | `/users/me` | Authenticated | ⬜ | Chỉ `fullName/email/phone` |
|✅| GET | `/users` | AdminOnly | ✅ | Có phân trang |
|✅| POST | `/users` | AdminOnly | ✅ | Body có `roleName` để gán role ngay |
|⬜| GET | `/users/{id}` | AdminOnly | ⬜ | |
|⬜| PUT | `/users/{id}` | AdminOnly | ⬜ | Không sửa `userName`/roles/isActive |
|⬜| PATCH | `/users/{id}/active` | AdminOnly | ⬜ | Khoá / mở khoá tài khoản |
|⬜| POST | `/users/{id}/reset-password` | AdminOnly | ⬜ | Admin đặt lại mật khẩu |
|⬜| POST | `/users/{userId}/roles` | AdminOnly | ⬜ | Gán role (trùng → 409) |
|⬜| DELETE | `/users/{userId}/roles/{roleName}` | AdminOnly | ⬜ | Không gỡ được Admin cuối |
|⬜| GET | `/roles` | AdminOnly | ⬜ | 5 vai trò cố định (chỉ đọc) |

## 3. Danh mục món (30)
### 3.1 Category (6)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/categories` | Authenticated | ✅ |
|⬜| GET | `/categories/{id}` | Authenticated | ⬜ |
|✅| POST | `/categories` | WarehouseDesk | ✅ |
|⬜| PUT | `/categories/{id}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/categories/{id}/active` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/categories/{id}` | WarehouseDesk | ⬜ |

### 3.2 Food (7)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/foods` | Authenticated | ✅ (`?includeInactive=true`) |
|⬜| GET | `/foods/{id}` | Authenticated | ⬜ |
|✅| POST | `/foods` | WarehouseDesk | ✅ |
|✅| PUT | `/foods/{id}` | WarehouseDesk | ✅ |
|✅| PATCH | `/foods/{id}/availability` | WarehouseDesk | ✅ |
|✅| PATCH | `/foods/{id}/active` | WarehouseDesk | ✅ |
|✅| DELETE | `/foods/{id}` | WarehouseDesk | ✅ |

### 3.3 Food size (7)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/foods/{foodId}/sizes` | Authenticated | ✅ |
|⬜| GET | `/foods/{foodId}/sizes/{sizeId}` | Authenticated | ⬜ |
|✅| POST | `/foods/{foodId}/sizes` | WarehouseDesk | ✅ |
|⬜| PUT | `/foods/{foodId}/sizes/{sizeId}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/foods/{foodId}/sizes/{sizeId}/default` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/foods/{foodId}/sizes/{sizeId}/active` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/foods/{foodId}/sizes/{sizeId}` | WarehouseDesk | ⬜ |

### 3.4 Topping (7) + gán topping cho món (3)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|⬜| GET | `/toppings` | Authenticated | ⬜ **chưa có màn hình nào** |
|⬜| GET | `/toppings/{id}` | Authenticated | ⬜ |
|⬜| POST | `/toppings` | WarehouseDesk | ⬜ |
|⬜| PUT | `/toppings/{id}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/toppings/{id}/availability` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/toppings/{id}/active` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/toppings/{id}` | WarehouseDesk | ⬜ |
|⬜| GET | `/foods/{foodId}/toppings` | Authenticated | ⬜ |
|⬜| POST | `/foods/{foodId}/toppings` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/foods/{foodId}/toppings/{toppingId}` | WarehouseDesk | ⬜ |

### 3.5 Menu POS (1)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/menu` | Authenticated | ✅ (chỉ món `IsAvailable`) |

## 4. Bàn & khu vực (12)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| GET | `/restaurant-tables` | PosOrCashier | ✅ | Trả cả bàn đã xoá (soft) |
|✅| GET | `/restaurant-tables/{id}` | PosKitchenRead | ✅ | |
|✅| POST | `/restaurant-tables` | AdminOnly | ✅ | |
|⬜| PUT | `/restaurant-tables/{id}` | AdminOnly | ⬜ | Sửa số bàn / sức chứa / khu vực |
|✅| DELETE | `/restaurant-tables/{id}` | AdminOnly | ✅ | Soft-delete; bàn có đơn mở → 409 |
|✅| PATCH | `/restaurant-tables/{id}/maintenance` | AdminOnly | ✅ | `?enabled=true|false` |
|✅| GET | `/table-areas` | PosOrCashier | ✅ (`?includeInactive=true`) | |
|⬜| GET | `/table-areas/{id}` | PosOrCashier | ⬜ | |
|⬜| POST | `/table-areas` | AdminOnly | ⬜ | |
|⬜| PUT | `/table-areas/{id}` | AdminOnly | ⬜ | |
|⬜| PATCH | `/table-areas/{id}/active` | AdminOnly | ⬜ | |
|⬜| DELETE | `/table-areas/{id}` | AdminOnly | ⬜ | |

## 5. Đơn (10) — trọng tâm Phase F1
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|⬜| GET | `/orders` | PosOrCashier | ⬜ | Lọc `status/tableId/from/to/page/pageSize` — **chưa có màn hình nào dùng** (POS cần để mở đơn theo bàn) |
|✅| GET | `/orders/{id}` | PosKitchenRead | ✅ | Chi tiết đơn + món + topping |
|✅| POST | `/orders` | PosOperator | ✅ | Tạo đơn; hỗ trợ header `Idempotency-Key` |
|⬜| POST | `/orders/{id}/items` | PosOperator | ⬜ | **Gọi thêm** — luôn tạo vòng bếp mới |
|⬜| PATCH | `/orders/{orderId}/items/{itemId}/quantity` | PosOperator | ⬜ | Đổi SL (đã nấu 1 phần vẫn dùng được) |
|⬜| POST | `/orders/{orderId}/items/{itemId}/cancel` | OrderCancellation | ⬜ | Huỷ 1 món + lý do |
|⬜| PATCH | `/orders/{id}/promotion/{promotionId}` | PosOperator | ⬜ | Áp khuyến mãi |
|⬜| PATCH | `/orders/{id}/status` | PosOperator | ⬜ | Đổi trạng thái đơn (huỷ đơn) |
|⬜| GET | `/orders/{id}/history` | PosOrCashier | ⬜ | Lịch sử trạng thái **đơn** |
|⬜| GET | `/orders/{id}/item-history` | PosOrCashier | ⬜ | Lịch sử trạng thái **từng món** (C-10) |
|⬜| GET | `/orders/table/{tableId}/bill` | PosOrCashier | ⬜ | **Mới (Đợt 3)** — hoá đơn gộp mọi đơn đang mở của bàn |
|⬜| PATCH | `/orders/{id}/note` | PosOperator | ⬜ | **Mới (Đợt 3)** — ghi chú cho cả hoá đơn |
|⛔| PUT | `/orders/{id}/items` | PosOperator | ❌ | **410 Gone (C-09)** — không dùng |

## 6. Bếp (7) — Phase F2
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| GET | `/management/kitchen/orders` | KitchenDesk | ✅ | Tất cả phiếu; hỗ trợ `?status=open` · `?since=` · `?page`/`?pageSize` (Đợt 3) |
|✅| GET | `/management/kitchen/orders/{id}/items` | KitchenDesk | ✅ | Món của phiếu |
|✅| PATCH | `/management/kitchen/orders/{id}/status` | KitchenDesk | ✅ | Trạng thái phiếu (kèm `reason`,`reasonCode`) |
|✅| PATCH | `/management/kitchen/items/{id}/status` | KitchenDesk | ✅ | Trạng thái món — **`Accepted` không hợp lệ** (C-11) |
|⬜| PATCH | `/management/kitchen/items/{id}/prepared-quantity` | KitchenDesk | ⬜ | Báo đã nấu được mấy phần |
|⬜| POST | `/management/kitchen/items/{id}/shortage` | KitchenDesk | ⬜ | Báo thiếu nguyên liệu (không hard-block) |
|⬜| POST | `/management/kitchen/items/{id}/reopen` | KitchenDesk | ⬜ | Mở lại món đã xong (C-06) |

## 7. Kho & nhập hàng (20)
### 7.1 Nguyên liệu (6)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/management/ingredients` | WarehouseDesk | ✅ |
|⬜| GET | `/management/ingredients/{id}` | WarehouseDesk | ⬜ |
|✅| POST | `/management/ingredients` | WarehouseDesk | ✅ |
|⬜| PUT | `/management/ingredients/{id}` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/management/ingredients/{id}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/management/ingredients/{id}/active` | WarehouseDesk | ⬜ |

### 7.2 Nhà cung cấp (6)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|⬜| GET | `/management/suppliers` | WarehouseDesk | ⬜ |
|⬜| GET | `/management/suppliers/{id}` | WarehouseDesk | ⬜ |
|⬜| POST | `/management/suppliers` | WarehouseDesk | ⬜ |
|⬜| PUT | `/management/suppliers/{id}` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/management/suppliers/{id}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/management/suppliers/{id}/active` | WarehouseDesk | ⬜ |

### 7.3 Tồn kho & biến động (4)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| GET | `/management/inventory` | WarehouseDesk | ✅ | Tồn hiện tại từng nguyên liệu |
|⬜| POST | `/management/inventory/{ingredientId}/adjust` | WarehouseDesk | ⬜ | Điều chỉnh tồn (có `note`) |
|⬜| GET | `/management/inventory-transactions` | WarehouseDesk | ⬜ | Biến động kho; `referenceId` cần kèm `referenceType`; có thêm `unit` |
|⬜| GET | `/management/inventory/low-stock` | WarehouseDesk | ⬜ | **Mới (Đợt 3)** — nguyên liệu sắp hết |

### 7.4 Phiếu nhập (5)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| GET | `/management/purchase-orders` | WarehouseDesk | ✅ | Chỉ danh sách |
|⬜| POST | `/management/purchase-orders` | WarehouseDesk | ⬜ | Tạo phiếu nhập |
|⬜| GET | `/management/purchase-orders/{id}` | WarehouseDesk | ⬜ | Chi tiết + dòng hàng |
|⬜| POST | `/management/purchase-orders/{id}/receive` | WarehouseDesk | ⬜ | Nhập hàng (cộng kho) |
|⬜| POST | `/management/purchase-orders/{id}/cancel` | WarehouseDesk | ⬜ | Huỷ phiếu (đã nhập → 409) |

### 7.5 Công thức (6)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/management/recipes` | WarehouseDesk | ✅ |
|✅| GET | `/management/recipes/{id}` | WarehouseDesk | ✅ |
|✅| POST | `/management/recipes` | WarehouseDesk | ✅ |
|✅| PUT | `/management/recipes/{id}` | WarehouseDesk | ✅ |
|✅| DELETE | `/management/recipes/{id}` | WarehouseDesk | ✅ |
|⬜| PATCH | `/management/recipes/{id}/active` | WarehouseDesk | ⬜ |

## 8. Khuyến mãi (6)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|⬜| GET | `/management/promotions` | AdminOnly | ⬜ | |
|⬜| GET | `/management/promotions/{id}` | AdminOnly | ⬜ | |
|⬜| POST | `/management/promotions` | AdminOnly | ⬜ | `discountType`: `Percent`\|`Fixed` |
|⬜| PUT | `/management/promotions/{id}` | AdminOnly | ⬜ | |
|⬜| PATCH | `/management/promotions/{id}/active` | AdminOnly | ⬜ | |
|⬜| DELETE | `/management/promotions/{id}` | AdminOnly | ⬜ | Soft-delete |
|⬜| PUT | `/management/promotions/{id}/targets` | AdminOnly | ⬜ | **Mới (C-18)** — gắn target (món/danh mục) được giảm |

## 9. Thanh toán (3) — Phase F3
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| POST | `/payments` | CashierDesk | ✅ | `paymentMethod`: `Cash`\|`Card`\|`Bank`\|`EWallet`\|`Transfer` |
|⬜| GET | `/payments` | CashierDesk | ⬜ | Lọc `status/method/orderId/from/to/page/pageSize` |
|⬜| GET | `/payments/order/{orderId}` | CashierDesk | ⬜ | Lịch sử thu của đơn |

## 10. Báo cáo (3) — Phase F5
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|⬜| GET | `/reports/dashboard` | AdminOnly | ⬜ | Doanh thu hôm nay, đơn mở, bàn, bếp, tồn thấp, 10 đơn gần nhất |
|⬜| GET | `/reports/revenue` | AdminOnly | ⬜ | `period=day\|week\|month`, `date=yyyy-MM-dd` |
|⬜| GET | `/reports/revenue/export.csv` | AdminOnly | ⬜ | UTF-8 BOM, cần tải bằng `blob` |

---

## Phụ lục — Endpoint BE **chưa có** mà FE sẽ cần (theo audit 2026-10-21)
| Đề xuất route | Dùng cho | Ưu tiên BE |
|---|---|---|
| `DELETE /api/orders/{id}/promotion/{promotionId}` | Gỡ khuyến mãi khỏi đơn | P0 |
| `GET/PUT /api/management/settings` | Cấu hình quán + VAT + phí dịch vụ | P0 |
| `GET /api/orders/table/{tableId}/bill` | Bill gom đơn của bàn cho thu ngân | P1 |
| `PATCH /api/orders/{id}/table` | Chuyển bàn | P1 |
| `POST /api/orders/{id}/split` | Chia / gộp bill | P1 |
| `GET /api/management/inventory/low-stock` | Danh sách tồn thấp | P1 |
| `GET/POST /api/management/shifts/*` | Ca làm việc / két tiền | P1 |
| `GET /api/reports/{revenue-by-method,top-dishes,wastage,stock-movement,purchases,by-staff}` | Báo cáo vận hành | P1 |
| `PATCH /api/orders/{id}/note` | Ghi chú mức đơn | P1 |
| `/api/payments/mo-mo/*` | QR MoMo, IPN, trạng thái, hoàn tiền | cuối cùng |

---

## Phụ lục 2026-10-21 — thay đổi sau baseline (C-13 + đợt sửa P0)

Tổng số action: **115 → 116** (thêm `PATCH /api/management/ingredients/{id}/status`).
Chi tiết đầy đủ: `docs/architecture/endpoint-baseline.md` (mục "C-13 + Đợt sửa P0") + `docs/architecture/kitchen-stock-rule-2026-10-21.md`.

### 6.1 Bếp (7 → 7 route, 2 thay đổi)

| Verb | Route | Thay đổi |
|---|---|---|
| GET | `/api/management/kitchen/orders` | **+ query `status=open`**; response **+** `tableNumber`, `waiterName`, `itemCount` |
| PATCH | `/api/management/kitchen/orders/{id}/status` | body **+** `changedBy` |
| PATCH | `/api/management/kitchen/items/{id}/status` | body **+** `changedBy`; món `Preparing` bị huỷ ⇒ ghi `Wastage`, **không** hoàn kho |
| POST | `/api/management/kitchen/items/{id}/reopen` | body **+** `changedBy` |
| POST | `/api/management/kitchen/items/{id}/shortage` | body **+** `ingredientId`, `deactivateDish`, `inactiveReason`; `deactivateDish=true` ⇒ món vô hiệu hoá + lưu lý do (nguyên liệu **giữ nguyên**) |

### 7.3.1 Nguyên liệu (6 → 7 route)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| PATCH | `/api/management/ingredients/{id:int}/status` | `IngredientStatusRequest`: `stockStatus` (`Available`/`OutOfStock`/`Spoiled`), `note` (tuỳ chọn) | `204` | `400` `{message}`, `404` |

`GET /api/management/ingredients` response **+** `stockStatus`.

### 5.1 Đơn (10 route) — thay đổi hành vi

| Route | Thay đổi |
|---|---|
| `GET /api/orders/{id}/history` | **+** dòng "đơn được tạo" (`Pending → SentToKitchen`) và dòng **`→ Paid`** |
| `GET /api/orders/{id}/item-history` | **+** dòng "dòng món được tạo" (`oldStatus` = **chuỗi rỗng**) |
| `PATCH /api/orders/{orderId}/items/{itemId}/quantity` | giảm món `Preparing` bởi Waiter ⇒ **`403`** `{message}`; giảm món `Preparing` (Chef/Admin) ⇒ ghi `Wastage`, **không** hoàn kho |
| `POST /api/orders/{orderId}/items/{itemId}/cancel` | huỷ món `Preparing` ⇒ ghi `Wastage`; xung đột ghi ⇒ **`409`** |
| `POST /api/orders`, `POST /api/orders/{id}/items` | xung đột ghi ⇒ **`409`** `{message}` (trước `500`) |
| `PUT /api/orders/{id}/items` | **không đổi** — vẫn `410 Gone` |

### 9. Thanh toán

| Route | Thay đổi |
|---|---|
| `POST /api/payments` | xung đột ghi ⇒ **`409`** `{message}` (trước `500`); đọc đơn + kiểm tra nằm trong transaction `Serializable` |

> **Lưu ý MoMo:** backend **không** thêm provider/thanh toán tích hợp. `Payment.Status` / cột `payments` **không** đổi — phần MoMo do chủ dự án tự làm sau.

---

## Phụ lục 2026-10-21 (C-14 — "Tiền đúng") — thay đổi sau C-13

Tổng số action: **116 → 119** (thêm `GET /api/management/settings`, `PUT /api/management/settings`, `DELETE /api/orders/{id}/promotion/{promotionId}`).
Chi tiết: `docs/architecture/endpoint-baseline.md` (mục "C-14 — Tiền đúng") + `docsforfe/fe-work-plan.md` mục H.

### Cấu hình quán (2 route mới — Admin)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| GET | `/api/management/settings` | - | `200` `RestaurantSettingsResponse` | `401`/`403` |
| PUT | `/api/management/settings` | `RestaurantSettingsRequest`: `restaurantName`, `address`, `phone`, `taxCode`, `currency`, `taxRate`, `taxIncluded`, `serviceChargeRate` | `200` cấu hình đã lưu | `400` `{message}`, `401`/`403` |

`RestaurantSettingsResponse`: `id`, `restaurantName`, `address`, `phone`, `taxCode`, `currency`, `taxRate`, `taxIncluded`, `serviceChargeRate`, `updatedAt`.

### 5.2 Đơn — thêm 1 route + 2 header

| Verb | Route | Thay đổi |
|---|---|---|
| DELETE | `/api/orders/{id:int}/promotion/{promotionId:int}` | gỡ khuyến mãi ⇒ `204`; `400` `{message}` nếu đơn không đang áp khuyến mãi đó; `409` nếu đơn đã `Paid`/`Cancelled` |
| POST | `/api/orders/{id}/items` | hỗ trợ header `Idempotency-Key` |
| PATCH | `/api/orders/{id}/items/{itemId}/quantity` | hỗ trợ header `Idempotency-Key` |

Response đơn (`OrderDetailResponse` / `OrderCreatedResponse`) **+** `serviceChargeAmount`, `promotionId`, `promotionDiscountType`, `promotionDiscountValue`.
`OrderListItemResponse` **+** `serviceChargeAmount`.

### 8. Khuyến mãi (6 route) — quy ước múi giờ

| Route | Thay đổi |
|---|---|
| `POST /api/management/promotions`, `PUT /api/management/promotions/{id}` | `startAt`/`endAt` nhận theo **giờ Việt Nam (UTC+7)**; không kèm múi giờ ⇒ hiểu là giờ VN; có kèm múi giờ ⇒ giữ nguyên. Server lưu **UTC** |
| `GET /api/management/promotions`, `GET /api/management/promotions/{id}` | response **+** `startAtUtc`, `endAtUtc`; `startAt`/`endAt` = **giờ VN** |

### 9. Thanh toán

| Route | Thay đổi |
|---|---|
| `POST /api/payments` | hỗ trợ header `Idempotency-Key` — bấm "thu tiền" 2 lần chỉ tạo 1 payment |

### 3.1 Danh mục món — đổi kiểu field

| Route | Thay đổi |
|---|---|
| `POST /api/management/categories`, `PUT /api/management/categories/{id}` | `displayOrder` đổi kiểu **`bool` → `int`** (sắp danh mục bằng số) |

### Lịch sử đơn

| Route | Thay đổi |
|---|---|
| `GET /api/orders/{id}/history` | response **+** `reasonCode` (`CustomerChange`/`OutOfStock`/`PhysicalShortage`/`KitchenError`) |

---

## Phụ lục 2026-10-21 (C-15) — chuyển bàn + thanh toán MoMo

Tổng số action: **119 → 124** (thêm `PATCH /api/orders/{id}/table`, `POST /api/payments/momo`,
`POST /api/payments/momo/notify`, `POST /api/payments/momo/mock/{paymentId}`, `POST /api/payments/{paymentId}/refund`).
Chi tiết: `docs/architecture/endpoint-baseline.md` (mục "C-15") + `docsforfe/fe-work-plan.md` mục I.

### 5.3 Chuyển bàn (1 route mới — Waiter + Admin)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| PATCH | `/api/orders/{id:int}/table` | `ChangeOrderTableRequest`: `tableId`, `changedBy` | `204` | `400` `{message}`, `404`, `409` `{message}` |

- `409` khi **bàn đích đang có đơn mở** (bàn đó đang có người) hoặc đơn đã `Paid`/`Cancelled`.
- `400` khi bàn đích không tồn tại / đang bảo trì / trùng bàn cũ.
- Thành công: bàn cũ → `Available`, bàn mới → `Occupied`, lịch sử đơn có dòng "Moved from table X to table Y".

### 9.1 Thanh toán (3 route mới)

| Verb | Route | Request | Success | Lỗi |
|---|---|---|---|---|
| POST | `/api/payments/momo` | `CreateMomoPaymentRequest`: `orderId`, `createdBy` | `200` `MomoIntentResponse` | `400` `{message}`, `409` `{message}` |
| POST | `/api/payments/momo/notify` | `MomoNotificationRequest` | `200` `{ accepted, applied, paymentId, message }` | — (IPN sai chữ ký ⇒ `accepted = false`) |
| POST | `/api/payments/momo/mock/{paymentId:int}?success=true` | — | `200` `{ accepted, applied, paymentId, message }` | `400` `{message}`, `409` `{message}` (chỉ khi `Provider = Mock`) |
| POST | `/api/payments/{paymentId:int}/refund` | `RefundPaymentRequest`: `reason`, `createdBy` | `200` `PaymentResponse` | `400` `{message}`, `409` `{message}` |

`MomoIntentResponse`: `paymentId`, `orderId`, `amount`, `status`, `provider`, `requestId`, `redirectUrl`, `qrCodeUrl`, `isMock`.

- `POST /api/payments/momo/notify` là endpoint **của MoMo** (`[AllowAnonymous]`) — FE không gọi.
- `POST /api/payments/momo/mock/{paymentId}` là endpoint **giả lập cho môi trường test** — không tốn tiền thật.
- FE cần **poll** `GET /api/payments/order/{orderId}` để biết khi nào `status` = `Paid`.

### 9.2 Response thanh toán đổi field

| DTO | Thay đổi |
|---|---|
| `PaymentResponse` | **+** `provider`, `requestId`, `providerTransactionId`, `note`, `providerResponse`, `createdAt`, `refundedAt`; `paidAt` **→ nullable** |

> **Lưu ý MoMo:** backend **không** thêm provider/thanh toán tích hợp khác. `Payment.Status` nay có
> `Pending`/`Paid`/`Failed`/`Refunded`; `Payment.Provider` = `Direct` (tiền mặt/thẻ nội bộ) hoặc `MoMo`.

---

## Phụ lục 2026-10-21 (C-16) — hợp đồng lỗi thống nhất

Không thêm route, không thêm field, không đổi request. **Chỉ** thay status code của lỗi và dạng body lỗi.

| Tình huống | HTTP | Body |
|---|---|---|
| Không tìm thấy món / danh mục / bàn / đơn | **`404`** | `{ "message": "..." }` |
| Dữ liệu đầu vào sai | **`400`** | `{ "message": "..." }` |
| Xung đột nghiệp vụ | **`409`** | `{ "message": "..." }` |
| Lỗi hệ thống | **`500`** | `{ "message": "An unexpected error occurred. Please try again." }` |

### Thay đổi cụ thể

| Endpoint | Trước | Nay |
|---|---|---|
| `GET /api/foods/{foodId}/sizes` (món không tồn tại) | `404` (text) | `404` `{message}` |
| `POST /api/foods` (danh mục không tồn tại) | `400` (text) | **`404`** `{message}` |
| `POST /api/toppings` (trùng tên) | `400` (text) | **`409`** `{message}` |
| `PUT /api/toppings/{id}` (trùng tên) | `400` (text) | **`409`** `{message}` |
| `POST /api/foods/{foodId}/sizes` (trùng tên size) | `400` (text) | **`409`** `{message}` |
| `POST /api/foods/{foodId}/toppings` (món/topping không tồn tại) | `400` (text) | **`404`** `{message}` |
| `POST /api/orders` (bàn không tồn tại) | `400` (text) | **`404`** `{message}` |
| **mọi** lỗi `409`/`400` khác | **text thuần** | **JSON** `{ "message": "..." }` |
| lỗi hệ thống (SQL, mất kết nối) | `400`/`404` kèm message kỹ thuật | **`500`** message chung |

> **FE:** `api.js` đọc `payload.message` — nay **mọi** lỗi đều có message đọc được.
> Màn hình nào đang bắt `400` để hiện "không tìm thấy món/bàn" cần chuyển sang `404`.

---

## Phụ lục 2026-10-21 (C-17) — ledger kho có đơn vị + khoá đơn vị nguyên liệu

Không thêm route, không đổi request. **1 field mới** + **1 trường hợp `409` mới**.

### K.1 `GET /api/management/inventory-transactions` — thêm field `unit`

Mỗi dòng biến động kho nay có **`unit`** (string, có thể `null`):

```json
{ "page": 1, "pageSize": 20, "total": 3,
  "items": [ { "id": 55, "ingredientId": 4, "ingredientName": "Thịt bò", "type": "Sale",
               "quanlity": 2.0, "quanlityBefore": 20.0, "quanlityAfter": 18.0,
               "unit": "kg",
               "referenceType": "Order", "referenceId": 42, "note": null,
               "createdAt": "...", "createdBy": 1 } ] }
```

> ⚠️ Endpoint này trả `quanlity` (giữ lỗi gõ cũ) — khác với `POST /api/management/inventory/{ingredientId}/adjust` nhận `quantity`.

**FE nên làm:** hiện cột "Đơn vị" cạnh "Số lượng" trong bảng biến động kho. `unit` là **snapshot lúc ghi dòng**
⇒ đọc đúng cả với dòng cũ.

### K.2 `PUT /api/management/ingredients/{id}` — đổi đơn vị có thể bị `409`

Body **không đổi** (`name` + `unit` + `minimumStock`).

| Tình huống | HTTP |
|---|---|
| Gửi lại **đúng** đơn vị đang lưu | `204` (kể cả khác hoa/thường) |
| Nguyên liệu **chưa** dùng ở đâu → đổi đơn vị | `204` |
| Nguyên liệu **đã** có công thức / ledger / phiếu mua → đổi đơn vị | **`409`** |

```json
{ "message": "Cannot change unit from 'kg' to 'g' because this ingredient is already used in recipes, stock history. Create a new ingredient instead." }
```

**FE nên làm:** trong form sửa nguyên liệu, khi nhận `409` với message trên ⇒ **disable ô "Đơn vị"** và
báo người dùng *"Nguyên liệu đã dùng nên không đổi được đơn vị — hãy tạo nguyên liệu mới"*.
Không nên coi đây là lỗi chung chung.

---

## Phụ lục 2026-10-21 (C-18) — target khuyến mãi

Thêm **1 endpoint** + **2 field** trong response. Không đổi request cũ.

### L.1 `PUT /api/management/promotions/{id}/targets`

```json
{ "sizeIds": [1, 2], "categoryIds": [3] }
```

| Tình huống | HTTP |
|---|---|
| Gắn target thành công | `204` |
| Khuyến mãi không tồn tại | `404` `{ "message": "Promotion not found" }` |
| Size/danh mục không tồn tại | `404` `{ "message": "Food size not found: 999" }` / `"Category not found: 999"` |

**Semantics: thay toàn bộ** — body là danh sách target **sau khi sửa**, không phải phần thêm.
Gửi `{"sizeIds": [], "categoryIds": []}` ⇒ **bỏ giới hạn** ⇒ khuyến mãi áp cho toàn bộ hoá đơn.
Gửi trùng id trong mảng ⇒ server tự bỏ trùng.

### L.2 Response khuyến mãi có thêm 2 field

`GET /api/management/promotions` và `GET /api/management/promotions/{id}`:

```json
{ "id": 3, "name": "Giảm giá món chính", "discountType": "Percent", "discountValue": 10,
  "sizeIds": [1, 2], "categoryIds": [3], "isActive": true, ... }
```

`sizeIds`/`categoryIds` rỗng ⇒ khuyến mãi áp cho toàn bộ hoá đơn.

> **Lưu ý:** thay đổi target **không** tính lại các đơn đang mở đã áp khuyến mãi (số tiền giảm
> đã ghi trên đơn giữ nguyên). Đơn sửa món sẽ tính lại theo target mới.

---

## Phụ lục 2026-10-21 (C-19) — mã giao dịch + phiên bản công thức

### M.1 Mã giao dịch thanh toán

| Tình huống | HTTP | Ghi chú |
|---|---|---|
| `POST /api/payments` **không** gửi `transactionCode` | `200` | Server **tự sinh** mã `PAY{yyyyMMddHHmmss}-{8 hex}` ⇒ `response.transactionCode` có giá trị |
| `POST /api/payments` gửi mã | `200` | Giữ nguyên mã (bỏ khoảng trắng thừa) |
| `POST /api/payments` gửi mã **đã tồn tại** | **`409`** | `{ "message": "Transaction code '...' already exists" }` |
| `POST /api/payments/momo` | `200` | Intent cũng có mã riêng (mã MoMo nằm ở `providerTransactionId`) |

> ⚠️ Trước đây mã rỗng ⇒ nhiều dòng trùng nhau. Nay **mọi** giao dịch đều có mã duy nhất.

### M.2 Sửa công thức — `recipeId` đổi

`PUT /api/management/recipes/{id}` — route **không đổi**, nhưng hành vi đổi:

| | Trước | Nay |
|---|---|---|
| `recipeId` | không đổi | **đổi** — bản cũ chuyển `isActive = false`, bản mới là bản đang chạy |
| Version | lấy đúng giá trị gửi lên | gửi **rỗng** hoặc **đúng bằng** version đang chạy ⇒ server **tự tăng** (`1` → `2`) |
| Sửa lại bản đã bị thay thế | sửa tiếp bản cũ | **`409`** `{ "message": "This recipe version is no longer active; ..." }` |

**FE:** sau khi lưu công thức **phải gọi lại** `GET /api/management/recipes` (id đã đổi).
Form hiện tại đã gọi lại danh sách sau khi lưu ⇒ **không cần sửa gì**.

---

## Phụ lục 2026-10-21 (Đợt 3) — hoá đơn theo bàn, ghi chú hoá đơn, tồn kho thấp, bảng bếp

### N.1 `GET /api/orders/table/{tableId}/bill` — hoá đơn gộp theo bàn

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

| Tình huống | HTTP |
|---|---|
| Bàn có đơn đang mở | `200` |
| Bàn không có đơn đang mở / bàn không tồn tại | `404` |

**Luật:**
- Gộp mọi đơn **đang mở** (không `Paid`, không `Cancelled`); dòng món đã huỷ không lên hoá đơn.
- `items` đã gộp: cùng món + cùng đơn giá + cùng ghi chú ⇒ 1 dòng.
- `notes` = ghi chú của các đơn được gộp.
- **Thanh toán vẫn làm từng đơn** (`POST /api/payments` nhận `orderId`) — dùng `orderIds` để lần lượt.

### N.2 `PATCH /api/orders/{id}/note` — ghi chú cho cả hoá đơn

```json
{ "note": "Xuất hoá đơn công ty ABC", "changedBy": 0 }
```

| Tình huống | HTTP |
|---|---|
| Ghi/xoá ghi chú | `204` |
| Đơn không tồn tại | `404` |

Gửi `note` rỗng ⇒ **xoá** ghi chú. `changedBy` = 0 ⇒ lấy actor từ token, không có token thì lùi về
**người tạo đơn**.

**Response đơn giờ có `note`:** `GET /api/orders/{id}`, `GET /api/orders` (mỗi dòng), `POST /api/orders`.

### N.3 `GET /api/management/inventory/low-stock` — tồn kho thấp

```json
[ { "ingredientId": 3, "ingredientName": "Cà phê", "unit": "kg",
    "quanlity": 0.2, "minimumStock": 1, "shortage": 0.8, "isOutOfStock": false } ]
```

Chỉ gồm nguyên liệu đang hoạt động có `tồn ≤ mức tối thiểu`; sắp theo `shortage` giảm dần.
⚠️ Dùng `quanlity` (giữ lỗi gõ cũ) — giống `GET /api/management/inventory`.

> FE **không cần** tự ghép `GET /management/inventory` + `GET /management/ingredients` nữa.

### N.4 `GET /api/management/kitchen/orders` — thêm `?since=` và phân trang

Response **vẫn là mảng** (không đổi shape). Thêm query param:

| Query | Ý nghĩa |
|---|---|
| `status=open` | chỉ phiếu đang mở (đã có) |
| `since` (ISO) | chỉ trả phiếu **có thay đổi** kể từ mốc: gửi mới / bắt đầu nấu / hoàn thành |
| `page`, `pageSize` | phân trang tuỳ chọn; không truyền ⇒ trả toàn bộ (hành vi cũ) |

**Mẫu dùng đúng khi bếp poll 3–5s:**
1. **Lần đầu**: gọi **không** kèm `since` (lấy hết phiếu).
2. **Các lần sau**: kèm `since` = mốc thời gian của lần trước ⇒ chỉ nhận phần thay đổi.

> ⚠️ Nếu lần đầu mà kèm `since` thì các phiếu cũ chưa xử lý sẽ **không** hiện.
> `pageSize` sai (vd 0) ⇒ `400`.

## Phụ lục 2026-10-21 (C-20) - tăng số lượng món: tách dòng `Pending` mới

> Luật (chủ dự án chốt): POS tăng SL mà món **đã vào nấu / đã ra đĩa** ⇒ **tách dòng `Pending` mới**,
> nằm **ngay dưới** dòng cũ, **cùng phiếu bếp**. Món còn dòng `Pending` ⇒ **gộp chung**, không tách.
> Chi tiết: `docs/architecture/kitchen-stock-rule-2026-10-21.md` §2.4.1.

### O.1 Màn bếp sẽ thấy 2 dòng cho 1 món

`GET /api/management/kitchen/orders/{id}/items` — **không** đổi route, **không** đổi shape.

```json
// bếp đang nấu 2 phần gà rán, POS tăng thêm 3
[ { "id": 25, "orderItemId": 27, "quanlity": 2, "quantity": 2, "status": "Preparing" },
  { "id": 26, "orderItemId": 28, "quanlity": 3, "quantity": 3, "status": "Pending"  } ]
```

- Dòng mới **luôn nằm dưới** (API đã `orderby Id`) ⇒ FE vẽ theo thứ tự trả về là đúng.
- **Trước đây** dòng bị gộp thành 1 (`5 [Preparing]`) ⇒ bếp tưởng phải nấu 5.
- Phiếu bếp **không đổi** (vẫn 1 phiếu) nếu phiếu còn mở; nếu phiếu đã `Ready`/đóng ⇒ tạo phiếu mới (như cũ).

### O.2 Màn POS (chi tiết đơn) sẽ thấy 2 dòng cho 1 món

`GET /api/orders/{id}` — `items` có **2 dòng** cho 1 môn đã tách:

```json
{ "id": 27, "quantity": 2, "status": "Preparing", "orderItemGroupId": "…" },
{ "id": 28, "quantity": 3, "status": "Pending",   "orderItemGroupId": "…" }
```

⇒ Nếu muốn hiển thị 1 dòng tổng trên hoá đơn, **gộp theo `orderItemGroupId`** (cùng món + cùng giá).
Nếu muốn giống màn bếp thì hiển thị từng dòng.

### O.3 Luật "khoá" của món `Preparing` (đã rõ)

- POS **KHÔNG** được **huỷ** hay **giảm SL** món `Preparing` (chỉ Chef/Admin) — **giữ nguyên**.
- POS **ĐƯỢC** **tăng** SL món `Preparing` ⇒ phần mới thành dòng `Pending` riêng (mục O.1).
- Lợi ích: sau này huỷ/giảm thì **phần mới còn hoàn kho được**, chỉ phần đang nấu mới thành hao hụt.

> Nguồn: `docs/architecture/endpoint-baseline.md` mục "C-20" · `docs/architecture/kitchen-stock-rule-2026-10-21.md` §2.4.1

## Phụ lục 2026-10-21 (C-21) - sửa 6 lỗi làm báo cáo sai

> Đợt sửa **trước** khi làm thêm báo cáo vận hành. Chi tiết: `docs/architecture/endpoint-baseline.md` mục "C-21".

### P.1 Điều chỉnh tồn kho: `note` BẮT BUỘC

`POST /api/management/inventory/{ingredientId}/adjust` — thiếu `note` (hoặc rỗng) ⇒ **`400`**
`{ "message": "Note (reason) is required for a manual stock adjustment" }`.

Dòng ledger giờ có `referenceType: "ManualAdjustment"`, `referenceId: null`, `note` = lý do.

### P.2 Danh sách tồn kho thấp: có `stockStatus` + gồm cả nguyên liệu hư

`GET /api/management/inventory/low-stock`:
- Response **+ `stockStatus`** (`Available` / `OutOfStock` / `Spoiled`).
- **Gồm cả** nguyên liệu `OutOfStock`/`Spoiled` dù tồn vẫn còn nhiều (trước chỉ lọc `tồn ≤ mức tối thiểu`).
  ⇒ **FE lọc hiển thị theo `stockStatus`**, đừng coi mọi dòng là "sắp hết".

### P.3 Bảng bếp `?since=` bắt được cả đổi trạng thái MÓN

`GET /api/management/kitchen/orders?since=` trả về **nhiều hơn** trước: giờ lọc cả lịch sử
trạng thái món, không chỉ 3 mốc của phiếu (Sent/Started/Completed).
Lý do: món `Ready` trong khi món khác còn `Preparing` ⇒ mốc phiếu không đổi ⇒ trước đây màn bếp
**không thấy** món vừa xong.

### P.4 Báo cáo doanh thu: lịch sử đơn có `reasonCode` đầy đủ

`GET /api/orders/{id}/history` — dòng `→ Cancelled` giờ có `reasonCode`
(`CustomerChange` / `OutOfStock` / `PhysicalShortage` / `KitchenError`) kể cả khi đơn bị huỷ
**dần từng món** hoặc bếp huỷ món cuối. Trước đây `reasonCode` chỉ có ở cấp **món**,
lịch sử **đơn** bị null ⇒ báo cáo "đơn huỷ vì hết hàng" bỏ sót.