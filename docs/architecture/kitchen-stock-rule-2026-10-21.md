# Luật vòng đời bếp ↔ kho ↔ hoàn kho — chốt 2026-10-21

> **Nguồn:** chủ dự án xác nhận trực tiếp (2 vòng: 2026-10-21 sáng & chiều).
> **Phạm vi:** backend-only. Không đụng `frontend/`.
> File này là **nguồn chuẩn**. Mọi code liên quan phải khớp file này.

---

## 1. Vì sao luật này tồn tại

Luật cũ (P1b/C-08) chia "hoàn kho" theo **mã lý do** (`CustomerChange` → hoàn; `OutOfStock`/`PhysicalShortage`/`KitchenError` → wastage),
nhưng **quên mất mốc trạng thái**: món đã bắt đầu nấu thì nguyên liệu đã tiêu thật, không thể hoàn.
⇒ 4 luồng trong cùng hệ thống cho 2 kết quả khác nhau với cùng một nghiệp vụ:

| Luồng | Điều kiện hiện tại | Món `Preparing` ⇒ kết quả |
|---|---|---|
| `CancelItemAsync` (`OrderService.cs:1426`) | `IsPending(kitchenStatus) && IsPending(item.Status)` | **không** hoàn ✔ |
| `OrderCancellation.ApplyAsync` (`OrderCancellation.cs:70`) | `IsPending(kitchenStatus) && IsPending(orderItem.Status)` | **không** hoàn ✔ |
| `KitchenService.UpdateItemStatusAsync` (`:229`) | `IsPending(item.Status) && IsPending(orderItem.Status)` | **không** hoàn ✔ |
| `AdjustItemQuantityAsync` (`OrderService.cs:534-540`) | `!IsDone(status)` | **HOÀN KHO** ✘ |

Ngoài ra: **cả 3 luồng "không hoàn" đều IM LẶNG** — nguyên liệu đã trừ mà ledger **không có dòng nào**.

Luật mới **chốt lại**: **mốc trạng thái là chính**, mã lý do chỉ quyết định *hoàn* hay *ghi hao hụt*.

---

## 2. Mô hình đã chốt

### 2.1. Bếp nhận list món theo bàn, tự bấm từng món

- POS gọi món → hệ thống tạo **đơn** + **phiếu bếp**. **Mọi món bắt đầu ở `Pending`.**
- Bếp mở list món theo bàn → **tự bấm từng món** để chuyển `Preparing`.
  - Bấp `Preparing` = *"tôi bắt đầu làm món này"*.
  - API có sẵn, **không cần route mới**: `PATCH /api/management/kitchen/items/{id}/status`.
- Màn hình bếp phải hiện **số bàn** + **tên phục vụ** (bếp biết món của bàn nào, ai gọi). → §5.4

### 2.2. Trừ kho: **lúc POS gọi món** (giữ nguyên hiện tại)

> "nên là khi pos bấm order thì trừ kho luôn, để tránh bàn khác cũng gọi… tránh việc order ảo"

- POS gọi món ⇒ **trừ kho ngay** (ledger `Sale`), giữ nguyên chặn cứng `Insufficient ingredient` (`OrderService.cs:1076-1079`).
- ⇒ **Không** chuyển sang trừ kho ở `Preparing`. **Không** thêm khái niệm "giữ chỗ / reserve".
- Việc hoàn kho hơi tốn công (tính lại từ ledger theo món) — **được chấp nhận**, đây là cách ổn nhất.

### 2.3. `Preparing` = **MỐC KHÓA**

- Từ lúc bếp bấm `Preparing` cho món đó ⇒ nguyên liệu **đã tiêu hao thật**.
- ⇒ Món đó **không được hoàn kho nữa**, bất kể ai huỷ (POS hay bếp) ⇒ mọi thay đổi sau mốc này đều ghi **`Wastage`**, không phải hoàn kho.

### 2.4. Quyền huỷ theo trạng thái

> "nếu khách hàng nhất quyết hủy thì nó quay lại bài toán ở mục 1, phải gọi admin ra giải quyết, như vậy nó sẽ ổn hơn là để pos tự quyết"
> "khi bếp bấm prepare là khóa không hủy hay giảm nữa"

