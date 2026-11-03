# Backend hiện đã có gì — 2026-10-21

> **Đây là bản "kiểm kê năng lực" của backend**: không phải hợp đồng API (xem
> `endpoint-baseline.md`), mà là **cái backend làm được gì, theo từng phần nghiệp vụ**,
> kèm những gì **chưa** làm.
>
> **Cập nhật:** 2026-10-21 · Nhánh `hai` · Working tree chưa commit
> **Số liệu:** 128 route · 30 bảng · 14 migration (đều đã apply trên DB thật) ·
> 670/670 test · 100/100 case E2E trên API thật

---

## 0. TL;DR

| | |
|---|---|
| **Đã có** | menu, kho + phiếu mua, công thức (có phiên bản), bàn/khu vực, đơn + gọi thêm + huỷ món, bếp (đủ luật kho), thanh toán tiền mặt/thẻ + MoMo + hoàn tiền, khuyến mãi (có phạm vi), cấu hình quán + VAT + phí DV, báo cáo doanh thu + CSV + dashboard, nhân viên + 5 vai trò + phân quyền, audit đầy đủ, idempotency, chống thu trùng, chống tồn âm |
| **Còn thiếu (2)** | `POST /api/orders/{id}/split` (chia bill) · `GET/POST /api/management/shifts/*` (ca làm việc / két tiền) |
| **Cố ý hoãn (P2)** | VNPay · hoàn tiền từng phần · tách 3 project · CQRS/MediatR · SignalR · kiểm kê kho (stock count) · nhiều kho · đặt bàn · refresh token / security stamp |
| **Cần quyết định** | `C8` — quirk `GET /api/restaurant-tables/{id}` **ghi DB** (đang được test pin) |

---

## 1. Năng lực theo module

### 1.1 Danh mục & menu

| Năng lực | Chi tiết |
|---|---|
| Danh mục | CRUD + soft-delete + bật/tắt · **thứ tự hiển thị** (`displayOrder` là `int`) |
| Món | CRUD + soft-delete + bật/tắt + **cờ còn hàng** (`isAvailable`) · **lý do ngừng bán** (`inactiveReason`) + thời điểm (`inactiveAt`) |
| Cỡ món | Nhiều cỡ/món · cỡ mặc định · khoá (soft-delete) |
| Topping | CRUD + gắn topping cho món (nhiều–nhiều) · **giá đã gồm vào `unitPrice` của dòng món** khi tạo đơn |
| Menu | `GET /api/menu` — gộp danh mục/món/cỡ/topping cho POS, chỉ món đang bán |

### 1.2 Kho & mua hàng

| Năng lực | Chi tiết |
|---|---|
| Nguyên liệu | CRUD + soft-delete + bật/tắt · **mức tối thiểu** · **đơn vị bị khoá** khi đã dùng (C2/C-17) |
| Trạng thái nguyên liệu | `Available` / `OutOfStock` / `Spoiled` — kho tự đánh dấu, **không xoá** nguyên liệu (giữ làm minh chứng báo cáo) |
| Tồn kho | 1 dòng/nguyên liệu · **chặn tồn âm** · **chống ghi đè khi giao nhập đồng thời** (`rowversion` + `Serializable`) |
| Điều chỉnh tồn | `POST /api/management/inventory/{id}/adjust` — số dương/âm + lý do → ghi ledger |
| Ledger kho | **mọi** biến động đều ghi dòng: `Sale` · `Purchase` · `AdjustmentIn/Out` · `Wastage` · kèm **đơn vị** (`unit`), tồn trước/sau, lý do, người, thời điểm |
| Báo cáo biến động | Lọc theo nguyên liệu / loại / đơn hàng / khoảng thời gian, có phân trang |
| **Tồn kho thấp** | `GET /api/management/inventory/low-stock` — danh sách sắp hết kèm mức tối thiểu + số lượng thiếu |
| Phiếu mua | Tạo → nhận (cộng kho, ghi ledger) → huỷ · **nhận 2 lần bị chặn** (không cộng kho 2 lần) |
| Nhà cung cấp | CRUD + bật/tắt |

### 1.3 Công thức (recipe)

