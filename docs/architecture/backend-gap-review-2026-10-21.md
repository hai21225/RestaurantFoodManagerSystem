# Rà soát khoảng trống backend — 2026-10-21

**Phạm vi:** backend-only (`RestaurantFoodManagerSystem/`), đối chiếu `.opencode/skills/restaurant-backend/SKILL.md` và các file trong `docs/architecture/`.
**Kiểu:** audit read-only — lần này **không sửa code**.
**Nguyên tắc:** **đúng trước, đủ sau**. Không đề xuất tính năng chưa cần, không đề xuất kiến trúc mới.

**Kiểm chứng lúc đọc:** `dotnet build` → **0 warning / 0 error**; `dotnet test` → **538/538 pass**.

---

## 0. Tóm tắt một dòng

Backend hiện là một **POS + kho + bếp chạy được** (115 route, 25 service, 29 bảng, audit đầy đủ ở cấp món).
Nhưng có **3 lỗi đúng–sai liên quan tiền/tồn kho** và **1 nhóm thiếu chức năng lõi**: **VAT + phí dịch vụ + cấu hình quán** chưa tồn tại (`TaxAmount` **luôn = 0**, không có nơi nào gán), **hoá đơn không có audit `→ Paid`**, và **chưa có cách hoàn/refund đơn đã thanh toán**.

---

## 1. Bức tranh hiện tại — đã đủ những gì

| Miền | Trạng thái | Ghi chú |
|---|---|---|
| Danh mục món (category/food/size/topping) | ✅ đủ | soft-delete + restore, giá snapshot, `/api/menu` cho POS |
| Bàn & khu vực | ⚠️ đủ, thiếu 1 | có bảo trì, soft-delete, 1 đơn mở/bàn — **thiếu chuyển bàn** |
| Đơn & vòng bếp | ✅ đủ | create / gọi thêm / đổi SL / huỷ món / huỷ đơn / history |
| Bếp | ⚠️ đủ, thiếu 1 | ticket, item status, prepared, shortage, reopen — **thiếu bảng hiển thị + tên bàn/phục vụ** |
| Kho | ⚠️ đủ mức cơ bản | nguyên liệu, ledger, điều chỉnh, PO, công thức — **thiếu danh sách tồn thấp & kiểm kê** |
| Khuyến mãi | ⚠️ còn thiếu 1 thao tác | đã có gỡ khỏi đơn + `PromotionId`/snapshot; targeting vẫn **không có API quản lý** (C4) |
| Thuế VAT + phí dịch vụ | ✅ **đủ** (Đợt "Tiền đúng") | `OrderTotalsCalculator` + bảng `restaurant_settings` |
| Cấu hình quán / hoá đơn | ✅ **đủ** (Đợt "Tiền đúng") | bảng `restaurant_settings` (1 dòng) + `GET/PUT /api/management/settings` |
| Giao dịch bàn & hoá đơn (chuyển bàn, chia/gộp bill) | ❌ thiếu chuyển bàn | "Cách A" chỉ là tách *dòng món*, không phải tách *hoá đơn* |
| Ca làm việc / két tiền | ❌ **thiếu hoàn toàn** | không có bảng ca, không có mở/đóng ca |
| Thanh toán | ⚠️ bản ghi nội bộ | đánh dấu `Paid` tức thì; **không refund, không partial, không pending** |
| Báo cáo | ⚠️ thiếu nhiều | chỉ doanh thu + dashboard |
| Phân quyền | ✅ đủ | 5 role cố định, policy gắn ở controller |
| Xác thực | ⚠️ còn thiếu | `Auth:Enforce=false`; thiếu refresh/logout/khoá tài khoản |

---

## 2. P0 — LỖI đúng–sai, phải sửa (không thêm tính năng)


> **Trạng thái 2026-10-21 (sau đợt "Tiền đúng"):** build **0 warning / 0 error**, test **569/569 pass**.
> **Đã sửa: 12/12 lỗi P0** — P0-01, P0-02, P0-03, P0-04, P0-05, P0-06, P0-07, P0-08, P0-09, P0-10, P0-11, P0-12
> + dọn dead code (đợt trước) + settings/VAT/phí DV/khuyến mãi 2 chiều/idempotency/`DisplayOrder`→`int` (đợt "Tiền đúng").
> **Còn lại:** không còn lỗi P0. Phần P1 còn lại nằm ở mục 3 (Đợt 3–4).

Đây là phần **quan trọng nhất**. Toàn bộ đều là fix nhỏ, không đổi hợp đồng API, không cần hạ tầng mới.

### P0-1 Hoàn kho cho món **đang nấu** ⇒ tồn kho bị thổi phồng — ✅ **ĐÃ SỬA 2026-10-21 (C-13)**

> **Đã xử lý** trong đợt luật bếp↔kho (`docs/architecture/kitchen-stock-rule-2026-10-21.md`):
> `AdjustItemQuantityAsync` tách `notStarted` / `inProgress`; món `Preparing` **không hoàn kho** mà ghi `Wastage`;
> món `Preparing` cũng bị **khóa với POS** (chỉ Chef/Admin được giảm SL).
> Test: `ReduceQuantity_WhilePreparingButNotCooked_DoesNotReturnStock_RecordsWastage`.
> Phần còn lại của P0-1 (bếp/POS huỷ món `Preparing` **im lặng, không ghi ledger**) cũng đã sửa trong cùng đợt.