| Món đang ở | POS (Waiter) | Bếp (Chef) | Admin |
|---|---|---|---|
| `Pending` | huỷ ✔ · giảm SL ✔ | huỷ ✔ | ✔ |
| `Preparing` | ✘ **KHÓA** — không huỷ, không giảm SL | ✔ huỷ (bắt buộc lý do hợp lệ) | ✔ (thoát hiểm) |
| `Ready`/`Completed` | ✘ | ✘ | ✔ (giữ nguyên hiện tại) |

> **Ghi chú đọc câu 4:** "khóa" = **khóa với POS**. Bếp vẫn phải huỷ được món hư/hết (luật §2.5) — nếu khóa tuyệt đối thì luật "bếp tự huỷ đơn phương" mất tác dụng. Admin là lối thoát (câu 2).
> **Ghi chú câu 1:** huỷ món đã ra (`Ready`/`Completed`) **không liên quan kho** (đã trừ rồi = chuẩn logic). Phần **tiền** khi huỷ món đã ra thuộc luồng **refund** (`backend-gap-review` A4) ⇒ **để sau, không làm trong đợt này**.

> ⚠️ **"KHÓA" ở ô `Preparing` = KHÓA huỷ + KHÓA giảm SL. POS vẫn được TĂNG số lượng** — xem §2.4.1.

### 2.4.1. POS **TĂNG** số lượng món ⇒ tách dòng `Pending` mới (C-20, 2026-10-21)

> "pos tăng thêm gà rán số lượng 3, thì số lượng 3 này tách riêng luôn chèn xuống mục dưới,
> và ở trạng thái pending như thường, nó sẽ gộp chung nếu là pending hết, nếu khác trạng thái thì tạo mới"
> — chủ dự án

| Dòng món đang ở | Hành vi khi POS tăng SL |
|---|---|
| **Còn dòng `Pending`** (bếp chưa bắt đầu) | **GỘP CHUNG** vào dòng `Pending` đó — chỉ tăng số lượng, **không** tạo dòng mới |
| **Đã vào nấu / đã ra đĩa** (`Preparing`/`Ready`/`Completed`) | **TÁCH thành dòng `Pending` MỚI**, nằm **ngay dưới** dòng cũ |

- Dòng mới nằm trong **cùng phiếu bếp** (phiếu còn mở) ⇒ màn bếp thấy:
  `Gà rán 2 [Preparing]` rồi ngay dưới `Gà rán 3 [Pending]`.
- Nếu phiếu đã `Ready` / đã đóng ⇒ tạo **phiếu mới** (giống "gọi thêm").
- Phía **đơn** cũng tách dòng (`OrderItem` mới, cùng `OrderItemGroupId`) ⇒ tổng tiền và kho
  vẫn đúng, **nhưng** phần mới giữ trạng thái `Pending` ⇒ sau này huỷ/giảm thì **phần mới
  còn hoàn kho được**, chỉ phần đang nấu mới thành `Wastage`.
  (Gộp chung vào dòng `Preparing` sẽ khiến **cả** phần mới bị ghi `Wastage` ⇒ mất nguyên liệu oan.)
- Ledger ghi theo từng dòng ⇒ dòng mới có `OrderItemId` riêng.

**Code:** `OrderService.AdjustItemQuantityAsync` (nhánh tăng) · `KitchenService.GetItemsAsync`
có `orderby ki.Id` ⇒ dòng mới **luôn** nằm dưới.

### 2.5. Bếp được quyền huỷ đơn phương (sau khi đã bấm `Preparing`)

> "bếp được quyền hủy đơn phương → tuy nhiên vẫn phải báo ra cho pos để pos báo khách"

- Bếp phát hiện **nguyên liệu thiếu so với thực tế** (hao hụt khi chế biến từ trước) hoặc **đồ bị hỏng**
  ⇒ **được huỷ ngay**, không cần POS đồng ý.
- **Bắt buộc báo POS** ⇒ món phải hiện `Cancelled` + lý do + người huỷ ở phía POS.
- **Không hoàn kho** — *"vì đồ ngoài đời cũng bị hao hụt"*.
- **Bắt buộc ghi `Wastage`** vào ledger (tồn đã trừ thật, không được sinh tồn kho ảo).

### 2.6. Hai nhóm lý do bếp huỷ

