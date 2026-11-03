# Audit ma trận trạng thái & luồng hủy món — 2026-10-20

**Trạng thái:** Phase 0 + Phase 2 đã triển khai 2026-10-20 — **C-01/C-02/C-03/C-05/C-06/C-07/C-08/C-09/C-10/C-11/C-12 đã sửa**; C-04/C-13 còn.  
**Phạm vi:** backend only — không sửa `frontend/`, `wwwroot/` hoặc file FE  
**Liên quan:**

- `docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md` — thiết kế P1c/P1d đã triển khai
- `docs/architecture/backend-audit-2026-10-20.md` — audit tổng thể backend
- `docs/architecture/endpoint-baseline.md` — hợp đồng API
- `docs/architecture/progress-summary-2026-10-20.md` — trạng thái tính năng

> Mục đích của tài liệu: sau khi bổ sung luồng **bếp được hủy món khi hết nguyên liệu / khách đổi ý**,
> kiểm tra lại toàn bộ ma trận trạng thái và các điểm đồng bộ giữa **đơn hàng ↔ phiếu bếp ↔ món ↔ kho ↔ thanh toán ↔ bàn**.
> Tài liệu ghi rõ cái gì đang đúng, cái gì là lỗi, cái gì là quyết định cố ý, và hướng sửa.

> **Cập nhật Phase 0 (2026-10-20):** đã thêm `OrderLifecycleCoordinator` và route 4 luồng hiện có
> (`OrderService.CancelItemAsync`, `OrderService.UpdateStatusAsync`, `KitchenService.UpdateOrderStatusAsync`,
> `KitchenService.UpdateItemStatusAsync`). C-01/C-02/C-03 đã có guard/test hồi quy; **không đổi API, schema
> hay migration**. Build `0 warning / 0 error`; test `505/505 pass` (tăng 7 test so với 498 test cũ).

---

## 0. TL;DR — 5 việc cần xử lý trước

| Ưu tiên | Mã | Vấn đề | Mức độ | Hướng xử lý đề xuất |
|---|---|---|---|---|
| 1 | **C-01** | Hủy 1 món từ POS không cập nhật lại trạng thái đơn/phiếu; nếu món còn lại đã `Ready` thì đơn kẹt `Preparing`, **không thanh toán được** | **High — đã sửa Phase 0** | Dùng chung một hàm đồng bộ trạng thái; tính aggregate từ trạng thái hiệu lực, không đọc lại DB cũ |
| 2 | **C-02** | Bếp đặt phiếu `Completed` khi còn món `Pending/Preparing` → phiếu khóa, món không sửa được, đơn `Ready` nhưng thanh toán bị chặn | **High — đã sửa Phase 0** | Chặn `Completed` nếu **món của phiếu đó** chưa xong; vẫn cho gọi thêm bằng ticket/round mới |
| 3 | **C-03** | `PATCH /orders/{id}/status` đổi đơn sang `Preparing/Ready/Completed` mà không đồng bộ phiếu bếp/món | **High — đã sửa Phase 0** | Chặn nếu bếp chưa xong, hoặc biến nó thành command đồng bộ 2 chiều |
| 4 | **C-04** | `Auth:Enforce=false` + FE không gửi Bearer → luật hủy theo role chưa được enforce | **High (đã chấp nhận tạm thời)** | Giữ nguyên; người dùng tự bật `Auth:Enforce` khi sẵn sàng. Role matrix chỉ là đích, không phải hành vi hiện tại |
| 5 | **C-05** | Không có API **giảm/tăng số lượng** sau khi bếp đã nhận; `PUT /orders/{id}/items` là replace-all và bị chặn | **High (nghiệp vụ)** | Thêm command điều chỉnh số lượng + xử lý phần đã nấu/chưa nấu; hỗ trợ thiếu nguyên liệu thực tế |

Các lỗi còn lại (C-04, C-13) là Medium/Low, chi tiết ở mục 5.

### 0.1. Quyết định nghiệp vụ mới (chốt 2026-10-20)

Bốn điểm này thay đổi mục tiêu thiết kế so với bản audit ban đầu:

1. **Order không đóng khi món xong.**  
   `Order.Completed` chỉ có nghĩa là **các món hiện tại đã phục vụ xong**, nhưng đơn vẫn mở để khách **gọi thêm**. Đơn chỉ thực sự đóng khi **`Paid`** (hoặc `Cancelled` có lý do). Bàn vẫn `Occupied` cho tới lúc đó; trong hệ thống hiện tại, **trả bàn = giải phóng bàn khi `Paid/Cancelled`**.

2. **Gọi thêm sau khi order đã xong = lượt mới, không mở lại món cũ.**  
   Mỗi lần gọi thêm tạo **`KitchenOrder` mới** (một “round”/lượt bếp mới) + `OrderItem` mới. Ticket cũ đã `Completed` vẫn terminal; order chuyển về `SentToKitchen/Preparing` theo lượt mới. Đây là **new round**, không phải reopen món cũ.

3. **Giảm/tăng số lượng sau khi bếp đã nhận phải được hỗ trợ**, không chỉ trước khi nấu.  
   Bao gồm:
   - khách giảm 2 → 1 khi bếp đã nhận nhưng chưa nấu;
   - bếp đã nấu 1 phần, khách giảm phần còn lại;
   - khách gọi thêm sau khi món đã xong;
   - hệ thống báo đủ nguyên liệu nhưng thực tế chỉ đủ làm 1 phần → bếp được giảm/cancel phần không làm được, ghi lý do `OutOfStock/PhysicalShortage`, không hard-block.

4. **Auth giữ nguyên `Auth:Enforce=false`.**  
   Đây là quyết định vận hành: người dùng sẽ tự bật thủ công khi sẵn sàng. Role matrix vẫn là thiết kế đích, nhưng **hiện tại không được enforce**; tài liệu không yêu cầu sửa code auth.

> Phân biệt quan trọng: **“không chặn gọi thêm”** không có nghĩa là **cho đóng phiếu `Completed` khi món của phiếu đó chưa xong**.  
> - Ticket `Completed` khi còn món `Pending/Preparing` → vẫn **chặn** (C-02).  
> - Order đã `Completed` nhưng khách gọi thêm → **cho phép**, bằng một ticket/round mới.

### 0.2. Ưu tiên triển khai (trả lời ngắn)

1. **Orchestrator mỏng + đồng bộ trạng thái — Phase 0.**  
   Tạo `OrderLifecycleCoordinator` làm **cửa vào duy nhất** cho các luồng hiện có; route hủy món, đổi trạng thái bếp, đổi trạng thái order qua đó. Đây là nơi sửa C-01/C-02/C-03.

2. **Gọi thêm + giảm/tăng số lượng + thiếu nguyên liệu — Phase 2.**  
   Các command mới đi qua **cùng coordinator**, không tự ghi DB.

3. **Audit/history/refund — sau.**  
   Không chặn Phase 0/2.

4. **Auth — không nằm trong lộ trình code.**  
   Giữ `Auth:Enforce=false`; người dùng tự bật thủ công khi sẵn sàng.

> Orchestrator **không phải việc “làm sau cùng”**, cũng **không phải “làm toàn bộ trước rồi mới sửa lỗi”**.  
> Làm **mỏng** ngay Phase 0; C-01/C-02/C-03 là 3 case đầu tiên chạy qua nó. Các ma trận tách dần, không big-bang.

---

## 1. Mô hình dữ liệu & nguồn sự thật

### 1.1. Các entity liên quan

```text
Order (orders)
  ├── OrderItem (order_items)                 ← dòng món khách gọi
  │     └── OrderItemTopping
  ├── KitchenOrder (kitchen_orders)           ← phiếu bếp / một lượt (round)
  │     └── KitchenOrderItem (kitchen_order_items)  ← món trên phiếu
  ├── OrderStatusHistory (order_status_histories)
  └── Payment (payments)

InventoryTransaction (inventory_transactions)
  ├── ReferenceType = "Order", ReferenceId = OrderId
  └── OrderItemId (nullable)                  ← ledger theo dòng món
```

Quan hệ thực tế:

- `OrderItem` giữ **trạng thái món ở tầng đơn** (dùng cho thanh toán, báo cáo, hủy).
- `KitchenOrderItem` giữ **trạng thái món ở tầng bếp**.
- Hiện tại `KitchenService` **ghi trạng thái bếp xuống `OrderItem.Status`** mỗi lần đổi món (tốt).
- `KitchenOrder` là trạng thái phiếu; `Order.Status` là trạng thái đơn.
- Schema cho phép **nhiều `KitchenOrder` cho một `Order`** (chỉ có index, không unique), nhưng luồng tạo đơn hiện chỉ tạo **một phiếu**. Target dùng nhiều `KitchenOrder` làm các **round** khi khách gọi thêm; ticket cũ vẫn terminal.

### 1.2. Hai tầng trạng thái — điểm gốc của phần lớn lỗi

Hệ thống có **hai trạng thái song song**:

| Tầng | Entity | Ai cập nhật | Dùng để |
|---|---|---|---|
| Đơn | `Order.Status` | POS/Waiter, Payment, Kitchen (gián tiếp) | hiển thị, điều kiện thanh toán, khóa bàn |
| Bếp | `KitchenOrder.Status`, `KitchenOrderItem.Status` | Chef/Admin | vận hành bếp |
| Món-đơn | `OrderItem.Status` | KitchenService ghi xuống; OrderService hủy | điều kiện thanh toán, báo cáo, policy hủy |

**Nguyên tắc đúng:** trạng thái món/bếp là **sự thật vận hành**; trạng thái đơn phải **được suy ra** từ món + hành động phục vụ, không được đặt tùy ý.

Hiện tại nguyên tắc này bị vi phạm ở 3 chỗ: C-01, C-02, C-03.

### 1.3. Bốn chữ “Completed/Paid” khác nghĩa nhau

Đây là chỗ dễ nhầm nhất:

| Tầng | Trạng thái | Nghĩa thực tế | Ai đặt |
|---|---|---|---|
| Món trên phiếu bếp | `Completed` | **Món đã nấu xong** | Chef/Admin |
| Phiếu bếp | `Completed` | **Phiếu đã đóng**, mọi món trên phiếu đã xong | Chef/Admin |
| Đơn hàng | `Completed` | **Các món hiện tại đã phục vụ xong**, nhưng đơn **vẫn mở để gọi thêm**; chưa chắc đã thu tiền | Waiter/Admin |
| Thanh toán | `Paid` | **Đã thu tiền** | Cashier/Admin |

Vì vậy:

- Món `Completed` **không** có nghĩa là đơn đã `Paid`.
- Phiếu `Completed` **không** có nghĩa là đơn đã `Completed`; hiện code map phiếu `Completed → Order.Ready`.
- Đơn `Completed` **không phải terminal**: khách vẫn gọi thêm được; gọi thêm tạo lượt/ticket mới.
- Đơn `Completed` vẫn chưa phải `Paid`; bàn vẫn `Occupied` cho tới khi thu tiền.
- Chỉ `Paid` (hoặc `Cancelled`) mới đóng hoàn toàn và trả bàn.

---

## 2. Ma trận trạng thái hiện tại (theo code)

### 2.1. Đơn hàng — `OrderStatuses`

Nguồn: `Application/Common/OrderStatuses.cs`.

| Từ | Được chuyển sang | Ghi chú |
|---|---|---|
| `Pending` | `SentToKitchen`, `Preparing`, `Ready`, `Cancelled` | `Pending` hầu như chỉ có ở dữ liệu cũ; tạo đơn mới đi thẳng `SentToKitchen` |
| `SentToKitchen` | `Preparing`, `Ready`, `Cancelled` | |
| `Preparing` | `Ready`, `Completed`, `Cancelled` | cho phép nhảy `Preparing → Completed` |
| `Ready` | `Completed`, `Cancelled` | |
| `Completed` | `Cancelled` | `Completed` **không** phải trạng thái cuối |
| `Paid` | — | cuối; chỉ `PaymentService` đặt |
| `Cancelled` | — | cuối |

- Gửi lại đúng trạng thái hiện tại là no-op hợp lệ.
- `Paid` không nằm trong allow-list của `PATCH /orders/{id}/status`.
- `OrderStatuses.IsTerminal` chỉ coi `Paid` và `Cancelled` là cuối.

**Điểm thiếu:** ma trận chỉ kiểm tra chuỗi trạng thái, **không kiểm tra trạng thái bếp/món**. Vì vậy `SentToKitchen → Ready` hoặc `Preparing → Completed` luôn hợp lệ trên giấy, dù bếp còn `Pending`.

### 2.2. Món trong đơn — `OrderItem.Status`

Không có ma trận riêng trong `OrderStatuses`; trạng thái được ghi từ `KitchenOrderItem`:

| Từ | Được chuyển sang (gián tiếp qua bếp) |
|---|---|
| `Pending` | `Preparing`, `Ready`, `Completed`, `Cancelled` |
| `Preparing` | `Ready`, `Completed`, `Cancelled` |
| `Ready` | `Completed`, `Cancelled` |
| `Completed` | — (đóng) |
| `Cancelled` | — (đóng) |

- Không có endpoint riêng để đặt trạng thái `OrderItem`.
- `OrderService.CancelItemAsync` đặt trực tiếp `Cancelled` và ghi `CancelReason/CancelledAt/CancelledBy`.

### 2.3. Phiếu bếp — `KitchenStatuses.TicketMatrix`

Nguồn: `Application/Common/KitchenStatuses.cs`.

| Từ | Được chuyển sang |
|---|---|
| `Pending` | `Accepted`, `Preparing`, `Ready`, `Completed`, `Cancelled` |
| `Accepted` | `Preparing`, `Ready`, `Completed`, `Cancelled` |
| `Preparing` | `Ready`, `Completed`, `Cancelled` |
| `Ready` | `Completed`, `Cancelled` |
| `Completed` | — (cuối) |
| `Cancelled` | — (cuối) |

- `Completed`/`Cancelled` là terminal: `UpdateItemStatusAsync` chặn sửa món khi phiếu đã terminal (`KitchenService.cs:137-140`).
- **Không có guard nào bắt buộc món phải xong trước khi phiếu `Ready/Completed`.** Đây là C-02.

### 2.4. Món trên phiếu bếp — `KitchenStatuses.ItemMatrix`

| Từ | Được chuyển sang |
|---|---|
| `Pending` | `Preparing`, `Ready`, `Completed`, `Cancelled` |
| `Preparing` | `Ready`, `Completed`, `Cancelled` |
| `Ready` | `Completed`, `Cancelled` |
| `Completed` | — (cuối) |
| `Cancelled` | — (cuối) |

- `Accepted` có trong `Allowed` nhưng **bị từ chối** ở `UpdateItemStatusAsync` (`KitchenStatuses.IsAccepted(status)` → `ArgumentException`). Đây là trạng thái “chết” ở cấp món.
- `Pending → Completed` được giữ để tương thích UI bếp; UI hiện tại gửi thẳng `Completed` khi tích món.