- **Vị trí:** `Application/Services/OrderService.cs:534-540` (`pendingItems` = mọi món **chưa `IsDone`**, tức `Pending` **và `Preparing`**), rồi `:715` / `:749` gọi `ReturnStockForItemAsync` / `ReturnStockForPartialItemAsync`.
- **Vì sao sai:** `KitchenStatuses.IsDone` chỉ gồm `Ready|Completed` (`Application/Common/KitchenStatuses.cs:44-46`). Món `Preparing` (bếp đã bắt đầu nấu, nguyên liệu đã xuất kho) bị xếp vào "chưa nấu" ⇒ **hoàn kho cho đồ đang nấu**.
- **Bằng chứng mâu thuẫn nội bộ:** `CancelItemAsync` yêu cầu **cả hai** trạng thái đều `Pending` mới hoàn kho (`OrderService.cs:1426`). Cùng một nghiệp vụ nhưng 2 luồng cho 2 kết quả.
- **Sửa tối giản:** tách `pendingItems` thành 2 nhóm — `Pending` (hoàn kho được) và `Preparing` (ghi `Wastage`/không hoàn kho). Không cần đổi schema.
- **Test cần thêm:** giảm SL món đang `Preparing` ⇒ **không** tăng tồn kho.

### P0-2 Thanh toán **không** ghi vào lịch sử trạng thái đơn — ✅ **ĐÃ SỬA**

- **Vị trí:** `Application/Services/PaymentService.cs:69` gán thẳng `order.Status = OrderStatuses.Paid`.
- **Vì sao sai:** toàn bộ repo chỉ có **2 chỗ** ghi `OrderStatusHistories` (`OrderLifecycleCoordinator.cs:348` và `:455`). `PaymentService` **không inject** coordinator nên không ghi được.
- **Hậu quả:** `GET /api/orders/{id}/history` **không có dòng `Ready → Paid`** ⇒ mất dấu vết thu tiền. Cũng vi phạm chính skill: *"Define valid status transitions and record actor/time for important transitions"*.
- **Trở ngại kỹ thuật:** `OrderStatuses.TransitionMatrix` **không có `Paid`** ⇒ coordinator hiện **không thể** xử lý `Ready → Paid` (sẽ trả `false`).
- **Sửa tối giản:** thêm `Paid` vào ma trận transition (`Ready|Completed → Paid`) **hoặc** thêm `OrderLifecycleCoordinator.ApplyPaidAsync(order, actor)` rồi gọi từ `PaymentService`. Ưu tiên cách 2 vì `Paid` là trạng thái **terminal** cần luật riêng (không ai được quay lui).

### P0-3 Tạo đơn không ghi history — ✅ **ĐÃ SỬA**

- **Vị trí:** `OrderService.cs:244` (`Pending`) và `:254` (`SentToKitchen`) gán thẳng.
- **Hậu quả:** lịch sử đơn bắt đầu từ lần thay đổi *đầu tiên*, không có dòng "created". Test hiện đang **pin** hành vi này (`Tests/BatchA2IntegrityTests.cs:91` → `Assert.Empty(...)`) ⇒ sửa cần **cập nhật test**.
- **Sửa:** ghi 1 dòng history khi tạo (`OldStatus = ""` hoặc `Pending`, `NewStatus = SentToKitchen`).

### P0-4 Không có dòng audit cho **món được tạo mới** — ✅ **ĐÃ SỬA**

- **Vị trí:** `OrderService.AddOrderItemsAsync` (~`:1088-1127`) không gọi `OrderItemMutations.RecordItemHistory`.
- **Hậu quả:** `order_item_status_histories` chỉ bắt đầu từ lần *sửa* đầu tiên ⇒ không biết dòng món lúc nào vào bếp.
- **Sửa:** ghi 1 dòng `"" → Pending` cho mỗi dòng mới (cả lúc tạo đơn lẫn gọi thêm).

### P0-5 `promotions.StartAt/EndAt` lệch **7 giờ** — ✅ **ĐÃ SỬA 2026-10-21 (Đợt "Tiền đúng")**

- **Vị trí:** `PromotionService.cs:27-28` lưu **nguyên** giá trị client gửi; `OrderService.cs:1626` so sánh với `DateTime.UtcNow`; cột là `datetime2` **không có timezone**; **không dùng** `VietnamTime` (helper đã có sẵn, `Application/Common/VietnamTime.cs`).
- **Hậu quả:** admin VN nhập `2026-10-21T00:00:00` (giờ VN) ⇒ lưu thẳng ⇒ so với UTC ⇒ **cửa sổ khuyến mãi lệch 7 tiếng**.
- **Sửa tối giản:** quy ước duy nhất cho API: **client gửi giờ VN (không offset) → server quy đổi sang UTC khi lưu** (dùng `VietnamTime.ToUtc`). Ghi rõ quy ước vào XML doc + `PromotionResponse` trả kèm mốc giờ UTC.
- **ĐÃ LÀM:** quy ước **"API nhận giờ VN → lưu UTC"** đã được chủ dự án chốt 2026-10-21 và đã triển khai:
  `VietnamTime.ToUtcFromApi` dùng ở `PromotionService.Create/Update`; `PromotionResponse` trả cả `startAt`/`endAt` (giờ VN)
  lẫn `startAtUtc`/`endAtUtc`; migration trừ 7 giờ cho dữ liệu `promotions` cũ.

### P0-6 ReportShortageAsync có thể tạo dòng **số lượng âm** — ✅ **ĐÃ SỬA**

- **Vị trí:** `KitchenService.cs:653-657` validate `PreparedQuantity` so với `kitchenItem.Quanlity`, nhưng `:665` tính `cancelledQuantity = orderItem.Quanlity - request.PreparedQuantity`.
- **Hậu quả:** nếu `orderItem.Quanlity < kitchenItem.Quanlity` (drift dữ liệu) ⇒ `cancelledQuantity < 0` ⇒ dòng `Cancelled` số lượng âm + ledger/wastage sai.
- **Sửa:** thêm 1 guard `if (cancelledQuantity < 0) throw new ArgumentException(...)`.