> "lô hàng đó hao hụt do bếp làm ẩu, hay là lô hàng đó không đảm bảo khi hư hết, lúc này chấm dứt luôn món ăn đó chuyển về inactive + cái lí do để làm báo cáo"

| Nhóm | Tình huống | Xử lý |
|---|---|---|
| **(a) Hao hụt do bếp làm ẩu** | nguyên liệu thiếu khi chế biến, đồ hỏng trong lúc làm | `Wastage` + `Ingredient.StockStatus = OutOfStock` |
| **(b) Lô hàng không còn đảm bảo** | lô hàng hư hỏng / hết hạn ⇒ không đáp ứng được món | `Wastage` + `Ingredient.StockStatus = Spoiled` + **vô hiệu hoá MÓN trong menu** + lưu lý do |

### 2.7. "inactive" = **MÓN trong menu**; nguyên liệu **giữ nguyên**

> "gà rán có dầu, bột, gà — chỉ cần 1 trong 3 bị hư thì inactive món đó thôi, **nguyên liệu vẫn để**, để lấy đó làm minh chứng báo cáo"

- Đơn vị bị vô hiệu hoá = **MÓN** (`Food.IsActive = false`), **KHÔNG** phải nguyên liệu.
  - `MenuService.cs:13` lọc `f.IsActive && f.IsAvailable` ⇒ món `IsActive=false` **biến mất khỏi menu POS** ⇒ không gọi được nữa. ✔
- **Nguyên liệu giữ nguyên**, chỉ gắn **status** (xem §2.8) — làm **minh chứng cho báo cáo**.
- "chỉ cần 1 trong 3 bị hư" ⇒ **một** nguyên liệu hư trong công thức ⇒ vô hiệu hoá **cả món**.
- Minh chứng vật lý nằm ở: ledger `inventory_transactions` loại `Wastage` + `order_item_status_histories`.
- Cần lưu **lý do vô hiệu hoá** ⇒ `Food` hiện **không có chỗ nào** lưu lý do ⇒ phải thêm (§5.3).

### 2.8. Nguyên liệu cần **1 status** quản lý vấn đề "hư hoặc thiếu"

> "wastage đó tôi tưởng chỉ là 1 status dạng hư hay thiếu thôi chứ, nó là note à, vì phải có 1 status chịu trách nhiệm quản lí cái vấn đề hư hoặc thiếu"

- **Đúng như bạn nghĩ:** `Wastage` là **ledger type** (`InventoryTransaction.Type`), lý do nằm ở `Note` (`"Wastage ({reasonCode}): {note}"`, `OrderItemMutations.cs:500-502`).
- **NHƯNG** hiện tại `Ingredient` chỉ có `IsActive` + `MinimumStock` ⇒ **không có** trạng thái "hư / thiếu" để kho quản lý.
- ⇒ Thêm **`Ingredient.StockStatus`**:

| Giá trị | Ý nghĩa | Ai đặt |
|---|---|---|
| `Available` | bình thường | mặc định |
| `OutOfStock` | hết hàng (bếp báo thiếu) | bếp báo lý do (a) · kho tự đánh dấu |
| `Spoiled` | hư / hỏng / hết hạn — lô hàng không đảm bảo | bếp báo lý do (b) · kho tự đánh dấu |

> **Giới hạn cần biết:** hệ thống **không có khái niệm lô hàng (batch/lot)** — `Inventory` có unique index 1 nguyên liệu = 1 dòng tồn.
> ⇒ `StockStatus` là **ở cấp nguyên liệu**, không phải cấp lô. Thêm khái niệm lô hàng = thay đổi lớn, **không làm lúc này**.

---

## 3. Ma trận quyết định (luật chính thức)

Điều kiện **hoàn kho** = **món CHƯA `Preparing`** **VÀ** **`ReasonCode = CustomerChange`**.

