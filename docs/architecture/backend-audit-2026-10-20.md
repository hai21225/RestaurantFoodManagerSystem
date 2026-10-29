# Audit backend - 2026-10-20

> **Cập nhật Phase 2 + C-06/C-09/C-10/C-11 (2026-10-20):** đã bổ sung gọi thêm, điều chỉnh quantity,
> prepared-quantity, shortage, `OrderItemGroupId` + migration; C-06 mở lại món đã xong;
> C-07 chặn huỷ ticket sau `Paid`; C-08 huỷ theo `ReasonCode`; C-09 chặn route replace-all (`410 Gone`);
> C-10 audit từng dòng món (`order_item_status_histories`); C-11 bỏ `Accepted` ở cấp món + CHECK constraint.
> Build **0 warning / 0 error**, test **538/538 pass**.
> Chi tiết ở `status-matrix-cancellation-audit-2026-10-20.md` mục 13.

Phạm vi: **toàn bộ backend** (`RestaurantFoodManagerSystem/`) sau P1c/P1d, đối chiếu với
`docs/architecture/*` và skill `restaurant-backend`.

Tính chất: **audit read-only**. Không sửa source, không sửa `frontend/`, `wwwroot/`, không đổi schema.

Kiểm chứng nền tại thời điểm audit:

- `dotnet build`: **0 warning / 0 error**
- `dotnet test`: **486/486 pass**
- Working tree vẫn **chưa commit** (theo `progress-summary-2026-10-20.md`)

> Các lỗi dưới đây được xác định bằng đọc code + đối chiếu tài liệu. Chưa chạy reproduction thủ công
> qua HTTP; mức độ ưu tiên dựa trên khả năng xảy ra và thiệt hại nghiệp vụ.

## 0. Trạng thái xử lý (cập nhật cuối 2026-10-20)

**Đã sửa + có test hồi quy:**

| Mã | Nội dung |
|---|---|
| H-01 | Hủy món từ bếp giờ tính lại `Subtotal`, reset `DiscountAmount`, cập nhật `TotalAmount`; món cuối vẫn giữ tổng lịch sử |
| M-01 | `CreateOrderItemRequest.Note` có `[MaxLength(500)]` |
| M-02 | `KitchenItemStatusRequest.Reason` có `[MaxLength(500)]` + service chặn thêm |
| M-03 | Bỏ `MinLength(5)` của `CancelOrderItemRequest.Reason` (đúng tài liệu: chỉ bắt buộc + tối đa 500) |
| M-04 | `RestaurantTablesController`: bỏ catch-all ở `GET/POST`, trả `404` đúng cho `PUT/DELETE` khi không tìm thấy, không trả message DB thô |
| M-05 | `PaymentService` chặn thanh toán khi `order.Status = Paid` dù thiếu payment row (một phần của M-05) |
| M-06 | `PUT /restaurant-tables/{id}` nhận `areaId: null` để bỏ phân khu |
| M-08 | Thêm `MaxLength` cho Food/Topping/FoodSize/TableArea/Category và `CreatePaymentRequest.TransactionCode`; riêng `RestaurantTableDto.TableNumber` vẫn cần tách request DTO riêng (chưa làm) |
| M-10 | `IngredientService` chỉ map lỗi unique thật (SQL 2601/2627) thành "trùng tên" |
| L-01 | `Paging.ResolveSkip` dùng `long` và chặn `page` quá lớn thay vì overflow |
| C-01 | Hủy món từ POS dùng `effectiveStatuses` + `OrderLifecycleCoordinator`; ticket/order được đồng bộ, không kẹt `Preparing` |
| C-02 | Chặn ticket `Ready/Completed` khi còn món active chưa xong |
| C-03 | Chặn POS đặt order `Ready/Completed` khi bếp chưa xong; đơn legacy không có món được bỏ qua |

**Chưa sửa (cần chốt hoặc để đợt sau):** H-02, H-03, M-07, M-09, M-11, L-02…L-12, C-04, C-13.

**Đã sửa thêm (Phase 0 + Phase 2 + C-06/C-09/C-10/C-11):** C-01, C-02, C-03, C-05, C-06, C-07, C-08, C-09, C-10, C-11, C-12.

Build/test sau Phase 0: **0 warning / 0 error**, **505/505 test pass** (498 test cũ + 7 test hồi quy).

> Rà chuyên sâu ma trận trạng thái và luồng hủy món (sau P1c/P1d): xem
> `docs/architecture/status-matrix-cancellation-audit-2026-10-20.md`.
> Tài liệu đó bổ sung các lỗi đồng bộ POS ↔ bếp (C-01…C-05) và lộ trình sửa; không thay thế audit này.
> Phase 0 (2026-10-20) đã sửa C-01/C-02/C-03; C-04 (auth) và C-05 (quantity) vẫn để đợt sau.

---

## 1. Tóm tắt

