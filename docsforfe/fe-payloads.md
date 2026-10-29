# Body & Response mẫu cho FRONTEND

**Cập nhật:** 2026-10-21 · Bám theo DTO thật trong `RestaurantFoodManagerSystem/Application/DTO`.
Quy ước: mọi lỗi trả `4xx` với body `{ "message": "..." }`.

> ⚠️ **Đặc biệt:** `createdBy` / `changedBy` / `receivedBy` là **ID người dùng** và hầu hết DTO yêu cầu `>= 1`.
> Khi bật `Auth:Enforce=true`, BE lấy actor từ token; các field này chỉ là fallback cho lúc chưa bật enforce.
> FE nên gửi userId của người đang đăng nhập (lấy từ response login `data.userId`).

---

## 1. Đăng nhập

### `POST /api/auth/login`
```json
{ "userName": "admin", "password": "••••••" }
```
Response `200`:
```json
{
  "message": "Login successful",
  "data": {
    "userId": 1,
    "userName": "admin",
    "email": "admin@local",
    "fullName": "Quản trị",
    "roles": ["Admin"],
    "token": "eyJhbGciOiJIUzI1NiIs..."
  }
}
```
FE: lưu `data.token` + `data.userId` + `data.roles` vào `localStorage`, gắn `Authorization: Bearer <token>` cho mọi request sau.

### `POST /api/auth/change-password`
```json
{ "currentPassword": "old", "newPassword": "newpass", "confirmNewPassword": "newpass" }
```

---

## 2. Đơn

### `POST /api/orders` — tạo đơn (có Idempotency-Key)
Header: `Idempotency-Key: <chuỗi <=128 ký tự>` (tuỳ chọn nhưng **nên** gửi để bấm 2 lần không tạo 2 đơn).
```json
{
  "tableId": 5,
  "createdBy": 1,
  "items": [
    {
      "sizeId": 12,
      "quantity": 2,
      "note": "ít cay",
      "toppings": [ { "toppingId": 3, "quantity": 1 } ]
    }
  ]
}
```
Response `200` (`OrderCreatedResponse`):
```json
{ "orderId": 42, "tableId": 5, "status": "SentToKitchen",
  "subtotal": 165.00, "discountAmount": 0, "taxAmount": 0, "totalAmount": 165.00,
  "createdBy": 1, "createdAt": "2026-10-21T09:00:00Z", "updatedAt": "2026-10-21T09:00:00Z" }
```
Lỗi thường gặp: `409 Table already has an open order` · `409 Insufficient ingredient: …` · `404 Table not found`.

### `POST /api/orders/{orderId}/items` — **gọi thêm**
```json
{
  "changedBy": 1,
  "items": [ { "sizeId": 12, "quantity": 1, "toppings": [] } ]
}
```
⇒ BE **luôn tạo vòng bếp mới** (ticket mới), không đụng món cũ; đơn `Completed` được mở lại; bàn vẫn `Occupied`.
Response: `OrderDetailResponse` (xem dưới).

### `PATCH /api/orders/{orderId}/items/{itemId}/quantity` — **đổi số lượng**
```json
{ "quantity": 2, "changedBy": 1, "reason": "Khách giảm 1 phần", "reasonCode": "CustomerChange", "allowWastage": false }
```
- Gửi **đúng một** trong `quantity` (số lượng đích) hoặc `delta` (lệch ±). Gửi cả hai → `400`.
- `quantity: 0` ⇒ **bắt buộc** có `reason` (không có → `400`).
- Giảm xuống dưới phần bếp đã nấu ⇒ cần `allowWastage: true` **và** vai trò Admin (không phải ⇒ `403`).
- `reasonCode`: `CustomerChange` (mặc định) = hoàn kho phần chưa nấu; `OutOfStock`/`PhysicalShortage`/`KitchenError` = ghi hao hụt, không hoàn kho.

### `POST /api/orders/{orderId}/items/{itemId}/cancel` — **huỷ 1 món**
```json
{ "reason": "Khách đổi ý", "reasonCode": "CustomerChange", "changedBy": 1 }
```
- `reason` **bắt buộc**, ≤500 ký tự.
- `reasonCode` quyết định hoàn kho hay ghi hao hụt (bảng dưới).
- Response `200`: `OrderDetailResponse` (đơn đã tính lại tiền).

