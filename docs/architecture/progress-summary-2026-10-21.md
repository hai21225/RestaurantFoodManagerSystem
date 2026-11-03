# Tổng kết tiến độ — 2026-10-21

> **Đây là điểm vào cho phiên làm việc mới** (thay cho `progress-summary-2026-10-20.md`).
> Cập nhật: **2026-10-21** (9 đợt) · Nhánh: `hai` · Working tree: **chưa commit**.
> Quy tắc bắt buộc: **chỉ làm backend**. Không sửa `frontend/`, `wwwroot/` hay file FE nào trừ khi có yêu cầu rõ ràng bằng văn bản.

**Kiểm chứng lúc kết thúc phiên:** build **0 warning / 0 error** · test **670/670 pass** · `has-pending-model-changes` = **No changes** · **không** đụng FE.

---

## 1. Phiên này làm gì

Chín đợt, cùng một nguyên tắc: **đúng trước, đủ sau** — không thêm tính năng chưa cần, không over-engineer.

### Đợt 1 — Chốt luật bếp ↔ kho ↔ hoàn kho (C-13)

Chủ dự án xác nhận trực tiếp (2 vòng hỏi–đáp). Trước đó luật "hoàn kho" chia theo **mã lý do** mà quên **mốc trạng thái** ⇒ 2 luồng cho 2 kết quả khác nhau với cùng một nghiệp vụ.

**Luật chốt (ngắn):**
- **Trừ kho lúc POS gọi món** (giữ nguyên) — tránh "order ảo" / bán quá tồn. **Không** thêm khái niệm "giữ chỗ".
- **`Preparing` = mốc KHÓA**: bếp bấm `Preparing` ⇒ nguyên liệu đã tiêu thật ⇒ món đó **không hoàn kho nữa**, mọi thay đổi đều ghi `Wastage`.
- **Quyền huỷ**: POS **khóa** món `Preparing` (gọi Admin) · **Bếp (Chef) được huỷ đơn phương** kèm lý do hợp lệ · Admin huỷ mọi món.
- **Bếp bắt buộc báo POS** — POS tự poll `GET /orders/{id}` là đủ, **không** tạo notification.
- **Hai nhóm lý do bếp huỷ**: (a) hao hụt do bếp làm ẩu → `Wastage`; (b) lô hàng hư/hết hạn → `Wastage` + **vô hiệu hoá MÓN trong menu** + lưu lý do. **Nguyên liệu giữ nguyên** làm minh chứng báo cáo.
- **Nguyên liệu có 1 status** quản lý "hư/thiếu": `Ingredient.StockStatus` ∈ `Available|OutOfStock|Spoiled`.
- **Màn hình bếp: poll `?status=open` 3–5s, KHÔNG SignalR** (lý do kỹ thuật ở mục 4.2 của spec).

**Chi tiết đầy đủ (ma trận 9 dòng + delta):** `docs/architecture/kitchen-stock-rule-2026-10-21.md`

### Đợt 2 — Sửa 10/12 lỗi P0 của gap-review

| # | Lỗi | Sửa |
|---|---|---|
| P0-01 | Hoàn kho cho món **đang nấu** ⇒ tồn kho thổi phồng | tách `notStarted`/`inProgress`; `Preparing` → `Wastage`; **khóa với POS** |
| P0-02 | Thanh toán không ghi `OrderStatusHistory` | `OrderLifecycleCoordinator.ApplyPaid()` |
| P0-03 | Tạo đơn không ghi history | `RecordOrderCreated()` (`Pending → SentToKitchen`) |
| P0-04 | Dòng món mới không ghi history | `RecordItemHistory` lúc tạo dòng (`"" → Pending`) |
| P0-06 | `ReportShortage` sinh dòng **số lượng âm** | guard `cancelledQuantity < 0` |
| P0-07 | Huỷ món chọn nhầm phiếu bếp | dùng `FindKitchenItemAsync` (lấy món **mới nhất**) |
| P0-08 | Huỷ đơn làm phiếu đã `Completed` thành `Cancelled` | bỏ qua item/ticket `IsTerminal` |
| P0-09 | `PaymentService` isolation yếu nhất | `Serializable` + đọc đơn & kiểm tra **trong** transaction |
| P0-10 | `ConcurrencyConflictException` → 500 | `OrdersController` + `PaymentsController` catch → **409** |
| P0-11 | Bếp ghi sai người vào audit | `KitchenService` dùng `ResolveExistingActorAsync` (5 chỗ) |
| — | Audit bị bỏ qua im lặng khi thiếu actor | `RecordItemHistory` **ném lỗi** thay vì bỏ qua |
| — | 3 luồng bếp đi vòng coordinator | thêm `SyncTicketAndOrderAsync` — **1 đường sync chung** |
| — | Dead code | xoá `UpdateItemsAsync` + `ApplyStockDeltas` + `CalculateRequirementsAsync` + `LoadOrderConsumptionAsync` + DTO `UpdateOrderItemsRequest` + 6 test |
| — | `GetAsync` INNER JOIN | đổi sang LEFT JOIN (phòng thủ) |

