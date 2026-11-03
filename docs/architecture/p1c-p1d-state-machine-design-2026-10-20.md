# P1c/P1d — Trạng thái đơn, món, phiếu bếp và hủy món

Cập nhật: **2026-10-20**  
Nhánh: **`hai`**  
Trạng thái: **ĐÃ TRIỂN KHAI** (backend + migration + test)  
Phạm vi: **backend only**, không sửa `frontend/`, `wwwroot/` hoặc file FE.

Tài liệu liên quan:

- `docs/architecture/progress-summary-2026-10-20.md`
- `docs/architecture/endpoint-baseline.md`
- `docs/architecture/backend-frontend-audit.md`

> Tài liệu này trước đây là bản đề xuất. Nay được cập nhật lại theo **code thực tế đã triển khai**,
> gồm cả các điểm khác với đề xuất ban đầu (ghi rõ ở mục 8).

---

## 0. Quyết định đã chốt và kết quả

| # | Quyết định | Kết quả đã triển khai |
|---|---|---|
| 1 | Migration lý do hủy + ledger theo món | ✅ Thêm `order_items.CancelReason/CancelledAt/CancelledBy`, `inventory_transactions.OrderItemId` |
| 2 | Hoàn kho món đã chế biến | Chỉ hoàn món `Pending`; `Preparing/Ready/Completed` không hoàn |
| 3 | Đơn đã `Paid` | Chặn hủy món/đơn; chưa có luồng refund |
| 4 | Tất cả món bị hủy | Đơn tự `Cancelled`, trả bàn; phiếu bếp `Cancelled` |
| 5 | API POS hủy món | ✅ `POST /api/orders/{orderId}/items/{itemId}/cancel` |
| 6 | Điều kiện thanh toán | Chỉ đơn `Ready`/`Completed` và mọi món active `Ready`/`Completed` |
| 7 | Discount khi hủy món | Reset `DiscountAmount = 0` để POS áp lại |
| 8 | Bếp hủy đơn/phiếu | Chef được hủy món `Pending`/`Preparing` và hủy đơn khi chưa có món `Ready`/`Completed` |

Điểm sửa so với đề xuất ban đầu theo yêu cầu chủ dự án:

- **Bếp (Chef) phải tự hủy được món đang nấu**, không phụ thuộc Admin.
- Admin chỉ bắt buộc khi món đã `Ready`/`Completed` hoặc đơn đã có món ra đĩa.

---

## 1. Ma trận trạng thái

### 1.1. Đơn hàng (`OrderStatuses`)

| Từ | Được chuyển sang |
|---|---|
| `Pending` | `SentToKitchen`, `Preparing`, `Ready`, `Cancelled` |
| `SentToKitchen` | `Preparing`, `Ready`, `Cancelled` |
| `Preparing` | `Ready`, `Completed`, `Cancelled` |
| `Ready` | `Completed`, `Cancelled` |
| `Completed` | `Cancelled` |
| `Paid` | — (trạng thái cuối) |
| `Cancelled` | — (trạng thái cuối) |

- Gửi lại đúng trạng thái hiện tại là no-op hợp lệ.
- `Paid` chỉ do `PaymentService` đặt, không nhận qua `PATCH /orders/{id}/status`.
- Đã chặn: `Ready → Pending`, `Pending → Completed`, `Preparing → SentToKitchen`, mọi chuyển khỏi `Paid`/`Cancelled` → `409`.

### 1.2. Món trong đơn và món trong phiếu bếp

| Từ | Được chuyển sang |
|---|---|
| `Pending` | `Preparing`, `Ready`, `Completed`, `Cancelled` |
| `Preparing` | `Ready`, `Completed`, `Cancelled` |
| `Ready` | `Completed`, `Cancelled` |
| `Completed` | — (đóng) |
| `Cancelled` | — (đóng) |