| `reasonCode` | Món còn `Pending` | Món đã `Preparing`/`Ready`/`Completed` |
|---|---|---|
| `CustomerChange` (mặc định) | **Hoàn kho** phần chưa nấu | Không hoàn (đã tiêu hao) |
| `OutOfStock` / `PhysicalShortage` / `KitchenError` | Ghi `Wastage`, **không** hoàn kho | Không hoàn |

Lỗi: `403` (role không được huỷ ở trạng thái này) · `409` (đơn `Paid`/`Cancelled`, món đã huỷ) · `400` (thiếu lý do).

### `PATCH /api/orders/{orderId}/promotion/{promotionId}` — áp khuyến mãi
Body rỗng `{}`. BE tự kiểm tra: khuyến mãi còn hạn & đang bật, `minimumOrderAmount`, có targeting theo món/nhóm không, `maxDiscountAmount`.
Lỗi: `404` (đơn không có), `409`/`false` khi không đủ điều kiện.

### `PATCH /api/orders/{orderId}/status` — đổi trạng thái đơn (VD huỷ đơn)
```json
{ "status": "Cancelled", "changedBy": 1, "note": "Khách hủy cả bàn", "reasonCode": "CustomerChange" }
```
`status` cho phép: `Pending`, `SentToKitchen`, `Preparing`, `Ready`, `Completed`, `Cancelled` (không có `Paid`).

### `GET /api/orders/{orderId}` — chi tiết đơn
```json
{
  "orderId": 42, "tableId": 5, "status": "SentToKitchen",
  "subtotal": 165.00, "discountAmount": 0, "taxAmount": 0, "totalAmount": 165.00,
  "createdBy": 1, "createdAt": "...", "updatedAt": "...",
  "items": [
    {
      "id": 88, "sizeId": 12, "foodName": "Phở bò", "sizeName": "L",
      "quantity": 2, "unitPrice": 55.00, "note": "ít cay", "status": "Pending",
      "orderItemGroupId": "0f3c…", "cancelReason": null, "cancelledAt": null, "cancelledBy": null,
      "toppings": [ { "id": 5, "toppingId": 3, "toppingName": "Trứng", "quantity": 1, "unitPrice": 5.00 } ]
    }
  ]
}
```
Lưu ý: `unitPrice` **đã gồm topping** ⇒ `unitPrice × quantity` là thành tiền của dòng, **không** cộng thêm `toppings[]`.
`orderItemGroupId` là nhóm dòng món gọi chung (dùng để biết dòng nào được tách ra từ dòng nào) — có thể `null` với dữ liệu cũ.

### `GET /api/orders/{orderId}/item-history` — audit từng dòng món (C-10)
```json
[ { "id": 12, "orderId": 42, "orderItemId": 88, "kitchenOrderItemId": 31,
    "oldStatus": "Pending", "newStatus": "Cancelled",
    "changedBy": 1, "changedByName": "Quản trị",
    "reason": "Khách đổi ý", "reasonCode": "CustomerChange",
    "createdAt": "2026-10-21T09:12:00Z" } ]
```
Dùng để hiển thị "món này bị huỷ lúc 16:12 vì …".

### `GET /api/orders/{orderId}/history` — audit trạng thái **đơn**
```json
[ { "id": 3, "orderId": 42, "oldStatus": "Preparing", "newStatus": "Cancelled",
    "changedBy": 1, "changedByName": "Quản trị", "note": "Khách hủy cả bàn",
    "createdAt": "2026-10-21T09:20:00Z" } ]
```

---

## 3. Bếp

### `GET /api/management/kitchen/orders`
```json
[ { "kitchenOrderId": 7, "orderId": 42, "status": "Pending",
    "sentAt": "...", "startedAt": null, "completedAt": null, "priority": 0 } ]
```

### `GET /api/management/kitchen/orders/{kitchenOrderId}/items`
```json
[ { "id": 31, "kitchenOrderId": 7, "orderItemId": 88,
    "quanlity": 2, "quantity": 2, "status": "Pending", "note": null,
    "foodName": "Phở bò", "sizeName": "L" } ]
```
(`quantity` là alias của `quanlity` — đọc `quantity`.)

### `PATCH /api/management/kitchen/items/{id}/status`
```json
{ "status": "Ready", "reason": null, "reasonCode": null }
```
- `status` hợp lệ ở **cấp món**: `Pending`, `Preparing`, `Ready`, `Completed`, `Cancelled` — **`Accepted` không hợp lệ** (C-11) → `400 Invalid kitchen item status`.
- `status: "Cancelled"` ⇒ **bắt buộc** nên có `reason`; `reasonCode` quyết định hoàn kho / hao hụt như bảng ở mục 2.
- Huỷ món `Ready`/`Completed` cần vai trò Admin (Chef bị `403`).
- Huỷ món của đơn đã `Paid` ⇒ `409`.