**Chi tiết:** `docs/architecture/backend-gap-review-2026-10-21.md` mục 2.

### C-14 — "Tiền đúng" (settings + VAT + phí DV + khuyến mãi 2 chiều + idempotency)

Đây là **Đợt 2** trong lộ trình gap-review §6 (mã **C-14**) — gộp **mọi** thay đổi schema vào **một** migration duy nhất.

**Luật đã chốt (chủ dự án, 2026-10-21):**

| Câu hỏi | Chốt |
|---|---|
| Giá trong menu đã gồm VAT chưa? | **Đã gồm** (`taxIncluded = true`) ⇒ VAT **chỉ tách ra để in**, không cộng thêm vào tổng |
| Phí dịch vụ tính trên cái gì? | **trên tiền hàng sau giảm giá**, **luôn cộng thêm** vào tổng |
| Múi giờ `promotions`? | **API nhận giờ VN → server lưu UTC** |

**Đã làm:**

| Mục | Nội dung |
|---|---|
| **A1** | Bảng `restaurant_settings` (1 dòng, `Id = 1`) + `GET/PUT /api/management/settings` (Admin). `OrderTotalsCalculator` — **một** nguồn sự thật cho `Subtotal → Discount → Tax → ServiceCharge → Total`, thay cho 6 chỗ gán `TotalAmount` lệch nhau |
| **A2** | `DELETE /api/orders/{id}/promotion/{promotionId}` — gỡ khuyến mãi (trước chỉ gỡ được gián tiếp khi sửa món) |
| **A3** | `orders.PromotionId` + snapshot `PromotionDiscountType`/`PromotionDiscountValue` ⇒ tra lại được "đơn dùng khuyến mãi nào"; sửa/xoá khuyến mãi sau này không đổi nghĩa của đơn cũ |
| **A5** | `Idempotency-Key` cho `POST /orders/{id}/items`, `PATCH /orders/{id}/items/{itemId}/quantity`, `POST /payments` ⇒ bấm 2 lần chỉ ghi 1 lần |
| **C1** | `Category.DisplayOrder` `bool` → `int` (menu sắp được theo số) |
| **P0-05** | Múi giờ khuyến mãi (API nhận giờ VN → lưu UTC; response trả cả giờ VN lẫn UTC) |
| **P0-12** | `order_status_histories.ReasonCode` + `OrderStatusHistoryResponse.reasonCode` |

**Lưu ý nghiệp vụ:** `Payment.Amount` = `order.TotalAmount` ⇒ khi bật VAT/phí DV, **tiền thu** và **doanh thu báo cáo**
đều là *tiền khách trả* (đã gồm VAT + phí DV). Báo cáo "doanh thu thuần" để Đợt 4.

**Chi tiết hợp đồng API:** `docs/architecture/endpoint-baseline.md` mục *"C-14 — Tiền đúng"*.

### C-15 — Chuyển bàn + thanh toán MoMo (B1 + A4 + provider)

**Chuyển bàn (B1)** — luật đã chốt (chủ dự án 2026-10-21):

| Câu hỏi | Chốt |
|---|---|
| Ai chuyển? | **Waiter + Admin** (phục vụ phải linh hoạt) |
| Bàn đích đang có đơn mở? | **Chặn** (`409`) — ngoài bàn đó đang có người |
| Ghi lịch sử? | **Có** — dùng lại `RecordOrderNote` sẵn có (1 dòng, 0 schema, 0 endpoint) |