### 2.5. Thanh toán — điều kiện hiện tại

Nguồn: `PaymentService.CreateAsync`.

Đơn được thanh toán khi **tất cả** điều kiện sau đúng:

1. Đơn không `Cancelled`, không `Paid`, chưa có payment `Paid`.
2. Đơn có ít nhất một dòng món (trừ legacy 0 dòng).
3. Còn ít nhất một món **active** (không `Cancelled`).
4. **Tất cả món active** có trạng thái `Ready` hoặc `Completed`.
5. `Order.Status` là `Ready` hoặc `Completed`.
6. `TotalAmount > 0`.

Hệ quả: nếu đơn và món lệch nhau (C-01/C-02/C-03), thanh toán bị chặn dù nhìn bề ngoài “đơn đã Ready”.

> Hiện tại điều kiện này chỉ xét món của một phiếu. Khi có **gọi thêm/round mới**, payment phải xét **tất cả món active của tất cả round**; nếu còn bất kỳ món `Pending/Preparing` thì vẫn 409.

### 2.6. Bàn — điều kiện trả bàn

Nguồn: `RestaurantTableService`.

- Bàn được tính **Occupied** nếu còn đơn có `Status != Paid` và `!= Cancelled`.
- `Completed` **vẫn tính là đơn mở** → bàn còn Occupied cho tới khi thanh toán.
- Chỉ `Paid` hoặc `Cancelled` mới giải phóng bàn.
- Đây là hành vi **cố ý**, không phải lỗi.
- Trong lúc bàn còn Occupied, khách vẫn được **gọi thêm**: tạo lượt/ticket mới, đơn quay về `SentToKitchen/Preparing`, không cần mở đơn mới.

### 2.7. Ma trận quyền theo hành động (hiện tại)

Nguồn: policy controller + kiểm tra role trong service.

| Hành động | Admin | Waiter | Chef | Cashier |
|---|---:|---:|---:|---:|
| Tạo đơn / sửa món (`POST /orders`, `PUT /orders/{id}/items`) | ✅ | ✅ | ❌ | ❌ |
| Xem đơn | ✅ | ✅ | ✅ | ✅ |
| Hủy món `Pending` | ✅ | ✅ | ✅ | ❌ |
| Hủy món `Preparing` | ✅ | ❌ | ✅ | ❌ |
| Hủy món `Ready/Completed` | ✅ | ❌ | ❌ | ❌ |
| Hủy đơn khi chưa có món done | ✅ | ✅ (PATCH order) | ✅ (PATCH ticket) | ❌ |
| Hủy đơn khi đã có món done | ✅ | ❌ | ❌ | ❌ |
| Đổi trạng thái món bếp | ✅ | ❌ | ✅ | ❌ |
| Đổi trạng thái phiếu bếp | ✅ | ❌ | ✅ | ❌ |
| Đặt đơn `Completed` | ✅ | ✅ | ❌ | ❌ |
| Gọi thêm / điều chỉnh số lượng *(target)* | ✅ | ✅ | ❌ | ❌ |
| Báo đã nấu một phần / thiếu nguyên liệu *(target)* | ✅ | ❌ | ✅ | ❌ |
| Thanh toán | ✅ | ❌ | ❌ | ✅ |

> Bảng này chỉ có hiệu lực khi request có role. Với `Auth:Enforce=false` và request **không gửi token**
> (FE hiện tại), controller policy và `OrderCancellationPolicy` đều bị bỏ qua — xem C-04.
>
> Hai dòng *(target)* chưa có endpoint; chúng ghi quyền dự kiến khi làm gọi thêm/điều chỉnh số lượng.

---

## 3. Luồng thực tế end-to-end

### 3.1. Tạo đơn

```text
POST /api/orders
  → Order.Status = Pending
  → tạo OrderItem (Pending), trừ kho + ghi Sale ledger
  → Order.Status = SentToKitchen
  → tạo KitchenOrder (Pending) + KitchenOrderItem (Pending)
  → commit
  → refresh bàn = Occupied
```

Điểm đúng: trừ kho ngay khi gửi bếp; ledger gắn `OrderItemId`.

### 3.2. Bếp đổi trạng thái một món

```text
PATCH /api/management/kitchen/items/{id}/status
  → KitchenOrderItem.Status = new
  → OrderItem.Status = new                 (đồng bộ)
  → aggregate phiếu từ tất cả món
  → map phiếu → trạng thái đơn
  → nếu đơn chưa terminal và transition hợp lệ: đổi Order.Status + ghi history
```

Điểm đúng: đây là luồng **duy nhất** hiện tại đồng bộ đầy đủ 3 lớp.

### 3.3. Bếp hủy một món

```text
PATCH /api/management/kitchen/items/{id}/status { status: "Cancelled", reason }
  → kiểm tra đơn chưa Paid
  → kiểm tra role (Chef/Admin; Waiter không gọi được route bếp)
  → nếu món Pending: hoàn kho theo ledger
  → OrderItem.Status = Cancelled + reason/actor/time
  → KitchenOrderItem.Status = Cancelled
  → tính lại Subtotal/Discount/Total nếu còn món active
  → aggregate phiếu
  → map phiếu → trạng thái đơn
  → nếu tất cả món hủy: OrderCancellation.ApplyAsync → Order Cancelled + trả bàn
```

Điểm đúng: hủy từ bếp đồng bộ tương đối đầy đủ (sau khi sửa H-01).

### 3.4. POS/Waiter hủy một món

```text
POST /api/orders/{orderId}/items/{itemId}/cancel
  → kiểm tra đơn chưa terminal
  → kiểm tra role theo trạng thái món
  → nếu món Pending: hoàn kho
  → OrderItem.Status = Cancelled + reason/actor/time
  → KitchenOrderItem.Status = Cancelled
  → nếu còn món active: tính lại tiền; aggregate phiếu
  → nếu hết món: OrderCancellation.ApplyAsync → Order Cancelled + trả bàn
```

**Điểm sai:** nhánh “còn món active” chỉ cập nhật `KitchenOrder.Status`, **không map sang `Order.Status`**; và aggregate đang đọc lại status từ DB nên không thấy món vừa hủy. Đây là C-01.

### 3.5. Hủy cả đơn từ POS

```text
PATCH /api/orders/{id}/status { status: "Cancelled", note }
  → kiểm tra role: nếu đã có món Ready/Completed chỉ Admin
  → OrderCancellation.ApplyAsync:
       - mọi OrderItem active → Cancelled
       - hoàn kho chỉ món Pending
       - mọi KitchenOrder/KitchenOrderItem → Cancelled
       - Order.Status = Cancelled
  → ghi OrderStatusHistory
  → trả bàn
```

Điểm đúng: đồng bộ đầy đủ; giữ nguyên tổng tiền lịch sử (cố ý).

### 3.6. Hủy phiếu bếp

```text
PATCH /api/management/kitchen/orders/{id}/status { status: "Cancelled", reason }
  → kiểm tra role: nếu đã có món Ready/Completed chỉ Admin
  → ApplyOrderStatusAsync → OrderCancellation.ApplyAsync
  → Order Cancelled + trả bàn
```

Điểm đúng: hủy ticket là hủy toàn bộ đơn; đúng với thiết kế “một phiếu = một đơn”.

### 3.7. Thanh toán

```text
POST /api/payments
  → kiểm tra order không Cancelled/Paid
  → kiểm tra mọi OrderItem active Ready/Completed
  → kiểm tra Order.Status Ready/Completed
  → tạo Payment Paid + Order Paid + refresh bàn
```

Điểm đúng: server-side amount; không tin client.

### 3.8. Giảm số lượng món

Hiện tại **không có API giảm số lượng**. Cách duy nhất là:

```text
PUT /api/orders/{id}/items
  → replace-all: xóa toàn bộ dòng cũ, tạo dòng mới
  → chỉ cho phép khi Order Pending/SentToKitchen
     và KitchenOrder Pending
     và mọi KitchenOrderItem Pending
  → điều chỉnh kho theo chênh lệch
```

Hệ quả:

- Giảm số lượng **trước khi bếp bắt đầu**: được, nhưng phải gửi lại toàn bộ danh sách món.
- Sau khi bếp bắt đầu: **không được**.
- Sau khi đã hủy một dòng bất kỳ: `KitchenOrderItem` có `Cancelled` → điều kiện “mọi item Pending” sai → **không sửa được nữa**, dù các món còn lại chưa nấu.
- Không thể biểu diễn “3 phần, bếp đã nấu 1, khách giảm còn 2” vì trạng thái chỉ ở cấp dòng, không ở cấp số lượng.

Đây là C-05. **Target:** không dùng replace-all; điều chỉnh theo từng dòng, hỗ trợ sau khi bếp đã nhận và tách phần đã nấu (xem 3.14, 6.6).

### 3.9. Sau khi đã thanh toán

- Hủy món: bị chặn (`Cannot cancel an item of a paid order`).
- Hủy đơn: bị chặn vì `Paid` terminal.
- Hủy ticket: **vẫn được** (C-07) nhưng không ghi ngược đơn/món, không hoàn kho/tiền.
- Không có refund.

### 3.10. Ví dụ 1 — luồng bình thường (2 món)

Bàn 5 gọi **phở bò (A)** và **trà đá (B)**.

| Bước | Order | Ticket bếp | Món A | Món B | Thanh toán |
|---|---|---|---|---|---|
| 1. Tạo đơn | `SentToKitchen` | `Pending` | `Pending` | `Pending` | ❌ |
| 2. Bếp bấm “Bắt đầu phiếu” | `Preparing` | `Preparing` | `Pending` | `Pending` | ❌ |
| 3. Bếp tích A xong | `Preparing` | `Preparing` | `Completed` | `Pending` | ❌ |
| 4. Bếp tích B xong | `Ready` | `Ready` | `Completed` | `Completed` | ✅ |
| 5. Waiter đánh dấu đã phục vụ | `Completed` | `Ready` | `Completed` | `Completed` | ✅ |
| 6. Thu ngân thu tiền | `Paid` | `Ready`/`Completed` | `Completed` | `Completed` | ✅ |

Điểm cần hiểu:

- Bước 2: đơn đã `Preparing` dù **từng món vẫn `Pending`**. Vì ticket đã bắt đầu; đây là trạng thái vận hành của cả đơn, không phải tổng của từng món.
- Bước 4: chỉ khi **tất cả món active** đã xong thì ticket mới `Ready`, kéo đơn lên `Ready`.
- Bước 5: `Order.Completed` là **waiter xác nhận đã phục vụ**, không phải bếp tự đặt.
- Bước 6: chỉ `Paid` mới giải phóng bàn. Ticket có thể vẫn `Ready`; bếp đóng `Completed` sau cũng được, nhưng không bắt buộc cho thanh toán.

### 3.11. Ví dụ 2 — C-01: hủy món từ POS làm đơn kẹt

Vẫn là bàn 5, nhưng:

- Bếp đã làm xong **phở bò A** (`Ready`).
- **Trà đá B** còn `Pending`.
- Đơn và ticket đang `Preparing` vì B chưa xong.

Khách đổi ý, không lấy trà đá nữa. Waiter bấm hủy B.

| Tầng | Trước khi hủy B | Đúng ra sau khi hủy B | Thực tế sau khi hủy B |
|---|---|---|---|
| Order | `Preparing` | `Ready` | **`Preparing`** |
| Ticket bếp | `Preparing` | `Ready` | **`Preparing`** |
| Món A | `Ready` | `Ready` | `Ready` |
| Món B | `Pending` | `Cancelled` | `Cancelled` |
| Thanh toán | ❌ | ✅ | **❌ 409** |

Điều đáng chú ý: **cùng một thao tác hủy B**, nếu **Chef** bấm hủy từ màn hình bếp thì đơn chuyển `Ready` và thu tiền được; nếu **Waiter** bấm hủy từ POS thì đơn kẹt `Preparing`. Vì hai service đang làm khác nhau:

- `KitchenService` (bếp) tính lại ticket từ trạng thái **hiệu lực** rồi map sang order.
- `OrderService` (POS) chỉ tính lại ticket, **không map sang order**, và còn đọc lại status cũ từ DB trước khi save.

Đây là lý do phải có **một hàm đồng bộ dùng chung** cho cả hai đường hủy.

### 3.12. Ví dụ 3 — C-02: bếp đóng phiếu khi món chưa xong

Đơn có A, B đều `Pending`; ticket `Pending`; order `SentToKitchen`.

Chef gọi API đóng phiếu:

```json
PATCH /api/management/kitchen/orders/{id}/status
{ "status": "Completed" }
```

Kết quả hiện tại:

| Tầng | Sau khi đóng phiếu |
|---|---|
| Order | `Ready` |
| Ticket bếp | `Completed` (terminal) |
| Món A | `Pending` |
| Món B | `Pending` |
| Thanh toán | ❌ 409 |
| Bếp sửa món | ❌ 409 vì ticket terminal |

Thực tế ngoài quán: quản lý bấm “xong phiếu” khi đầu bếp chưa tích từng món. Hệ thống đóng phiếu, khóa luôn việc tích món, nhưng món vẫn chưa xong. Chỉ còn cách hủy cả đơn.

> Hiện FE bếp chưa có nút “đóng phiếu”; nhưng API đang cho phép. Nếu sau này thêm nút, lỗi sẽ xảy ra ngay.

**Hướng đúng:** chỉ cho ticket `Completed` khi **mọi món active** đã `Ready/Completed`; hoặc nút “đóng phiếu” phải có ý nghĩa “đóng tất cả món còn lại” và ghi rõ.

### 3.13. Ví dụ 4 — C-03: POS đánh dấu đơn Ready khi bếp chưa nấu

Đơn A, B đều `Pending`; ticket `Pending`; order `SentToKitchen`.

Waiter gọi:

```json
PATCH /api/orders/{id}/status
{ "status": "Ready" }
```

Kết quả hiện tại:

| Tầng | Sau khi PATCH |
|---|---|
| Order | `Ready` |
| Ticket bếp | `Pending` |
| Món A/B | `Pending` |
| Thanh toán | ❌ 409 |

Waiter có thể bê đồ ra dù bếp chưa nấu; thu ngân vẫn không thu tiền được vì món chưa xong. Đây là “trạng thái ảo” thứ hai.

> Hiện FE chưa gọi endpoint này, nhưng API mở cho Admin/Waiter. Nếu POS dùng, lỗi xảy ra.

**Hướng đúng:** `Ready/Completed` chỉ được đặt khi mọi món active đã xong; hoặc bỏ quyền POS đặt `Ready/Preparing`, để bếp tự đẩy trạng thái.

### 3.14. Ví dụ 5 — giảm số lượng: hiện tại vs mục tiêu

Khách gọi **3 phần cơm gà**.