- Giữ `Pending → Completed` và `Pending → Ready` để tương thích UI bếp hiện tại.
- Không cho quay lui (`Ready → Preparing`, `Completed → Ready`, …).
- Món trong phiếu bếp không nhận `Accepted`.
- Cập nhật C-11 (2026-10-20): `Accepted` đã bị gỡ khỏi allow-list cấp món (`KitchenStatuses.AllowedItem`)
  và có CHECK constraint ở DB; `Accepted` chỉ còn ở cấp phiếu bếp. Món `Ready`/`Completed` chỉ quay lại
  `Preparing` qua command `POST /api/management/kitchen/items/{id}/reopen` (C-06) — ma trận ở trên không đổi.
- Ma trận trên áp dụng cho các route đổi trạng thái thường. Route hủy món là ngoại lệ có kiểm soát:
  Admin được hủy cả món `Ready`/`Completed`; Chef/Waiter bị chặn (xem mục 2).

### 1.3. Phiếu bếp (`KitchenStatuses`)

| Từ | Được chuyển sang |
|---|---|
| `Pending` | `Accepted`, `Preparing`, `Ready`, `Completed`, `Cancelled` |
| `Accepted` | `Preparing`, `Ready`, `Completed`, `Cancelled` |
| `Preparing` | `Ready`, `Completed`, `Cancelled` |
| `Ready` | `Completed`, `Cancelled` |
| `Completed` | — (trạng thái cuối) |
| `Cancelled` | — (trạng thái cuối) |

---

## 2. Phân quyền hủy (thực tế)

| Hành động | Waiter | Chef | Admin | Cashier |
|---|---|---|---|---|
| Hủy món `Pending` | ✅ | ✅ | ✅ | ❌ |
| Hủy món `Preparing` | ❌ | ✅ | ✅ | ❌ |
| Hủy món `Ready`/`Completed` | ❌ | ❌ | ✅ | ❌ |
| Hủy đơn khi chưa món nào `Ready`/`Completed` | ✅ | ✅ | ✅ | ❌ |
| Hủy đơn khi đã có món `Ready`/`Completed` | ❌ | ❌ | ✅ | ❌ |
| Hủy đơn đã `Paid` | ❌ | ❌ | ❌ | ❌ |

Quy ước triển khai:

- Policy controller:
  - `POST /api/orders/{orderId}/items/{itemId}/cancel` → `OrderCancellation` (Admin + Waiter + Chef).
  - `PATCH /api/orders/{id}/status` → `PosOperator` (Admin + Waiter).
  - `PATCH /api/management/kitchen/*` → `KitchenDesk` (Admin + Chef).
- Luật chi tiết theo trạng thái món nằm ở `OrderCancellationPolicy` (service).
- Nếu request không có role (test cũ hoặc `Auth:Enforce=false` không có principal), tầng service bỏ qua kiểm tra role và để policy controller quyết định.
- Vi phạm quyền → `403`.

---

## 3. P1c — tính lại phiếu bếp

`KitchenStatuses.Aggregate(current, itemStatuses)`:

```text
active = các món có Status != Cancelled

Nếu có item và active rỗng:
    phiếu = Cancelled          // sửa lỗi P1c
Ngược lại nếu active toàn Ready/Completed:
    phiếu = Ready
Ngược lại nếu có ít nhất một active Preparing:
    phiếu = Preparing
Ngược lại:
    giữ nguyên trạng thái hiện tại
```

- Món `Cancelled` bị loại khỏi phép gộp.
- Tất cả món bị hủy → phiếu `Cancelled`, set `CompletedAt`.
- Nếu đơn chưa `Paid` → đơn `Cancelled`, ghi `OrderStatusHistory`, trả bàn.
- Hủy 1 món khi còn món khác → **không** tự hủy đơn.

Khác với đề xuất cũ: trường hợp `Ready/Completed` xen kẽ `Pending` sẽ giữ trạng thái hiện tại
(không tự ép về `Preparing`). Đây là hành vi thực tế của aggregator sau triển khai.