| Mức | Số lượng | Ghi chú |
|---|---|---|
| Critical | 0 | Không có lỗi làm hỏng dữ liệu hàng loạt hoặc lộ secret |
| High | 3 | Sai tiền khi hủy món từ bếp; kẹt trạng thái bếp; lệch trạng thái đơn/bếp |
| Medium | 11 | Validation → 500, mapping lỗi sai, thiếu đồng bộ, contract mismatch |
| Low | 12 | Chất lượng dữ liệu, nhất quán API, hardening |

**3 việc nên sửa trước:**

1. `H-01` — Hủy món từ bếp không trừ tiền khỏi hóa đơn.
2. `H-02` — Phiếu bếp có thể `Completed` khi món còn `Pending`, sau đó không thể sửa và không thể thanh toán.
3. `H-03` — `PATCH /orders/{id}/status` đổi trạng thái đơn mà không đồng bộ món/phiếu bếp.

---

## 2. High

### H-01. Hủy món từ bếp không cập nhật `Subtotal` / `TotalAmount` / `DiscountAmount`

**File:** `Application/Services/KitchenService.cs:116-217` (nhánh hủy `149-181`)

**Hiện trạng**

`UpdateItemStatusAsync` khi nhận `status = Cancelled`:

- kiểm tra quyền, hoàn kho nếu món `Pending`,
- gọi `OrderCancellation.MarkItemCancelled(orderItem, ...)`,
- đặt `item.Status` / `orderItem.Status = Cancelled`,
- tính lại trạng thái phiếu,
- **không** đụng tới `order.Subtotal`, `order.DiscountAmount`, `order.TotalAmount`.

Trong khi đó luồng POS `OrderService.CancelItemAsync` **có** làm việc này
(`OrderService.cs:914-919`):

```csharp
order.Subtotal = activeItems.Sum(x => x.UnitPrice * x.Quanlity);
order.DiscountAmount = 0m;
order.TotalAmount = Math.Round(order.Subtotal + order.TaxAmount, 2, ...);
```

**Đối chiếu tài liệu**

`endpoint-baseline.md` (mục *Hủy món / hủy đơn*):

> Hủy món giảm `Subtotal/TotalAmount`, reset `DiscountAmount = 0`; món cuối bị hủy → đơn `Cancelled` và trả bàn.