| # | Món | Ai huỷ | `ReasonCode` | Hoàn kho? | Ledger | Báo POS | `StockStatus` | Vô hiệu hoá món |
|---|---|---|---|---|---|---|---|---|
| 1 | `Pending` | POS | `CustomerChange` | **CÓ** | `AdjustmentIn` | – | – | – |
| 2 | `Pending` | Bếp | `CustomerChange` | **CÓ** | `AdjustmentIn` | **Có** | – | – |
| 3 | `Pending` | Bếp | `OutOfStock`/`PhysicalShortage` | **KHÔNG** | `Wastage` | **Có** | `OutOfStock` | – |
| 4 | `Preparing` | **Bếp** | `KitchenError` (hao hụt chế biến / đồ hỏng) | **KHÔNG** | `Wastage` | **Có** | `OutOfStock` | – |
| 5 | `Preparing` | **Bếp** | `OutOfStock`/`PhysicalShortage` + lô hàng hư/hết hạn | **KHÔNG** | `Wastage` | **Có** | `Spoiled` | **CÓ** + lưu lý do |
| 6 | `Preparing` | POS | – | **KHÓA** (không huỷ/giảm được) | – | – | – | – |
| 7 | `Preparing` | Admin | bất kỳ | **KHÔNG** | `Wastage` | – | theo lý do | theo lý do |
| 8 | `Ready`/`Completed` | Admin | bất kỳ | **KHÔNG** | **không ghi wastage** | – | – | – |
| 9 | giảm SL | POS | `CustomerChange` | phần `Pending`: **CÓ**; phần `Preparing`: **KHÓA** | `AdjustmentIn` | – | – | – |
| 10 | **tăng SL** | POS | – | – (chỉ trừ thêm phần mới) | `Sale` | – | – | – |

> Ô #7: món **đang nấu** bị huỷ ⇒ nguyên liệu đã tiêu thật nên **không hoàn** nhưng **vẫn ghi `Wastage`** để báo cáo hao hụt không mất dấu vết.
> Ô #8: món **đã ra** ⇒ **không liên quan kho** ("món đã ra kho đã trừ là chuẩn logic" — chủ dự án).
> Việc huỷ món đã ra là chuyện **thanh toán** (refund) ⇒ thuộc luồng `backend-gap-review` A4, **để Phase sau**.
> Ô #10 (bổ sung 2026-10-21 — C-20): POS **tăng** SL món đang nấu ⇒ **tách dòng `Pending` mới** (không gộp vào dòng `Preparing`) ⇒ phần mới **còn hoàn kho được**. Xem §2.4.1.
---

## 4. "Báo POS" + "màn hình bếp" (quyết định kỹ thuật)

### 4.1. Báo POS — dùng cơ chế có sẵn, **không** tạo notification

- `GET /api/orders/{id}` ⇒ món hiện `Status="Cancelled"` + `CancelReason` + `CancelledBy` ⇒ **POS thấy ngay** món nào bị bếp huỷ và vì sao.
- `GET /api/orders/{id}/item-history` ⇒ lịch sử từng dòng món kèm `Reason` + `ReasonCode` + người thực hiện (C-10).
- ⇒ **POS tự poll** là đủ cho 1 quán. **Không** tạo bảng notification, **không** SignalR.

### 4.2. Màn hình bếp — **poll**, không SignalR (quyết định)

> "nếu backend xử lí thì mấy cái này nên là dạng realtime hết nha" — chủ dự án giao quyền chọn.

**Quyết định: poll `?status=open` mỗi 3–5 giây, không làm SignalR.** Lý do cụ thể:
1. **1 quán, 1 backend instance** — dữ liệu nhỏ (~10–40 phiếu mở). Poll 3s là 1 query rẻ, không phải realtime "thật" mà cũng không cần.
2. **SignalR/SSE gặp vướng auth**: `EventSource` (SSE) **không gửi được header `Authorization`** ⇒ phải mẹo query-string token hoặc cookie ⇒ rò rỉ token vào log/proxy. WebSocket thì phải xử lý riêng.
3. **SignalR còn cần**: hub + group theo bàn + client JS + reconnect + test ⇒ nhiều hơn giá trị ở quy mô 1 quán.
4. FE vốn đã phải poll cho phía POS ⇒ cùng 1 cơ chế, không 2.

⇒ **Nếu sau này cần push thật** (nhiều bàn, độ trễ <1s, nhiều backend instance) thì thêm SignalR ở P2 — không khó, nhưng **không phải bây giờ**.