### `PATCH /api/management/kitchen/items/{id}/prepared-quantity`
```json
{ "preparedQuantity": 1, "changedBy": 1, "reason": "Mới nấu được 1 phần" }
```
- `preparedQuantity` ≥ 1 và ≤ số lượng món.
- Nhỏ hơn số lượng món ⇒ BE **tách dòng**: phần đã nấu (`Completed`) + phần còn lại; **tổng tiền và tồn kho không đổi**.

### `POST /api/management/kitchen/items/{id}/shortage`
```json
{ "preparedQuantity": 1, "reasonCode": "PhysicalShortage", "changedBy": 1, "reason": "Thiếu 1kg thịt" }
```
- `reasonCode` chỉ nhận `OutOfStock` hoặc `PhysicalShortage` (sai → `400`).
- `preparedQuantity` < số lượng món; `0` = không nấu được phần nào.
- Phần thiếu bị huỷ + ghi `Wastage` (không hoàn kho ảo) ⇒ bếp **không bị chặn** vì tồn kho hệ thống.

### `POST /api/management/kitchen/items/{id}/reopen` — mở lại món đã xong (C-06)
```json
{ "reason": "Khách yêu cầu làm lại" }
```
- Chỉ mở được món `Ready`/`Completed`.
- Điều kiện: đơn chưa `Paid`/`Cancelled`, phiếu chưa `Completed`/`Cancelled` ⇒ nếu không thì `409`.
- Kết quả: món + phiếu + đơn về `Preparing`; bàn vẫn `Occupied`.

### `PATCH /api/management/kitchen/orders/{kitchenOrderId}/status`
```json
{ "status": "Preparing", "reason": null, "reasonCode": null }
```
`status`: `Pending`, `Accepted`, `Preparing`, `Ready`, `Completed`, `Cancelled`.
`Cancelled` ⇒ huỷ cả đơn nếu đơn chưa `Paid`; sau `Paid` thì bị chặn (C-07).

---

## 4. Thanh toán (hiện tại — chưa có provider)

### `POST /api/payments`
```json
{ "orderId": 42, "paymentMethod": "Cash", "transactionCode": "", "createdBy": 1 }
```
`paymentMethod` ∈ `Cash`, `Card`, `Bank`, `EWallet`, `Transfer` (không phân biệt hoa thường, BE trả về tên chuẩn).
Lỗi: `409 Order is not ready for payment` (đơn chưa `Ready`/`Completed` hoặc còn món chưa xong) · `409 Order is already paid` · `409 Order has no payable items` · `409 Order total must be greater than zero` · `409 Transaction code '...' already exists` (mã giao dịch đã tồn tại).

ℹ️ **C-19:** `transactionCode` **không** gửi (hoặc rỗng) ⇒ **server tự sinh mã** `PAY{yyyyMMddHHmmss}-{8 hex}`.
Mọi giao dịch đều có mã **duy nhất** (index unique ở tầng DB).

Response `200` (`PaymentResponse`):
```json
{ "paymentId": 9, "orderId": 42, "amount": 165.00, "paymentMethod": "Cash",
  "status": "Paid", "transactionCode": "PAY20261021153012-a1b2c3d4", "paidAt": "...", "createdBy": 1 }
```

### `GET /api/payments?page=1&pageSize=20&method=cash&from=&to=&orderId=&status=`
```json
{ "page": 1, "pageSize": 20, "total": 12, "items": [ { "paymentId": 9, "…": "…" } ] }
```

### `GET /api/payments/order/{orderId}`
`[ { "paymentId": 9, "orderId": 42, "amount": 165.00, "paymentMethod": "Cash", "status": "Paid", "transactionCode": "", "paidAt": "...", "createdBy": 1 } ]`

---

## 5. Kho

### `POST /api/management/inventory/{ingredientId}/adjust`
```json
{ "quantity": -2.5, "createdBy": 1, "note": "Kiểm kê hao mất" }
```
- `quantity` âm = giảm, dương = tăng; không được để tồn âm (`409`); `0` → `400`.
- ⚠️ **`note` BẮT BUỘC** (C-21) — là lý do điều chỉnh. Thiếu / rỗng ⇒ **`400`**
  `{ "message": "Note (reason) is required for a manual stock adjustment" }`.
  Lý do: ledger phải trả lời được "tồn giảm 3kg vì lý do gì" (báo cáo biến động kho).