`PATCH /api/orders/{id}/table` → đơn đổi bàn · bàn cũ `Available` · bàn mới `Occupied` · lịch sử đơn có dòng "Moved from table X to table Y".

**Thanh toán MoMo (A4 + provider)** — luồng đầy đủ:

```
POST /api/payments/momo        → intent (Payment.Status = Pending) + redirectUrl + qrCodeUrl
POST /api/payments/momo/notify  → MoMo gọi về: verify chữ ký → kiểm tra số tiền → Paid
POST /api/payments/{id}/refund  → hoàn tiền toàn bộ, đơn mở lại về Ready
```

- `Payment.Status` có thêm `Pending` / `Failed` / `Refunded`; `PaidAt` **nullable**.
- Số tiền **luôn lấy từ đơn**; IPN sai chữ ký / sai số tiền ⇒ từ chối; IPN lặp ⇒ idempotent.
- **Chế độ giả lập** (`Payments:Provider = Mock`, mặc định): **không gọi ra ngoài**, backend tự sinh
  payload IPN **đã ký chữ ký đúng** ⇒ chạy đúng đường verify thật ⇒ test được cả offline.
  Bật MoMo thật: đặt `Payments__MoMo__PartnerCode/AccessKey/SecretKey` (không commit) + `Provider = MoMo`.
- **Hoàn tiền**: `Payment` → `Refunded`, đơn → `Ready` (chờ thu lại), lịch sử `Paid → Ready`, bàn → `Occupied`.

**Chi tiết hợp đồng API:** `docs/architecture/endpoint-baseline.md` mục *"C-15 — Chuyển bàn + thanh toán MoMo"*.

### C-16 — Hợp đồng lỗi thống nhất (lỗi an toàn)

Đợt này **không** thêm tính năng — chỉ sửa lỗi "báo sai" và "rò rỉ".

| Việc | Trước | Nay |
|---|---|---|
| **Lọc lỗi toàn cục** | 5 controller danh mục `catch (Exception ex)` ⇒ **lỗi SQL / mất kết nối cũng trả `400`/`404`** kèm `ex.Message` thô | `ApiExceptionFilter` (`Infrastructure/Errors/`) ánh xạ exception → HTTP; lỗi hệ thống ⇒ `500` + message chung, chi tiết chỉ ghi log |
| **Exception có ngữ nghĩa** | 18 chỗ `throw new Exception("...")` | `KeyNotFoundException` / `InvalidOperationException` |
| **Body lỗi** | **50** chỗ trả **text thuần** (`Conflict(e.Message)`) ⇒ `payload.message` = `undefined` ở FE | **JSON `{ "message": "..." }` ở mọi nơi** |
| **"Không tìm thấy"** | 7 chỗ `KeyNotFoundException` trả `400` | **`404`** (khớp filter + bảng lỗi FE) |
| **Ràng buộc DB** | `payments.Status` tự do | CHECK `CK_payments_Status` |

> **Không** đổi route, không đổi tên field JSON, không đổi body request — chỉ status code lỗi và dạng body lỗi.

**Chi tiết hợp đồng API:** `docs/architecture/endpoint-baseline.md` mục *"C-16 — Hợp đồng lỗi thống nhất"*.

### C-17 — Ledger kho có đơn vị + khoá đơn vị nguyên liệu (C2)

Đợt này gộp **2 việc** vào **1 migration** (skill yêu cầu ledger ghi đơn vị; gap-review C2 yêu cầu
chặn sửa `Ingredient.Unit` — hai việc này là hai mặt của cùng một vấn đề).

| Việc | Trước | Nay |
|---|---|---|
| **Ledger ghi đơn vị** | `inventory_transactions` **không** có cột đơn vị ⇒ dòng "2" không nói được là 2 kg hay 2 g | `inventory_transactions.unit` (nvarchar(50), nullable) = **snapshot** đơn vị lúc ghi dòng; ghi ở **mọi** luồng: `Sale` · `Purchase` · `AdjustmentIn/Out` · `Wastage` · dòng tách (giữ đơn vị dòng gốc) |
| **Chặn sửa đơn vị** | `IngredientService.UpdateAsync` cho đổi `kg → g` tự do ⇒ **âm thầm sai** mọi công thức + ledger + báo cáo cũ | Đổi đơn vị (khác hoa/thường) khi nguyên liệu đã có **công thức / ledger / phiếu mua** ⇒ **`409`** kèm message nêu rõ đã dùng ở đâu. Chưa dùng ở đâu thì vẫn sửa được (chữa typo) |
| **Migration** | — | `20261021061510_AddInventoryTransactionUnit` — **backfill** đơn vị cho toàn bộ dòng ledger cũ |