### P0-7 Huỷ món chọn **phiếu bếp ngẫu nhiên** — ✅ **ĐÃ SỬA**

- **Vị trí:** `OrderService.cs:1409-1410` — `FirstOrDefaultAsync(x => x.OrderItemId == item.Id)` **không ORDER BY**.
- **Hậu quả:** sau khi tách dòng (`prepared-quantity` tạo nhiều `KitchenOrderItem` cho 1 `OrderItem`) sẽ huỷ **nhầm** phiếu, `RecalculateTicketStatus` tính sai phiếu.
- **Đã có sẵn cách đúng:** `OrderItemMutations.FindKitchenItemAsync` (`OrderItemMutations.cs:140-147`) lấy `OrderByDescending(x => x.Id).First()` — **nhưng method này hiện không được gọi ở đâu cả (dead code)**.
- **Sửa:** dùng lại `FindKitchenItemAsync`.

### P0-8 Huỷ đơn huỷ **cả phiếu/món đã Completed** — ✅ **ĐÃ SỬA**

- **Vị trí:** `Application/Common/OrderCancellation.cs:107-116` gán thẳng `Status = Cancelled` cho mọi kitchen item/ticket của đơn, **bypass** terminal guard của `RecalculateTicketStatus`.
- **Hậu quả:** phiếu đã đóng vận hành bị đánh dấu huỷ ⇒ bếp thấy phiếu cũ quay lại.
- **Sửa:** bỏ qua item/ticket đã `IsTerminal` (giữ nguyên trạng thái, chỉ huỷ phần còn mở).

### P0-9 PaymentService dùng isolation **yếu nhất** module — ✅ **ĐÃ SỬA**

- **Vị trí:** `PaymentService.cs:59` — `BeginTransactionAsync(ct)` (mặc định = READ COMMITTED), trong khi `OrderService`/`KitchenService` dùng `Serializable` ở mọi transaction. Ngoài ra đơn được đọc ở `:20` và toàn bộ kiểm tra `:28-56` nằm **ngoài** transaction.
- **Hậu quả:** chống trùng thanh toán thực sự dựa vào unique index lọc `(OrderId, Status='Paid')` (`AppDbContext.cs:830-832`), không dựa vào logic. Rủi ro thấp nhưng đường tiền nên mạnh nhất, không yếu nhất.
- **Sửa tối giản:** đổi thành `IsolationLevel.Serializable` + đọc/kiểm tra order **trong** transaction. Không cần hạ tầng mới.

### P0-10 ConcurrencyConflictException thành **500** thay vì 409 — ✅ **ĐÃ SỬA**

- **Vị trí:** `AppDbContext.cs:1264-1291` dịch `DbUpdateConcurrencyException` → `ConcurrencyConflictException` (thiết kế đúng), nhưng `OrdersController` / `PaymentsController` / `ManagementController` (phần bếp) **không catch** ⇒ mất update trả 500.
- **Sửa:** catch `ConcurrencyConflictException` → 409 ở các controller còn thiếu (đã làm ở `ManagementController:113,150` và `RestaurantTablesController`).

### P0-11 Bếp ghi sai **người thực hiện** vào audit — ✅ **ĐÃ SỬA**

- **Vị trí:** `KitchenService.cs:130, 476, 662` dùng `_currentUser.UserId ?? order.CreatedBy`.
  Trong khi `OrderService` dùng `_currentUser.ResolveExistingActorAsync(...)` (`OrderService.cs:570, 1420, 1580`) — hàm này ưu tiên claim `sub`, và nếu không có token thì mới lấy `ChangedBy` trong body (có kiểm tra user tồn tại).
- **Hậu quả (hiện đang xảy ra):** `PreparedQuantityRequest.ChangedBy` và `ReportShortageRequest.ChangedBy` **được nhận vào DTO nhưng bị bỏ qua hoàn toàn**. Khi không có JWT (chế độ dev hiện tại, `Auth:Enforce=false`), mọi thao tác của bếp (`prepared-quantity`, `shortage`, `reopen`) đều bị ghi vào `order_item_status_histories.ChangedBy` = **người POS đã tạo đơn**, không phải đầu bếp.
  ⇒ Audit trả lời sai câu hỏi "ai đã huỷ món / báo thiếu nguyên liệu?".
- **Sửa tối giản:** `KitchenService` dùng `ResolveExistingActorAsync` giống `OrderService` (giữ nguyên ưu tiên token ⇒ không đổi hành vi khi đã bật `Auth:Enforce`).
- **Ghi chú:** `OrderItemMutations.RecordItemHistory` bỏ qua **im lặng** khi `changedBy <= 0` (`:58-61`) ⇒ nếu actor không resolve được thì dòng audit **biến mất không ai biết**. Nên **log warning** thay vì `return` im lặng.

### P0-12 Lịch sử đơn **mất mã lý do** (`ReasonCode`) — ✅ **ĐÃ SỬA 2026-10-21 (Đợt "Tiền đúng")**

- **Vị trí:** `Domain/Entity/Order/OrderStatusHistory.cs` — **không có** trường `ReasonCode`; chỉ có `Note` (text tự do).
- **Bối cảnh:** C-08 đã chốt 4 mã lý do huỷ: `CustomerChange` (khách đổi → **hoàn kho**), `OutOfStock` / `PhysicalShortage` / `KitchenError` (**ghi wastage**, không hoàn kho).
- **Hậu quả:**
  - Ở cấp **món**: có lưu (`OrderItemStatusHistory.ReasonCode`) ✔
  - Ở cấp **đơn**: `OrderCancellation.ApplyAsync` **tính** `effectiveReasonCode` (`OrderCancellation.cs:37`) và dùng nó để quyết định hoàn kho vs wastage (`:73-89`), **nhưng không lưu lại** — dòng `order_status_histories` chỉ có `Note` text tự do.
  - ⇒ Khi tra "đơn bị huỷ vì khách đổi hay vì hết hàng?" **không phân biệt được** ⇒ mất thông tin quyết định nghiệp vụ đã ghi.