| Thời điểm | Hiện tại | Mục tiêu |
|---|---|---|
| Bếp chưa nhận | ✅ Được, qua `PUT /orders/{id}/items` (replace-all) | ✅ Được, qua command quantity |
| Bếp đã `Preparing`, chưa nấu | ❌ Không được | ✅ Được; hoàn kho phần giảm |
| Đã hủy một món khác trước đó | ❌ Không được, vì có item `Cancelled` | ✅ Được theo từng dòng; không dùng replace-all |
| Bếp đã nấu 1 phần, khách muốn còn 2 | ❌ Không biểu diễn được | ✅ Tách 1 phần đã nấu + 2 phần chưa nấu; hủy 1 phần chưa nấu |
| Bếp đã nấu đủ 3, khách chỉ muốn 2 | ❌ Không biểu diễn được | ❌ Không “uncook”; phục vụ/charge 3, hoặc Admin override có wastage |

Với trường hợp “đã nấu 1, còn 2”: hệ thống chỉ có **một status cho cả dòng 3 phần**, không có chỗ ghi “1 phần đã nấu, 2 phần chưa nấu”. Muốn đúng phải **tách dòng**:

```text
Dòng ban đầu: 3 phần, Pending
Bếp nấu 1 phần:
  → Dòng 1: 1 phần, Completed
  → Dòng 2: 2 phần, Pending
Khách giảm 1:
  → Hủy 1 phần từ dòng 2 → còn 1 phần Pending
```

**Quyết định 2026-10-20:** hỗ trợ giảm **sau khi bếp đã nhận**, kể cả sau khi đã nấu một phần; dùng tách dòng (hoặc `PreparedQuantity`) thay vì chỉ cho giảm trước khi nấu.

### 3.15. Ví dụ 6 — gọi thêm sau khi order đã `Completed`

Bàn 5 đã ăn xong 2 món; order `Completed`, ticket `Completed`, bàn vẫn `Occupied`, chưa `Paid`.

| Bước | Hành động | Order | Ticket | Bàn |
|---|---|---|---|---|
| 1 | Khách gọi thêm 1 trà đá | `Completed` | Ticket cũ `Completed` | `Occupied` |
| 2 | `POST /orders/{id}/items` | `SentToKitchen` | **Ticket mới** `Pending` | `Occupied` |
| 3 | Bếp bắt đầu ticket mới | `Preparing` | Ticket mới `Preparing` | `Occupied` |
| 4 | Bếp xong trà đá | `Ready` | Ticket mới `Ready` | `Occupied` |
| 5 | Waiter xác nhận phục vụ | `Completed` | Ticket mới `Completed` | `Occupied` |
| 6 | Thu tiền | `Paid` | — | `Available` |

Điểm cốt lõi:

- Ticket cũ **không bị mở lại**; món cũ **không đổi trạng thái**.
- Order `Completed → SentToKitchen` là **new round**, không phải reopen món cũ.
- Nếu order đã `Paid`: `POST /orders/{id}/items` bị **409**; phải mở đơn mới.
- Payment chỉ thành công khi **tất cả round** đã xong.

---

## 4. Đối chiếu với nghiệp vụ mong muốn

Các quy tắc bạn mô tả:

| Mã | Quy tắc nghiệp vụ | Hiện trạng | Kết luận |
|---|---|---|---|
| **R1** | Bếp được hủy món khi kho thật hết / nguyên liệu có vấn đề | Chef hủy được `Pending`/`Preparing`; Admin hủy được cả `Ready/Completed` | ✅ Đúng |
| **R2** | Bếp chỉ không hủy khi món đã làm xong | Chef bị chặn ở `Ready/Completed`; Admin vẫn được (ngoại lệ) | ✅ Đúng theo thiết kế; Admin override đã chốt ở D4 |
| **R3** | Khách không được hủy món đã xong | Waiter bị chặn ở `Ready/Completed` | ⚠️ Đúng khi request có role; **sai khi request không có token** (FE hiện tại) — C-04 |
| **R4** | Khách muốn hủy món đang nấu → bếp hủy | Waiter bị chặn `Preparing`, phải nhờ Chef; Chef hủy được | ✅ Đúng vai trò, nhưng là gap UX |
| **R5** | Khách muốn giảm/tăng số lượng sau khi bếp đã nhận | `PATCH /orders/{id}/items/{itemId}/quantity` + `POST /orders/{id}/items` (gọi thêm); tách dòng, hoàn kho phần chưa nấu, wastage khi giảm dưới phần đã nấu | ✅ Đã sửa Phase 2 (C-05) |
| **R6** | Hủy phải đồng bộ ngay giữa POS/bếp/kho/bàn/thanh toán | Mọi luồng huỷ đi qua `OrderLifecycleCoordinator` + `OrderCancellation` | ✅ Đã sửa Phase 0 (C-01) + Phase 2 (C-08) |
| **R7** | Đơn đã xong mới được thu tiền | Payment kiểm tra cả đơn và món, nhưng chưa tính “tất cả round” | ✅ Đúng hướng, nhưng dễ bị kẹt do C-01/C-02/C-03; target check all rounds |
| **R8** | Không hủy được khi đã thanh toán | Item/order bị chặn; ticket `Cancelled` sau `Paid` cũng bị chặn | ✅ Đã sửa C-07 |
| **R9** | Hoàn kho đúng phần chưa nấu | Chỉ hoàn `Pending` khi `CustomerChange`; `OutOfStock/PhysicalShortage/KitchenError` ghi wastage | ✅ Đã sửa C-08 |
| **R10** | Lý do hủy phải lưu để đối soát | `CancelReason/CancelledAt/CancelledBy` trên OrderItem + bảng `order_item_status_histories` (reason/reasonCode/actor) | ✅ Đã sửa C-10 |
| **R11** | Order đã xong hết vẫn được gọi thêm | `POST /orders/{id}/items` tạo round/ticket mới; order mở lại, bàn vẫn `Occupied` | ✅ Đã sửa Phase 2 |
| **R12** | Order chỉ đóng hoàn toàn khi `Paid` và trả bàn | Bàn đúng (Completed vẫn Occupied), nhưng order chưa có add-on | ⚠️ Target: `Completed` không terminal; `Paid/Cancelled` mới đóng |
| **R13** | Hệ thống báo đủ kho nhưng thực tế chỉ đủ một phần | `POST /management/kitchen/items/{id}/shortage` giữ phần làm được, huỷ phần thiếu, ghi `Wastage`, không hard-block | ✅ Đã sửa Phase 2 |
| **R14** | Auth để người dùng tự bật thủ công | `Auth:Enforce=false`; role matrix chưa enforce | ⚠️ Đã chấp nhận tạm thời (C-04) |

---

## 5. Danh sách lỗi & hướng sửa

### C-01 — Hủy món từ POS không đồng bộ trạng thái đơn/phiếu

**Mức độ:** High — làm đơn kẹt, không thanh toán được.  
**Trạng thái:** ✅ đã sửa Phase 0 (2026-10-20).

**Code liên quan:**

- `Application/Services/OrderService.cs:900-919` — sau khi hủy món, chỉ aggregate `KitchenOrder`, không map `Order.Status`.
- `Application/Services/OrderService.cs:902-905` — query `.Select(x => x.Status)` đọc lại từ DB, **không thấy** `kitchenItem.Status = Cancelled` vừa set trong bộ nhớ.
- So sánh: `Application/Services/KitchenService.cs:205-226` — bản bếp dùng `effectiveStatuses` và map sang đơn.

**Kịch bản tái hiện:**

1. Đơn có 2 món A, B; cả hai `Pending`; đơn `SentToKitchen`.
2. Bếp set A `Preparing` → đơn `Preparing`.
3. Bếp set A `Ready` → phiếu vẫn `Preparing` vì B còn `Pending`; đơn `Preparing`.
4. Waiter hủy B (Pending) qua `POST /orders/{id}/items/{B}/cancel`.
5. Kết quả hiện tại:
   - B bị hủy, hoàn kho đúng.
   - A là món active duy nhất và đã `Ready`.
   - Nhưng phiếu vẫn `Preparing`, đơn vẫn `Preparing`.
6. Thanh toán:
   - món A `Ready` → qua điều kiện món;
   - đơn `Preparing` → **409 "Order is not ready for payment"**.

**Hậu quả:** đơn không thể thu tiền cho tới khi ai đó vào sửa trạng thái đơn/phiếu bằng tay; nếu không ai biết, đơn treo.

**Hướng sửa:**

1. Tạo một hàm dùng chung, ví dụ `OrderLifecycle.SyncFromKitchenAsync(order, kitchenOrder, changedBy, reason, ct)`:
   - nhận danh sách trạng thái **hiệu lực** (đã gồm thay đổi trong bộ nhớ);
   - `aggregate` phiếu;
   - map phiếu → trạng thái đơn;
   - nếu transition hợp lệ thì đổi đơn + ghi history;
   - nếu tất cả món hủy → `OrderCancellation.ApplyAsync`.
2. Trong `OrderService.CancelItemAsync`, sau khi set `kitchenItem.Status = Cancelled`, gọi hàm này với `effectiveStatuses` (thay status của đúng item), **không** query lại DB.
3. Chỉ refresh bàn khi order chuyển `Cancelled`.
4. Giữ nguyên logic tính tiền hiện tại (đã đúng).

**Test cần thêm:**

- `CancelItem_WhenRemainingItemIsReady_OrderAndTicketBecomeReady` → thanh toán được.
- `CancelItem_WhenRemainingItemsStillPending_TicketStaysPreparing` (không đổi sai).
- `CancelItem_LastActiveItem_CancelsOrderAndFreesTable` (đã có, giữ).

---

### C-02 — Phiếu bếp `Completed` khi còn món chưa xong

**Mức độ:** High — deadlock mềm, chỉ thoát bằng hủy đơn.  
**Trạng thái:** ✅ đã sửa Phase 0 (2026-10-20).

**Code liên quan:**

- `Application/Common/KitchenStatuses.cs:77-84` — `TicketMatrix` cho `Pending/Accepted/Preparing → Completed` mà không kiểm tra item.
- `Application/Services/KitchenService.cs:60-102` — đổi ticket status, map `Completed → Order.Ready`.
- `Application/Services/KitchenService.cs:137-140` — khi ticket terminal, mọi update item bị chặn.
- `Application/Services/PaymentService.cs:42-55` — thanh toán đòi mọi item active `Ready/Completed`.

**Kịch bản tái hiện:**

1. Đơn 2 món đang `Pending`; phiếu `Pending`.
2. Gọi `PATCH /api/management/kitchen/orders/{id}/status { "status": "Completed" }`.
3. Hiện tại: phiếu `Completed`, đơn `Ready`, 2 món vẫn `Pending`.
4. Bếp muốn tích món → **409** vì phiếu terminal.
5. Thu ngân thanh toán → **409** vì món chưa xong.
6. Chỉ còn cách hủy đơn (mất doanh thu).

**Hướng sửa (chọn 1):**

- **A (khuyến nghị):** trước khi cho ticket `Ready/Completed`, load món active; nếu `active.Count == 0` hoặc có món chưa `Ready/Completed` → `409` với message rõ. UI phải tích món trước.
- **B:** thêm command rõ ràng `completeAllItems=true` để đóng tất cả món active trong cùng transaction; không tự động ngầm.

Không nên giữ trạng thái “Completed nhưng món Pending” vì nghĩa của `Completed` là đã xong.

> **Không mâu thuẫn với “cho gọi thêm”.** Order `Completed` vẫn **mở**; nếu khách gọi thêm, hệ thống tạo **ticket/round mới**.  
> Chỉ **ticket hiện tại** không được `Completed` khi món của chính ticket đó chưa xong.

**Test cần thêm:**

- `KitchenStatus_Completed_WhenItemsPending_IsRejected`.
- `KitchenStatus_Completed_WhenAllItemsDone_Succeeds`.
- `KitchenStatus_Ready_WhenItemsPending_IsRejected` (nếu chọn chặn cả `Ready`).
- Cập nhật test cũ `KitchenFlowTests.UpdateOrderStatus_Completed_...` (đang cố ý cho phép) theo hành vi mới.

---

### C-03 — `PATCH /orders/{id}/status` không đồng bộ bếp

**Mức độ:** High — POS có thể báo “Ready/Completed” khi bếp chưa nấu.  
**Trạng thái:** ✅ đã sửa Phase 0 (2026-10-20).

**Code liên quan:**

- `Application/Services/OrderService.cs:940-1014` — chỉ nhánh `Cancelled` gọi `ApplyAsync`; các trạng thái khác chỉ set `order.Status`.
- `Controllers/OrdersController.cs:22-23` — policy `PosOperator` (Admin + Waiter).
- `Application/Common/OrderStatuses.cs:33-41` — cho phép `SentToKitchen → Ready`, `Preparing → Completed`.

**Kịch bản tái hiện:**

1. Đơn 2 món `Pending`.
2. Waiter gọi `PATCH /orders/{id}/status { "status": "Ready" }`.
3. Đơn `Ready`; phiếu bếp vẫn `Pending`; món vẫn `Pending`.
4. Waiter có thể bê đồ ra khi bếp chưa nấu.
5. Thanh toán vẫn bị chặn vì món chưa xong.

Trường hợp nặng hơn: đặt `Completed` khi món `Pending`; đơn được ghi “đã phục vụ” nhưng thực tế bếp chưa nấu, dù đơn vẫn mở để gọi thêm. Đây là trạng thái ảo, không phải đóng đơn.

> `Order.Completed` **không** phải terminal: khách vẫn gọi thêm được bằng round mới. Nhưng `Completed` chỉ được set khi **các món hiện tại** đã xong; không được dùng nó để “đóng” đơn trước khi bếp làm.

**Hướng sửa (chọn 1):**

- **A (khuyến nghị):** với `Ready/Completed`, kiểm tra phiếu bếp; nếu còn món active chưa xong → `409`. Với `Preparing`, nếu có phiếu thì yêu cầu phiếu đã `Preparing` hoặc cập nhật phiếu trong cùng transaction.
- **B:** biến `PATCH /orders/{id}/status` thành command đồng bộ: set order + ticket + item theo một bảng ánh xạ, có audit.
- **C:** bỏ khả năng POS đặt `Preparing/Ready`; chỉ cho `Completed`/`Cancelled` sau khi bếp xong. Đây là phương án sạch nhất về lâu dài nhưng thay đổi hợp đồng.

**Test cần thêm:**

- `UpdateOrderStatus_Ready_WhenKitchenPending_IsRejected`.
- `UpdateOrderStatus_Completed_WhenItemsPending_IsRejected`.
- `UpdateOrderStatus_Completed_WhenAllItemsDone_Succeeds`.
- `UpdateOrderStatus_Preparing_WithKitchenTicket_SyncsTicket` (nếu chọn B).

---

### C-04 — Auth đang tắt: role matrix chưa được enforce (đã chấp nhận tạm thời)