**Quyết định thiết kế:** đơn vị là **snapshot**, không phải join. Nhờ vậy dòng ledger cũ vẫn đọc
đúng sau này, kể cả khi nguyên liệu bị ẩn hay đơn vị của nó bị khoá. Vì đơn vị bị khoá sau khi
dùng, ledger và nguyên liệu luôn khớp tuyệt đối.

**Không** cần bảng quy đổi đơn vị: muốn đổi đơn vị thì tạo nguyên liệu mới.

**Chi tiết hợp đồng API:** `docs/architecture/endpoint-baseline.md` mục *"C-17 — Ledger kho có đơn vị + khoá đơn vị nguyên liệu"*.

### C-18 — Target khuyến mãi (gap-review **C4**)

Đóng lỗ "**cơ chế có, đường ghi không**": `promotion_foods` + `promotion_categories` được đọc
bởi `OrderService.ResolveDiscountBaseAsync` nhưng **không API nào ghi** ⇒ 2 bảng **luôn rỗng**
⇒ mọi khuyến mãi âm thầm giảm trên **toàn bộ** hoá đơn.

| Việc | Kết quả |
|---|---|
| **Endpoint mới (1)** | `PUT /api/management/promotions/{id}/targets` — **thay toàn bộ** target (idempotent), gửi mảng rỗng = bỏ giới hạn |
| **Response thêm field (2)** | `PromotionResponse` += `sizeIds` / `categoryIds` (có trong list **và** detail) |
| **Dead code xoá** | `PromotionLinkRequest` (không nơi nào dùng) |
| **Migration** | **không** — 2 bảng + unique index đã có sẵn từ `InitialCreate` |

**Quyết định sản phẩm (chủ dự án chốt):** **giữ tính năng targeting**, không xoá 2 bảng.

### C-19 — Mã giao dịch + phiên bản công thức (gap-review **C5** + **C3**)

| Việc | Trước | Nay |
|---|---|---|
| **C5 — mã giao dịch** | client không gửi ⇒ `""`; index **không** unique ⇒ không phát hiện thu trùng | server **sinh mã** `PAY{yyyyMMddHHmmss}-{8 hex}` khi client không gửi; client gửi mã trùng ⇒ **`409`**; index **unique** (có filter) + backfill dữ liệu cũ |
| **C3 — phiên bản công thức** | `RecipeService.UpdateAsync` ghi đè tại chỗ ⇒ **mất phiên bản cũ** | sửa = soft-delete bản cũ (giữ nguyên định lượng) + **tạo bản mới**; version **tự tăng** khi client gửi trùng; sửa bản đã bị thay thế ⇒ `409` |

**Migration:** `20261021064129_AddPaymentTransactionCodeUnique` (đã apply trên DB thật) — backfill
mã cho dòng rỗng + ghi hậu tố riêng cho mã trùng, rồi mới tạo unique index.

> **2 test cũ đang pin hành vi sai** đã được sửa lại theo hành vi mới:
> `Create_UsesEmptyString_WhenTransactionCodeIsMissing` (nay sinh mã) ·
> `UpdateRecipe_ReplacesIngredients_AndUpdatesFields` (nay tạo bản mới, giữ bản cũ).

### Đợt 3 — "Đủ vận hành" (B2 · B3 · B4 · B6)

4 mục còn lại của lộ trình gap-review §6 — đều là **API quán thật dùng hằng ngày** đang thiếu.