---

## 4. Hủy món / hủy đơn và tồn kho

### 4.1. Quy tắc hoàn kho

| Trạng thái món khi hủy | Hoàn kho |
|---|---|
| `Pending` (cả order item và kitchen item) | Hoàn 100% phần đã ghi nhận cho món |
| `Preparing` | Không hoàn |
| `Ready` | Không hoàn |
| `Completed` | Không hoàn |
| `Cancelled` | Bỏ qua, không hoàn lần hai |

- Hủy cả đơn áp dụng cùng quy tắc cho **từng món active**; không còn hoàn toàn bộ kho.
- Hủy món đã `Cancelled` → `409`.
- Hủy đơn/món đã `Paid` → `409`, không đổi dữ liệu, không hoàn kho.
- Mọi thao tác hủy nằm trong transaction `Serializable`.

### 4.2. Ledger theo món

- Khi **tạo đơn**: ghi 1 giao dịch `Sale` cho mỗi cặp (dòng món × nguyên liệu), kèm `OrderItemId`.
- Khi **sửa đơn** (`PUT /orders/{id}/items`): ledger điều chỉnh theo chênh lệch ở cấp đơn
  (`OrderItemId = null`) — chấp nhận được vì đơn chỉ sửa được trước khi bếp bắt đầu.
- Khi **hủy món**:
  - đọc ledger có `OrderItemId = itemId`;
  - nếu đơn có ledger nhưng dòng món chưa được gắn (đơn đã sửa), ước tính từ công thức hiện tại;
  - luôn chặn bởi phần tiêu hao còn lại của cả đơn → không hoàn vượt;
  - ghi `AdjustmentIn` gắn đúng `OrderItemId`.
- Đơn legacy chưa từng trừ kho → không cộng tồn kho ảo.

### 4.3. Tính lại tiền khi hủy món

- `Subtotal = Σ(UnitPrice × Quanlity)` của các món còn active.
- `DiscountAmount = 0`.
- `TotalAmount = Subtotal + TaxAmount`.
- Dòng cuối cùng bị hủy → đơn `Cancelled` (không tính lại tiền).
- Hủy cả đơn → giữ nguyên `Subtotal/TotalAmount` lịch sử, đơn ở trạng thái `Cancelled`.

### 4.4. Trả bàn

- Chỉ trả bàn khi đơn `Paid` hoặc `Cancelled` và không còn đơn mở khác.
- Hủy 1 món → bàn vẫn `Occupied`.
- Hủy món cuối → đơn `Cancelled` → bàn `Available`.

---

## 5. Thanh toán

`PaymentService.CreateAsync` chặn khi:

- Đơn `Cancelled` → `409 "Cannot pay a cancelled order"`.
- Đơn đã có payment `Paid` → `409 "Order is already paid"`.
- Đơn **có dòng món** nhưng:
  - không còn món active → `409 "Order has no payable items"`;
  - còn món chưa `Ready`/`Completed` → `409 "Order is not ready for payment"`;
  - trạng thái đơn không phải `Ready`/`Completed` → `409 "Order is not ready for payment"`.
- Đơn tổng ≤ 0 → `409`.

Ngoại lệ tương thích: đơn **không có dòng món** (dữ liệu legacy/test) vẫn giữ hành vi cũ —
bỏ qua kiểm tra readiness. Đơn thật luôn có món.

Sau khi thanh toán: đơn `Paid`, bàn được refresh về `Available` nếu không còn đơn mở.

---

## 6. Schema và migration

Migration: **`20261020093304_AddOrderItemCancellationAndItemLedger`**

Thêm vào `order_items`:

- `CancelReason nvarchar(500) NULL`
- `CancelledAt datetime2 NULL`
- `CancelledBy int NULL` + FK `users(Id)` `ON DELETE RESTRICT` + index