- Dòng ledger ghi thêm `referenceType: "ManualAdjustment"`, `referenceId: null`.

### `GET /api/management/inventory/low-stock`
```json
[ { "ingredientId": 3, "ingredientName": "Cà phê", "unit": "kg",
    "quanlity": 0.2, "minimumStock": 1, "shortage": 0.8, "isOutOfStock": false,
    "stockStatus": "Available" } ]
```
- ⚠️ C-21: response **có thêm `stockStatus`** (`Available` / `OutOfStock` / `Spoiled`).
- ⚠️ C-21: danh sách giờ **gồm cả nguyên liệu bị đánh dấu `OutOfStock`/`Spoiled`**
  **dù tồn vẫn còn nhiều** (trước đây chỉ lọc `tồn ≤ mức tối thiểu` ⇒ nguyên liệu bếp
  đánh dấu hư mà còn tồn nhiều thì **không hiện ở đâu cả**).
  ⇒ **FE lọc hiển thị theo `stockStatus`**, đừng coi mọi dòng là "sắp hết".

### `GET /api/management/inventory-transactions?ingredientId=&type=&referenceType=&referenceId=&from=&to=&page=&pageSize=`
```json
{ "page": 1, "pageSize": 20, "total": 3,
  "items": [ { "id": 55, "ingredientId": 4, "ingredientName": "Thịt bò", "type": "Sale",
               "quanlity": 2.0, "quanlityBefore": 20.0, "quanlityAfter": 18.0,
               "unit": "kg",
               "referenceType": "Order", "referenceId": 42, "note": null,
               "createdAt": "...", "createdBy": 1 } ] }
```
`type` ∈ `Sale`, `AdjustmentIn`, `AdjustmentOut`, `Wastage`, `Purchase`.
⚠️ `referenceId` **bắt buộc** kèm `referenceType` (`Order` | `PurchaseOrder`) — thiếu → `400`.
⚠️ Endpoint này dùng `quanlity` (giữ lỗi gõ cũ) — khác với `POST /api/management/inventory/{ingredientId}/adjust` nhận `quantity`.
ℹ️ `unit` = đơn vị tính **snapshot lúc ghi dòng** (C-17) — có thể `null` với dòng rất cũ.

### `POST /api/management/purchase-orders`
```json
{ "supplierId": 2, "createdBy": 1,
  "items": [ { "ingredientId": 4, "quantity": 10.0, "unitPrice": 85000.0 } ] }
```
### `POST /api/management/purchase-orders/{id}/receive`
```json
{ "receivedBy": 1 }
```
⇒ cộng kho + ghi ledger `Purchase`. Nhập 2 lần → lần 2 trả `404` (đã nhận).
### `POST /api/management/purchase-orders/{id}/cancel`
Body rỗng `{}`. Phiếu đã nhận ⇒ `409`.

---

## 6. Báo cáo

### `GET /api/reports/dashboard`
```json
{ "generatedAt": "...", "timeZone": "Asia/Ho_Chi_Minh", "businessDate": "2026-10-21",
  "from": "...", "to": "...",
  "revenueToday": 4250000.0, "paidOrderCountToday": 31, "openOrderCount": 4, "lowStockCount": 2,
  "tables": { "total": 20, "available": 12, "occupied": 6, "maintenance": 2 },
  "kitchen": { "open": 3, "pending": 1, "preparing": 1, "ready": 1 },
  "recentOrders": [ { "orderId": 42, "tableId": 5, "tableNumber": "B05",
                      "status": "SentToKitchen", "totalAmount": 165.0, "createdAt": "..." } ] }
```

### `GET /api/reports/revenue?period=day&date=2026-10-21`
`period` ∈ `day` (mặc định), `week`, `month`.
```json
{ "period": "day", "timeZone": "Asia/Ho_Chi_Minh", "from": "...", "to": "...",
  "totalRevenue": 4250000.0, "orderCount": 31, "averagePerOrder": 137096.77,
  "groups": [ { "label": "08:00", "from": "...", "to": "...", "revenue": 320000.0, "orderCount": 3 } ] }
```

