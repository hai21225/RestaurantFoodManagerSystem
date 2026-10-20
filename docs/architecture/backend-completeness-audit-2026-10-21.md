# Audit độ đầy đủ backend — hệ thống quản lý nhà hàng (2026-10-21)

**Phạm vi:** backend only (`RestaurantFoodManagerSystem/`), đối chiếu với skill `.opencode/skills/restaurant-backend`.
**Kiểu:** audit read-only — không sửa code trong lần quét này.
**Ưu tiên đánh giá:** **đúng** và **đủ** trước; hiệu năng chưa đưa vào tiêu chí.
**Kết luận ngắn:** backend đã là một hệ thống **POS + kho + bếp chạy được** (đơn vòng bếp, huỷ món, tồn kho theo ledger, audit), nhưng **chưa đủ cho vận hành nhà hàng thực tế**: còn thiếu 4 nhóm tính năng cốt lõi (thuế/phí dịch vụ, cấu hình quán, giao dịch bàn/hoá đơn, ca làm việc) và 4 lỗi "đúng-sai" cần sửa trước khi làm payment provider.

---

## 1. Quy mô hiện tại

| Hạng mục | Số lượng |
|---|---|
| Controller | 15 file (trong `Controllers/`, `Controllers/Food`, `Controllers/Users`, `Controllers/RestaurantTables`) |
| Action (route) | **115** |
| Application service | 25 file |
| DbSet / entity | 22 nhóm bảng nghiệp vụ |
| Test | 36 file / **538 test** (build 0 warning / 0 error) |
| Migration | 7 — đã apply hết trên DB thật (`__EFMigrationsHistory`) |
| Skill | `.opencode/skills/restaurant-backend/SKILL.md` |

### 1.1. Bản đồ route theo miền nghiệp vụ
| Miền | Base route | Action | Policy |
|---|---|---|---|
| Danh mục | `api/categories`, `api/foods`, `api/foods/{foodId}/sizes`, `api/foods/{foodId}/toppings`, `api/toppings` | 26 | `Authenticated` (đọc) / `WarehouseDesk` (ghi) |
| Bàn & khu vực | `api/restaurant-tables`, `api/table-areas` | 12 | `PosOrCashier` (đọc) / `AdminOnly` (ghi) |
| Đơn | `api/orders` | 10 | `PosOperator` / `PosOrCashier` / `PosKitchenRead` / `OrderCancellation` |
| Bếp | `api/management/kitchen/**` | 7 | `KitchenDesk` |
| Kho | `api/management/ingredients`, `/suppliers`, `/inventory`, `/inventory-transactions`, `/purchase-orders` | 18 | `WarehouseDesk` |
| Công thức | `api/management/recipes` | 6 | `WarehouseDesk` |
| Khuyến mãi | `api/management/promotions` | 6 | `AdminOnly` |
| Thanh toán | `api/payments` | 3 | `CashierDesk` |
| Báo cáo | `api/reports` | 3 | `AdminOnly` (cả class) |
| Nhân viên/role | `api/users`, `api/roles`, `api/auth` | 13 | `AdminOnly` / `Authenticated` |
| Khởi tạo | `api/setup` | 2 | `AllowAnonymous` (có kiểm tra "chưa có user nào") |
| Menu POS | `api/menu` | 1 | `Authenticated` |