- **Sửa:** thêm cột `ReasonCode` (max 50) cho `order_status_histories` + ghi `effectiveReasonCode` khi ghi dòng history. **Gộp cùng đợt migration** ở Đợt 2.
- **ĐÃ LÀM:** cột `order_status_histories.ReasonCode` (nvarchar 50, nullable) đã có trong migration
  `20261021041728_AddSettingsTotalsPromotionSnapshot`; `OrderLifecycleCoordinator.ApplyOrderStatusAsync` ghi mã lý do
  vào dòng history; `OrderStatusHistoryResponse.reasonCode` trả ra API.

> **Đã loại khỏi danh sách (sai khi rà lại):** "chuỗi quá dài ⇒ lỗi DB 500". Đã kiểm chứng: `KitchenStatusRequest.Reason` có `[MaxLength(500)]` và `[ApiController]` tự validate ModelState ⇒ trả **400** đúng. `UpdateOrderStatusRequest.Note` được `OrderService.cs:1542-1543` kiểm tra thủ công ⇒ 400. Các DTO user thiếu `[MaxLength]` nhưng `UserService` đã kiểm tra thủ công (`UserService.cs:254-276, 327-344`) ⇒ 400. **Không có lỗi này.**

---

## 3. P1 — THIẾU chức năng lõi, cần để vận hành nhà hàng thật

### 3.1 Nhóm A — "Tiền đúng" (chặn mọi thứ khác)

#### A1. Thuế VAT + phí dịch vụ + cấu hình quán — ✅ **ĐÃ SỬA 2026-10-21 (Đợt "Tiền đúng")**

- **Hiện trạng:** `Order.TaxAmount` (`Domain/Entity/Order/Order.cs:8`) **không có bất kỳ câu lệnh gán nào trong toàn repo** ⇒ **luôn = 0**. Grep `ServiceCharge` → **0 kết quả**. Grep `class .*Setting` → **0 kết quả** (không có bảng settings).
- **Công thức tiền hiện tại (6 chỗ ghi, KHÔNG nhất quán):**
  | Chỗ | Công thức |
  |---|---|
  | `OrderService.cs:253` (tạo đơn) | `Total = Subtotal` ← **không cộng Tax** |
  | `OrderService.cs:354` (update items) | `Total = Subtotal + Tax` |
  | `OrderItemMutations.cs:136` | `Total = Subtotal + Tax` |
  | `OrderService.cs:1478` (huỷ món) | `Total = Subtotal + Tax` |
  | `KitchenService.cs:274` (huỷ món từ bếp) | `Total = Subtotal + Tax` |
  | `OrderService.cs:1652` (khuyến mãi) | `Total = Subtotal − Discount + Tax` |
  ⇒ Khi bật VAT, đơn tạo ra sẽ **sai** (thiếu thuế) cho tới lần sửa món đầu tiên.
- **Cần làm (tối giản, đúng đủ):**
  1. Bảng `settings` — **1 bản ghi duy nhất** (id cố định): `TaxRate`, `ServiceChargeRate`, `TaxIncluded` (giá đã gồm VAT chưa), `RestaurantName`, `Address`, `Phone`, `TaxCode` (MST), `Currency`.
  2. `GET /api/management/settings` + `PUT /api/management/settings` (Admin).
  3. **Một** hàm dùng chất `OrderTotalsCalculator` đặt ở `Application/Common/` tính `Subtotal → Discount → Tax → ServiceCharge → TotalAmount`, và **gọi lại ở cả 6 chỗ** trên.
  4. `Payment.Amount` lấy `order.TotalAmount` ⇒ tự khớp sau khi có công thức.
- **Không over-engineer:** 1 tỉ lệ VAT + 1 tỉ lệ phí DV (không theo từng nhóm món / theo ngày / nhiều mức thuế), không cần bảng `tax_rates`, không cần "thuế theo từng loại hàng".
- **ĐÃ LÀM:** bảng `restaurant_settings` (1 dòng, `Id = 1`) + `GET/PUT /api/management/settings` (Admin) +
  `OrderTotalsCalculator` (một nguồn sự thật cho công thức tiền, thay cho 6 chỗ gán `TotalAmount` lệch nhau).
  Luật đã chốt: **giá menu đã gồm VAT** (`taxIncluded = true`) ⇒ VAT chỉ tách ra để in, không cộng thêm;
  **phí dịch vụ** tính trên tiền hàng sau giảm giá và luôn cộng thêm. Mặc định 0%/0% ⇒ hành vi cũ giữ nguyên.

#### A2. Gỡ khuyến mãi khỏi đơn — ✅ **ĐÃ SỬA 2026-10-21 (Đợt "Tiền đúng")**

- **Hiện trạng:** chỉ có `PATCH /api/orders/{id}/promotion/{promotionId}` (áp). Không có đường gỡ ⇒ chỉ gỡ được **gián tiếp** khi sửa món, vì mọi luồng sửa đơn reset `DiscountAmount = 0` (`OrderItemMutations.cs:134`, `OrderService.cs:353,1476`, `KitchenService.cs:272`).
- **Cần làm:** `DELETE /api/orders/{id}/promotion/{promotionId}` → tính lại `TotalAmount` (dùng `OrderTotalsCalculator` ở A1).
- **Không over-engineer:** 1 promotion / đơn, không cần giỏ khuyến mãi, không cần nhiều promotion cùng lúc.
- **ĐÃ LÀM:** `DELETE /api/orders/{id}/promotion/{promotionId}` ⇒ `discountAmount = 0`, `promotionId = null`,
  tính lại `TaxAmount`/`ServiceChargeAmount`/`TotalAmount`; gỡ nhầm khuyến mãi khác trả `400`.