Thêm vào `inventory_transactions`:

- `OrderItemId int NULL` + FK `order_items(Id)` `ON DELETE SET NULL` + index

Lý do `SET NULL`: `PUT /orders/{id}/items` xóa và tạo lại dòng món; lịch sử kho phải được giữ,
chỉ ngắt liên kết tới dòng món đã bị xóa.

> Migration **chưa apply** vào SQL Server thật tại thời điểm cập nhật tài liệu này.

---

## 7. API

### 7.1. Hủy một món (mới)

```http
POST /api/orders/{orderId}/items/{itemId}/cancel
Authorization: Policy OrderCancellation (Admin/Waiter/Chef)
Content-Type: application/json

{
  "reason": "Khách đổi món",
  "changedBy": 3
}
```

| Kết quả | Ý nghĩa |
|---|---|
| `200 OrderDetailResponse` | Hủy thành công, trả chi tiết đơn đã cập nhật |
| `400` | Thiếu lý do / lý do > 500 ký tự |
| `403` | Role không được hủy món ở trạng thái hiện tại |
| `404` | Không tìm thấy đơn hoặc món |
| `409` | Đơn đã `Paid`/`Cancelled`, món đã hủy, hoặc xung đột |

`reason` bắt buộc, tối đa 500 ký tự.

### 7.2. Hủy đơn qua POS (mở rộng)

```http
PATCH /api/orders/{id}/status

{
  "status": "Cancelled",
  "changedBy": 3,
  "note": "Khách hủy đơn"
}
```

- `note` **bắt buộc** khi `status = Cancelled`, tối đa 500 ký tự → thiếu `400`.
- Transition không hợp lệ → `409`.
- Không đủ quyền (đã có món `Ready`/`Completed`) → `403`.

### 7.3. Bếp (mở rộng body)

```http
PATCH /api/management/kitchen/orders/{id}/status
{ "status": "Cancelled", "reason": "Hết nguyên liệu" }

PATCH /api/management/kitchen/items/{id}/status
{ "status": "Cancelled", "reason": "Hết nguyên liệu" }
```

- `reason` là tùy chọn ở tầng API; nếu trống, món lưu `"Cancelled from kitchen"`.
- Hủy món `Preparing` bởi Chef → OK.
- Hủy món `Ready`/`Completed` bởi Chef → `403`; Admin → OK.
- Hủy ticket khi còn món `Pending`/`Preparing` → OK (hủy toàn bộ món active).
- Hủy ticket khi đã có món `Ready`/`Completed`:
  - Chef/Waiter → `403`;
  - Admin → OK.
- Đơn đã `Paid`: bếp vẫn có thể đóng ticket nhưng **không** ghi ngược trạng thái đơn/món;
  hủy món qua API hủy món vẫn bị chặn.
- Vi phạm transition/terminal → `409`.

### 7.4. Order detail

`GET /api/orders/{id}` trả thêm cho mỗi item:

- `cancelReason`
- `cancelledAt`
- `cancelledBy`

---

## 8. Khác biệt so với bản đề xuất ban đầu

| Điểm | Đề xuất cũ | Thực tế đã làm |
|---|---|---|
| Quyền hủy | Bếp không hủy đơn, Admin mới hủy sau khi xong | Chef hủy món `Pending`/`Preparing`; Chef/Waiter hủy đơn khi chưa có món `Ready` |
| Route hủy món | `PATCH .../cancel`, trả `204` | `POST .../cancel`, trả `200 OrderDetailResponse` |
| POS đặt `Preparing`/`Ready` | Cấm | Vẫn cho phép (giữ tương thích); chưa tách tuyệt đối POS/bếp |
| Món `Pending → Completed` | Cấm | Vẫn cho phép để tương thích UI bếp |
| Bếp hủy ticket khi còn món active | Cấm | Cho phép; hủy toàn bộ món active theo quy tắc hoàn kho |
| Ledger khi sửa đơn | Ghi theo từng món | Ghi cấp đơn; hủy món fallback ước tính từ công thức, chặn theo lượng còn lại |
| Hủy món một phần số lượng | Chưa quyết | Không hỗ trợ; hủy cả dòng |