### 1.2. Đối chiếu với skill
| Yêu cầu trong skill | Trạng thái |
|---|---|
| Chỉ backend, không đụng FE | ✅ đang tuân thủ |
| Không tin actor/giá/tổng tiền từ client | ✅ `ResolveExistingActorAsync`, giá snapshot trên `OrderItem`, tổng tính server-side |
| Chuyển trạng thái hợp lệ + ghi actor/thời điểm | ✅ `OrderLifecycleCoordinator` + `OrderStatusHistory` + `order_item_status_histories` (C-10) |
| Trạng thái cấp món độc lập | ✅ `KitchenOrderItem.Status` + `AllowedItem` (C-11) |
| Chống trùng side effect khi retry | ⚠️ chỉ `POST /orders` có `Idempotency-Key`; các lệnh ghi khác chưa có |
| Ghi biến động kho kèm lý do/actor/thời điểm | ✅ `InventoryTransaction` (Sale/AdjustmentIn/AdjustmentOut/Wastage/Purchase) |
| Phân biệt tồn thực vs ước tính công thức | ✅ `Wastage` zero-stock, `PhysicalShortage`, không hard-block bếp |
| Transaction + chống race | ✅ `Serializable` + `RowVersion` + unique index |
| Lỗi trả về nhất quán, không lộ stack trace | ✅ controller map `ArgumentException`→400, `InvalidOperationException`→409, `UnauthorizedAccessException`→403 |
| CSV: header ổn định, escape, UTF-8, kỳ rõ ràng | ✅ `ReportService.RevenueCsvHeader`, BOM, escape, InvariantCulture, giờ VN |
| Không tính doanh thu chưa thu | ✅ chỉ `Payment.Status = Paid` |
| Xác định discounts / partial payments / refunds / combined bills **trước khi code** | ❌ **mới chỉ làm discount**; partial/combined/refund chưa chốt |
| Không hard-delete lịch sử đơn/tài chính | ✅ soft-delete cho catalog, ledger giữ nguyên |

---

## 2. Đánh giá theo nghiệp vụ nhà hàng

| Nhóm nghiệp vụ | Trạng thái | Ghi chú |
|---|---|---|
| Danh mục món (category / food / size / topping / gán topping cho món) | ✅ **đủ** | có soft-delete + restore, snapshot giá, `Menu` cho POS |
| Bàn & khu vực | ✅ **đủ, thiếu 1 thao tác** | 1 đơn mở/bàn, trả bàn khi `Paid/Cancelled`, bảo trì, soft-delete; **thiếu chuyển bàn** |
| Đơn & vòng bếp (round) | ✅ **đủ** | create / gọi thêm / đổi SL / huỷ món / huỷ đơn / lịch sử; `PUT /items` đã chặn (C-09) |
| Bếp | ✅ **đủ** | ticket, item status, prepared-quantity, shortage, reopen (C-06) |
| Kho | ✅ **đủ ở mức cơ bản** | nguyên liệu, tồn, ledger, điều chỉnh, PO nhập hàng, công thức; **thiếu** endpoint tồn thấp & kiểm kê |
| Khuyến mãi | ⚠️ **thiếu 1 thao tác** | có targeting theo món/nhóm, cap, min amount; **thiếu gỡ khuyến mãi khỏi đơn** |
| **Thuế VAT + phí dịch vụ** | ❌ **thiếu hoàn toàn** | `Order.TaxAmount` luôn = 0 |
| **Cấu hình quán / hoá đơn** | ❌ **thiếu hoàn toàn** | không có bảng settings |
| **Giao dịch bàn & hoá đơn** (chuyển bàn, gộp bàn, chia bill, gộp bill) | ❌ **thiếu hoàn toàn** | "Cách A" chỉ là tách *dòng món*, không phải tách *hoá đơn* |
| **Ca làm việc / két tiền** | ❌ **thiếu hoàn toàn** | không có bảng ca, không có mở/đóng ca |
| Thanh toán | ⚠️ **bản ghi nội bộ, chưa có provider** | đánh dấu `Paid` tức thì; chưa có pending/verify/refund/partial (theo kế hoạch: làm **sau cùng**) |
| Báo cáo | ⚠️ **thiếu nhiều** | chỉ doanh thu + dashboard |
| Phân quyền / vai trò | ✅ **đủ** | 5 role cố định seed sẵn, policy gắn ở controller |
| Xác thực | ⚠️ **còn thiếu** | `Auth:Enforce=false`; thiếu refresh/logout/khoá tài khoản/thu hồi token |

---

## 3. P0 — sửa trước, ảnh hưởng tiền hoặc tính đúng-sai