| Mã | Việc | Kết quả |
|---|---|---|
| **B2** | Hoá đơn theo bàn | `GET /api/orders/table/{tableId}/bill` — gom mọi đơn **đang mở** của bàn thành 1 bản tổng hợp (tiền hàng / giảm giá / VAT / phí DV / tổng + dòng món đã gộp + ghi chú + chi tiết từng đơn). Tổng = **tổng của từng đơn** ⇒ không lệch tiền thật |
| **B3** | Ghi chú hoá đơn | cột mới `orders.Note` + `PATCH /api/orders/{id}/note` + `note` trong request tạo đơn và trong 3 response đơn. Mỗi lần sửa ghi 1 dòng lịch sử |
| **B4** | Tồn kho thấp | `GET /api/management/inventory/low-stock` — danh sách nguyên liệu sắp hết kèm `minimumStock`/`shortage`/`unit`/`isOutOfStock`, sắp theo số lượng thiếu giảm dần |
| **B6** | Bảng bếp | `GET /api/management/kitchen/orders` thêm `?since=` (chỉ phiếu **có thay đổi** kể từ mốc) + `?page`/`?pageSize` tuỳ chọn. Response **vẫn là mảng** ⇒ không phá FE |

**Migration:** `20261021070215_AddOrderBillNote` (đã apply trên DB thật) — chỉ thêm cột `orders.Note`;
B2/B4/B6 **không** đổi schema.

> **Tương tác quan trọng (B2):** luật nghiệp vụ hiện tại là **1 bàn chỉ có 1 đơn đang mở**
> (`POST /api/orders` chặn "Table already has an open order") ⇒ bàn thường chỉ có 1 đơn đang mở.
> Endpoint hoá đơn gộp vẫn gộp **mọi** đơn đang mở nên đúng cả khi dữ liệu cũ có nhiều đơn.
> Thanh toán vẫn làm **từng đơn** (`POST /api/payments` nhận `orderId`) — gộp chỉ để hiển thị/in.

---

## 2. Migration đã apply

| Migration | Nội dung | Trạng thái |
|---|---|---|
| `20261021025953_AddIngredientStockStatusAndFoodInactiveReason` | `ingredients.StockStatus` (default `Available`) · `foods.InactiveReason` · `foods.InactiveAt` | ✅ **đã apply trên DB thật** |
| `20261021041728_AddSettingsTotalsPromotionSnapshot` | bảng `restaurant_settings` (+ seed dòng `Id = 1`) · `orders.ServiceChargeAmount` · `orders.PromotionId`/`PromotionDiscountType`/`PromotionDiscountValue` · `order_status_histories.ReasonCode` · `categories.DisplayOrder` `bit`→`int` · backfill giờ VN→UTC cho `promotions` | ✅ **đã apply trên DB thật** |
| `20261021055037_AddPaymentsStatusCheckConstraint` | CHECK constraint `CK_payments_Status` trên `payments.Status` | ✅ **đã apply trên DB thật** |
| `20261021052642_AddMomoPaymentProvider` | `payments.PaidAt` → nullable · `+ Provider` (default `Direct`) · `+ RequestId` · `+ ProviderTransactionId` · `+ Note` · `+ ProviderResponse` · `+ CreatedAt` (default `GETUTCDATE()`, backfill từ `PaidAt`) · `+ RefundedAt` · index `IX_payments_RequestId` | ✅ **đã apply trên DB thật** |
| `20261021061510_AddInventoryTransactionUnit` | `inventory_transactions.Unit` (nvarchar(50), nullable) + backfill đơn vị cho dòng ledger cũ | ✅ **đã apply trên DB thật** |
| `20261021064129_AddPaymentTransactionCodeUnique` | `IX_payments_TransactionCode` → **unique** (filter `TransactionCode <> ''`) + backfill mã cho dòng rỗng / mã trùng | ✅ **đã apply trên DB thật** |
| `20261021070215_AddOrderBillNote` | `orders.Note` (nvarchar(500), nullable) — ghi chú cho cả hoá đơn | ✅ **đã apply trên DB thật** |

Đợt 1–2 không có migration nào khác. `has-pending-model-changes` = **No changes** sau cả 9 đợt.

---

## 3. Hợp đồng API thay đổi (FE cần biết)

Tóm tắt, chi tiết ở `docs/architecture/endpoint-baseline.md` mục *"C-13 + Đợt sửa P0"*.