---

## 9. File đã sửa / thêm

Domain:

- `Domain/Entity/Order/OrderItem.cs`
- `Domain/Entity/Recipe_Inventory/InventoryTransaction.cs`

Application:

- `Application/Common/OrderStatuses.cs`
- `Application/Common/KitchenStatuses.cs` (mới)
- `Application/Common/OrderCancellation.cs` (viết lại)
- `Application/Common/OrderCancellationPolicy.cs` (mới)
- `Application/Common/ICurrentUser.cs`
- `Application/Services/OrderService.cs`
- `Application/Services/KitchenService.cs`
- `Application/Services/PaymentService.cs`
- `Application/Features/Auth/AuthPolicies.cs`
- DTO: `OrderRequests.cs`, `OrderDetailResponse.cs`, `KitchenRequests.cs`, `ManagementRequests.cs`

Infrastructure / API:

- `Infrastructure/Persistence/AppDbContext.cs`
- `Infrastructure/Auth/HttpContextCurrentUser.cs`
- `Infrastructure/Auth/AuthorizationServiceCollectionExtensions.cs`
- `Controllers/OrdersController.cs`
- `Controllers/ManagementController.cs`
- `Migrations/20261020093304_AddOrderItemCancellationAndItemLedger.cs`
- `Migrations/AppDbContextModelSnapshot.cs`

Test:

- `RestaurantFoodManagerSystem.Tests/OrderCancellationTests.cs` (mới, 13 test)
- `RestaurantFoodManagerSystem.Tests/TestBase.cs` (role cho `TestCurrentUser`)
- `RestaurantFoodManagerSystem.Tests/BatchA2IntegrityTests.cs` (bổ sung `note` khi hủy đơn)

Không sửa `frontend/`.

---

## 10. Kiểm chứng

```powershell
dotnet build RestaurantFoodManagerSystem.sln --nologo
# 0 Warning(s), 0 Error(s)

dotnet test RestaurantFoodManagerSystem.sln --nologo
# Passed: 466/466
```

Test mới bao phủ:

- Chặn `Ready → Pending`, `Pending → Completed`.
- Hủy đơn thiếu lý do → `400`.
- Hủy 1 món: chỉ hoàn kho món đó, giảm tiền đơn đúng.
- Hủy món `Preparing`: không hoàn kho; món cuối → đơn `Cancelled`, bàn `Available`.
- Waiter không hủy được món `Preparing`; Chef được.
- Chef không hủy được món `Ready`; Admin được.
- Chef hủy đơn trước khi có món `Ready`; bị chặn sau khi có món `Ready`.
- Tất cả món bị hủy từ bếp → phiếu `Cancelled`, đơn `Cancelled`, hoàn đủ kho.
- Hủy cả đơn có 1 món `Pending` + 1 món `Preparing` → chỉ hoàn món `Pending`.
- Thanh toán đơn chưa xong → `409`; tất cả món `Ready` → `Paid`.

---

## 11. Việc còn mở (không thuộc phạm vi đã làm)

- Chưa tách tuyệt đối POS/bếp: `PATCH /orders/{id}/status` vẫn cho Admin/Waiter đặt `Preparing`/`Ready`.
- Chưa có module refund cho đơn đã `Paid`.
- Chưa hỗ trợ hủy một phần số lượng của dòng món.
- Chưa ghi ledger theo món cho luồng `PUT /orders/{id}/items`.
- Topping chưa có công thức nguyên liệu nên không sinh tiêu hao kho.
- Migration mới chưa apply vào SQL Server thật.
- `Auth:Enforce` vẫn đang `false`; frontend chưa gửi Bearer token.