#### A3. Lưu `PromotionId` + snapshot lên `orders` — ✅ **ĐÃ SỬA 2026-10-21 (Đợt "Tiền đúng")**

- **Hiện trạng:** `Order` **không có** cột `PromotionId` (ghi rõ tại `OrderService.cs:1612-1615`). Đơn không có FK nào tới `promotions`.
- **Hậu quả:** không tra lại được đơn dùng promotion nào; sửa/xoá promotion **đổi nghĩa của đơn cũ**.
- **Cần làm:** thêm `orders.PromotionId` (nullable FK) + `orders.PromotionDiscountType`/`PromotionDiscountValue` (snapshot). **Gộp cùng đợt migration với A1** để không phải migrate 2 lần.
- **ĐÃ LÀM:** `orders.PromotionId` (FK → `promotions`, `ON DELETE SET NULL`) + `orders.PromotionDiscountType` +
  `orders.PromotionDiscountValue` đã có trong migration `20261021041728_AddSettingsTotalsPromotionSnapshot`.
  Sửa/xoá khuyến mãi sau này **không** làm đổi nghĩa của đơn cũ vì đơn giữ snapshot.

#### A4. Refund / huỷ đơn đã thanh toán

- **Hiện trạng:** `Paid` là terminal (`OrderStatuses.cs:48-51`); `OrderLifecycleCoordinator.cs:389-394`, `KitchenService.cs:86-92,217-221` chặn mọi thao tác sau `Paid`. **Không có** entity/endpoint refund/void.
- **Hậu quả:** đơn thu sai (nhập nhầm, khách đổi ý, thu tiền 2 lần) **chỉ sửa được bằng tay trong DB** ⇒ lệch ledger kho và báo cáo doanh thu.
- **Cần làm (tối thiểu):** `POST /api/orders/{id}/refund` (Admin/Cashier) — tạo `Payment` với `Status = "Refunded"` + `Amount = -order.TotalAmount` (hoặc cột `RefundedAmount` trên `Payment`), mở lại đơn về trạng thái trước khi trả tiền, ghi `OrderStatusHistory`.
- **ĐÃ LÀM 2026-10-21:** `POST /api/payments/{paymentId}/refund` (Cashier/Admin) — `Payment.Status` → `Refunded`, `RefundedAt` được ghi, **đơn mở lại về `Ready`** (chờ thu lại) + dòng lịch sử `Paid → Ready`, bàn trở lại `Occupied`. Giao dịch MoMo thật ⇒ gọi API refund của MoMo trước khi ghi nhận.
- **Không over-engineer:** chưa cần refund **một phần**, chưa cần payment provider/webhook, chưa cần "giảm giá sau thanh toán". 1 nút "huỷ đơn đã thu" là đủ.

#### A5. Idempotency cho các lệnh ghi tiền/số lượng — ✅ **ĐÃ SỬA 2026-10-21 (Đợt "Tiền đúng")**

- **Hiện trạng:** **chỉ** `POST /api/orders` có `Idempotency-Key` (`OrderService.cs:173-197`, store in-memory `Application/Common/OrderIdempotencyStore.cs`). Các lệnh khác — gọi thêm, đổi SL, huỷ món, **thanh toán** — **không** chống double-click ⇒ bấm 2 lần là trừ kho 2 lần / tạo 2 vòng bếp / thu tiền 2 lần.
- **Cần làm (tối giển, không xây hạ tầng):** dùng lại `IOrderIdempotencyStore` sẵn có cho **3 route tiền-số lượng**: `POST /orders/{id}/items`, `PATCH /orders/{id}/items/{itemId}/quantity`, `POST /payments`.
- **Không over-engineer:** không cần middleware idempotency tổng quát, không cần bảng DB lưu key (in-memory đủ cho 1 quán), không cần bắt buộc header.
- **ĐÃ LÀM:** `IOrderIdempotencyStore` được làm generic (`ExecuteAsync<TResponse>`) và dùng lại cho
  `POST /api/orders/{id}/items`, `PATCH /api/orders/{id}/items/{itemId}/quantity`, `POST /api/payments`.
  Key được gộp theo người dùng + route; cùng key + payload khác ⇒ `409`.

### 3.2 Nhóm B — "Đủ vận hành"