**Endpoint mới (1):** `PATCH /api/management/ingredients/{id}/status`
**Query param mới (1):** `GET /api/management/kitchen/orders?status=open`
**Response thêm field:** `KitchenOrderResponse` += `tableNumber`/`waiterName`/`itemCount` · `IngredientResponse` += `stockStatus`
**Request mở rộng (4):** `ReportShortageRequest` += `ingredientId`/`deactivateDish`/`inactiveReason` · 3 DTO bếp += `changedBy`
**Status code đổi (5):** xung đột ghi đồng thời ở đơn/thanh toán: `500` → **`409`**
**Hành vi mới:** món `Preparing` không hoàn kho + khóa với POS · lịch sử đơn có dòng "tạo" và "→ Paid" · lịch sử món có dòng "tạo" · huỷ đơn giữ nguyên phiếu đã đóng.

### C-14 — "Tiền đúng" (2026-10-21)

**Endpoint mới (3):** `GET/PUT /api/management/settings` · `DELETE /api/orders/{id}/promotion/{promotionId}`
**Header mới (3 route):** `Idempotency-Key` cho gọi thêm món / đổi số lượng / thanh toán
**Response thêm field (11):** `serviceChargeAmount` (3 DTO đơn) · `promotionId`/`promotionDiscountType`/`promotionDiscountValue` · `reasonCode` (lịch sử đơn) · `startAtUtc`/`endAtUtc` (khuyến mãi)
**Đổi kiểu field (1):** `CategoryDTO.displayOrder` `bool` → `int`
**Quy ước múi giờ (1):** `promotions.startAt`/`endAt` nhận theo **giờ VN** ⇒ lưu UTC; response trả cả giờ VN lẫn UTC
**Công thức tiền (1):** `OrderTotalsCalculator` — VAT + phí dịch vụ theo cấu hình quán, mặc định 0%/0% (hành vi cũ)

### C-17 — Ledger kho có đơn vị + khoá đơn vị nguyên liệu (2026-10-21)

**Response thêm field (1):** `unit` trong `GET /api/management/inventory-transactions`
**Status code mới (1):** `PUT /api/management/ingredients/{id}` đổi `unit` của nguyên liệu **đã dùng** ⇒ **`409`** (kèm `message` nêu rõ đã dùng ở công thức / ledger / phiếu mua nào)
**Hành vi mới:** ledger ghi kèm đơn vị ở **mọi** luồng ghi dòng (`Sale`/`Purchase`/`Adjustment`/`Wastage`) · nguyên liệu **chưa** dùng thì vẫn sửa được đơn vị

### C-18 + C-19 (2026-10-21)

**Endpoint mới (1):** `PUT /api/management/promotions/{id}/targets` — gắn target khuyến mãi (món/danh mục); gửi mảng rỗng = bỏ giới hạn
**Response thêm field (2):** `PromotionResponse` += `sizeIds` / `categoryIds`
**Request mới (1):** `PromotionTargetsRequest` (`sizeIds`, `categoryIds`) — `PromotionLinkRequest` (dead code) đã xoá
**Status code mới (2):** mã giao dịch trùng ⇒ `409` · sửa phiên bản công thức đã bị thay thế ⇒ `409`
**Hành vi mới:** mã giao dịch luôn có & luôn duy nhất (server sinh nếu client không gửi) · sửa công thức = tạo phiên bản mới, giữ bản cũ · version tự tăng khi client gửi trùng

---

## 4. Còn lại — việc tiếp theo