`p1c-p1d-state-machine-design-2026-10-20.md` (quyết định #7):

> Discount khi hủy món: Reset `DiscountAmount = 0` để POS áp lại.

**Tác động**

- Khách/chef hủy 1 món từ màn bếp → món được đánh dấu `Cancelled` nhưng **tổng tiền vẫn giữ nguyên**.
- `PaymentService` thu `order.TotalAmount` (đã cũ) → **thu thừa tiền**.
- Nếu đơn có promotion, discount vẫn tính trên cơ sở cũ → sai thêm.
- Không có test nào phủ nhánh này: `KitchenFlowTests` chỉ hủy **tất cả** món (đơn chuyển `Cancelled`,
  tổng được giữ nguyên theo thiết kế), nên không quan sát được lỗi khi hủy **một phần**.

**Hướng sửa**

Tách helper dùng chung, ví dụ `OrderTotals.Recalculate(order, activeOrderItems)`, và gọi ở **cả**
`OrderService.CancelItemAsync` lẫn `KitchenService.UpdateItemStatusAsync`:

```csharp
if (orderItem is not null && order is not null)
{
    OrderCancellation.MarkItemCancelled(...);

    // Chỉ tính lại khi còn món active; món cuối bị hủy thì đơn chuyển Cancelled
    // và giữ nguyên tổng theo quy tắc lịch sử (giống OrderService.CancelItemAsync).
    var active = await _db.OrderItems
        .Where(x => x.OrderId == order.OrderId && !KitchenStatuses.IsCancelled(x.Status))
        .ToListAsync(ct);
    if (active.Count > 0)
    {
        order.Subtotal = active.Sum(x => x.UnitPrice * x.Quanlity);
        order.DiscountAmount = 0m;
        order.TotalAmount = Math.Round(order.Subtotal + order.TaxAmount, 2, MidpointRounding.AwayFromZero);
        order.UpdatedAt = now;
    }
}
```

Lưu ý: phải tính trên **item còn active**, không dựa vào `orderItem` vừa set `Cancelled` mà quên
`SaveChanges` (EF vẫn theo dõi nên ổn, nhưng tường minh hơn).

**Test đề xuất**

- Tạo đơn 2 món (100k + 50k), hủy 1 món từ kitchen item API → `Subtotal = 100k`, `TotalAmount = 100k + tax`, `DiscountAmount = 0`.
- Đơn có promotion, hủy 1 món → discount bị reset, tổng đúng.
- Hủy món cuối từ bếp → đơn `Cancelled`, tổng **giữ nguyên lịch sử** (theo tài liệu), bàn về `Available`.

---

### H-02. Phiếu bếp có thể `Completed` khi món còn `Pending` → phiếu terminal, đơn không thanh toán được

**File:** `Application/Common/KitchenStatuses.cs:77-84`; `Application/Services/KitchenService.cs:84-104`; `Application/Services/PaymentService.cs:49-52`

**Hiện trạng**

Ma trận phiếu bếp cho phép nhảy cóc:

```csharp
[Pending]   = [Accepted, Preparing, Ready, Completed, Cancelled]
[Accepted]  = [Preparing, Ready, Completed, Cancelled]
[Preparing] = [Ready, Completed, Cancelled]
```

`UpdateOrderStatusAsync` chỉ đổi `kitchenOrder.Status`, **không** đổi trạng thái các
`KitchenOrderItem`. Sau đó phiếu `Completed` là terminal (`IsTerminal`), nên:

- `UpdateItemStatusAsync` từ chối mọi thay đổi món (`KitchenService.cs:134-137`),
- `MapOrderStatus("Completed")` trả `Ready` cho đơn,
- `PaymentService` yêu cầu **mọi món active phải `Ready`/`Completed`** (`PaymentService.cs:49-52`),
- các món vẫn `Pending` → `409 "Order is not ready for payment"`,
- không còn đường nào đưa món sang `Ready` (phiếu đã terminal), chỉ còn cách hủy đơn.

**Đối chiếu tài liệu**

`p1c-p1d-state-machine-design-2026-10-20.md` **cố ý** cho `Pending → Completed` để tương thích UI.
Vì vậy đây là **design gap**, không phải vi phạm contract. Nhưng hậu quả vận hành là thật.

**Tác động**

- Một cú bấm "Hoàn tất" trên phiếu bếp (hoặc client cũ) làm đơn **kẹt vĩnh viễn**: không sửa món, không thu tiền.
- Nhân viên buộc phải hủy đơn → mất doanh thu, sai lịch sử kho (món đã nấu nhưng đơn bị hủy).
- Test hiện tại không phủ tổ hợp này.

**Hướng sửa (chọn 1, cần chốt với chủ dự án)**

1. **Chặn ở service (khuyến nghị, không đổi schema):** khi `status = Completed`, kiểm tra
   `allItems.Where(!cancelled).All(IsDone)`; nếu chưa → `InvalidOperationException` (409).
   Vẫn giữ `Pending → Completed` trong ma trận cho tương thích, nhưng chỉ cho phép khi món đã xong.
2. **Đồng bộ khi completed:** set toàn bộ món active sang `Completed` trong cùng transaction
   (hành vi "đóng phiếu = hoàn tất tất cả"), rồi mới set phiếu.
3. Nếu muốn giữ nguyên: ghi rõ trong `endpoint-baseline.md` rằng đây là hành vi hợp lệ và
   POS phải tự mở lại phiếu — nhưng hiện **không có API mở lại**, nên không khả thi.

**Test đề xuất**

- Phiếu có 1 món `Pending`; `PATCH kitchen-orders/{id}/status {Completed}` → 409 (nếu chọn 1)
  hoặc món thành `Completed` (nếu chọn 2).
- Sau khi phiếu `Completed`, gọi hủy món → 409 như hiện tại (giữ nguyên).

---

### H-03. `PATCH /orders/{id}/status` đổi trạng thái đơn mà không đồng bộ phiếu bếp / món

**File:** `Application/Services/OrderService.cs:940-1014`; `Application/Common/OrderStatuses.cs:33-41`; `Application/Services/PaymentService.cs:49-52`

**Hiện trạng**

`UpdateStatusAsync` chỉ:

- validate trạng thái và transition,
- (nếu hủy) gọi `OrderCancellation.ApplyAsync`,
- ghi `OrderStatusHistory`,
- `SaveChanges`.

Không hề cập nhật `KitchenOrder.Status` hay `KitchenOrderItem.Status`.

Ma trận đơn cho phép `Pending → Preparing/Ready` và `Preparing → Completed`:

```csharp
[Pending]   = [SentToKitchen, Preparing, Ready, Cancelled]
[Preparing] = [Ready, Completed, Cancelled]
[Ready]     = [Completed, Cancelled]
```

**Tác động**

- Đơn `Ready` nhưng món vẫn `Pending` → `PaymentService` từ chối thanh toán (đúng theo quy tắc
  "mọi món active phải xong"), trong khi UI POS hiển thị đơn đã sẵn sàng.
- Đơn `Completed` nhưng món chưa xong → chưa thu được tiền; đơn đã terminal theo ma trận
  (`Completed` chỉ còn `Cancelled`) nên POS không sửa được nữa. Bếp vẫn có thể đóng món
  (phiếu chưa terminal), nhưng nếu bếp không biết thì đơn treo ở `Completed` **chưa thanh toán**.
- Bếp nhìn phiếu vẫn `Pending/Preparing` dù POS đã đóng đơn → hai màn hình nói hai câu chuyện.

**Hướng sửa (cần chốt hợp đồng)**

- Nếu POS được phép đặt trạng thái đơn: **đồng bộ một chiều** order → kitchen ticket/item
  trong cùng transaction (ví dụ `Ready` → item active `Ready`, ticket `Ready`), và ghi history.
- Nếu không: **chặn** `PATCH` sang `Preparing/Ready/Completed` khi phiếu bếp/món chưa tương ứng,
  trả 409 kèm thông báo rõ.
- Không nên để hai luồng cùng ghi trạng thái mà không có quy tắc ưu tiên.

**Test đề xuất**

- Đơn `Pending`, món `Pending`; `PATCH /orders/{id}/status {Ready}` → 409 hoặc món + phiếu cùng `Ready`.
- Sau đó thanh toán phải thành công (nếu chọn đồng bộ) hoặc vẫn bị chặn có thông báo đúng (nếu chọn chặn).

---

## 3. Medium

### M-01. `CreateOrderItemRequest.Note` thiếu `MaxLength(500)` → 500 khi ghi DB

**File:** `Application/DTO/Requests/Order/OrderRequests.cs:16`; DB `AppDbContext.cs` (`order_items.Note` max 500)

`Note` không có validation. Chuỗi > 500 ký tự lọt qua model binding, tới `SaveChangesAsync` thì
SQL Server truncation → `DbUpdateException` → không controller nào bắt → **500**.

Áp dụng cho cả:

- `POST /api/orders`
- `PUT /api/orders/{id}/items`

**Sửa:** `[MaxLength(500)] public string? Note { get; set; }`.

**Test:** POST order với `note` 501 ký tự → 400, không phải 500.

---

### M-02. `KitchenItemStatusRequest.Reason` thiếu `MaxLength(500)`

**File:** `Application/DTO/Requests/Management/KitchenRequests.cs:8`

So sánh: `KitchenStatusRequest` (cấp phiếu) **có** `[MaxLength(500)]`, nhưng request cấp món thì không.
`KitchenService.UpdateItemStatusAsync` dùng `request.Reason.Trim()` trực tiếp (`:176`) và không kiểm độ dài
→ lý do > 500 → truncation → **500**.

**Sửa:** thêm `[MaxLength(500)]`, và (defense in depth) kiểm độ dài trong service như
`OrderService.CancelItemAsync:826-827`.

---

### M-03. `CancelOrderItemRequest.Reason` yêu cầu tối thiểu 5 ký tự, trái tài liệu

**File:** `Application/DTO/Requests/Order/OrderRequests.cs:72`

```csharp
[Required, MinLength(5), MaxLength(500)]
```

`endpoint-baseline.md` chỉ yêu cầu **bắt buộc** và **tối đa 500 ký tự**, không có tối thiểu.
Lý do hợp lệ như `"hết"`, `"khách"`, `"sai"` bị trả 400 dù nghiệp vụ chấp nhận.

**Sửa:** bỏ `MinLength(5)`, giữ `[Required, MaxLength(500)]`.

**Test:** hủy món với `reason = "hết"` → 200/204.

---

### M-04. `RestaurantTablesController` bắt mọi exception và trả sai status / lộ message nội bộ

**File:** `Controllers/RestaurantTables/RestaurantTablesController.cs`

- `GetById` (`:31-48`): `catch (Exception ex) → NotFound(new { message = ex.Message })`.
  Lỗi DB/timeout/bug đều thành **404**, message thô trả về client.
- `Create` (`:51-68`): `catch (Exception ex) → BadRequest(new { message = ex.Message })`.
  Lỗi SQL/unique/FK thành **400** kèm chi tiết DB.
- `Delete` (`:127-160`): bàn không tồn tại trả **400** (`:136`) thay vì **404**; các nhánh sau
  cũng trả 400/409 không nhất quán.

**Tác động:** client không phân biệt được "không tìm thấy" với "server lỗi"; rò rỉ tên bảng/cột
trong message; khó debug vì lỗi thật bị che thành 404/400.

**Sửa:**

- Bỏ `catch (Exception)`; chỉ bắt các exception nghiệp vụ đã biết.
- `GetById`: trả `NotFound()` không body.
- `Delete` khi không tìm thấy: `NotFound()`.
- Thêm global exception handler (xem L-10) để lỗi không lường trước thành 500 + ProblemDetails.

---

### M-05. `PaymentService` bỏ qua kiểm tra sẵn sàng khi đơn không có dòng món; kiểm tra trùng chỉ dựa vào bảng `payments`

**File:** `Application/Services/PaymentService.cs:28-52`

```csharp
if (await _db.Payments.AnyAsync(x => x.OrderId == request.OrderId && x.Status == Paid, ct))
    throw ... "Order is already paid";

var itemStatuses = ...;
if (itemStatuses.Count > 0)   // ← đơn 0 dòng món thì bỏ qua toàn bộ
{
    ...
}
```

Hai vấn đề:

1. **Legacy bypass:** đơn không có `OrderItem` nào (dữ liệu cũ) được thanh toán mà không kiểm
   `order.Status` có `Ready/Completed` hay không. Chỉ cần `order.Status != Cancelled` và
   `TotalAmount > 0` là thu tiền được — kể cả đơn `Pending`.
2. **Double-pay khi dữ liệu lệch:** nếu `order.Status = Paid` nhưng **không có** payment row
   (import tay, xóa payment, migration lỗi), lần thanh toán sau tạo payment thứ hai. Unique filtered
   index `(OrderId, Status='Paid')` không bảo vệ vì chưa có row nào.

**Sửa:**

- Luôn kiểm `OrderStatuses.IsPayable(order.Status)` (trừ khi có lý do legacy rõ ràng và ghi chú).
- Thêm `if (order.Status == Paid) throw "Order is already paid"` trước khi tạo payment.
- Cân nhắc chặn luôn đơn 0 dòng món thay vì bỏ qua kiểm tra; docs hiện cho phép legacy nên cần chốt.

**Test:** order `Pending`, 0 item, `TotalAmount > 0` → 409. Order `Paid` không có payment row → 409.

---

### M-06. Không thể bỏ khu vực của bàn (`AreaId = null` bị bỏ qua)

**File:** `Application/Services/Restaurant/RestaurantTableService.cs:101-104`

```csharp
if (r.AreaId.HasValue)
{
    await EnsureAreaExistsAsync(...);
    x.AreaId = r.AreaId;
}
```

`RestaurantTableDto.AreaId` là `int?`, nhưng `null` không bao giờ được gán ⇒ **không có cách nào**
đưa bàn về "chưa phân khu" qua `PUT`. UI gửi `areaId: null` sẽ thấy bàn vẫn ở khu cũ.

**Sửa:** luôn gán `x.AreaId = r.AreaId;` và chỉ `EnsureAreaExistsAsync` khi có giá trị.

---

### M-07. `ApplyPromotionAsync` so sánh `StartAt/EndAt` với UTC nhưng nhận giá trị client không chuẩn hóa

**File:** `Application/Services/OrderService.cs:1040-1043`

```csharp
x => x.Id == promotionId && x.IsActive && x.StartAt <= now && x.EndAt >= now
```

`PromotionRequest.StartAt/EndAt` là `DateTime` không ràng buộc `Kind`; `PromotionService` lưu nguyên
giá trị client gửi. Client gửi giờ VN (UTC+7) → lệch 7 giờ so với `DateTime.UtcNow`.

Tài liệu đã ghi đây là **known open item**, nhưng hậu quả là promotion bật/tắt sai giờ.

**Sửa (không đổi schema):**

- Chuẩn hóa ở DTO/service: nếu `Kind == Unspecified`, coi là giờ VN → `DateTime.SpecifyKind(...).ToUniversalTime()`
  (hoặc dùng helper `VietnamTime` đã có trong repo).
- Ghi rõ trong `endpoint-baseline.md` định dạng thời gian nhận vào.

---

### M-08. Nhiều DTO thiếu `MaxLength` → truncation 500

**File / trường:**

| DTO | Trường | DB limit |
|---|---|---|
| `CreateFoodRequest` | `FoodName`, `FoodDescription`, `FoodImageUrl` | 150 / 1000 / 500 |
| `UpdateFoodRequest` | tương tự | 150 / 1000 / 500 |
| `CreateToppingRequest` | `ToppingName` | 100 |
| `CreateFoodSizeRequest` | `SizeName` | 50 |
| `TableAreaRequest` | `AreaName`, `Description` | 100 / 500 |
| `RestaurantTableDto` (dùng làm body Create/Update) | `TableNumber` | 20 |
| `CreatePaymentRequest` | `TransactionCode` | 100 |
| `CategoryDTO` | không có validation nào | 100 / 500 |

Hiện chỉ `[Required]`/`[Range]`, không giới hạn độ dài. Chuỗi dài → SQL truncation → 500 (hoặc
message DB thô nếu controller bắt `Exception`).

**Sửa:** thêm `[MaxLength(n)]` khớp `AppDbContext`; với `RestaurantTableDto`, nên tách
`CreateRestaurantTableRequest`/`UpdateRestaurantTableRequest` thay vì dùng response DTO làm input
(tránh client gửi `Status`, `AreaName`, `TableId`).

**Test:** mỗi endpoint nhận chuỗi vượt giới hạn → 400.

---

### M-09. `CategoryDTO.DisplayOrder` là `bool` trong khi entity/DB là `int`

**File:** `Application/DTO/Responses/Food/CategoryResponse.cs:6`; entity `Category.DisplayOrder` (`int`)

- API trả `true/false`, client gửi số (`0,1,2`) → **400 model binding**.
- Không thể biểu diễn thứ tự > 1; sắp xếp danh mục bị mất.
- Tài liệu đã ghi là known open item vì cần đổi contract/schema.

**Sửa:** đổi DTO sang `int DisplayOrder`; nếu cần tương thích ngược, thêm
`[JsonNumberHandling]`/custom converter tạm thời và ghi rõ trong baseline.

---

### M-10. `IngredientService` gán mọi `DbUpdateException` thành "trùng tên"

**File:** `Application/Services/Inventory/IngredientService.cs:38-41, 71-73, 132-134`

```csharp
catch (DbUpdateException)
{
    throw new InvalidOperationException($"Ingredient '{name}' already exists");
}
```

Bắt **tất cả** lỗi DB: truncation, FK violation, unique index khác, deadlock… rồi báo "đã tồn tại".
Ngoài ra ở `CreateAsync`, entity lỗi vẫn nằm trong `ChangeTracker` sau khi catch, không rollback/clear
→ nếu cùng scope còn `SaveChanges` khác có thể lỗi dây chuyền.

**Sửa:**

- Kiểm tra trùng tên **trước** khi add (đã có ở `CreateAsync:23`), và chỉ map lỗi unique thật
  (SQL error number 2601/2627) sang 409.
- Các `DbUpdateException` khác: log rồi `throw` (để global handler trả 500) hoặc map 400 nếu là validation.
- Trong catch của `CreateAsync`: `_db.Entry(entity).State = EntityState.Detached;` hoặc rollback transaction.

---

### M-11. Định nghĩa dashboard/report dễ gây hiểu nhầm

**File:** `Application/Services/ReportService.cs:80-83, 127-130`

- `openOrderCount` tính mọi đơn **khác `Paid` và `Cancelled`**, bao gồm `Completed`.
  Đơn đã phục vụ xong nhưng chưa thu tiền vẫn nằm trong "đơn đang mở" — có thể đúng nghiệp vụ
  (chưa thanh toán) nhưng tên `OpenOrderCount` dễ hiểu là "đang phục vụ".
- `lowStockCount` dùng `Quanlity <= MinimumStock`; nguyên liệu có `MinimumStock = 0` và tồn 0
  bị tính là "sắp hết" dù ngưỡng chưa cấu hình.

Cả hai đã được ghi nhận là known open trong tài liệu; giữ nguyên nếu thống nhất nghiệp vụ.
Nếu sửa: đổi tên field/thêm mô tả, hoặc loại `MinimumStock = 0` khỏi cảnh báo.

---

## 4. Low

### L-01. `Paging.ResolveSkip` có thể overflow

**File:** `Application/Common/Paging.cs:21`

```csharp
return (page - 1) * pageSize;
```

`page = int.MaxValue` → overflow thành số âm → `OFFSET` âm → SQL error 500.
**Sửa:** dùng `long` và chặn `page` tối đa, hoặc `checked` + trả 400.

---

### L-02. Không chặn topping trùng trong cùng một món

**File:** `Application/Services/OrderService.cs:420-440`

Hai phần tử cùng `ToppingId` được cộng tiền hai lần và tạo hai dòng `OrderItemToppings`.
Có thể là chủ ý ("2 phần topping") nhưng UI thường gộp số lượng. Nên chốt: hoặc gộp theo
`ToppingId`, hoặc trả 400 khi trùng. Ghi rõ trong baseline.

---

### L-03. `CreateCoreAsync` chuyển `Pending → SentToKitchen` không ghi `OrderStatusHistory`

**File:** `Application/Services/OrderService.cs:248-250`

Trong khi `UpdateItemsAsync` có ghi history (`:360-373`). Lịch sử trạng thái của đơn mới tạo bị thiếu
bước đầu tiên → report/audit không đầy đủ.

**Sửa:** thêm `OrderStatusHistory` với `OldStatus = Pending`, `NewStatus = SentToKitchen`,
`Note = "Order created and sent to kitchen"`.

---

### L-04. `GET /orders` — `ItemCount` tính cả món đã hủy

**File:** `Application/Services/OrderService.cs:139`

```csharp
ItemCount = _db.OrderItems.Count(x => x.OrderId == o.OrderId)
```

Sau khi hủy món, danh sách vẫn báo 3 món trong khi hóa đơn chỉ còn 2. Nên đếm
`x.Status != Cancelled` (hoặc trả thêm `ActiveItemCount`).

---

### L-05. `PurchaseOrderService` — idempotency receive trả 404; create dùng isolation mặc định và không chặn dòng trùng

**File:** `Application/Services/Inventory/PurchaseOrderService.cs`

- `ReceiveAsync` (`:175-176`): phiếu đã `Received` → `return false` → controller map **404**.
  Đây là thao tác lặp (idempotent) nên nên trả **200/204** hoặc **409**, không phải "không tìm thấy".
- `CreateAsync` (`:112`): `BeginTransactionAsync(ct)` **không** `Serializable`, trong khi `Cancel`/`Receive`
  dùng `Serializable`. Create không đụng tồn kho nên rủi ro thấp, nhưng việc supplier/ingredient
  bị ngừng kích hoạt đồng thời có thể lọt qua bước kiểm tra — nên dùng cùng mức isolation cho nhất quán.
- `CreateAsync` (`:127-135`) không kiểm `IngredientId` trùng trong `Items` → một phiếu có 2 dòng cùng
  nguyên liệu; `Receive` cộng tồn hai lần (đúng số dòng, nhưng khó đối soát).

**Sửa:** đổi receive đã nhận thành 409/idempotent; dùng `IsolationLevel.Serializable`; gộp hoặc từ chối
dòng trùng.

---

### L-06. Nhà cung cấp không kiểm trùng tên; endpoint supplier không map lỗi

**File:** `Application/Services/SupplierService.cs:20`; `Controllers/ManagementController.cs:84-101`

- Không có `AnyAsync` kiểm tên trùng; DB chỉ có index **không unique** (`AppDbContext.cs:996`)
  → cho phép 2 nhà cung cấp cùng tên.
- `CreateSupplier`/`UpdateSupplier` trả thẳng `Ok(...)`, không bắt `DbUpdateException`/`ArgumentException`
  → lỗi DB thành 500.

Tài liệu đã ghi "suppliers name non-unique" là known open. Nếu chấp nhận, chỉ cần thêm try/catch map 409/400
và ghi rõ trong baseline.

---

### L-07. Ledger của `PUT /orders/{id}/items` không gắn `OrderItemId`

**File:** `Application/Services/OrderService.cs:722-755`

`ApplyStockDeltas` tạo `InventoryTransaction` với `ReferenceId = orderId` nhưng **không** `OrderItemId`.
Hệ quả: sau khi sửa món, hủy từng món phải fallback ước lượng theo recipe
(`OrderService.cs:846` → `LoadOrderRemainingAsync`), có thể hoàn kho lệch nếu recipe thay đổi.

Đã ghi trong baseline là known gap. Sửa đúng cần map delta ↔ item mới; nên làm khi có yêu cầu
đối soát kho theo món.

---

### L-08. Auth: không rate limit/lockout, không thu hồi JWT khi đổi/reset mật khẩu

**File:** `Application/Features/Auth/AuthService.cs:20-35, 131-197`

- Login sai liên tục không bị chặn → brute force (BCrypt giảm tốc nhưng vẫn nên lockout).
- Đổi mật khẩu/reset không vô hiệu hóa token cũ; token còn hạn vẫn dùng được.
- Kiểm tra "admin cuối cùng" (nếu có) không chạy trong transaction → race.

Đã ghi known open (JWT revocation/security stamp). Đề xuất: thêm `SecurityStamp`/`TokenVersion`
vào user, kiểm trong `OnTokenValidated`, và lockout đếm theo `userId + IP`.

---

### L-09. `GET /restaurant-tables/{id}` ghi DB

**File:** `Application/Services/Restaurant/RestaurantTableService.cs:12-20`

`GetRestaurantTableByIdAsync` gọi `SaveRefreshedStatusAsync` (ghi trạng thái bàn), trong khi
`GetAllRestaurantTablesAsync` chỉ tính toán, không ghi (`:31`).

- GET có side effect: gọi lặp/retry có thể ghi đè trạng thái do request khác vừa set.
- Không nhất quán giữa list và detail.

**Sửa:** bỏ ghi trong GET; nếu cần refresh, làm ở background job hoặc endpoint riêng.

---

### L-10. Không có global exception handler / ProblemDetails

**File:** `Program.cs` (không có `UseExceptionHandler`/`AddProblemDetails`)

Mỗi controller tự bắt exception với format khác nhau (`e.Message` string, `{ message }`, `NotFound()`).
Exception không được bắt → 500 mặc định, có thể lộ stack trace ở môi trường dev.

**Sửa:** thêm `AddProblemDetails()` + `UseExceptionHandler()`; map:
`ArgumentException → 400`, `KeyNotFoundException → 404`, `UnauthorizedAccessException → 403`,
`InvalidOperationException/ConcurrencyConflictException → 409`, còn lại `500` (không lộ chi tiết).
Sau đó có thể xóa các `catch (Exception)` thủ công.

---

### L-11. `Auth:Enforce=false` khiến toàn bộ `[Authorize]` thành no-op

**File:** `Program.cs:121-126`

Hiện tại mọi policy đều cho qua. Đây là chủ ý cho dev (frontend chưa gửi Bearer token), nhưng:

- Nếu deploy nhầm cấu hình → **toàn bộ API mở**, kể cả xóa/sửa.
- Log cảnh báo đã có, nhưng không có "fail fast" ngoài môi trường Development.

**Sửa:** chỉ cho phép `Enforce=false` khi `ASPNETCORE_ENVIRONMENT=Development`; môi trường khác
ném lỗi khởi động nếu tắt enforcement.

---

### L-12. Idempotency của `POST /orders` chỉ nằm trong RAM

**File:** `Application/Common/OrderIdempotencyStore.cs:16-28`; đăng ký `Program.cs:103`

Store là `ConcurrentDictionary` TTL 24h, đăng ký `Singleton`:

- Restart process → mất toàn bộ key; client retry sau restart tạo **đơn trùng + trừ kho lần hai**.
- Chạy nhiều instance → mỗi instance một cache; retry rơi vào instance khác cũng tạo đơn trùng.
- Không có cơ chế dọn theo bộ nhớ ngoài ngưỡng 2048 (chấp nhận được).

Tài liệu đã ghi known open. Sửa đúng cần bảng/cột idempotency trong DB (schema change) → phải được duyệt.

---

## 5. Việc **không phải** bug (đã cố ý, đừng "sửa")

Ghi lại để tránh regression trong các vòng sau:

1. **Đơn bị hủy toàn bộ giữ nguyên `Subtotal/TotalAmount`** — theo tài liệu, để giữ lịch sử.
   Chỉ đơn còn món active mới phải cập nhật tổng.
2. **`Pending → Completed` của phiếu bếp được phép trong ma trận** — chủ ý tương thích UI (xem H-02;
   nếu siết thì siết ở điều kiện service, không xóa entry ma trận nếu chưa chốt).
3. **`Completed` vẫn nằm trong `Updatable` của order** — POS cần đóng đơn sau khi phục vụ.
4. **Chỉ hoàn kho món `Pending`** — `Preparing/Ready/Completed` coi như đã tiêu hao.
5. **Đơn `Paid` không cho hủy món/đơn** — chưa có luồng refund; trả 409 là đúng hiện trạng.
6. **`orders` không có `PromotionId`** — known gap, không tự thêm cột khi chưa được duyệt.
7. **Dashboard tính `Completed` là đơn mở** — cần chốt nghiệp vụ, không tự đổi.
8. **Auth bypass khi `Enforce=false`** — chủ ý dev; chỉ cần chặn ở môi trường khác.

---

## 6. Thứ tự sửa đề xuất

| Ưu tiên | Việc | Lý do |
|---|---|---|
| P0 | H-01 (tổng tiền khi hủy món từ bếp) | Sai tiền trực tiếp, ảnh hưởng thu ngân |
| P0 | H-02 (chặn/đồng bộ `Completed` khi món chưa xong) | Kẹt đơn không thanh toán được |
| P1 | H-03 (đồng bộ hoặc chặn `PATCH /orders/{id}/status`) | Lệch trạng thái POS ↔ bếp |
| P1 | M-01…M-03 (validation 400 thay vì 500, contract reason) | Lỗi 500 do input, dễ tái hiện |
| P1 | M-04 + L-10 (map lỗi, global handler) | Giảm rò rỉ nội bộ, API nhất quán |
| P2 | M-05 (payment readiness/double-pay), M-06 (AreaId) | Đúng đắn dữ liệu |
| P2 | M-08 (MaxLength hàng loạt), M-09 (Category.DisplayOrder) | Chất lượng contract |
| P3 | M-07, M-10, M-11, L-01…L-12 | Hardening, known gaps |

Mỗi thay đổi nên kèm test hồi quy; chạy lại `dotnet test` (baseline 486) và `dotnet build`.

---

## 7. Kiểm chứng

- Không thay đổi file source nào trong quá trình audit.
- Không thay đổi `frontend/`, `wwwroot/`.
- Không tạo migration, không đổi schema.
- File này chỉ là ghi chú audit; mọi sửa code cần được duyệt riêng.

---

## 7. Cập nhật Phase 2 + C-06/C-09/C-10/C-11 (2026-10-20)

| Mã | Nội dung | Test hồi quy |
|---|---|---|
| C-05 | `POST /orders/{id}/items` (gọi thêm → round/ticket mới), `PATCH /orders/{id}/items/{itemId}/quantity` (tách dòng, hoàn kho phần chưa nấu, wastage khi giảm dưới phần đã nấu) | `Phase2LifecycleTests` (17 test) |
| C-06 | `POST /api/management/kitchen/items/{id}/reopen` — mở lại món `Ready/Completed` khi order chưa `Paid/Cancelled` và ticket chưa `Completed/Cancelled`; món/ticket/order về `Preparing` | `C06C10C11Tests.ReopenItem_*` (4 test) |
| C-07 | Chặn huỷ ticket sau `Paid`; ticket vẫn được `Completed` để đóng vận hành | `OrderCancellationTests` |
| C-08 | `ReasonCode` cho 4 luồng huỷ: `CustomerChange` hoàn kho, `OutOfStock`/`PhysicalShortage`/`KitchenError` ghi wastage | `OrderCancellationTests` (5 test) |
| C-09 | `PUT /api/orders/{id}/items` bị chặn → `410 Gone`; dùng 3 route lệnh (gọi thêm / đổi số lượng / huỷ món) | `ResponseDtoHttpTests.PutOrderItems_IsRetired_Returns410Gone_AndLeavesOrderUntouched` |
| C-10 | Bảng `order_item_status_histories` + `GET /api/orders/{id}/item-history`; ghi audit ở mọi luồng đổi trạng thái / giảm số lượng / prepared / shortage | `C06C10C11Tests` (5 test) |
| C-11 | `KitchenStatuses.AllowedTicket` / `AllowedItem`; món không còn `Accepted`; 4 CHECK constraint ở DB | `C06C10C11Tests` (2 test) |
| C-12 | Trạng thái order tính lại từ tất cả dòng của tất cả round | `Phase2LifecycleTests` |

- Migration `20261020155551_AddOrderItemHistoryAndStatusGuards` (bảng `order_item_status_histories` + 4 CHECK constraint) — **đã apply trên DB thật**.
- Build **0 warning / 0 error**; test **538/538 pass**.