| # | Hạng mục | Hiện trạng | API đề xuất |
|---|---|---|---|
| B1 | **Chuyển bàn** | ✅ **ĐÃ SỬA 2026-10-21** — `PATCH /api/orders/{id}/table` (Waiter/Admin); bàn đích có đơn mở ⇒ `409`; bàn cũ → `Available`, bàn mới → `Occupied`; ghi 1 dòng lịch sử |
| B2 | **Hoá đơn theo bàn** cho thu ngân | chỉ `GET /api/orders?tableId=` | ✅ **ĐÃ SỬA 2026-10-21** (Đợt 3) — `GET /api/orders/table/{tableId}/bill` gom mọi đơn **đang mở** của bàn + tổng + dòng món đã gộp + ghi chú |
| B3 | **Ghi chú hoá đơn** | `Order` không có `Note` (chỉ `OrderItem.Note`) | ✅ **ĐÃ SỬA 2026-10-21** (Đợt 3) — `orders.Note` + `PATCH /api/orders/{id}/note` + `note` trong request tạo đơn & 3 response đơn |
| B4 | **Danh sách tồn kho thấp** | chỉ **số đếm** `LowStockCount` trong dashboard (`ReportService.cs:127`); `InventoryResponse` **không** có `MinimumStock` | ✅ **ĐÃ SỬA 2026-10-21** (Đợt 3) — `GET /api/management/inventory/low-stock` (trả `MinimumStock`, `IngredientName`, `Unit`, `Shortage`, `IsOutOfStock`) |
| B5 | **Báo cáo vận hành** | chỉ doanh thu + dashboard | 5 báo cáo, mỗi cái 1 query + 1 DTO: `revenue-by-method`, `top-dishes`, `wastage`, `stock-movement`, `by-staff` |
| B6 | **Bảng bếp** (màn hình bếp) | `GET /api/management/kitchen/orders` trả **tất cả** ticket, không lọc, không phân trang; DTO **không** có số bàn / tên phục vụ / số món | ✅ **ĐÃ SỬA 2026-10-21** (Đợt 3) — C-13 đã thêm field + `?status=open`; Đợt 3 thêm `?since=` (chỉ phiếu thay đổi) + `?page`/`?pageSize` tuỳ chọn |
| B7 | **Ca làm việc / két tiền** | không có bảng ca, không mở/đóng ca | bảng `shifts` + `POST /api/management/shifts/open|close` + `GET /api/reports/shift` |

**Ghi chú B6 (bếp):** bếp thực tế cần **số bàn + tên phục vụ + số món** để làm việc. Hiện `KitchenOrderResponse` chỉ có `KitchenOrderId, OrderId, Status, SentAt, StartedAt, CompletedAt, Priority` ⇒ bếp phải gọi thêm `GET /orders/{id}` mỗi phiếu. Cần sửa DTO + query, **không** cần WebSocket/SSE.

**Ghi chú B7 (ca):** đây là hạng mục **lớn nhất** còn thiếu, nhưng cũng dễ bị over-engineer. Bản tối giản đủ dùng: `shifts` (OpenedBy, OpenedAt, ClosedBy, ClosedAt, OpeningCash, CountedCash, ExpectedCash, Note) + 2 endpoint + 1 báo cáo đối soát. **Không** cần nhiều két, **không** cần quy trình duyệt ca.

### 3.3 Nhóm C — Vệ sinh dữ liệu (làm cùng đợt migration A1/A3)

| # | Hạng mục | Vì sao |
|---|---|---|
| C1 | `Category.DisplayOrder` đang là **`bool`** | ✅ **ĐÃ SỬA 2026-10-21** — đổi thành `int` (migration đổi kiểu cột `bit` → `int`) |
| C2 | `Ingredient.Unit` sửa tự do | ✅ **ĐÃ SỬA 2026-10-21** (mã **C-17**) — đổi đơn vị khi nguyên liệu đã có **công thức / ledger / phiếu mua** ⇒ `409`; chưa dùng ở đâu thì vẫn sửa được. Đồng thời ledger ghi kèm cột `unit` (snapshot lúc ghi dòng) |
| C3 | `Recipe.Version` bị ghi đè khi sửa | ✅ **ĐÃ SỬA 2026-10-21** (mã **C-19**) — sửa = soft-delete bản cũ (giữ nguyên định lượng) + tạo bản mới; unique filter `[IsActive]=1` đã có sẵn ⇒ **không** cần schema mới; version tự tăng khi client gửi trùng; sửa bản đã bị thay thế ⇒ `409` |
| C4 | `PromotionLinkRequest` là **dead code** | ✅ **ĐÃ SỬA 2026-10-21** (mã **C-18**) — chủ dự án chốt **giữ tính năng targeting**: thêm `PUT /api/management/promotions/{id}/targets` (thay toàn bộ target) + `PromotionResponse` trả `sizeIds`/`categoryIds`; xoá DTO dead. **0 migration** (2 bảng đã có sẵn) |
| C5 | `TransactionCode` không sinh, không unique | ✅ **ĐÃ SỬA 2026-10-21** (mã **C-19**) — server **sinh mã** nội bộ khi client không gửi; client gửi mã trùng ⇒ `409`; index `IX_payments_TransactionCode` → **unique** (filter `TransactionCode <> ''`) + migration backfill dữ liệu cũ |
| C6 | Dead code `OrderService.UpdateItemsAsync` (route đã `410`) | ✅ **ĐÃ XOÁ** (Đợt sửa P0) — chỉ còn 1 dòng comment nhắc lịch sử trong `OrdersController` |
| C7 | Dead code `OrderItemMutations.FindKitchenItemAsync` | ✅ **ĐÃ DÙNG LẠI** (Đợt sửa P0-07) — `OrderService.cs:1373` gọi để lấy phiếu bếp **mới nhất** |
| C8 | `GET /api/restaurant-tables/{id}` ghi DB, `GET /api/restaurant-tables` thì không | quirk đã biết và **đang được test pin** (`GetAll_ComputesStatus_ButDoesNotPersistIt`) ⇒ để sau, cần quyết định hợp đồng |

---

## 4. KHÔNG cần làm lúc này (chống over-engineer)

Các hạng mục dưới đây **không thiếu cho vận hành 1 quán** — để ở P2, chỉ làm khi có yêu cầu cụ thể:

| Hạng mục | Lý do hoãn |
|---|---|
| ~~Payment provider (MoMo/VNPay), webhook, chữ ký IPN~~ | ✅ **MoMo ĐÃ LÀM 2026-10-21** (intent → QR/redirect → IPN verify chữ ký + kiểm tra số tiền → idempotent; có chế độ giả lập cho môi trường test). **VNPay** vẫn hoãn |
| Payment `Pending` / verify / pre-auth | Với tiền mặt + thẻ nội bộ thì `Paid` tức thì là đủ |
| Refund **một phần** / split-tender / thanh toán nhiều lần | Chưa có nhu cầu thực tế; full-refund đã đủ |
| Tách Domain/Application/Infrastructure thành 3 project | Skill nói rõ: giữ `.csproj` hiện tại trừ khi có kế hoạch migration được duyệt |
| CQRS / MediatR / Repository pattern lại | `AppDbContext` trực tiếp là quyết định đã chốt (`docs/architecture/data-access.md`); đang hoạt động tốt |
| WebSocket / SSE cho bảng bếp | Bếp 1 quán: poll 5–10s là đủ |
| Kiểm kê tồn kho (stock count session) | Cần phiên đếm thực tế; khi nào có quy trình kiểm kê mới làm |
| Chuyển kho giữa nhiều kho | `Inventory` có unique index 1 nguyên liệu = 1 dòng tồn ⇒ đúng cho 1 kho |
| Bảng đơn vị + hệ số quy đổi đầy đủ | ✅ **ĐÃ CHỐT** — chặn sửa `Unit` (C2 / mã **C-17**) + ledger ghi kèm đơn vị là đủ an toàn; không cần bảng quy đổi |
| Đặt bàn trước (reservation) | Chỉ khi chủ dự án xác nhận nhà hàng cần |
| Roles CRUD động | 5 role cố định + policy trong code là đủ |
| Refresh token / logout / security stamp / lockout | Đi kèm bật `Auth:Enforce`; cần `security stamp` ⇒ đổi schema. Chưa cần lúc này |
| Báo cáo food-cost / COGS / hiệu suất bếp / so sánh ngày | Cần định nghĩa nghiệp vụ trước khi số |
| Phân trang cho **mọi** list endpoint | Chỉ cần cho `inventory-transactions` (đã có) và các list sẽ lớn (`kitchen/orders`, `orders`) |
| Xoá/sửa migration cũ | Skill: migration là lịch sử, không xoá |
| Sửa quirk GET-bàn-không-ghi-DB (C8) | Đang được test pin ⇒ cần quyết định hợp đồng riêng |

---

## 5. Về kiến trúc / kỹ thuật (nhỏ, không cần hạ tầng mới)

| Vấn đề | Vị trí | Ghi chú |
|---|---|---|
| `OrderLifecycleCoordinator` **không phải** điểm vào duy nhất | `reopen`/`prepared-quantity`/`shortage` tự gọi `RecalculateTicketStatus` + `RefreshOrderStatusFromAllItemsAsync` thay vì `SyncTicketAndOrderAfterItemChangeAsync` | ✅ **ĐÃ SỬA**: thêm `SyncTicketAndOrderAsync` (1 đường sync chung, có `allowReopen`); cả 3 luồng bếp + `UpdateItemStatusAsync` đều dùng |
| `Payment.Status` dùng nhầm hằng `OrderStatuses.Paid` | ✅ **ĐÃ LÀM 2026-10-21** — tách hằng `PaymentStatuses` (`Pending`/`Paid`/`Failed`/`Refunded`/`Cancelled`) |
| Hằng trạng thái ledger rải rác 6 file, không có class hằng | `AdjustmentIn/Out`, `Purchase`, `Sale`, `Wastage` | 1 class `InventoryTransactionTypes` là đủ |
| `RestaurantTableStatuses` nằm trong file service | `RestaurantTableService.cs:5` | Chuyển sang `Application/Common/` cùng `OrderStatuses`/`KitchenStatuses` |
| Nhiều chỗ hard-code `"Pending"` thay vì `KitchenStatuses.Pending` | `OrderService.cs:261,317,324,365,1102,1138` | Vệ sinh, không ảnh hưởng hành vi |
| `GetListAsync` dùng `o.Status.ToLower()` trong query | `OrderService.cs:129`, `PaymentService.cs:129-130` | Phá index `Status`; dùng `EF.Functions.Like` hoặc so sánh thẳng |
| `OrderService.GetAsync` dùng **INNER JOIN** tới `FoodSizes`/`Foods` | `OrderService.cs:42-60` | ✅ **ĐÃ SỬA**: đổi sang LEFT JOIN. Lưu ý: EF/DB **đã** chặn sẵn (`order_items.size_id` là `Restrict`, EF không cho cắt quan hệ bắt buộc) ⇒ đây là **lưới an toàn phòng thủ**, không phải lỗi đang xảy ra |
| `Category.DisplayOrder` kiểu `bool` | `Domain/Entity/Food/Category.cs:5` | Xem C1 |
| `PromotionService.UpdateAsync` bỏ qua `IsActive` | `PromotionService.cs:38-55` | Không có query "promotion đang hiệu lực" ⇒ UI phải lọc client-side |
| Báo cáo nạp toàn bảng vào RAM để đếm | `ReportService.cs:102-104,123-125` | Chấp nhận được với 1 quán; chuyển `GROUP BY` khi dữ liệu lớn |
| CSV: chưa chặn formula injection (`=`, `+`, `-`, `@`) | `ReportService.Csv` | Hiện mọi giá trị do server sinh ⇒ rủi ro thấp; nhưng helper này dùng chung nên nên chặn |

---

## 6. Lộ trình đề xuất (4 đợt, mỗi đợt build + test + migration)

> **Cách đánh số:** mục này dùng số thứ tự của lộ trình 4 giai đoạn. Trong `progress-summary-2026-10-21.md` các đợt đã xong được ghi theo mã khác: **C-14** = "Tiền đúng" (= Đợt 2 ở đây) · **C-15** = chuyển bàn + MoMo · **C-16** = hợp đồng lỗi · **C-17** = ledger có đơn vị + khoá `Ingredient.Unit` (C2) · **C-18** = target khuyến mãi (C4) · **C-19** = mã giao dịch (C5) + phiên bản công thức (C3).