**Phía backend cần hỗ trợ cho poll:**
- `GET /api/management/kitchen/orders?status=open` → chỉ trả phiếu **đang mở** (hiện trả **tất cả**, kể cả `Completed`/`Cancelled` ⇒ phình vô hạn).
- `KitchenOrderResponse` thêm `TableNumber`, `WaiterName`, `ItemCount` ⇒ bếp nhìn 1 màn hình là đủ, **không** phải gọi thêm `GET /orders/{id}` mỗi phiếu.
- `GET /api/management/kitchen/orders/{id}/items` ⇒ **đã đủ** (`FoodName`, `SizeName`, `Quantity`, `Status`, `Note`) ✔.

---

## 5. ĐÃ TRIỂN KHAI (2026-10-21)

**Kiểm chứng:** build `0 warning / 0 error`; test **549/549 pass** (trước: 538 → thêm 11 test, 1 test cũ được đổi vì nó đang **pin hành vi sai**).

### 5.1. Sửa bug — luật kho (không đổi hợp đồng API)

| # | File | Đã làm |
|---|---|---|
| B1 | `Application/Services/OrderService.cs` (`AdjustItemQuantityAsync`) | tách `activeItems` → `notStarted` (`IsPending`), `inProgress` (không `Pending`/không `IsDone`), `prepared` (`IsDone`). `notStarted` mới được hoàn kho; `inProgress` ghi `Wastage`. → **fix P0-1** |
| B2 | `Application/Services/OrderService.cs` (`AdjustItemQuantityAsync`) | **KHÓA**: POS/Waiter không được giảm SL món `Preparing` → `UnauthorizedAccessException` (403). Bếp (Chef) + Admin vẫn dùng được |
| B3 | `Application/Services/OrderService.cs` (`CancelItemAsync`) | món `Preparing` bị huỷ ⇒ ghi `Wastage` (trước: im lặng) |
| B4 | `Application/Services/KitchenService.cs` (`UpdateItemStatusAsync`) | món `Preparing` bị huỷ ⇒ ghi `Wastage` (trước: im lặng) |
| B5 | `Application/Common/OrderCancellation.cs` (`ApplyAsync`) | huỷ cả đơn có món `Preparing` ⇒ ghi `Wastage` (trước: im lặng) |

### 5.2. Trạng thái nguyên liệu + vô hiệu hoá món

| # | Nơi | Đã làm |
|---|---|---|
| C1 | `Domain/Entity/Recipe_Inventory/Ingredient.cs` | thêm `StockStatus` (string, mặc định `Available`) |
| C2 | `Infrastructure/Persistence/AppDbContext.cs` | map `StockStatus` (`HasMaxLength(30)`, default `Available`) |
| C3 | `Application/DTO/Responses/Management/IngredientResponse.cs` | trả `StockStatus` |
| C4 | `Application/Services/Inventory/IngredientService.cs` + `ManagementController` | `PATCH /api/management/ingredients/{id}/status` (WarehouseDesk) — kho tự đánh dấu `OutOfStock`/`Spoiled`/`Available` |
| C5 | `Application/DTO/Requests/Management/KitchenRequests.cs` (`ReportShortageRequest`) | thêm `int? IngredientId` + `bool DeactivateDish` + `string? InactiveReason` |
| C6 | `Application/Services/KitchenService.cs` (`ReportShortageAsync`) | set `Ingredient.StockStatus` (`OutOfStock` khi hết, `Spoiled` khi hư/hết hạn); ghi `Wastage` cho **đúng nguyên liệu** đó; `DeactivateDish` ⇒ `Food.IsActive=false` + `InactiveReason` + `InactiveAt` |
| C7 | `Application/Services/Food/FoodService.cs` (`UpdateActiveAsync`, `DeleteFoodAsync`) | khi vô hiệu hoá ⇒ ghi `InactiveAt`; **không** xoá `InactiveReason`/`InactiveAt` khi bật lại món |
| C8 | `Domain/Enums/IngredientStockStatuses.cs` (**mới**) | hằng `Available`/`OutOfStock`/`Spoiled` + `Normalize` (sai ⇒ 400). Đặt ở **Domain** vì entity `Ingredient` dùng hằng làm giá trị mặc định — Domain không được phụ thuộc Application |

### 5.3. Schema — **1 migration duy nhất** — `20261021025953_AddIngredientStockStatusAndFoodInactiveReason` (đã apply trên DB thật)