| Năng lực | Chi tiết |
|---|---|
| Định lượng | Nhiều nguyên liệu/món, **1 cỡ món chỉ 1 công thức đang chạy** |
| **Phiên bản** | Sửa công thức **không ghi đè**: bản cũ → `isActive=false` (giữ nguyên định lượng), bản mới `isActive=true`; version **tự tăng** khi client gửi trùng |
| Chặn | Không cho 2 công thức cùng size cùng active · nguyên liệu lặp trong 1 công thức · lượng ≤ 0 |
| Tác động | Công thức đang chạy quyết định **lượng trừ kho** khi POS gọi món |

### 1.4 Bàn & khu vực

| Năng lực | Chi tiết |
|---|---|
| Bàn | CRUD + soft-delete + **bảo trì** · trạng thái `Available`/`Occupied`/`Maintenance` **tự tính** từ đơn đang mở |
| Khu vực | CRUD + bật/tắt + thứ tự hiển thị |
| **Chuyển bàn** | `PATCH /api/orders/{id}/table` — bàn đích phải trống, bàn cũ → `Available`, bàn mới → `Occupied`, ghi 1 dòng lịch sử |

### 1.5 Đơn (POS)

| Năng lực | Chi tiết |
|---|---|
| Tạo đơn | Nhiều món + topping; **snapshot giá** (giá sau này đổi không ảnh hưởng đơn cũ) |
| **1 bàn = 1 đơn đang mở** | Tạo đơn thứ 2 trên cùng bàn ⇒ `409` |
| **Gọi thêm** | `POST /api/orders/{id}/items` — thêm vào đơn đang mở, **luôn tạo vòng bếp mới** |
| Đổi số lượng | Tăng/giảm; giảm về 0 = huỷ món; có thể giảm cả khi bếp đã nhận (bếp đã nấu phần nào) |
| Huỷ món | Có lý do + **mã lý do** quyết định hoàn kho hay ghi hao hụt |
| Trạng thái | `Pending → SentToKitchen → Preparing → Ready → Completed → Paid` (+ `Cancelled`), có ma trận chuyển trạng thái |
| Áp / gỡ khuyến mãi | Áp 2 chiều + snapshot (`promotionId` + loại/giá trị) để đơn cũ không đổi nghĩa |
| **Hoá đơn theo bàn** | `GET /api/orders/table/{tableId}/bill` — gom mọi đơn đang mở + tổng + dòng món gộp + ghi chú |
| **Ghi chú hoá đơn** | `orders.Note` + `PATCH /api/orders/{id}/note` (mỗi lần sửa ghi 1 dòng lịch sử) |
| Idempotency | `Idempotency-Key` cho tạo đơn / gọi thêm / đổi SL / thanh toán — bấm 2 lần chỉ 1 lần có hiệu lực |

### 1.6 Bếp

| Năng lực | Chi tiết |
|---|---|
| Bảng bếp | `GET /api/management/kitchen/orders` — `?status=open`, **`?since=`** (chỉ phiếu thay đổi), `?page`/`?pageSize`; kèm **số bàn / tên phục vụ / số món** để 1 màn hình là đủ |
| Trạng thái phiếu | `Pending → Accepted → Preparing → Ready → Completed` (+ `Cancelled`) |
| Trạng thái món | `Pending → Preparing → Ready` (+ `Completed`, `Cancelled`) |
| **Nấu được mấy phần** | Tách dòng thành phần `Completed` + phần còn lại — tổng tiền/kho **không đổi** |
| **Báo thiếu nguyên liệu** | Huỷ phần thiếu + ghi `Wastage` (không hoàn kho ảo) + có thể **ngừng bán món** đó kèm lý do |
| **Mở lại món đã xong** | Chỉ khi đơn chưa `Paid`/`Cancelled` và phiếu chưa `Completed`/`Cancelled` |
| Huỷ từ bếp | Bếp (Chef) được huỷ đơn phương kèm lý do; **POS bị khoá** với món `Preparing` |

### 1.7 Thanh toán