### P0-1 Thuế VAT + phí dịch vụ chưa tồn tại
- **Hiện trạng:** `Order.TaxAmount` (`Domain/Entity/Order/Order.cs:8`) **không có chỗ nào gán giá trị** ⇒ luôn = 0.
  Mọi nơi tính tổng đều dùng `Subtotal − Discount + Tax` (`OrderService.cs:1652`, `OrderService.cs:353-354`, `OrderItemMutations.cs:132-136`, `KitchenService.cs:271-274`) ⇒ tiền khách trả = tiền món − giảm giá, **không có VAT/phí DV**.
- **Cần làm:**
  1. Bảng `settings` (1 bản ghi duy nhất): `TaxRate`, `ServiceChargeRate`, `TaxIncluded` (giá đã gồm VAT chưa), `RestaurantName`, `Address`, `Phone`, `TaxCode` (MST), `Currency`, `TimeZone`.
  2. API `GET /api/management/settings` + `PUT /api/management/settings` (Admin).
  3. Một hàm dùng chất `OrderTotalsCalculator` (đặt ở `Application/Common`) tính: `Subtotal → Discount → Tax → ServiceCharge → TotalAmount`; **gọi lại ở mọi nơi đụng tổng tiền**: tạo đơn, gọi thêm, đổi SL, huỷ món, huỷ đơn, áp/gỡ khuyến mãi, prepared split, shortage, thanh toán.
  4. `Payment.Amount` phải bằng `TotalAmount` đã gồm thuế (hiện đã lấy `order.TotalAmount` ⇒ tự khớp sau khi có công thức).
- **Rủi ro nếu không làm:** hoá đơn không có VAT ⇒ sai số thu, sai báo cáo thuế.

### P0-2 Gỡ khuyến mãi khỏi đơn
- **Hiện trạng:** chỉ có `PATCH /api/orders/{id}/promotion/{promotionId}` (áp). Không có đường gỡ ⇒ chỉ gỡ được gián tiếp khi sửa món (mọi luồng sửa đơn reset `DiscountAmount = 0`), tức mất quyền kiểm soát.
- **Cần làm:** `DELETE /api/orders/{id}/promotion/{promotionId}` → tính lại `TotalAmount` (dùng `OrderTotalsCalculator` ở P0-1).

### P0-3 Thanh toán không ghi vào lịch sử trạng thái đơn
- **Hiện trạng:** `PaymentService.CreateAsync` gán thẳng `order.Status = Paid` (`PaymentService.cs:69`), **ngoài coordinator**.
  `OrderStatusHistory` chỉ được ghi ở đúng 2 chỗ, đều trong `OrderLifecycleCoordinator` (`OrderLifecycleCoordinator.cs:348` và `:455`).
  ⇒ `GET /api/orders/{id}/history` **không có dòng `Ready/Completed → Paid`** ⇒ mất dấu vết thu tiền.
- **Cần làm:** cho `PaymentService` đi qua `OrderLifecycleCoordinator.ApplyOrderStatusAsync` (hoặc thêm method `ApplyPaidAsync`) để ghi history + đồng bộ bàn trong cùng cơ chế.

### P0-4 Idempotency chỉ có ở tạo đơn
- **Hiện trạng:** `POST /api/orders` có `Idempotency-Key` (`OrderService.CreateIdempotentAsync:173`, store in-memory `Program.cs:103`).
  Các lệnh ghi khác **không chống double-click**: gọi thêm, đổi số lượng, huỷ món, prepared-quantity, shortage, reopen ⇒ bấm 2 lần là trừ kho 2 lần / tạo 2 vòng bếp.
- **Cần làm:** mở rộng `IOrderIdempotencyStore` cho các route ghi (ít nhất 3 route tiền-số lượng: add/quantity/cancel), hoặc tối thiểu chống bằng state-guard trong transaction `Serializable` (đã có phần lớn guard, nhưng chưa chống "2 request cùng payload" vì cả hai đều hợp lệ tuần tự).

---

## 4. P1 — cần để vận hành đủ