| Bảng | Thêm | Lý do |
|---|---|---|
| `ingredients` | `StockStatus nvarchar(30) NOT NULL` default `'Available'` | trạng thái "hư / thiếu" để kho quản lý (§2.8) |
| `foods` | `InactiveReason nvarchar(500) NULL` | lưu **lý do** vô hiệu hoá món để làm báo cáo |
| `foods` | `InactiveAt datetime2 NULL` | mốc thời gian vô hiệu hoá |

> **Không** thêm bảng mới. **Không** đụng cấu trúc lô hàng. **Không** xoá nguyên liệu.

### 5.4. Hỗ trợ màn hình bếp (poll)

| # | Nơi | Đã làm |
|---|---|---|
| E1 | `Application/Services/KitchenService.cs` (`GetOrdersAsync`) + `ManagementController` | hỗ trợ `?status=open` → chỉ phiếu **đang mở** (không `Completed`/`Cancelled`) |
| E2 | `Application/DTO/Responses/Management/KitchenOrderResponse.cs` | thêm `TableNumber`, `WaiterName`, `ItemCount` (join `orders` + `restaurant_tables` + `users`) |
| E3 | `Application/Services/KitchenService.cs` (`GetItemsAsync`) | giữ nguyên (đã đủ field, đã trả cả món đã `Cancelled`) |

> E1–E3 **không phá hợp đồng API** (thêm query param tuỳ chọn + thêm field vào response).

### 5.5. Endpoint

| # | Route | Trạng thái |
|---|---|---|
| N1 | `PATCH /api/management/ingredients/{id}/status` | **mới** (kho đánh dấu hư/hết) |
| N2 | `PATCH /api/management/kitchen/items/{id}/shortage` | **đã có** — mở rộng body (`IngredientId`, `DeactivateDish`, `InactiveReason`) |

### 5.6. Test

| File | Nội dung |
|---|---|
| `Tests/KitchenStockRuleTests.cs` (**mới**, 9 test) | bếp huỷ món đang nấu ⇒ wastage + không hoàn · POS huỷ món đang nấu (Admin) ⇒ wastage · món `Pending` huỷ bởi POS vẫn hoàn kho · bếp huỷ món `Pending` + `CustomerChange` ⇒ vẫn hoàn kho · bếp huỷ món `Pending` + `PhysicalShortage` ⇒ không hoàn + wastage · shortage có `IngredientId` ⇒ `StockStatus` + vô hiệu hoá món kèm lý do · shortage không `DeactivateDish` ⇒ món vẫn active · `SetStockStatus` đổi status + từ chối giá trị lạ · `?status=open` chỉ trả phiếu mở + có số bàn/phục vụ/số món |
| `Tests/Phase2LifecycleTests.cs` | 1 test cũ **đổi** (`ReduceQuantity_WhilePreparingButNotCooked_ReturnsStockAndKeepsStatus` → `..._DoesNotReturnStock_RecordsWastage` — nó đang **pin hành vi sai**), thêm 2 test: Waiter bị 403, Chef vẫn được phép |

---

## 6. Câu hỏi — đã chốt hết

| # | Câu hỏi | Chốt |
|---|---|---|
| 1 | Món `Ready`/`Completed` → ai huỷ? | **Admin** (giữ nguyên). Phần **tiền** (refund) để Phase sau |
| 2 | POS huỷ món `Preparing`? | **KHÓA** — POS không tự quyết, gọi Admin |
| 3 | Notification realtime? | **poll `?status=open` 3–5s**, không SignalR (§4.2). SignalR để P2 nếu cần |
| 4 | Giảm SL món `Preparing`? | **KHÓA với POS** — bếp (Chef) + Admin vẫn dùng được |
| 5 | `Wastage` ghi cho nguyên liệu nào? | `Ingredient.StockStatus` (C-13 §2.8) + `Wastage` ghi cho **đúng nguyên liệu** (C6) |

---

## 7. Liên quan

- `docs/architecture/backend-gap-review-2026-10-21.md` — P0-1 trùng với B1 ở đây.
- `docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md` — ma trận trạng thái đơn/món/phiếu.
- `docs/architecture/status-matrix-cancellation-audit-2026-10-20.md` — lịch sử các quyết định huỷ trước đây.