| Năng lực | Chi tiết |
|---|---|
| Thu tiền | Tiền mặt / thẻ / chuyển khoản / ví điện tử (allow-list, không phân biệt hoa thường) — **tiền lấy từ server**, không tin client |
| Chặn | Đơn chưa `Ready`/`Completed` hoặc còn món chưa xong ⇒ `409` · thu trùng ⇒ `409` · đơn đã huỷ ⇒ `409` |
| **Mã giao dịch** | Client không gửi ⇒ **server sinh** `PAY{...}`; mã trùng ⇒ `409`; index **unique** ở tầng DB |
| **MoMo** | intent → QR/redirect → **IPN** (verify HMAC + kiểm tra số tiền + idempotent) · `Provider`/`RequestId`/`ProviderTransactionId` snapshot |
| **Chế độ giả lập** | `Payments:Provider=Mock` (mặc định) — **không gọi ra ngoài**, tự sinh IPN **đã ký chữ ký đúng** ⇒ chạy đúng đường verify thật, test offline |
| **Hoàn tiền** | `POST /api/payments/{id}/refund` — giao dịch `Refunded`, **đơn mở lại `Ready`**, bàn về `Occupied`; đã thu rồi hoàn ⇒ **không còn tính doanh thu** |
| Lịch sử thu | Lọc theo trạng thái / phương thức / đơn / khoảng thời gian, có phân trang |

### 1.8 Khuyến mãi

| Năng lực | Chi tiết |
|---|---|
| CRUD | `%` hoặc số tiền cố định · trần giảm · đơn tối thiểu · bật/tắt · soft-delete |
| Múi giờ | API nhận **giờ VN** → lưu UTC; response trả cả giờ VN lẫn UTC |
| **Phạm vi (target)** | `PUT /api/management/promotions/{id}/targets` — gắn theo **món (size)** và **danh mục**; không có target ⇒ áp toàn hoá đơn |
| Snapshot trên đơn | `promotionId` + loại/giá trị ⇒ sửa/xoá khuyến mãi sau không đổi nghĩa đơn cũ |

### 1.9 Cấu hình quán & tiền

| Năng lực | Chi tiết |
|---|---|
| Cấu hình | Tên quán, địa chỉ, điện thoại, MST, tiền tệ, **tỉ lệ VAT**, **tỉ lệ phí dịch vụ**, giá đã gồm VAT hay chưa — 1 dòng singleton |
| **Một công thức tiền** | `OrderTotalsCalculator` là nguồn sự thật **duy nhất** (thay cho 6 chỗ gán `TotalAmount` lệch nhau) |
| Luật đã chốt | Giá menu **đã gồm VAT** ⇒ VAT chỉ tách ra để in, không cộng thêm · **phí dịch vụ** tính trên tiền hàng **sau giảm giá**, luôn cộng thêm · mặc định 0%/0% ⇒ hành vi cũ giữ nguyên |

### 1.10 Báo cáo & tổng quan

| Năng lực | Chi tiết |
|---|---|
| Dashboard | Doanh thu hôm nay, số đơn đang mở, **tỉ lệ bàn** (tổng/có khách/bảo trì), **bếp đang mở**, **số nguyên liệu sắp hết** |
| Báo cáo doanh thu | `day` / `week` / `month` + ngày tuỳ chọn, gom theo giờ |
| Xuất CSV | Header ổn định, escape chuẩn, UTF-8 có BOM, số & ngày theo định dạng VN |
| **Chưa có** | 5 báo cáo vận hành: `revenue-by-method` · `top-dishes` · `wastage` · `stock-movement` · `by-staff` (Đợt 4) · và **chưa tách gross / discount / VAT / refund / net** |

### 1.11 Nhân viên & phân quyền

| Năng lực | Chi tiết |
|---|---|
| Tài khoản | CRUD + bật/tắt + đặt lại mật khẩu + hồ sơ cá nhân (`GET/PUT /api/users/me`) |
| Vai trò | 5 vai trò cố định: `Admin` · `Waiter` · `Cashier` · `Chef` · `WarehouseManager` — gán/thu theo user |
| Policy | 9 policy theo quầy (POS / thu ngân / bếp / kho / admin) |
| JWT | HS256, claim `role`, hạn 8h (cấu hình được) |
| Mật khẩu | BCrypt · validate độ dài & định dạng email · **không bao giờ** trả hash ra API |

---

## 2. Schema — 30 bảng