**Mức độ:** Medium/High (vận hành) — đúng nghiệp vụ trên giấy nhưng chưa được thực thi khi chạy thật. **Đã chấp nhận tạm thời theo quyết định 2026-10-20.**

**Code liên quan:**

- `RestaurantFoodManagerSystem/appsettings.json:26-28` — `Auth:Enforce = false`.
- `Infrastructure/Auth/EnforceableAuthorizationPolicyProvider.cs:32-39` — mọi policy trả “cho qua”.
- `Application/Common/OrderCancellationPolicy.cs:20-23,47-51` — nếu `Roles.Count == 0` thì **return true**.
- `frontend/js/common/api.js:5-12` — không gửi header `Authorization`.

**Hệ quả thực tế:**

- Request từ FE không có token → `ICurrentUser.Roles` rỗng.
- Controller policy bị bypass.
- Service policy cũng bypass.
- Bất kỳ ai gọi API **mà không gửi token** (như FE hiện tại) đều có thể:
  - hủy món `Ready/Completed` (trừ đơn `Paid`);
  - hủy đơn đã có món xong;
  - hủy ticket bếp.
- Điều này trái trực tiếp với R3 (“khách không được hủy món đã xong”).

**Quyết định 2026-10-20:**

1. **Giữ `Auth:Enforce=false`.** Người dùng sẽ tự bật thủ công bằng một thay đổi cấu hình khi sẵn sàng.
2. **Không thêm cờ fail-closed, không sửa auth trong đợt này.**
3. Role matrix trong tài liệu vẫn là **thiết kế đích**; khi bật enforce, nó trở thành hành vi thực tế.
4. Khi bật enforce, FE phải gửi `Authorization: Bearer`; nếu không, mọi API có `[Authorize]` sẽ 401/403.

> Vì vậy trong giai đoạn hiện tại, không được coi luật hủy theo role là đã được bảo vệ. Đây là rủi ro vận hành đã biết, không phải lỗi logic cần sửa ngay.

**Test (chỉ chạy khi bật enforce):**

- `CancelItem_WithoutRoles_InProduction_IsRejected`.
- `CancelItem_Ready_WithChefToken_IsForbidden` (đã có ở tầng service).
- `CancelItem_Pending_WithWaiterToken_IsAllowed`.
- `CancelItem_Preparing_WithWaiterToken_IsForbidden`.

---

### C-05 — Chưa hỗ trợ giảm/tăng số lượng sau khi bếp nhận

**Mức độ:** High (nghiệp vụ) — đúng yêu cầu “khách muốn giảm số lượng / gọi thêm / thiếu nguyên liệu thực tế”.

**Code liên quan:**

- `Application/DTO/Requests/Order/OrderRequests.cs:26-34` — `PUT /orders/{id}/items` là replace-all.
- `Application/Services/OrderService.cs:280-314` — chỉ cho sửa khi đơn `Pending/SentToKitchen`, phiếu `Pending`, **mọi** kitchen item `Pending`.
- `Application/Services/OrderService.cs:305-310` — nếu có item `Cancelled` → chặn.
- Không có endpoint `quantity` trong `OrdersController`.

**Các trường hợp cần hỗ trợ:**

| Tình huống | Hiện tại | Mục tiêu |
|---|---|---|
| Giảm 2 → 1 trước khi bếp nhận | Được, qua replace-all | Được, qua command riêng |
| Giảm 2 → 1 sau khi bếp đã nhận, chưa nấu | Không được | **Được**; hoàn kho phần giảm |
| Bếp đã nấu 1/2, khách giảm còn 1 | Không biểu diễn được | **Được**; hủy phần chưa nấu |
| Bếp đã nấu đủ 2, khách chỉ muốn 1 | Không biểu diễn được | **Không thể “uncook”**; phải phục vụ/charge, hoặc Admin override có wastage |
| Gọi thêm sau khi order đã `Completed` | Không được (order coi như xong) | **Được**; tạo round/ticket mới, order mở lại |
| Hệ thống báo đủ kho nhưng thực tế chỉ đủ 1 | Bếp chỉ còn cách hủy cả dòng | **Được** giảm xuống 1, ghi reason `OutOfStock/PhysicalShortage` |

**Hướng thiết kế đề xuất:**

1. **Tách “lượt gọi món” (round) khỏi “đơn”.**
   - Mỗi lần gọi thêm tạo `OrderItem` mới + `KitchenOrder` mới (schema hiện đã cho phép nhiều `KitchenOrder` cho một `Order`).
   - Ticket cũ đã `Completed` vẫn terminal. Order không terminal; trạng thái order được suy ra từ **tất cả round**.
   - Không mở lại món cũ; không sửa ticket cũ.

2. **Theo dõi số lượng đã nấu / đã hủy ở cấp dòng.**
   Có 2 cách; cần chốt trước khi code:

   **Cách A — tách dòng (khuyến nghị):**
   - Khi bếp nấu một phần, tách dòng:
     ```text
     Ban đầu: 2 phần, Pending
     Bếp nấu 1:
       → Dòng 1: 1 phần, Completed
       → Dòng 2: 1 phần, Pending
     Khách giảm 1:
       → Hủy dòng 2
     ```
   - Thêm `OrderItemGroupId` (nullable) để nhóm các dòng tách ra, giữ hóa đơn/UI gọn.
   - Dùng lại status line-level hiện tại; payment/stock ít phải viết lại.

   **Cách B — thêm số lượng trên một dòng:**
   - Thêm `PreparedQuantity`, `CancelledQuantity` (và có thể `OriginalQuantity`).
   - Một dòng có thể vừa `Preparing` vừa có 1 phần đã nấu; phải sửa payment/report/status logic để hiểu “remaining”.
   - Ít dòng hơn nhưng logic trạng thái phức tạp hơn.

3. **Command/API đề xuất:**
   - `PATCH /orders/{id}/items/{itemId}/quantity` — giảm/tăng số lượng; body có `quantity` hoặc `delta`, `reasonCode`.
   - `POST /orders/{id}/items` — gọi thêm; tạo round/ticket mới, không replace-all.
   - `PATCH /management/kitchen/items/{id}/prepared-quantity` — bếp ghi đã nấu bao nhiêu phần (nếu dùng cách A, command này tách dòng).
   - `POST /management/kitchen/items/{id}/shortage` — báo thiếu nguyên liệu; giảm phần làm được + hủy phần còn lại với reason.

4. **Quy tắc giảm số lượng:**
   - `newQuantity >= preparedQuantity`: giảm phần chưa nấu; hoàn kho theo reason.
   - `newQuantity < preparedQuantity`: **không được** tự động hủy phần đã nấu; trả `409` với message rõ, hoặc Admin override có wastage.
   - `newQuantity = 0`: chuyển thành hủy dòng; bắt buộc reason.
   - Không cho giảm nếu order đã `Paid`.

5. **Quy tắc gọi thêm:**
   - Nếu order chưa `Paid`: tạo round mới; nếu món cũ đã xong, order chuyển `SentToKitchen/Preparing` theo round mới.
   - Nếu order đã `Paid`: chặn; phải mở đơn mới.
   - Bàn vẫn `Occupied` giữa các round.
   - Payment chỉ thành công khi **tất cả round** đã xong.

6. **Quy tắc kho khi thiếu nguyên liệu:**
   - `CustomerChange` + chưa nấu → hoàn kho phần giảm.
   - `OutOfStock/PhysicalShortage` → **không hoàn kho ảo**; ghi `Wastage`/`StockAdjustment` để điều chỉnh về tồn thực tế.
   - Nếu hệ thống báo đủ nhưng thực tế thiếu, bếp vẫn được giảm/cancel; không hard-block theo tồn kho hệ thống.

Không nên tiếp tục dùng replace-all cho giảm số lượng vì:
- mất `OrderItemId` của ledger;
- mất lịch sử hủy;
- dễ ghi đè nhầm món khác;
- không biểu diễn được phần đã nấu.

---

### C-06 — Bếp không thể bỏ tích “món đã xong”

**Trạng thái:** ✅ Đã sửa 2026-10-20 — thêm `POST /api/management/kitchen/items/{id}/reopen` (Chef/Admin).
Chỉ mở được món `Ready/Completed` khi order chưa `Paid/Cancelled` và ticket chưa `Completed/Cancelled`;
món, ticket và order quay về `Preparing` (`CompletedAt` của ticket được xoá), bàn vẫn `Occupied`.
Test: `C06C10C11Tests.ReopenItem_*`.

**Mức độ:** Medium — lỗi thao tác, không ảnh hưởng tiền trực tiếp nhưng gây kẹt trạng thái.

**Code liên quan:**

- `frontend/js/pages/kitchen.js:303-320` — khi bỏ tích gửi `status: "Preparing"`.
- `Application/Common/KitchenStatuses.cs:86-92` — `ItemMatrix` không có `Completed/Ready → Preparing`.
- `Application/Services/KitchenService.cs:142-144` — trả `409 Cannot change kitchen item status from Completed to Preparing`.

**Hệ quả:** bếp lỡ tích “xong” thì không thể sửa lại; nếu món thực tế chưa xong, phải hủy dòng hoặc nhờ Admin can thiệp.

**Hướng sửa (chọn 1):**

- **A (khuyến nghị):** thêm command rõ ràng `POST /api/management/kitchen/items/{id}/reopen` (Chef/Admin), chỉ cho khi:
  - order chưa `Paid`;
  - ticket chưa `Completed/Cancelled`;
  - item đang `Ready/Completed`;
  - ghi audit.
- **B:** cho phép `Ready/Completed → Preparing` trong `ItemMatrix` nhưng giới hạn role + trạng thái; đơn giản hơn nhưng mở rộng ma trận ngược.
- **C:** FE không cho bỏ tích; chỉ backend guard. Nhưng FE hiện vẫn gửi, nên cần sửa FE (ngoài phạm vi).

---

### C-07 — Hủy ticket sau khi đơn đã `Paid`

**Trạng thái:** ✅ Đã sửa 2026-10-20 — `KitchenService.UpdateOrderStatusAsync` chặn `Cancelled`
khi order `Paid`; `ApplyOrderStatusIfAllowedAsync` cũng có guard phòng thủ. Ticket vẫn có thể
`Completed` để đóng vận hành. Test: `KitchenCancelTicket_AfterPaid_IsRejected`.

**Mức độ:** Medium — dữ liệu lệch, không hoàn tiền/kho.

**Code liên quan:**

- `Application/Services/KitchenService.cs:67-79` — chỉ kiểm tra policy khi `!OrderStatuses.IsTerminal(order.Status)`.
- `Application/Services/KitchenService.cs:81-102` — nếu đơn terminal, bỏ qua cập nhật đơn nhưng vẫn set `kitchenOrder.Status`.
- `Application/Services/KitchenService.cs:154-159` — hủy **món** sau Paid thì bị chặn.

**Hệ quả:** cùng một đơn Paid, hủy món bị chặn nhưng hủy ticket lại được; ticket `Cancelled` trong khi đơn `Paid`, không có refund/stock. Báo cáo bếp và báo cáo doanh thu nhìn thấy hai sự thật khác nhau.

**Hướng sửa:**

- Với đơn `Paid`, chỉ cho ticket `Completed` (đóng vận hành), **không** cho `Cancelled`; hoặc
- Chặn mọi thay đổi ticket sau Paid, trừ một command “đóng ticket” riêng;
- Nếu cần hủy sau Paid → phải đi luồng refund (C-13).

---

### C-08 — Hoàn kho theo trạng thái, không theo lý do

**Trạng thái:** ✅ Đã sửa 2026-10-20 — bốn request huỷ (`CancelOrderItemRequest`,
`UpdateOrderStatusRequest`, `KitchenItemStatusRequest`, `KitchenStatusRequest`) nhận
`ReasonCode`; `CustomerChange` hoàn kho phần `Pending`, còn
`OutOfStock/PhysicalShortage/KitchenError` ghi `Wastage` zero-stock và không hoàn kho.
Mặc định khi client cũ không gửi là `CustomerChange` để giữ tương thích. Test:
`CancelItem_OutOfStock_DoesNotReturnStock_AndRecordsWastage`,
`CancelOrder_OutOfStock_DoesNotReturnStock_AndRecordsWastage`,
`KitchenCancelItem_PhysicalShortage_DoesNotReturnStock_AndRecordsWastage`,
`KitchenCancelTicket_OutOfStock_DoesNotReturnStock_AndRecordsWastage`.

**Mức độ:** Medium - có thể làm sai tồn kho thực tế.

**Code liên quan:**

- `Application/Common/OrderCancellation.cs:65-71` — chỉ hoàn nếu cả kitchen item và order item `Pending`.
- `Application/Common/OrderCancellation.cs:14-19` — quy tắc đã chốt: `Preparing/Ready/Completed` coi như đã tiêu hao.

**Vấn đề:**

- `Pending` + lý do “nguyên liệu hỏng/hết hàng thực tế” → hệ thống vẫn hoàn kho, dù nguyên liệu không dùng được.
- `Preparing` + lý do “khách đổi ý, chưa dùng nguyên liệu” → không hoàn, dù có thể còn dùng được.
- Không phân biệt `CustomerChange`, `OutOfStock`, `Spoiled`, `KitchenError`.

**Hướng sửa:**

- Thêm `CancelReasonCode` (enum/string) vào request + `OrderItem`.
- Chính sách đề xuất:

| ReasonCode | Trạng thái | Hoàn kho |
|---|---|---|
| `CustomerChange` | Pending | Có |
| `CustomerChange` | Preparing+ | Không (đã dùng) |
| `OutOfStock` | Pending | **Không** — ghi wastage/adjustment |
| `Spoiled` | Pending | **Không** — ghi wastage |
| `KitchenError` | Pending | Không — ghi wastage |
| `Other` | Pending | Tùy quyết định, mặc định có |

- Cần chốt với chủ dự án vì ảnh hưởng báo cáo hao hụt.

---

### C-09 — `PUT /orders/{id}/items` là replace-all, mất định danh dòng & ledger

**Trạng thái:** ✅ Đã xử lý 2026-10-20 — route đã bị chặn, trả `410 Gone` kèm hướng dẫn route thay thế.
Lý do: replace-all xoá hẳn `OrderItem` cũ (mất `OrderItemId` ⇒ mất liên kết ledger theo dòng và mất dấu vết huỷ),
ghi ledger ở cấp đơn nên huỷ món sau đó phải ước tính lại từ công thức, và không ghi history món (C-10).
Dùng 3 route lệnh: `POST /orders/{id}/items` (gọi thêm), `PATCH /orders/{id}/items/{itemId}/quantity` (đổi số lượng),
`POST /orders/{id}/items/{itemId}/cancel` (huỷ món). `OrderService.UpdateItemsAsync` còn giữ cho test, không expose qua HTTP.
Test: `ResponseDtoHttpTests.PutOrderItems_IsRetired_Returns410Gone_AndLeavesOrderUntouched`.