### Đợt 1 — Sửa lỗi đúng–sai (không đổi API, không đổi schema) — ✅ **XONG 2026-10-21 (12/12)**
`P0-1` … `P0-12`.
**Đã sửa:** P0-01 (C-13), P0-02 (`ApplyPaid` qua coordinator), P0-03 (`RecordOrderCreated`), P0-04 (history dòng món mới),
P0-06 (guard số lượng âm), P0-07 (`FindKitchenItemAsync`), P0-08 (bỏ qua phiếu terminal), P0-09 (`Serializable` + validate trong tx),
P0-10 (409 thay 500), P0-11 (`ResolveExistingActorAsync` cho bếp) + dọn dead code (`UpdateItemsAsync`, `ApplyStockDeltas`, `CalculateRequirementsAsync`, `LoadOrderConsumptionAsync`, DTO `UpdateOrderItemsRequest`, 6 test) + gom 3 luồng bếp về 1 đường sync.
**Còn lại:** không còn — P0-05 (múi giờ promotion) và P0-12 (`ReasonCode`) đã sửa ở Đợt 2 (mã **C-14**).
**Kiểm chứng:** build 0 warning/0 error, test **550/550 pass**, **không** có migration mới, **không** đụng FE.

### Đợt 2 — Tiền đúng (1 đợt migration duy nhất) — ✅ **XONG 2026-10-21**
`A1` (settings + VAT + phí DV + `OrderTotalsCalculator`) + `A3` (`PromotionId` + snapshot) + `A2` (gỡ khuyến mãi) + `A5` (idempotency 3 route) + `C1` (`DisplayOrder` → `int`) + **P0-12** (`ReasonCode` cho `order_status_histories`) + **P0-05** (múi giờ khuyến mãi).
Đây là đợt **duy nhất** cần migration ⇒ gộp hết để không phải migrate nhiều lần.
**Migration:** `20261021041728_AddSettingsTotalsPromotionSnapshot` — bảng `restaurant_settings` (+ seed dòng `Id = 1`) ·
`orders.ServiceChargeAmount` · `orders.PromotionId`/`PromotionDiscountType`/`PromotionDiscountValue` ·
`order_status_histories.ReasonCode` · `categories.DisplayOrder` `bit` → `int` · backfill giờ VN → UTC cho `promotions`.
**Kiểm chứng:** build **0 warning / 0 error** · test **569/569 pass** · `has-pending-model-changes` = **No changes** · **không** đụng FE.

### Đợt 3 — Đủ vận hành — ✅ **XONG 2026-10-21**
`B2` (bill theo bàn) + `B3` (bill note) + `B4` (tồn kho thấp) + `B6` (bảng bếp `?since` + phân trang).
**Migration:** `20261021070215_AddOrderBillNote` — chỉ thêm cột `orders.Note`; B2/B4/B6 không đổi schema.
**Kiểm chứng:** build **0 warning / 0 error** · test **670/670 pass** · `has-pending-model-changes` = **No changes** · **không** đụng FE.

> **Còn lại:** `A4` (refund) + `B1` (chuyển bàn) — mã **C-15** (2026-10-21) ·
> `C2` (khoá đơn vị nguyên liệu + ledger có cột `unit`) — mã **C-17** (2026-10-21) ·
> `C3` (phiên bản công thức) + `C4` (target khuyến mãi) + `C5` (mã giao dịch) — mã **C-18/C-19** (2026-10-21) ·
> `C6`/`C7` (dead code) — đã xoá/dùng lại trong Đợt sửa P0.

### Đợt 4 — Báo cáo + ca làm việc
`B5` (5 báo cáo vận hành) + `B7` (ca/két tiền).
`B7` làm **sau** vì cần B2/B5 để báo cáo ca có ý nghĩa.

---

## 7. Việc phía ngoài backend (để chủ dự án quyết, không tự làm)

- **`Auth:Enforce=false`** (`Program.cs:122-128` đang log cảnh báo): backend đã có JWT + policy + 5 role; **chỉ thiếu FE gửi header `Authorization: Bearer`**. Khi FE làm xong thì bật `Auth:Enforce=true` — **không cần** thêm refresh token/security stamp/lockout ở bước đầu.
- **FE chưa dùng các route mới**: gọi thêm / đổi SL / huỷ món / prepared / shortage / reopen / item-history — BE là nguồn chuẩn.
- **Quy ước múi giờ:** đề xuất **mọi API dùng giờ Việt Nam (UTC+7)** ở đầu vào/đầu ra, lưu UTC trong DB. Cần chốt 1 lần rồi áp dụng cho `promotions` (P0-5), `payments`, `reports`.
- **Bật `Auth:Enforce=true`** là thay đổi hợp đồng với FE ⇒ cần thông báo trước.

---

## 8. Tài liệu liên quan

- `.opencode/skills/restaurant-backend/SKILL.md` — nguyên tắc bắt buộc (backend-only, đúng trước, không over-engineer).
- `docs/architecture/backend-completeness-audit-2026-10-21.md` — audit độ đầy đủ (tổng quan P0/P1/P2).
- `docs/architecture/status-matrix-cancellation-audit-2026-10-20.md` — ma trận trạng thái đơn/bếp/kho.
- `docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md` — ma trận trạng thái, phân quyền huỷ, hoàn kho.
- `docs/architecture/endpoint-baseline.md` — hợp đồng API (115 action).
- `docs/architecture/data-access.md` — quy tắc `AppDbContext`, tracking/`AsNoTracking`.
- `docs/architecture/progress-summary-2026-10-20.md` — tổng kết đợt trước.
- `docsforfe/fe-work-plan.md` — bảng công việc phía FE (BE là nguồn chuẩn).