### `GET /api/reports/revenue/export.csv?period=day&date=2026-10-21`
File CSV (UTF-8 **có BOM**), header cố định:
```
period,groupStartVn,groupEndVn,revenue,orderCount
day,2026-10-21 00:00,2026-10-21 01:00,320000,3
…
TOTAL,2026-10-21 00:00,2026-10-22 00:00,4250000,31
```
FE tải bằng `blob` (xem F0-3).

---

## 7. Nhân viên & vai trò

### `POST /api/users`
```json
{ "userName": "waiter01", "email": "waiter01@local", "password": "••••••",
  "confirmPassword": "••••••", "fullName": "Nguyễn Văn A", "phone": "0900000000",
  "roleName": "Waiter" }
```
`roleName` ∈ `Admin`, `WarehouseManager`, `Chef`, `Cashier`, `Waiter` (gán ngay khi tạo).

### `PUT /api/users/{id}` — chỉ sửa `fullName`, `email`, `phone` (mass-assignment bị chặn).
### `PATCH /api/users/{id}/active` — body `{ "isActive": false }` (khoá tài khoản).
### `POST /api/users/{id}/reset-password` — `{ "newPassword": "••••••", "confirmPassword": "••••••" }`.
### `POST /api/users/{userId}/roles` — `{ "roleName": "Chef" }`.
### `DELETE /api/users/{userId}/roles/{roleName}` — gỡ role (không gỡ được Admin cuối → `409`).
### `GET /api/roles` — 5 vai trò (chỉ đọc).

---

## 8. Danh mục (tham khảo)
| Endpoint | Body tối thiểu |
|---|---|
| `POST /api/foods` | `{ categoryId, foodName, foodDescription?, foodImageUrl?, foodPrice, isAvailable?, isActive? }` |
| `POST /api/foods/{foodId}/sizes` | `{ sizeName, sizePrice, isDefault? }` |
| `POST /api/foods/{foodId}/toppings` | `{ toppingId }` (gán topping cho món) |
| `POST /api/toppings` | `{ toppingName, toppingPrice, isAvailable?, isActive? }` |
| `POST /api/categories` | `{ categoryName, categoryDescription?, displayOrder? }` |
| `POST /api/management/recipes` | `{ sizeId, name, version?, ingredients: [ { ingredientId, quantity } ] }` |
| `PUT /api/management/recipes/{id}` | `{ sizeId, name, version?, ingredients: [...] }` — **tạo phiên bản mới**, `recipeId` **đổi**; gửi version rỗng/trùng ⇒ server tự tăng; sửa bản đã bị thay thế ⇒ `409` (C-19) |
| `PUT /api/management/promotions/{id}/targets` | `{ sizeIds: [], categoryIds: [] }` — **thay toàn bộ** target; mảng rỗng = áp cho toàn bộ hoá đơn (C-18) |
| `PATCH /api/orders/{id}/note` | `{ note, changedBy? }` — ghi chú cho **cả hoá đơn**; `note` rỗng = xoá (Đợt 3) |
| `POST /api/management/ingredients` | `{ name, unit, minimumStock? }` |
| `PUT /api/management/ingredients/{id}` | `{ name, unit, minimumStock? }` — đổi `unit` của nguyên liệu **đã dùng** ⇒ `409` (C-17) |
| `POST /api/management/suppliers` | `{ name, phone, email, address }` |
| `POST /api/management/promotions` | `{ name, description?, discountType: "Percent"\|"Fixed", discountValue, startAt, endAt, minimumOrderAmount, maxDiscountAmount }` |
| `POST /api/restaurant-tables` | `{ tableNumber, capacity, areaId? }` |
| `POST /api/table-areas` | `{ areaName, description?, displayOrder? }` |

---

## 9. Body lỗi chuẩn của BE
```json
{ "message": "Only an administrator can reduce below prepared quantity" }
```
| Status | Khi nào |
|---|---|
| `400` | Sai dữ liệu (thiếu `reason`, sai enum, quá độ dài, `quantity` và `delta` cùng lúc…) |
| `401` | Chưa đăng nhập / token hết hạn / token sai |
| `403` | Không đủ vai trò (VD: Chef huỷ món `Ready`, giảm dưới phần đã nấu) |
| `404` | Không tìm thấy đơn/món/bàn/phiếu |
| `409` | Xung đột nghiệp vụ (đơn `Paid`, bếp đã bắt đầu, thiếu kho, đã nhận phiếu, Admin cuối…) |
| `410` | Route đã bị bỏ (`PUT /api/orders/{id}/items`) |