**Mức độ:** Medium — ảnh hưởng audit và hoàn kho khi hủy sau khi sửa.

**Code liên quan:**

- `Application/Services/OrderService.cs:312-335` — xóa toàn bộ `OrderItem`/`KitchenOrderItem` cũ, tạo mới.
- `Application/Services/OrderService.cs:375` — `ApplyStockDeltas` ghi ledger **không** kèm `OrderItemId`.
- `Application/Services/OrderService.cs:217-276` — khi hủy món sau khi sửa, fallback ước tính từ recipe.

**Hệ quả:**

- `OrderItem.Id` thay đổi; lịch sử hủy cũ mất liên kết.
- Ledger điều chỉnh không gắn dòng; hủy món sau đó phải ước tính.
- Nếu đã có item `Cancelled`, replace-all bị chặn (C-05).

**Hướng sửa:**

- Không dùng replace-all cho thao tác nhỏ; thêm command `add item`, `update quantity`, `cancel item`.
- Nếu vẫn giữ replace-all: ghi ledger theo từng dòng mới, hoặc lưu `PreviousOrderItemId` để truy vết.
- Không cho replace-all khi đã có item `Cancelled`; hướng dẫn dùng command hủy/thêm riêng.

---

### C-10 — Audit hủy món chưa đầy đủ

**Trạng thái:** ✅ Đã sửa 2026-10-20 — bảng `order_item_status_histories` + entity `OrderItemStatusHistory`
(old/new status, reason, reasonCode, actor, thời điểm); ghi ở mọi luồng đổi trạng thái món, huỷ món/ticket/đơn,
tăng/giảm số lượng, prepared split và shortage. Đọc qua `GET /api/orders/{id}/item-history`.
Huỷ ticket từ bếp cũng ghi lý do thật vào history order thay vì `"Updated from kitchen"`.
Test: `C06C10C11Tests` (5 test).

**Mức độ:** Medium — khó đối soát khi có tranh chấp.

**Hiện trạng:**

- `OrderItem` có `CancelReason/CancelledAt/CancelledBy`.
- `KitchenOrderItem` **không có** field hủy; nếu món bị hủy từ bếp, thông tin nằm ở `OrderItem`.
- `OrderStatusHistory` chỉ ghi thay đổi **order status**, không ghi hủy từng món.
- Hủy ticket từ bếp ghi history note `"Updated from kitchen"`, **mất lý do** (reason chỉ vào item).
- Hủy món một phần từ POS không ghi history.

**Hướng sửa:**

- Thêm bảng `OrderItemStatusHistory` (OrderItemId, OldStatus, NewStatus, ChangedBy, Reason, CreatedAt) hoặc mở rộng `OrderStatusHistory` với `OrderItemId`/`KitchenOrderItemId`.
- Ghi history cho mọi lần hủy/đổi trạng thái món, kể cả hủy một phần.
- Ghi reason vào history order khi hủy từ bếp (không chỉ “Updated from kitchen”).

---

### C-11 — `Accepted` là trạng thái chết ở cấp món; schema cho nhiều ticket/order

**Trạng thái:** ✅ Đã sửa 2026-10-20 — tách `KitchenStatuses.AllowedTicket` (có `Accepted`) và `AllowedItem`
(không có `Accepted`); `UpdateItemStatusAsync` từ chối `Accepted`. Migration thêm CHECK constraint cho
`orders`, `order_items`, `kitchen_orders`, `kitchen_order_items` và chuyển item `Accepted` cũ sang `Preparing`.
Phần aggregate toàn bộ round đã xử lý ở C-12 (`RefreshOrderStatusFromAllItemsAsync`).
Test: `C06C10C11Tests` (2 test).

**Mức độ:** Medium — cần cho gọi thêm/round mới.

- `KitchenStatuses.Allowed` có `Accepted`, nhưng `UpdateItemStatusAsync` từ chối (`IsAccepted`).
- `KitchenOrder` có thể có nhiều bản ghi cho một `Order` (không unique). Đây là nền tảng cho **round mới**, nhưng `MapOrderStatus` đang xử lý từng ticket riêng: một ticket `Ready` có thể đẩy order `Ready` dù ticket/round khác còn nấu.
- Không có CHECK constraint/enum cho các cột `Status`; dữ liệu sai có thể vào từ migration/script.

**Hướng sửa:**

- Bỏ `Accepted` khỏi item `Allowed` hoặc hỗ trợ đầy đủ (item Accepted → ticket Accepted).
- Aggregate **toàn bộ ticket/món của tất cả round** trước khi map order status; ticket `Completed` chỉ terminal **của ticket đó**.
- Thêm CHECK constraint cho các status hợp lệ (SQL Server) hoặc ít nhất test data integrity.

---

### C-12 — Payment phụ thuộc hai trạng thái, không có cơ chế tự sửa

**Mức độ:** Low/Medium — Triệu chứng của C-01/C-02/C-03.

- Payment yêu cầu cả `Order.Status` và mọi `OrderItem.Status` đúng.
- Nếu dữ liệu lệch, không có endpoint/job “recalculate order status from items”.
- Legacy đơn 0 dòng món được bỏ qua readiness (cố ý).
- Khi có **gọi thêm/round mới**, điều kiện “mọi item” phải là **mọi item active của tất cả round**, không chỉ ticket hiện tại.

**Hướng sửa:**

- Sau khi sửa C-01/C-02/C-03, thêm một hàm `RecalculateOrderStatusAsync(orderId)` dùng cho:
  - repair dữ liệu cũ;
  - job định kỳ/command admin;
  - test.
- Hàm này phải aggregate **tất cả round** và không coi `Order.Completed` là terminal.
- Cân nhắc Payment chỉ cần kiểm tra item + một cờ “served” thay vì hai trạng thái.

---

### C-13 — `Completed` không phải terminal; không có refund

**Mức độ:** Low/Medium — cần chốt nghiệp vụ.

- `Order.Completed` vẫn được chuyển `Cancelled` (Admin).
- `Paid` terminal, không refund.
- Nếu khách hủy sau khi đã trả tiền: không có luồng hoàn tiền.

**Hướng sửa:**

- **Đã chốt:** `Completed` = “các món hiện tại đã phục vụ, chưa thu tiền”, **không terminal**; khách vẫn gọi thêm bằng round mới; Admin có thể hủy nếu chưa `Paid`.
- Chỉ `Paid/Cancelled` là terminal; bàn trả khi `Paid/Cancelled`.
- Nếu cần refund: thêm `Payment.RefundedAmount/RefundedAt`, trạng thái `Refunded`, command refund; không mở lại order.

---

## 6. Mô hình trạng thái đề xuất

### 6.1. Nguyên tắc

1. **Một nguồn sự thật:** trạng thái món/bếp là thật; trạng thái đơn là **hàm suy ra** + hành động phục vụ.
2. **Một command ghi nhiều lớp trong một transaction:** OrderItem + KitchenOrderItem + KitchenOrder + Order + stock + totals + table + history.
3. **Không có trạng thái ảo:** `Ready/Completed` chỉ khi tất cả món active đã xong.
4. **Paid là bất biến:** không hủy/sửa sau Paid; refund là luồng riêng.
5. **Role matrix là thiết kế đích:** hiện `Auth:Enforce=false` theo quyết định vận hành; người dùng tự bật thủ công khi sẵn sàng.
6. **Audit mọi thay đổi:** ai, khi nào, lý do, trạng thái cũ/mới.
7. **Order mở tới khi Paid:** `Completed` chỉ là “các món hiện tại đã phục vụ”; khách vẫn gọi thêm được.
8. **Gọi thêm = round mới:** `KitchenOrder` mới + `OrderItem` mới; ticket cũ terminal, order không terminal.

### 6.2. Invariants

| Mã | Invariant |
|---|---|
| I1 | `Order.Ready/Completed` ⇒ mọi `OrderItem` active `Ready/Completed` |
| I2 | `KitchenOrder.Ready/Completed` ⇒ mọi `KitchenOrderItem` active `Ready/Completed` |
| I3 | `Order.Paid` ⇒ mọi item active done + có payment `Paid` + `TotalAmount > 0` |
| I4 | `OrderItem.Cancelled` ⇒ không thể quay lại `Preparing/Ready/Completed` |
| I5 | `KitchenOrder` terminal ⇒ không sửa item (trừ command reopen có audit) |
| I6 | `Order.Paid` ⇒ không hủy item/order/ticket Cancelled |
| I7 | Bàn chỉ `Available` khi order `Paid/Cancelled` |
| I8 | Hoàn kho chỉ cho phần chưa nấu; reason code quyết định wastage |
| I9 | `Order.Completed` **không** terminal; gọi thêm tạo round mới, không mở lại món cũ |
| I10 | `Order.Paid` chỉ khi **tất cả round** đã xong; bàn chỉ trả khi `Paid/Cancelled` |
| I11 | `KitchenOrder.Completed` chỉ khi **món của chính ticket đó** đã xong; round mới dùng ticket mới |
| I12 | Không thể giảm số lượng xuống dưới số phần đã nấu; phần đã nấu phải phục vụ/charge hoặc Admin override có wastage |

### 6.3. Ma trận đích đề xuất

**Order:**

| Từ | Sang | Ai | Điều kiện |
|---|---|---|---|
| `SentToKitchen` | `Preparing` | Kitchen (tự động) | có item Preparing |
| `SentToKitchen/Preparing` | `Ready` | Kitchen (tự động) | tất cả item active done |
| `Ready` | `Completed` | Waiter/Admin | đã phục vụ các món hiện tại |
| `Ready/Completed` | `Paid` | Cashier/Admin | **tất cả round** đã xong |
| `Ready/Completed` | `SentToKitchen/Preparing` | Waiter/Admin + `AddItems` | **gọi thêm**: tạo round/ticket mới, không mở lại món cũ |
| bất kỳ (trừ Paid/Cancelled) | `Cancelled` | Admin; Waiter/Chef nếu chưa món done | policy + reason |
| `Paid/Cancelled` | — | — | Terminal |

- Bỏ khả năng POS tự đặt `Preparing/Ready` (hoặc biến thành command đồng bộ bếp).
- `Completed` **không terminal**: khách vẫn gọi thêm; order chuyển về `SentToKitchen/Preparing` theo round mới.
- Chỉ `Paid`/`Cancelled` mới đóng hoàn toàn.

**Kitchen ticket:**

| Từ | Sang | Điều kiện |
|---|---|---|
| `Pending` | `Accepted/Preparing` | bếp nhận |
| `Accepted/Preparing` | `Ready` | tất cả item active **của ticket này** done |
| `Ready` | `Completed` | tất cả item active **của ticket này** done; đóng ticket |
| bất kỳ (trừ terminal) | `Cancelled` | policy; hủy toàn bộ đơn |
| `Completed/Cancelled` | — | Terminal **của ticket** |

- `Pending/Accepted → Completed` bị chặn nếu còn món chưa xong.
- Gọi thêm sau khi ticket `Completed` → **tạo ticket mới**, không mở lại ticket cũ.
- Order không terminal chỉ vì ticket terminal.

**Kitchen item:**

| Từ | Sang | Ghi chú |
|---|---|---|
| `Pending` | `Preparing` | bắt đầu |
| `Preparing` | `Ready` | xong |
| `Pending/Preparing/Ready` | `Completed` | nếu UI cần; hoặc bỏ `Completed` ở item và dùng `Ready` |
| `Pending/Preparing` | `Cancelled` | policy + stock |
| `Ready/Completed` | `Preparing` | chỉ qua command `reopen` có audit |
| `Completed/Cancelled` | — | Terminal **của món** |

- Gọi thêm cùng món → tạo `OrderItem`/`KitchenOrderItem` mới trong round mới; không mở lại món cũ.
- Nấu một phần số lượng → tách dòng hoặc ghi `PreparedQuantity` (xem C-05 / 6.6).

### 6.4. Thuật toán suy ra trạng thái đơn

```text
function deriveOrderStatus(order, allRoundItems, allKitchenTickets, payment):
    if payment.Paid: return Paid
    active = allRoundItems where Status != Cancelled
    if allRoundItems.Count > 0 and active.Count == 0: return Cancelled

    if active.All(IsDone):
        if order.Status == Completed: return Completed   // các món hiện tại đã phục vụ
        return Ready

    if active.Any(IsPreparing): return Preparing
    if allKitchenTickets.Any(t => t.Status == Preparing): return Preparing
    return SentToKitchen
```

- `Completed` chỉ giữ khi **tất cả món active hiện tại đã done**; nếu có món mới `Pending`, order tự về `SentToKitchen/Preparing`.
- Không dùng `order.Status` cũ làm input duy nhất; luôn tính từ **tất cả item của tất cả round** + ticket + payment.
- `Order.Completed` không terminal; `AddItems` tạo round mới và đưa order về trạng thái phù hợp.

### 6.5. Command/API đề xuất

| Command | Route | Trách nhiệm |
|---|---|---|
| Hủy món | `POST /orders/{id}/items/{itemId}/cancel` | hủy 1 dòng + sync ticket/order + stock + totals + history |
| Đổi trạng thái món | `PATCH /management/kitchen/items/{id}/status` | item + order item + ticket + order + history |
| Đổi trạng thái phiếu | `PATCH /management/kitchen/orders/{id}/status` | chỉ ticket; guard món của ticket; cancel toàn đơn |
| Đổi trạng thái đơn | `PATCH /orders/{id}/status` | chỉ `Completed/Cancelled` (hoặc command đồng bộ) |
| **Gọi thêm** | `POST /orders/{id}/items` (mới) | tạo `OrderItem` + **round/ticket mới**; không replace-all; order mở lại |
| **Giảm/tăng số lượng** | `PATCH /orders/{id}/items/{itemId}/quantity` (mới) | điều chỉnh số lượng; hỗ trợ sau khi bếp nhận; tách phần đã nấu |
| **Bếp báo đã nấu một phần** | `PATCH /management/kitchen/items/{id}/prepared-quantity` (mới) | ghi số phần đã nấu; tách dòng nếu cần |
| **Bếp báo thiếu nguyên liệu** | `POST /management/kitchen/items/{id}/shortage` (mới) | giảm phần làm được + hủy phần còn lại; reason `OutOfStock/PhysicalShortage` |
| Mở lại món | `POST /management/kitchen/items/{id}/reopen` (mới) | sửa lỡ tích, có audit |
| Thanh toán | `POST /payments` | chỉ khi **tất cả round** đã xong |
| Refund | `POST /payments/{id}/refund` (mới, nếu cần) | luồng riêng |

### 6.6. Thiết kế giảm/tăng số lượng, gọi thêm & thiếu nguyên liệu

#### 6.6.1. Mục tiêu

Hỗ trợ **sau khi bếp đã nhận**, không chỉ trước khi nấu:

- giảm 2 → 1 khi bếp đã nhận nhưng chưa nấu;
- bếp đã nấu 1/2, khách giảm còn 1;
- khách gọi thêm sau khi các món hiện tại đã xong;
- hệ thống báo đủ kho nhưng thực tế chỉ đủ làm 1 phần.

#### 6.6.2. Mô hình dữ liệu — chọn 1

**Cách A — tách dòng (khuyến nghị):**

- Khi bếp nấu một phần, tách dòng theo số phần đã nấu/chưa nấu.
- Thêm `OrderItemGroupId` (nullable) để nhóm các dòng tách ra.
- Dùng lại status line-level hiện tại; payment/stock ít phải viết lại.

```text
Ban đầu: 2 phần, Pending
Bếp nấu 1:
  → Dòng 1: 1 phần, Completed
  → Dòng 2: 1 phần, Pending
Khách giảm 1:
  → Hủy dòng 2
```

**Cách B — thêm số lượng trên một dòng:**

- Thêm `PreparedQuantity`, `CancelledQuantity`, có thể `OriginalQuantity`.
- Một dòng có thể vừa `Preparing` vừa có 1 phần đã nấu.
- Ít dòng hơn nhưng phải sửa payment/report/status để hiểu “remaining”.

#### 6.6.3. Quy tắc xử lý

| Tình huống | Xử lý |
|---|---|
| Giảm khi chưa nấu | Giảm số lượng; hoàn kho phần giảm nếu `CustomerChange` |
| Giảm khi đã nấu một phần | Chỉ hủy phần **chưa nấu**; phần đã nấu giữ nguyên |
| Giảm xuống dưới số đã nấu | `409`; phải phục vụ/charge phần đã nấu, hoặc Admin override có wastage |
| Tăng khi ticket chưa `Completed` | Tăng số lượng + kiểm tra kho + cập nhật kitchen item |
| Tăng sau khi ticket `Completed` | Tạo **round/ticket mới**; không mở lại món cũ |
| Gọi thêm sau order `Completed` | Tạo round mới; order về `SentToKitchen/Preparing`; bàn vẫn Occupied |
| Thiếu nguyên liệu thực tế | Bếp giảm xuống số làm được; hủy phần còn lại với reason `OutOfStock/PhysicalShortage` |
| Order đã `Paid` | Chặn mọi thay đổi; mở đơn mới nếu cần |

#### 6.6.4. API đề xuất

- `PATCH /orders/{id}/items/{itemId}/quantity`
  - body: `{ quantity | delta, changedBy, reasonCode }`
  - giảm/tăng; tách phần đã nấu nếu cần.
- `POST /orders/{id}/items`
  - gọi thêm; tạo `OrderItem` + `KitchenOrder` mới (round mới).
- `PATCH /management/kitchen/items/{id}/prepared-quantity`
  - bếp ghi đã nấu bao nhiêu phần; tách dòng nếu dùng cách A.
- `POST /management/kitchen/items/{id}/shortage`
  - báo thiếu; giảm phần làm được + hủy phần còn lại.

#### 6.6.5. Ví dụ 2 gà rán

| Bước | Dữ liệu | Kết quả |
|---|---|---|
| Khách gọi 2 | OrderItem qty=2, Pending | Ticket Pending |
| Bếp nhận | Ticket Accepted/Preparing | Item vẫn qty=2 |
| Khách giảm còn 1, chưa nấu | Quantity 2→1 | Hoàn kho 1 phần; kitchen item qty=1 |
| Bếp đã nấu 1, khách giảm còn 1 | Tách: 1 Completed + 1 Pending | Hủy phần Pending; không hoàn phần đã nấu |
| Bếp đã nấu đủ 2, khách chỉ muốn 1 | Không thể giảm | Phục vụ/charge 2, hoặc Admin override + wastage |
| Hệ thống báo đủ 2, thực tế chỉ đủ 1 | Bếp ghi prepared=1; hủy 1 | Reason `OutOfStock/PhysicalShortage`; **không** hoàn kho ảo; ghi adjustment |
| Khách gọi thêm 1 sau khi xong | Tạo round mới qty=1 | Order về Preparing; bàn vẫn Occupied |

#### 6.6.6. Kho & lý do

- `CustomerChange` + chưa nấu → hoàn kho phần giảm.
- `OutOfStock/PhysicalShortage` → **không hoàn kho ảo**; ghi `Wastage`/`StockAdjustment` để điều chỉnh về tồn thực tế.
- Phần đã nấu → không hoàn; nếu hủy do lỗi bếp, ghi wastage.
- Hệ thống không được hard-block bếp chỉ vì tồn kho hệ thống báo đủ; bếp luôn có quyền báo thiếu thực tế.

### 6.7. Tách ma trận theo tên + một orchestrator (khả thi)

**Kết luận:** rất khả thi và nên làm, nhưng phải tách đúng:

- **Ma trận = quyết định thuần túy** (được/không, trạng thái đích, hiệu ứng cần làm).
- **Một orchestrator = nơi duy nhất được ghi trạng thái** vào DB.
- **Không** để mỗi ma trận tự mở transaction/tự update entity — nếu không sẽ lặp lại đúng lỗi C-01.

Nếu mỗi endpoint có một ma trận riêng và mỗi nơi tự ghi DB, hệ thống sẽ có nhiều nguồn sự thật hơn, không phải ít hơn.

#### 6.7.1. Ba tầng đề xuất

```text
Controller
  → OrderLifecycleCoordinator (một cửa vào, một transaction)
      → Matrices/Policies (pure, không DB)
          - OrderStatusMatrix
          - OrderItemStatusMatrix
          - KitchenTicketStatusMatrix
          - KitchenItemStatusMatrix
          - CancellationAuthorityMatrix
          - StockReturnMatrix
          - PaymentReadinessMatrix
          - TableStatusMatrix
      → ApplyEffects (một nơi ghi DB)
          - Order / OrderItem / KitchenOrder / KitchenOrderItem
          - InventoryTransaction
          - OrderStatusHistory
          - RestaurantTable
      → Save + Commit
```

#### 6.7.2. Module đề xuất

| Module | Quyết định gì | Input chính | Output |
|---|---|---|---|
| `OrderStatusMatrix` | Order được chuyển trạng thái nào | order hiện tại, item active **của tất cả round**, ticket, payment, actor | target + allow/deny |
| `OrderItemStatusMatrix` | Dòng món được chuyển trạng thái nào | item hiện tại, ticket, actor | target + allow/deny |
| `KitchenTicketStatusMatrix` | Phiếu bếp được chuyển trạng thái nào | ticket, **toàn bộ món trên phiếu**, actor | target + allow/deny |
| `KitchenItemStatusMatrix` | Món bếp được chuyển trạng thái nào | item, ticket, actor | target + allow/deny |
| `CancellationAuthorityMatrix` | Ai được hủy món/đơn | role, item status, order status, đã có món done chưa, đã Paid chưa | allow/deny + lý do |
| `StockReturnMatrix` | Hủy/giảm thì hoàn kho hay ghi wastage | item status, reason code, phần đã nấu | `ReturnStock` / `NoReturn` / `Wastage` |
| `QuantityAdjustmentMatrix` | Giảm/tăng được bao nhiêu, tách dòng thế nào | item, prepared/cancelled, ticket, reason, actor | target quantity + split plan + allow/deny |
| `PaymentReadinessMatrix` | Đơn có được thanh toán không | order, **tất cả item active của tất cả round**, payment | allow/deny |
| `TableStatusMatrix` | Bàn Available/Occupied/Maintenance | open orders, status bàn | target status |

Lưu ý: `OrderItemStatusMatrix` **không nên là nguồn sự thật thứ hai** nếu `OrderItem` chỉ mirror `KitchenOrderItem`. Khi đó nên gọi là `OrderItemProjection` (map kitchen status → order item status), còn transition gốc nằm ở `KitchenItemStatusMatrix`.

#### 6.7.3. Một cửa vào duy nhất

```text
OrderLifecycleCoordinator
  CancelOrderItemAsync(command)
  ChangeKitchenItemStatusAsync(command)
  ChangeKitchenTicketStatusAsync(command)
  ChangeOrderStatusAsync(command)
  AddOrderItemsAsync(command)          // gọi thêm = round/ticket mới
  AdjustItemQuantityAsync(command)    // giảm/tăng, kể cả sau khi bếp nhận
  ChangePreparedQuantityAsync(command)// bếp báo đã nấu một phần
  ReportShortageAsync(command)        // thiếu nguyên liệu thực tế
  PayAsync(command)
```

Bên trong, tất cả gọi chung một hàm private:

```text
ExecuteAsync(command):
  tx = begin transaction (Serializable)
  aggregate = load(orderId)   // order + items + tickets + items + payment
  context = snapshot(aggregate, command.Actor, command.ReasonCode)
  decision = command.Matrix.Decide(context, command.Target)
  if !decision.Allowed: throw DomainException(decision.Code, decision.Message)
  ApplyEffects(aggregate, decision)
  SaveChanges()
  Commit()
```

`ApplyEffects` là **nơi duy nhất** set `Order.Status`, `OrderItem.Status`, `KitchenOrder.Status`, `KitchenOrderItem.Status`, cộng tiền, hoàn kho, ghi history, trả bàn.

#### 6.7.4. Ví dụ điều phối

**Hủy món từ POS:**

1. `CancelOrderItemCommand` → load order + item + kitchen item + ticket.
2. `CancellationAuthorityMatrix` kiểm tra role/trạng thái/Paid.
3. `StockReturnMatrix` quyết định hoàn kho hay không.
4. `OrderItemStatusMatrix` xác nhận `Pending → Cancelled`.
5. `ApplyEffects`:
   - item + kitchen item → `Cancelled`;
   - tính lại tiền;
   - `KitchenTicketStatusMatrix` suy ra ticket;
   - `OrderStatusMatrix` suy ra order;
   - history + table nếu hủy hết.
6. Commit.

**Đóng phiếu bếp `Completed`:**

1. `ChangeKitchenTicketCommand` → load ticket + toàn bộ món.
2. `KitchenTicketStatusMatrix` thấy còn món `Pending` → **deny** `KITCHEN_ITEMS_NOT_DONE`.
3. Nếu tất cả món done → cho `Completed`; `ApplyEffects` set ticket + order `Ready` + history.
4. Không có chuyện ticket terminal mà món còn `Pending`.
5. Gọi thêm sau đó → `AddOrderItemsAsync` tạo ticket mới; **không** reopen ticket này.

**POS đặt order `Ready`:**

1. `ChangeOrderCommand` → load order + items + ticket.
2. `OrderStatusMatrix` thấy món chưa done → **deny** `KITCHEN_ITEMS_NOT_DONE`.
3. Nếu hợp lệ → chỉ đổi order + history; hoặc theo phương án B, gọi luôn ticket matrix để sync.

**Gọi thêm:**

1. `AddOrderItemsCommand` → load order + tất cả round + bàn.
2. `OrderStatusMatrix` cho phép nếu order chưa `Paid/Cancelled` và bàn còn mở.
3. `ApplyEffects`:
   - tạo `OrderItem` mới + `KitchenOrder` mới (round mới);
   - trừ kho cho món mới;
   - order → `SentToKitchen` (hoặc `Preparing` nếu bếp bắt đầu ngay);
   - không đổi ticket/món cũ.
4. History ghi rõ “new round”.

**Giảm/tăng số lượng:**

1. `AdjustItemQuantityCommand` → load item + kitchen item + ticket + các dòng đã tách (nếu có).
2. `QuantityAdjustmentMatrix` kiểm tra:
   - order chưa `Paid`;
   - số lượng mới so với số đã nấu;
   - ticket đã `Completed` chưa (nếu tăng → tạo round mới).
3. `StockReturnMatrix` quyết định hoàn kho / wastage.
4. `ApplyEffects`:
   - nếu đã nấu một phần → tách dòng (hoặc cập nhật `PreparedQuantity`);
   - update quantity, tiền, kho, history;
   - nếu tăng sau ticket `Completed` → tạo round/ticket mới.
5. Recompute ticket/order qua cùng coordinator.

**Thiếu nguyên liệu thực tế:**

1. `ReportShortageCommand` → load item + kitchen item + ticket.
2. `QuantityAdjustmentMatrix` đặt `preparedQuantity = số làm được`; phần còn lại → `Cancelled` với reason `OutOfStock/PhysicalShortage`.
3. `StockReturnMatrix` trả `NoReturn/Wastage` cho phần thiếu; **không** hoàn kho ảo.
4. `ApplyEffects` update item/ticket/order + inventory adjustment + history.

#### 6.7.5. Quy tắc đặt tên để không lạm phát ma trận

- Tạo ma trận theo **lớp trạng thái** hoặc **loại quyết định**, **không** theo endpoint.
  - Đúng: `KitchenTicketStatusMatrix`, `CancellationAuthorityMatrix`.
  - Sai: `CancelItemEndpointMatrix`, `KitchenCompletedButtonMatrix`.
- Dùng hậu tố:
  - `...StatusMatrix` cho bảng from → to.
  - `...AuthorityMatrix` / `...Policy` cho điều kiện role/quyền.
  - `...ReadinessMatrix` cho điều kiện thanh toán.
  - `...ReturnMatrix` cho kho.
- Ma trận không được:
  - query DB;
  - mở transaction;
  - set entity;
  - gọi service khác.
- Ma trận chỉ nhận `Context` + `Target`, trả `Decision`.
- Controller không gọi ma trận trực tiếp; chỉ gọi coordinator.

#### 6.7.6. Rủi ro và cách giảm

| Rủi ro | Cách giảm |
|---|---|
| Orchestrator thành “god object” | Mỗi command một handler mỏng; coordinator chỉ điều phối + apply effects |
| Ma trận và DB lệch nhau | Context lấy từ aggregate đã load; test invariant sau mỗi command |
| Hai nguồn sự thật OrderItem/KitchenItem | Chọn một nguồn gốc; order item chỉ là projection |
| Refactor làm vỡ API | Giữ nguyên route/DTO; chỉ đổi service bên trong |
| Deadlock/transaction | Một transaction Serializable; khóa theo thứ tự order → ticket → item; retry |
| Không ai tuân thủ | Architecture test: cấm set status ngoài `ApplyEffects` |

#### 6.7.7. Lộ trình refactor an toàn

**Ưu tiên:** Bước 1 làm **ngay trong Phase 0**, không chờ hoàn thiện bước 2–5. Đây là cái khung để sửa C-01/C-02/C-03 và để các feature sau không tự ghi trạng thái.