| Ưu tiên | Hạng mục | Ghi chú |
|---|---|---|
| ~~**P0-05**~~ | ✅ múi giờ `promotions` | đã chốt + sửa (C-14) |
| ~~**P0-12**~~ | ✅ `ReasonCode` lịch sử đơn | đã sửa (C-14) |
| ~~**C-14**~~ | ✅ "Tiền đúng" (Đợt 2 của lộ trình) | xong 2026-10-21 — 1 migration duy nhất |
| ~~**C-17**~~ | ✅ ledger kho có đơn vị + khoá `Ingredient.Unit` | xong 2026-10-21 — 1 migration (`AddInventoryTransactionUnit`) |
| ~~**C-18**~~ | ✅ target khuyến mãi (gap-review C4) | xong 2026-10-21 — **0 migration** (2 bảng đã có sẵn) |
| ~~**C-19**~~ | ✅ mã giao dịch (C5) + phiên bản công thức (C3) | xong 2026-10-21 — 1 migration (`AddPaymentTransactionCodeUnique`) |
| ~~**A4**~~ | ✅ refund | xong 2026-10-21 (`POST /api/payments/{id}/refund`) |
| ~~**B1**~~ | ✅ chuyển bàn | xong 2026-10-21 (`PATCH /api/orders/{id}/table`) |
| **Đợt 3** | ~~"Đủ vận hành"~~ | ✅ xong 2026-10-21 — `B2` bill theo bàn · `B3` bill note · `B4` tồn kho thấp · `B6` bảng bếp (`?since` + phân trang) |
| **Đợt 4** | Báo cáo + ca làm việc | `B5` 5 báo cáo vận hành (**chưa** tách doanh thu thuần khỏi VAT) · `B7` ca/két tiền |
| — | *Vệ sinh text* | 103 dòng comment bị **mojibake** trong `OrderService.cs` + `OrdersFlowTests.cs` (lỗi có sẵn từ phiên trước, **không** ảnh hưởng runtime) · 3 file docs cũ dính lỗi format 1-dòng — nên gộp 1 đợt riêng |
| — | *CSV formula injection* | chặn tiền đầu `=`/`+`/`-`/`@` trong helper CSV |
| — | *Báo cáo thuần* | tách gross / discount / VAT / refund / net (skill: *"distinguish gross… net revenue"*) |
| — | `Auth:Enforce=true` | backend đã đủ JWT/policy/5 role — **chỉ thiếu FE gửi `Authorization: Bearer`** |
| — | Bật MoMo thật | cần 3 khoá (`Payments__MoMo__*`) + `Provider = MoMo` — hiện chạy **chế độ giả lập** |
| — | P2 (cố ý hoãn) | payment provider, refund từng phần, tách 3 project, CQRS/MediatR, SignalR, kiểm kê kho, multi-kho, đặt bàn, refresh token/security stamp |

---

## 5. Bài học rút ra cho phiên sau

1. **Mốc trạng thái quan trọng hơn mã lý do.** "Hoàn kho" phải dựa trên *đã bắt đầu làm chưa*, không chỉ dựa vào *lý do*.
2. **Bếp bấm `Preparing` là hành động nghiệp vụ**, không phải thao tác kỹ thuật ⇒ nó là mốc phụ thuộc cho kho, cho phân quyền, cho audit.
3. **Đừng để "im lặng" trong luồng tiền/kho.** 3 luồng huỷ trước đó nguyệt món `Preparing` mà **không ghi gì vào ledger** ⇒ cần `Wastage` cho mọi thay đổi sau mốc.
4. **Audit thiếu thì không truy lại được.** Đơn không có dòng "tạo"/"→ Paid", món không có dòng "tạo" ⇒ thêm 3 method trong coordinator, không phải sửa rải rác.
5. **Test cũ có thể đang pin hành vi sai** — đọc lại trước khi "sửa cho khớp".

---

## 6. Tài liệu liên quan

| File | Nội dung |
|---|---|
| `docs/architecture/e2e-smoke-test-2026-10-21.md` | **Smoke test E2E trên API thật** — script `scripts/e2e-smoke.ps1` (100 case, 0 fail) + script dọn dữ liệu `scripts/cleanup-e2e.sql` + bẫy PowerShell/SQL |
| `docs/architecture/kitchen-stock-rule-2026-10-21.md` | **Luật bếp ↔ kho ↔ hoàn kho** (nguồn chuẩn) + ma trận 9 dòng + delta |
| `docs/architecture/backend-gap-review-2026-10-21.md` | Gap review P0/P1/P2 + trạng thái sửa từng mục (P0 đã sửa **12/12**) |
| `docs/architecture/endpoint-baseline.md` | Hợp đồng API (mục C-13 + đợt sửa P0) |
| `docs/architecture/status-matrix-cancellation-audit-2026-10-20.md` | Lịch sử ma trận trạng thái trước C-13 |
| `docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md` | Ma trận trạng thái, phân quyền huỷ, hoàn kho |
| `docs/architecture/data-access.md` | Quy tắc `AppDbContext`, tracking/`AsNoTracking` |
| `docsforfe/fe-work-plan.md` | Bảng công việc FE (mục G = thay đổi BE 2026-10-21) |
| `docsforfe/fe-api-reference.md` | API reference cho FE (phụ lục 2026-10-21) |