| # | Hạng mục | Hiện trạng | Đề xuất API |
|---|---|---|---|
| P1-1 | Chuyển bàn / gộp bàn | `Order.TableId` chỉ set lúc tạo, không có API chuyển | `PATCH /api/orders/{id}/table` (Admin/Waiter) — chuyển đơn sang bàn khác kèm kiểm tra bàn đích trống |
| P1-2 | Chia bill / gộp bill | chưa có gì (khác "Cách A" = tách dòng món) | `POST /api/orders/{id}/split` (tách hoá đơn thành 2 đơn con, giữ quan hệ cha–con) + `POST /api/orders/split-bill` (tạo nhiều payment cho 1 đơn) |
| P1-3 | Bill theo bàn cho thu ngân | chỉ `GET /api/orders?tableId=` | `GET /api/orders/table/{tableId}/bill` (gồm mọi đơn mở của bàn + tổng) |
| P1-4 | Báo cáo vận hành | chỉ doanh thu + dashboard | `GET /api/reports/revenue-by-method`, `/top-dishes`, `/wastage`, `/stock-movement`, `/purchases`, `/by-staff` |
| P1-5 | Cấu hình quán/hoá đơn | chưa có bảng settings | `GET/PUT /api/management/settings` (đi kèm P0-1) |
| P1-6 | Ca làm việc / két tiền | chưa có gì | bảng `shifts` + `GET/POST /api/management/shifts/open|close`, `GET /api/reports/shift` |
| P1-7 | Danh sách tồn kho thấp | chỉ có *số đếm* trong dashboard (`ReportService.cs:127`) | `GET /api/management/inventory/low-stock` |
| P1-8 | Ghi chú mức đơn (bill note) | `Order` không có trường Note (chỉ `OrderItem.Note`) | thêm `Order.Note` + `PATCH /api/orders/{id}/note` |

---

## 5. P2 — nâng cao / tuỳ chọn

| # | Hạng mục | Ghi chú |
|---|---|---|
| P2-1 | Kiểm kê tồn kho (stock count + chênh lệch) | chưa có phiên kiểm kê; cần bảng `stock_counts` |
| P2-2 | Ghi hao hủy thủ công ngoài bếp | hiện `Wastage` chỉ sinh từ luồng bếp (shortage/cancel) |
| P2-3 | Chuyển kho giữa nguyên liệu / kho | chưa có |
| P2-4 | Lịch sử giá theo nhà cung cấp | `PurchaseOrderItem.UnitPrice` lưu giá từng phiếu nhưng không có API đọc lịch sử giá |
| P2-5 | Đặt bàn trước (reservation) | chưa có; chỉ làm nếu nhà hàng cần |
| P2-6 | Refresh token / logout / thu hồi token / khoá tài khoản sau N lần sai | cần `security stamp` ⇒ đổi schema; làm khi bật `Auth:Enforce` |
| P2-7 | Roles CRUD | hiện 5 role cố định, policy nằm trong code — chỉ cần khi muốn role tuỳ biến |
| P2-8 | `Category.DisplayOrder` đang là `bool` | cần đổi schema (đã ghi nhận từ đợt trước) |
| P2-9 | Lưu `PromotionId` + snapshot lên `orders` | hiện chỉ lưu `DiscountAmount`; không tra lại được đơn dùng promotion nào |
| P2-10 | Lưu `PromotionId` trên hoá đơn khi thanh toán | đi kèm P2-9 |

---

## 6. Lỗi logic/audit còn lại (ngoài P0)

1. **Tạo đơn không ghi history**: `order.Status` gán thẳng `SentToKitchen` (`OrderService.cs:254`) ⇒ lịch sử đơn bắt đầu từ lần thay đổi đầu tiên, không có dòng "created".
2. **Thanh toán không đi qua coordinator** → xem P0-3.
3. **Ba luồng bếp mới đi đường vòng coordinator**: `reopen` (C-06), `prepared-quantity`, `shortage` tự gọi `RecalculateTicketStatus` + `RefreshOrderStatusFromAllItemsAsync` thay vì `SyncTicketAndOrderAfterItemChangeAsync` ⇒ nếu sau này sửa logic trong method sync thì 3 luồng này không tự được hưởng. Có thể gom về 1 method có tham số `allowReopen`.
4. **Audit item bỏ qua khi không resolve được actor** (`OrderItemMutations.RecordItemHistory` bỏ qua nếu `changedBy <= 0`) — hiện không xảy ra, nhưng nên log cảnh báo thay vì bỏ qua im lặng.
5. **Thiếu `OrderStatusHistory` khi đổi trạng thái đơn qua `PUT /items`** — route này đã bị chặn (C-09) nên không còn là vấn đề.