| Nhóm | Bảng | Ghi chú |
|---|---|---|
| **Menu** | `categories`, `foods`, `food_sizes`, `food_toppings`, `toppings` | `categories.displayOrder` là `int` · `foods.inactiveReason`/`inactiveAt` |
| **Kho** | `ingredients`, `inventory`, `inventory_transactions` | `ingredients.unit` bị khoá khi đã dùng · `inventory_transactions.unit` = snapshot đơn vị · ledger có `rowversion` chống ghi đè |
| **Công thức** | `recipes`, `recipe_ingredients` | unique filter `(SizeId) WHERE IsActive=1` ⇒ 1 bản đang chạy/size, nhiều bản lịch sử |
| **Bàn** | `restaurant_tables`, `table_areas` | trạng thái bàn tự tính từ đơn đang mở |
| **Đơn** | `orders`, `order_items`, `order_item_toppings`, `order_status_histories`, `order_item_status_histories` | `orders.Note` (ghi chú hoá đơn) · `orders.PromotionId` + snapshot · `order_items.OrderItemGroupId` (nhóm dòng tách) · history có `ReasonCode` |
| **Bếp** | `kitchen_orders`, `kitchen_order_items` | |
| **Thu tiền** | `payments` | `Status` có **CHECK constraint** (`Pending`/`Paid`/`Failed`/`Refunded`/`Cancelled`) · `Provider`/`RequestId`/`ProviderTransactionId` · `TransactionCode` **unique** |
| **Khuyến mãi** | `promotions`, `promotion_foods`, `promotion_categories` | unique filter `(PromotionId, SizeId)` / `(PromotionId, CategoryId)` |
| **Mua hàng** | `purchase_orders`, `purchase_order_items`, `suppliers` | |
| **Cấu hình** | `restaurant_settings` | 1 dòng singleton (`Id = 1`) |
| **Tài khoản** | `users`, `roles`, `user_roles` | FK `Restrict` — không xoá user còn dữ liệu tham chiếu |

**Quy ước chung:** mọi mốc thời gian lưu **UTC**; tiền là `decimal(18,2)`; lượng là `decimal(18,3)`;
mọi bảng nghiệp vụ có `rowversion` để chống ghi đè; xoá nghiệp vụ = **soft-delete** (`IsActive`).

---

## 3. Độ tin cậy đã cài

| Vấn đề | Cách xử lý |
|---|---|
| Ghi đè mất dữ liệu | `rowversion` (concurrency token) + bắt `ConcurrencyConflictException` → **`409`** |
| Hai request đồng thời (thanh toán, nhập kho, tạo đơn) | transaction `Serializable` + unique index làm chốt chặn cuối |
| Bấm 2 lần / retry mạng | `Idempotency-Key` (4 route) — cùng key + payload khác ⇒ `409` |
| Tồn kho âm | chặn ở tầng service |
| Sửa tiền tính sai ở 6 chỗ | `OrderTotalsCalculator` — **một** nguồn sự thật |
| Lỗi hệ thống lộ ra ngoài | `ApiExceptionFilter` — mọi lỗi trả JSON `{message}`, lỗi hệ thống ⇒ `500` + message chung, chi tiết chỉ log |
| Ràng buộc dữ liệu ở tầng DB | CHECK constraint trạng thái đơn & thanh toán · unique index cho tên/đơn/mã |
| Mất dấu vết | audit đầy đủ: lịch sử đơn, lịch sử món, lịch sử thay đổi món, ledger kho, `ChangedBy` từ token |
| Nhân viên bị ghi sai vào audit | actor lấy từ **token** (ưu tiên) → `body` → người tạo đơn; body lệch token thì log cảnh báo |

---

## 4. Kiểm chứng

| Lớp | Công cụ | Kết quả |
|---|---|---|
| Unit + integration | xUnit + **SQLite in-memory** (không chạm SQL Server) | **670/670 pass** |
| E2E trên API thật | `scripts\e2e-smoke.ps1` — 20 nhóm case, đi hết 1 ca vận hành | **100/100 pass**, log server **0 lỗi** |
| Model vs DB | `dotnet ef migrations has-pending-model-changes` | **No changes** |
| Build | `dotnet build --no-incremental` | **0 warning / 0 error** |

Chi tiết E2E: `e2e-smoke-test-2026-10-21.md` (kèm 5 bẫy kỹ thuật PowerShell/SQL đã gặp).

---

## 5. Vận hành

```powershell
# Chạy API (tự apply migration + seed role/khu vực lúc khởi động)
dotnet run --project RestaurantFoodManagerSystem
#   http://localhost:5225/api  ·  /swagger

# Kiểm chứng nhanh
dotnet build RestaurantFoodManagerSystem.sln --no-incremental     # 0 warning / 0 error
dotnet test  RestaurantFoodManagerSystem.Tests                      # 670/670
powershell -ExecutionPolicy Bypass -File scripts\e2e-smoke.ps1     # 100/100 (cần API đang chạy)

# Dọn dữ liệu smoke test (chỉ xoá bản ghi có "E2E" trong tên)
sqlcmd -S HAI2\SQLEXPRESS -d RestaurantFoodManager -E -i scripts\cleanup-e2e.sql
```