1. **Bước 1 (Phase 0):** tạo `OrderLifecycleCoordinator` facade; route `OrderService.CancelItemAsync`, `OrderService.UpdateStatusAsync`, `KitchenService.UpdateOrderStatusAsync`, `KitchenService.UpdateItemStatusAsync` qua đó. Chưa đổi API/schema.
2. **Bước 2:** tách các ma trận hiện có (`OrderStatuses`, `KitchenStatuses`, `OrderCancellationPolicy`) thành module thuần; test exhaustive từng cặp from/to.
3. **Bước 3:** sửa C-01/C-02/C-03 bằng guard trong ma trận + coordinator.
4. **Bước 4:** thêm command gọi thêm/điều chỉnh số lượng/prepared-quantity/shortage/reopen; controller gọi coordinator.
5. **Bước 5:** thêm architecture test + regenerate tài liệu ma trận từ code để tránh lệch.

> Làm **mỏng trước, mở rộng sau**: Phase 0 chỉ cần một cửa vào + shared sync; Phase 2 mới thêm command mới; Phase 4 dọn các ma trận còn lại. Không big-bang refactor.

---

## 7. Kế hoạch triển khai đề xuất

### Phase 0 — Coordinator mỏng + hotfix đồng bộ (✅ đã xong 2026-10-20)

1. **Orchestrator mỏng:** đã thêm `Application/Services/OrderLifecycleCoordinator.cs`; route `OrderService.CancelItemAsync`, `OrderService.UpdateStatusAsync`, `KitchenService.UpdateOrderStatusAsync`, `KitchenService.UpdateItemStatusAsync` qua đó. Chưa tách hết matrix, chưa đổi API/schema.
2. **C-01:** `CancelItemAsync` dùng `effectiveStatuses` + shared sync; order/ticket được cập nhật sau khi hủy món, không đọc lại DB cũ.
3. **C-02:** guard ticket `Ready/Completed` theo item; ticket còn món `Pending/Preparing` bị từ chối.
4. **C-03:** guard order `Ready/Completed` theo cả order item và kitchen item; đơn legacy không có món được bỏ qua.
5. **Test:** tăng 7 test so với 498 test cũ = **505/505 pass**.
6. Không đổi schema, không đổi route, không đụng frontend.

**File chính:** `Application/Services/OrderLifecycleCoordinator.cs`, `OrderService.cs`, `KitchenService.cs`, `Program.cs`, `TestBase.cs`, `KitchenFlowTests.cs`, `OrderCancellationTests.cs`.

**Rủi ro đã xử lý:** hành vi `PATCH /orders/{id}/status` và ticket `Completed` thay đổi; test cũ đã được cập nhật. FE hiện không gọi ticket `Completed`/order status nên ít ảnh hưởng UI. **Không chặn gọi thêm**: order `Completed` vẫn mở, chỉ ticket hiện tại bị guard.

> Đây là **bước 1 của 6.7.7** (xem thêm 0.2): làm coordinator mỏng trước, không chờ hoàn thiện toàn bộ ma trận. Các ma trận tên được tách dần ở Phase 2/4.

### Phase 1 — Auth (hoãn theo quyết định, không code)

1. Giữ `Auth:Enforce=false`; người dùng tự bật thủ công khi sẵn sàng.
2. Khi bật: FE phải gửi `Authorization: Bearer`; nếu không, API có `[Authorize]` sẽ 401/403.
3. Không thêm cờ fail-closed / không sửa auth trong đợt này.
4. Ghi rõ trong tài liệu vận hành: role matrix chưa được enforce cho tới khi bật.

### Phase 2 — Gọi thêm, điều chỉnh số lượng & thiếu nguyên liệu (✅ đã xong 2026-10-20; Cách A)

1. **Đã chốt Cách A (tách dòng):** thêm `OrderItem.OrderItemGroupId` (nullable, có index) + migration `AddOrderItemGrouping`. Dòng gốc và dòng tách dùng chung group id; dữ liệu cũ null được xử lý như nhóm một dòng.
2. **Đã thêm `POST /orders/{id}/items`** — gọi thêm, luôn tạo `KitchenOrder`/round mới; không replace-all; order `Completed/Ready` được mở lại `SentToKitchen`, bàn vẫn `Occupied`.
3. **Đã thêm `PATCH /orders/{id}/items/{itemId}/quantity`** — nhận `quantity` hoặc `delta`; giảm phần chưa nấu trước, hoàn kho theo ledger; tăng khi ticket chưa xong thì tăng line + kitchen item, khi ticket `Completed/Ready` thì tạo round mới.
4. **Đã thêm `PATCH /management/kitchen/items/{id}/prepared-quantity`** — tách dòng thành phần `Completed` + phần còn lại; ledger được tách theo tỷ lệ, không đổi tổng tiền/tồn kho.
5. **Đã thêm `POST /management/kitchen/items/{id}/shortage`** — giữ phần làm được, huỷ phần thiếu với `OutOfStock/PhysicalShortage`, ghi `Wastage` zero-stock; không hard-block bếp.
6. **Chính sách kho theo reason:** `CustomerChange` hoàn phần chưa nấu; `OutOfStock/PhysicalShortage/KitchenError` ghi wastage, không hoàn kho ảo. Wastage được trừ khỏi “remaining consumption” để các lần giảm sau không hoàn vượt.
7. **Đã bổ sung C-12:** trạng thái order được tính lại từ **tất cả dòng của tất cả round**, không chỉ ticket vừa đổi; huỷ round mới không còn huỷ oan order còn món cũ.
8. **Test:** thêm 17 test Phase 2 (gọi thêm, round mới, quantity tăng/giảm, prepared split, shortage, Admin wastage, legacy null group, nhiều lần giảm) và 5 test C-07/C-08. Tổng **538/538 pass** (sau C-06/C-09/C-10/C-11), build **0 warning / 0 error**.
9. **Không sửa frontend**; route cũ giữ nguyên; chỉ bổ sung route/DTO/cột mới.

### Phase 3 — Audit & lịch sử (1–2 ngày)

1. Chuẩn hóa `CancelReasonCode` (gồm `CustomerChange`, `OutOfStock`, `PhysicalShortage`, `KitchenError`) — ✅ đã có ở request huỷ; còn lưu code thành cột audit trên `OrderItem`.
2. `OrderItemStatusHistory` — ✅ đã làm (C-10): bảng `order_item_status_histories` + `GET /orders/{id}/item-history`.
3. Ghi reason + phần đã nấu/đã hủy vào history.
4. Ghi inventory adjustment reason khi wastage.

### Phase 4 — Dọn mô hình (1 ngày)

1. C-06 reopen item — ✅ đã làm (`POST /management/kitchen/items/{id}/reopen`).
2. C-07 chặn ticket Cancelled sau Paid — ✅ đã làm.
3. C-11 bỏ `Accepted` item / CHECK constraint — ✅ đã làm (`AllowedTicket`/`AllowedItem` + 4 CHECK constraint).
4. C-12 hàm recalculate order status cho **tất cả round** — ✅ đã làm trong Phase 2 (`RefreshOrderStatusFromAllItemsAsync`).
5. C-13 refund; xác nhận `Completed` không terminal trong mọi tài liệu/UI.

---

## 8. Test plan chi tiết

### 8.1. Hủy từ POS

| # | Kịch bản | Kỳ vọng |
|---|---|---|
| 1 | Hủy 1 món Pending, còn 1 món Pending | order/ticket giữ `SentToKitchen`/`Pending`; tiền đúng; kho hoàn đúng |
| 2 | Hủy 1 món Pending, món còn lại `Ready` | order/ticket `Ready`; payment OK |
| 3 | Hủy món cuối cùng | order `Cancelled`; ticket `Cancelled`; bàn `Available`; kho hoàn |
| 4 | Hủy món `Preparing` bởi Waiter | 403 |
| 5 | Hủy món `Preparing` bởi Chef | OK; không hoàn kho; ticket/order cập nhật |
| 6 | Hủy món `Ready` bởi Chef | 403 |
| 7 | Hủy món `Ready` bởi Admin | OK; không hoàn kho |
| 8 | Hủy món đã `Cancelled` | 409 |
| 9 | Hủy món đơn `Paid` | 409 |
| 10 | Hủy món không tồn tại | 404 |

### 8.2. Hủy từ bếp

| # | Kịch bản | Kỳ vọng |
|---|---|---|
| 11 | Chef hủy ticket khi mọi món Pending | order/ticket `Cancelled`; hoàn kho; bàn Available |
| 12 | Chef hủy ticket khi có món Ready | 403 |
| 13 | Admin hủy ticket khi có món Ready | OK; không hoàn kho món Ready |
| 14 | Chef hủy 1 món Preparing | OK; ticket/order cập nhật |
| 15 | Hủy tất cả món từ bếp | ticket/order `Cancelled`; hoàn kho món Pending |

### 8.3. Guard trạng thái

| # | Kịch bản | Kỳ vọng |
|---|---|---|
| 16 | Ticket `Completed` khi món Pending | 409 |
| 16a | Ticket `Completed` khi mọi món done | OK; ticket terminal |
| 16b | Ticket `Ready` khi món Pending | 409 |
| 17 | Order `Ready` khi món Pending | 409 |
| 18 | Order `Completed` khi món Pending | 409 |
| 19 | Order `Completed` khi mọi món hiện tại done | OK; **không** terminal |
| 20 | Order `Completed` + gọi thêm 1 món | tạo ticket mới `Pending`; order `SentToKitchen`; ticket cũ giữ `Completed` |
| 21 | Order `Paid` + gọi thêm | 409 |
| 22 | Order `Preparing` khi ticket Pending | 409 hoặc sync ticket (theo quyết định) |

### 8.4. Giảm/tăng số lượng & thiếu nguyên liệu

| # | Kịch bản | Kỳ vọng |
|---|---|---|
| 23 | Giảm 3→2 trước khi bếp nhận | tiền/kho đúng; kitchen item quantity = 2 |
| 24 | Tăng 2→3 trước khi bếp nhận, đủ kho | tiền/kho đúng |
| 25 | Tăng vượt kho | 409 |
| 26 | Giảm sau khi bếp `Preparing`, **chưa nấu** | OK; hoàn kho phần giảm; kitchen item quantity giảm |
| 26a | Bếp đã nấu 1/2, khách giảm còn 1 | tách 1 `Completed` + 1 `Pending`; hủy phần `Pending`; không hoàn phần đã nấu |
| 26b | Giảm xuống dưới số đã nấu | 409; hoặc Admin override có wastage |
| 27 | Giảm sau khi đã hủy dòng khác | OK theo từng dòng; **không** dùng replace-all |
| 28 | Giảm về 0 | chuyển thành cancel; bắt buộc reason |
| 28a | Hệ thống báo đủ kho nhưng thực tế chỉ đủ 1/2 | bếp giảm còn 1; hủy 1 với `OutOfStock/PhysicalShortage`; **không** hoàn kho ảo |
| 28b | Tăng sau khi ticket `Completed` | tạo round/ticket mới; không mở lại món cũ |
| 28c | Giảm/tăng khi order `Paid` | 409 |

### 8.5. Thanh toán & bàn

| # | Kịch bản | Kỳ vọng |
|---|---|---|
| 29 | Đơn `Ready` + mọi món done | Paid; bàn Available |
| 29a | Đơn `Ready` + gọi thêm, món mới `Pending` | 409 cho tới khi mọi round done |
| 29b | Tất cả round done + add-on done | Paid; bàn Available |
| 29c | Giữa hai round, bàn còn đơn mở | bàn vẫn `Occupied` |
| 30 | Đơn `Ready` + còn món Pending | 409 |
| 31 | Đơn `Preparing` + mọi món done (dữ liệu lệch) | 409; sau recalculate → OK |
| 32 | Đơn `Paid` | 409 |
| 33 | Đơn `Cancelled` | 409 |
| 34 | Đơn 0 dòng món legacy | giữ hành vi cũ (nếu còn hỗ trợ) |

### 8.6. Auth

> Bảng dưới là hành vi **khi bật `Auth:Enforce=true`**. Hiện tại quyết định là giữ `false` và bật thủ công, nên các test này ở trạng thái **manual/skip**, không phải acceptance hiện tại.

| # | Kịch bản | Kỳ vọng |
|---|---|---|
| 35 | Không token, Production, hủy món Ready | 401/403 |
| 36 | Chef token, hủy món Ready | 403 |
| 37 | Admin token, hủy món Ready | OK |
| 38 | Waiter token, hủy món Preparing | 403 |
| 39 | Cashier token, hủy món Pending | 403 |
| 39a | `Enforce=false`, không token, hủy món Ready | hiện tại **role bị bỏ qua** (đã chấp nhận tạm thời) |

### 8.7. Gọi thêm / round mới

| # | Kịch bản | Kỳ vọng |
|---|---|---|
| 40 | Gọi thêm sau khi order `Completed` | tạo `KitchenOrder` mới `Pending`; order `SentToKitchen`; ticket cũ vẫn `Completed` |
| 41 | Gọi thêm khi order `Paid` | 409 |
| 42 | Gọi thêm khi order `Cancelled` | 409 |
| 43 | Gọi thêm nhiều món cùng lúc | một round/ticket mới chứa các món mới; món cũ không đổi |
| 44 | Gọi thêm rồi thanh toán khi món mới chưa xong | 409 |
| 45 | Gọi thêm rồi thanh toán khi tất cả round done | Paid; bàn Available |
| 46 | Gọi thêm khi bàn đã trả | 409; phải mở đơn mới |

---

## 9. Quyết định đã chốt (2026-10-20)

| Mã | Câu hỏi | Lựa chọn | Quyết định |
|---|---|---|---|
| D1 | Phiếu `Completed` khi món chưa xong | Chặn / tự đóng tất cả món | **Chặn**; chỉ ticket hiện tại, không liên quan gọi thêm |
| D2 | `PATCH /orders/{id}/status` cho `Preparing/Ready` | Chặn nếu bếp chưa xong / đồng bộ / bỏ | **Chặn + chỉ Kitchen tự đặt** |
| D3 | Giảm số lượng sau khi bếp đã nhận | Không hỗ trợ / tách dòng / theo dõi số lượng | **Hỗ trợ**; tách dòng hoặc `PreparedQuantity` |
| D4 | Admin hủy món `Ready/Completed` | Giữ / bỏ | **Giữ** (ngoại lệ đặc biệt, có audit) |
| D5 | Hoàn kho khi lý do là hết hàng/hỏng | Hoàn / không hoàn | **Không hoàn; ghi wastage/stock adjustment** |
| D6 | Thời điểm bật `Auth:Enforce` | Ngay / sau / để người dùng tự bật | **Hoãn; giữ `false`; người dùng tự bật thủ công** |
| D7 | Mở lại món đã tích xong | Có / không | **Có, qua command riêng** |
| D8 | Refund sau Paid | Làm / để sau | **Để sau, ghi rõ giới hạn** |
| D9 | `Order.Completed` có terminal? | Có / không | **Không**; chỉ `Paid`/`Cancelled` đóng hoàn toàn |
| D10 | Gọi thêm sau khi order `Completed` | Chặn / cho phép | **Cho phép**; tạo round/ticket mới, không mở lại món cũ |
| D11 | Giảm xuống dưới số phần đã nấu | Cho / chặn / override | **409**, hoặc Admin override có wastage; không “uncook” |
| D12 | Hệ thống báo đủ kho nhưng thực tế thiếu | Hard-block / cho bếp xử lý | **Cho bếp giảm/cancel phần thiếu**, reason `OutOfStock/PhysicalShortage`, không hoàn kho ảo |
| D13 | Ai được gọi thêm/điều chỉnh số lượng | Waiter/Admin / Chef | **Waiter/Admin**; Chef báo prepared/shortage |