---

## 7. Phần đã ổn — không cần làm lại

- **Vòng đời đơn ↔ phiếu ↔ món**: đi qua `OrderLifecycleCoordinator`; C-01/02/03/05/06/07/08/09/10/11/12 đã xử lý và có test hồi quy.
- **Kho**: ledger theo từng dòng món; `CustomerChange` hoàn kho phần `Pending`; `OutOfStock`/`PhysicalShortage`/`KitchenError` ghi `Wastage` zero-stock; không hard-block bếp; mọi ghi kho trong transaction `Serializable` + `RowVersion`.
- **Bàn**: 1 đơn mở/bàn, bàn trả khi `Paid/Cancelled`, bảo trì chặn đơn mới, soft-delete + restore, khu vực bàn.
- **Danh mục**: soft-delete + restore, `Menu` cho POS, 1 công thức active/size, PO nhập hàng cộng kho kèm ledger.
- **Xác thực**: BCrypt (kiểm tra theo byte UTF-8, ≤72 byte), JWT có claim role, chặn gỡ role Admin cuối, `setup/first-admin` an toàn (Serializable + chỉ khi bảng `users` rỗng), cảnh báo log khi `Auth:Enforce=false`.
- **Báo cáo**: doanh thu theo kỳ giờ VN + CSV (header ổn định, BOM, escape, InvariantCulture).
- **Idempotency tạo đơn** + unique index `(OrderId, Status='Paid')` chống thanh toán trùng.

---

## 8. Lộ trình đề xuất (payment làm cuối cùng)

| Phase | Nội dung | Lý do |
|---|---|---|
| **A — Đúng tiền** | Settings + VAT/phí DV + `OrderTotalsCalculator`; gỡ khuyến mãi; audit `→ Paid`; idempotency cho lệnh ghi | Mọi thứ liên quan tổng tiền đều dựa vào Phase A |
| **B — Đủ vận hành** | Chuyển/gộp bàn; bill theo bàn; ca & két; báo cáo vận hành; tồn kho thấp; bill note | Xoá các khoảng trống nghiệp vụ thu ngân/bếp/kho |
| **C — Hoàn thiện** | Bật `Auth:Enforce` + FE gửi Bearer; refresh/logout/lockout; stock count; waste/transfer kho; price history; reservation | Chỉ làm được khi FE đã gửi token |
| **D — Thanh toán nâng cao** | Payment provider (MoMo): payment intent `Pending`, xác thực IPN/webhook + chữ ký, idempotency, kiểm tra số tiền server-side, refund, partial/combined payment | Làm **sau cùng** theo yêu cầu chủ dự án; cần Phase A (tổng tiền đúng) + B (bill) làm nền |

---

## 9. Tài liệu liên quan

- `docs/architecture/status-matrix-cancellation-audit-2026-10-20.md` — ma trận trạng thái, quyết định nghiệp vụ đơn/bếp/kho.
- `docs/architecture/progress-summary-2026-10-20.md` — tổng kết đã làm / còn lại.
- `docs/architecture/endpoint-baseline.md` — hợp đồng API (115 action).
- `docs/architecture/backend-audit-2026-10-20.md` — audit tổng thể backend + danh sách H/M/L.
- `docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md` — ma trận trạng thái đơn/món/phiếu.
- `docs/architecture/data-access.md` — quy tắc tracking/`AsNoTracking` của `AppDbContext`.
- `docsforfe/fe-work-plan.md` — bảng công việc phía FE (BE là nguồn chuẩn).