| Cấu hình | Mặc định | Ý nghĩa |
|---|---|---|
| `Auth:Enforce` | `false` | **dev**: mọi `[Authorize]` cho qua. Bật `true` khi FE đã gửi Bearer |
| `Payments:Provider` | `Mock` | `Mock` = không gọi ra ngoài. `MoMo` = cần 3 khoá `Payments__MoMo__*` |
| `Database:MigrateOnStartup` | `true` | apply migration khi khởi động |
| `Database:SeedOnStartup` | `true` | seed 5 vai trò + khu vực mặc định |

> **Lưu ý:** đừng `dotnet build` khi API đang chạy — file `.exe`/`.dll` bị khoá ⇒ lỗi `MSB3021`.
> Dừng API trước, build xong chạy lại.

---

## 6. Chưa làm & đã quyết định hoãn

### 6.1 Còn thiếu (2 — FE đang chờ)

| # | Việc | Ghi chú |
|---|---|---|
| 1 | **`POST /api/orders/{id}/split`** — chia / gộp bill | Khác hẳn "bill theo bàn" (đã có, chỉ để hiển thị). Cần chốt: chia tiền 1 đơn cho 2 khách, hay tách 1 đơn thành 2 đơn? |
| 2 | **Ca làm việc / két tiền** — bảng `shifts` + `open`/`close` + báo cáo đối soát | Cần chốt: 1 ca = 1 ca làm việc hay nhiều két? Đơn giản nhất: 1 ca/ngày + đối soát tiền mặt |

### 6.2 Đã quyết định hoãn (P2 — không thiếu cho 1 quán)

payment provider khác (VNPay) · hoàn tiền từng phần / split-tender / nhiều lần thu ·
tách Domain/Application/Infrastructure thành 3 project · CQRS/MediatR/Repository ·
WebSocket/SSE cho bảng bếp · phiên kiểm kê tồn kho · nhiều kho · đặt bàn trước ·
refresh token / security stamp / lockout.

### 6.3 Cần quyết định hợp đồng (1)

| # | Vấn đề | Hiện trạng |
|---|---|---|
| C8 | `GET /api/restaurant-tables/{id}` **ghi DB** (cập nhật trạng thái bàn), còn `GET /api/restaurant-tables` thì không | Đã được test pin (`GetAll_ComputesStatus_ButDoesNotPersistIt`) ⇒ giữ nguyên hay sửa cho nhất quán? |

### 6.4 Việc kỹ thuật nhỏ còn lại

| Việc | Mức độ |
|---|---|
| CSV chặn **formula injection** (tiền đầu `=` `+` `-` `@`) | Thấp — dữ liệu CSV hiện do server sinh, nhưng helper dùng chung nên nên chặn |
| Báo cáo **tách gross / discount / VAT / refund / net** | Skill yêu cầu *"distinguish gross… net revenue"* — hiện `totalRevenue` = tổng `Paid` |
| Vệ sinh text: 103 dòng comment **mojibake** trong `OrderService.cs` + `OrdersFlowTests.cs` | **100% comment, 0 ảnh hưởng runtime** (lỗi có sẵn từ phiên trước) |

---

## 7. Tài liệu liên quan

| File | Nội dung |
|---|---|
| `docs/README.md` | Mục lục + trạng thái từng đợt |
| `docs/architecture/endpoint-baseline.md` | **Hợp đồng API** (nguồn chuẩn) |
| `docs/architecture/progress-summary-2026-10-21.md` | 8 đợt đã làm + việc còn lại |
| `docs/architecture/backend-gap-review-2026-10-21.md` | Gap review P0/P1/P2 + trạng thái từng mục |
| `docs/architecture/kitchen-stock-rule-2026-10-21.md` | **Luật bếp ↔ kho ↔ hoàn kho** (nguồn chuẩn) |
| `docs/architecture/e2e-smoke-test-2026-10-21.md` | 100 case E2E + quirks hợp đồng |
| `docs/architecture/data-access.md` | Quy tắc truy cập dữ liệu |
| `docsforfe/api-contract.md` | **Hợp đồng API viết riêng cho FE** |