---

## 10. Những hành vi là cố ý — không sửa nhầm

1. **Hủy cả đơn giữ nguyên `Subtotal/TotalAmount` lịch sử** — đúng thiết kế P1a/P1d.
2. **Chỉ hoàn kho món `Pending`** — đúng quy tắc “đã nấu thì không hoàn”.
3. **Bàn chỉ trả khi `Paid/Cancelled`** — `Completed` vẫn Occupied.
4. **Admin được hủy món `Ready/Completed`** — ngoại lệ đặc biệt, có test.
5. **Đơn `Paid` không hủy được** — chưa có refund.
6. **Legacy đơn 0 dòng món được thanh toán** — tương thích dữ liệu cũ.
7. **UI bếp gửi thẳng `Completed` từ `Pending`** — tương thích UI; không tự ý đổi nếu chưa sửa FE.
8. **FE không gửi Bearer token** — `Auth:Enforce=false` là quyết định tạm thời; người dùng tự bật thủ công. Khi bật, FE phải gửi token; trước đó role matrix chưa được enforce.
9. **Order `Completed` không terminal** — khách vẫn gọi thêm; chỉ `Paid/Cancelled` mới đóng hoàn toàn.
10. **Gọi thêm tạo round/ticket mới** — không mở lại ticket `Completed`, không đổi món cũ.
11. **Không dùng replace-all để giảm số lượng** — target dùng command theo dòng để giữ ledger/lịch sử.
12. **Thiếu nguyên liệu thực tế không hard-block** — bếp được giảm/cancel phần thiếu; kho ghi wastage/adjustment thay vì hoàn ảo.

---

## 11. Phụ lục — file/dòng chính

| Nội dung | File/dòng |
|---|---|
| Order matrix | `Application/Common/OrderStatuses.cs:33-41` |
| Order terminal/payable | `Application/Common/OrderStatuses.cs:47-83` |
| Kitchen matrix | `Application/Common/KitchenStatuses.cs:77-92` |
| Kitchen aggregate | `Application/Common/KitchenStatuses.cs:54-75` |
| Role hủy | `Application/Common/OrderCancellationPolicy.cs:18-66` |
| Hủy món POS | `Application/Services/OrderService.cs:817-929` |
| Sync POS bug | `Application/Services/OrderService.cs:900-919` |
| Order status PATCH | `Application/Services/OrderService.cs:940-1014` |
| Sửa món replace-all | `Application/Services/OrderService.cs:280-381` |
| Bếp đổi ticket | `Application/Services/KitchenService.cs:43-114` |
| Bếp đổi item | `Application/Services/KitchenService.cs:116-238` |
| Map ticket → order | `Application/Services/KitchenService.cs:280-287` |
| Payment readiness | `Application/Services/PaymentService.cs:18-93` |
| Hủy toàn đơn/hoàn kho | `Application/Common/OrderCancellation.cs:28-175` |
| Bàn | `Application/Services/Restaurant/RestaurantTableService.cs:228-261` |
| Auth bypass | `Infrastructure/Auth/EnforceableAuthorizationPolicyProvider.cs:32-39` |
| Auth config | `RestaurantFoodManagerSystem/appsettings.json:26-28` |
| Policy roles | `Infrastructure/Auth/AuthorizationServiceCollectionExtensions.cs:18-56` |
| Controller hủy món | `Controllers/OrdersController.cs:22-36` |
| Controller bếp | `Controllers/ManagementController.cs:280-300` |
| UI bếp toggle | `frontend/js/pages/kitchen.js:303-320` (chỉ đọc, không sửa) |

---

## 12. Kết luận ngắn

- **Order mở tới khi `Paid`**: `Completed` chỉ là “các món hiện tại đã phục vụ”; khách vẫn gọi thêm bằng round/ticket mới. Bàn chỉ trả khi `Paid/Cancelled`.
- Luồng hủy từ bếp sau sửa H-01 đã **đúng tiền và tương đối đủ đồng bộ**.
- Luồng hủy từ POS là điểm yếu nhất: **không cập nhật trạng thái đơn** và đọc trạng thái cũ từ DB → đơn kẹt.
- Hai lỗi trạng thái (ticket `Completed` sớm, POS đặt `Ready/Completed` sớm) tạo **trạng thái ảo**; thanh toán là nơi phát hiện, nhưng không tự sửa. Guard ticket vẫn cần, nhưng **không chặn gọi thêm**.
- Nghiệp vụ **giảm/tăng số lượng sau khi bếp nhận** chưa được hỗ trợ đúng; chỉ có replace-all trước khi bếp bắt đầu. Target: tách dòng/`PreparedQuantity`, xử lý cả thiếu nguyên liệu thực tế.
- Luật role hiện **không được bảo vệ** khi chạy thật vì Auth tắt và FE không gửi token; đây là quyết định tạm thời, người dùng tự bật.
- Hướng đi: **một command — một transaction — một orchestrator**, các ma trận tên quyết định thuần, **round cho gọi thêm**, **partial quantity cho giảm/tăng**, rồi mới tới refund.

---

## 13. Phase 2 — trạng thái triển khai thực tế (2026-10-20)

### 13.1. Schema & entity

- `Domain/Entity/Order/OrderItem.cs`: thêm `Guid? OrderItemGroupId`.
- `Infrastructure/Persistence/AppDbContext.cs`: map cột + index `IX_order_items_OrderItemGroupId` (không unique).
- Migration: `Migrations/20261020150925_AddOrderItemGrouping.cs` (+ designer/snapshot).
- Dữ liệu cũ có `OrderItemGroupId = null`: `LoadGroupAsync` coi là nhóm một dòng; lần điều chỉnh đầu tiên sẽ gán group id mới.

### 13.2. API mới (backend-only)

| Method | Route | Policy | Ghi chú |
|---|---|---|---|
| `POST` | `/api/orders/{id}/items` | `PosOperator` | Gọi thêm; luôn tạo round/ticket mới; trả `OrderDetailResponse` |
| `PATCH` | `/api/orders/{id}/items/{itemId}/quantity` | `PosOperator` | `quantity` hoặc `delta`; reason code; `allowWastage` cho Admin |
| `PATCH` | `/api/management/kitchen/items/{id}/prepared-quantity` | `KitchenDesk` | Tách phần đã nấu; `204` |
| `POST` | `/api/management/kitchen/items/{id}/shortage` | `KitchenDesk` | Báo thiếu thực tế; `204` |

- `AdjustOrderItemQuantityRequest` nhận `quantity` (đích) hoặc `delta`; `reasonCode` ∈ `CustomerChange`, `OutOfStock`, `PhysicalShortage`, `KitchenError`; `reason` bắt buộc khi đích = 0; `allowWastage` chỉ Admin.
- `PreparedQuantityRequest` nhận `preparedQuantity` (1..số lượng hiện tại).
- `ReportShortageRequest` nhận `preparedQuantity` (0..số lượng-1), `reasonCode` chỉ `OutOfStock/PhysicalShortage`.

### 13.3. Quy tắc nghiệp vụ đã hiện thực

1. **Gọi thêm:** order phải còn mở (`Pending/SentToKitchen/Preparing/Ready/Completed`); bàn phải `Occupied` và active; order `Paid/Cancelled` → 409; bàn đã trả → 409. Stock được kiểm tra + trừ cho từng món mới; order `Completed/Ready` mở lại `SentToKitchen`; ticket cũ không bị mở lại.
2. **Tăng số lượng:** nếu còn dòng chưa nấu và ticket chưa `Completed/Ready`, tăng line + kitchen item và trừ thêm kho; nếu không, tạo dòng + round mới. Tăng vượt kho → 409.
3. **Giảm số lượng:** chỉ tác động phần chưa nấu; hoàn kho theo ledger (sau khi trừ wastage/returns trước đó). Giảm xuống dưới số đã nấu → 409, trừ khi Admin + `allowWastage`, khi đó phần đã nấu ghi wastage và **không** hoàn kho.
4. **Giảm về 0:** bắt buộc `reason`, chuyển dòng thành cancel; nếu là dòng cuối thì order `Cancelled`, bàn `Available`, giữ `Subtotal/TotalAmount` lịch sử.
5. **Prepared quantity:** `P < quantity` tách dòng: dòng gốc còn phần chưa nấu, dòng mới `Completed` với P phần; ledger tách theo tỷ lệ; tổng tiền không đổi. `P = quantity` đánh dấu dòng `Completed`.
6. **Shortage:** `P > 0` tách phần đã làm được (`Completed`) + phần thiếu (`Cancelled`); `P = 0` huỷ cả dòng. Ghi `Wastage` zero-stock cho phần thiếu; không hoàn kho; không hard-block bếp.
7. **Order status:** `RefreshOrderStatusFromAllItemsAsync` đọc tất cả active order item + kitchen item của mọi round. `Completed` không terminal; chỉ `Paid/Cancelled` đóng hoàn toàn; bàn chỉ trả khi `Paid/Cancelled`.
8. **Wastage & ledger:** `Wastage` không đổi `Inventories.Quanlity` nhưng bị trừ khỏi “remaining consumption” trong `ResolveItemConsumptionAsync`/`LoadOrderRemainingAsync`/`LoadOrderConsumptionAsync`, nhờ đó nhiều lần giảm liên tiếp không hoàn vượt.

### 13.4. Test Phase 2

File `RestaurantFoodManagerSystem.Tests/Phase2LifecycleTests.cs` gồm 17 test:
- add-on sau `Completed` tạo ticket mới, mở lại order, bàn vẫn `Occupied`;
- add-on khi `Paid` hoặc bàn đã trả → reject;
- giảm/tăng trước khi bếp nhận; tăng vượt kho rollback;
- giảm khi `Preparing` chưa nấu; prepared split rồi giảm phần pending chỉ hoàn đúng phần đó;
- giảm dưới prepared: non-Admin 403, Admin + wastage OK;
- shortage có/không có phần làm được; shortage sau prepared split; không hoàn kho ảo;
- tăng sau ticket `Completed` tạo round mới;
- huỷ round mới không huỷ order còn round cũ đã xong;
- giảm về 0; dòng legacy `OrderItemGroupId = null`; nhiều lần giảm liên tiếp không over-return.

**Kết quả:** `dotnet build` **0 warning / 0 error**; `dotnet test` **527/527 pass** (522 test cũ + 5 test C-07/C-08).
> Cập nhật sau C-06/C-09/C-10/C-11: **538/538 pass** (thêm 11 test mới, 1 test HTTP cũ được thay bằng test chặn route).

### 13.5. Việc còn lại (không thuộc Phase 2)

- Đã xong: C-06 (reopen item), C-07 (chặn ticket Cancelled sau Paid), C-08 (reason code cho mọi luồng huỷ),
  C-09 (chặn route replace-all), C-10 (audit dòng món), C-11 (bỏ `Accepted` ở cấp món + CHECK constraint).
- Còn lại: C-04 auth enforcement (`Auth:Enforce=false`, cần FE gửi Bearer), C-13 refund sau `Paid`.
- Phase 3 (còn 1 phần nhỏ): chuẩn hoá reason code sâu hơn + ghi inventory adjustment reason cho wastage.
- Frontend chưa có UI dùng các route mới; BE là nguồn chuẩn, FE cần bám theo 3 route lệnh (gọi thêm / đổi số lượng / huỷ món).



---

## C-06 / C-09 / C-10 / C-11 — cập nhật 2026-10-20

- **C-06 (mở lại món đã xong):** thêm `POST /api/management/kitchen/items/{id}/reopen` (policy `KitchenDesk` = Chef/Admin).
  Chỉ mở được món đang `Ready`/`Completed` khi order chưa `Paid/Cancelled` và ticket chưa `Completed/Cancelled`.
  Món, ticket và order cùng quay về `Preparing`; `CompletedAt` của ticket được xoá khi ticket về `Preparing`; bàn vẫn `Occupied`.
- **C-09 (route thay toàn bộ dòng món):** `PUT /api/orders/{id}/items` đã bị chặn, trả `410 Gone` kèm hướng dẫn route thay thế.
  Lý do: replace-all xoá hẳn `OrderItem` cũ (mất `OrderItemId` ⇒ mất liên kết ledger theo dòng và mất dấu vết huỷ),
  ghi ledger ở cấp đơn (không có `OrderItemId`) nên huỷ món sau đó phải ước tính lại từ công thức, và không ghi history món.
  Thay bằng 3 route lệnh: `POST /api/orders/{id}/items` (gọi thêm), `PATCH /api/orders/{id}/items/{itemId}/quantity` (đổi số lượng),
  `POST /api/orders/{id}/items/{itemId}/cancel` (huỷ món). `OrderService.UpdateItemsAsync` còn giữ cho test, không expose qua HTTP.
- **C-10 (audit từng dòng món):** bảng mới `order_item_status_histories` + entity `OrderItemStatusHistory`
  (old/new status, reason, reasonCode, actor, thời điểm).
  Ghi ở: đổi trạng thái món bếp, huỷ ticket/món (POS và bếp), tăng/giảm số lượng (kể cả khi giảm dưới phần đã nấu),
  prepared split, shortage, huỷ đơn. Đọc qua `GET /api/orders/{id}/item-history`.
  Huỷ ticket từ bếp giờ ghi lý do thật vào history order thay vì `"Updated from kitchen"`.
- **C-11 (`Accepted` chỉ ở cấp phiếu):** tách `KitchenStatuses.AllowedTicket` (có `Accepted`) và `AllowedItem` (không có `Accepted`);
  `PATCH /api/management/kitchen/items/{id}/status` từ chối `Accepted`.
  Migration thêm CHECK constraint cho `orders`, `order_items`, `kitchen_orders`, `kitchen_order_items`
  và chuyển item `Accepted` cũ sang `Preparing` trước khi thêm constraint.
- **Migration:** `20261020155551_AddOrderItemHistoryAndStatusGuards` — **đã apply trên DB thật** (đã kiểm tra `__EFMigrationsHistory`).
- **Kiểm chứng:** build `0 warning / 0 error`; test **538/538 pass**.
- **Còn lại:** C-04 (`Auth:Enforce=false` — cần FE gửi Bearer), C-13 (refund sau `Paid`).
  Phía FE: hiện chưa có UI dùng các route mới (gọi thêm / đổi số lượng / huỷ món / prepared / shortage / reopen / item-history);
  BE là nguồn chuẩn, FE cần bám theo các route lệnh này.
