# Bảng công việc FRONTEND — hệ thống Quản lý Nhà hàng

**Cập nhật:** 2026-10-21 · **Nguồn chuẩn:** backend `RestaurantFoodManagerSystem` (128 action)
**Quy tắc:** xem `docsforfe/README.md`. FE không sửa backend; backend không sửa FE.

---

## 0. Backend đã sẵn sàng cho FE chưa? (2026-10-21)

> **Kết luận: CÓ — làm được gần như toàn bộ F0–F7. Chỉ còn 2 màn hình chưa có API.**
> Đã kiểm chứng bằng **smoke test E2E trên API thật**: `scripts\e2e-smoke.ps1` ⇒ **100 case / 0 FAIL**
> (chi tiết: `docs/architecture/e2e-smoke-test-2026-10-21.md`).

| Nhóm FE | Trạng thái backend |
|---|---|
| F0 — đăng nhập, Bearer, blob, header | ✅ `POST /api/auth/login` · `GET/PUT /api/users/me` · `POST /api/auth/change-password` |
| F1 — POS (gọi thêm, đổi SL, huỷ món, khuyến mãi, lịch sử) | ✅ đủ 9 route |
| F2 — bếp (nấu được mấy phần, thiếu nguyên liệu, mở lại món) | ✅ đủ 4 route |
| F3 — thu ngân (đơn chờ thu, phương thức, lịch sử thu) | ✅ đủ |
| F4 — kho (điều chỉnh tồn, biến động, phiếu mua, NCC, **tồn thấp**) | ✅ đủ |
| F5 — dashboard, doanh thu, CSV | ✅ `GET /api/reports/dashboard` · `GET /api/reports/revenue` · `.../export.csv` |
| F6 — quản trị (món, cỡ, topping, danh mục, nguyên liệu, công thức, NV, role) | ✅ đủ |
| F7 — cấu hình quán, chuyển bàn, MoMo, hoàn tiền, **bill theo bàn**, **bill note**, **target khuyến mãi** | ✅ đủ, trừ 2 mục dưới |
| **FE-19 — chia/gộp bill** | ❌ **CHƯA CÓ** `POST /api/orders/{id}/split` — cần BE làm thêm |
| **FE-20 — ca làm việc / két tiền** | ❌ **CHƯA CÓ** `GET/POST /api/management/shifts/*` — cần BE làm thêm |

### 3 điều kiện / lưu ý bắt buộc

1. **FE phải gửi `Authorization: Bearer` (F0-1) trước** — backend đã sẵn sàng JWT + 5 role + policy,
   nhưng `Auth:Enforce` đang để `false` (chế độ dev). Bật `true` là để không ai gọi được API vô tội.
2. **Có 1 quirk cần biết:** `GET /api/restaurant-tables/{id}` **ghi DB** (cập nhật trạng thái bàn),
   còn `GET /api/restaurant-tables` thì không. Đã được test pin ⇒ **đừng gọi endpoint `{id}` chỉ để xem**.
3. **Luật nghiệp vụ ảnh hưởng UI:**
   - **1 bàn = 1 đơn đang mở.** Gọi thêm ⇒ thêm món vào đơn hiện có (**không** tạo đơn mới).
   - **Gọi thêm ⇒ đơn quay lại `SentToKitchen`** ⇒ thu ngân **không** thu được tiền cho tới khi bếp nấy xong vòng mới.
   - **Bill theo bàn chỉ để hiển thị/in** — thanh toán vẫn **từng đơn** (dùng `orderIds`).

### Quirks hợp đồng API (đã ghi đầy đủ trong `fe-api-reference.md`)

| Nội dung | Chi tiết |
|---|---|
| Status code lẫn lộn | `POST /foods`·`/toppings`·`/sizes` → **201**; `PATCH`/`PUT` cập nhật + `adjust` + `receive` → **204**; còn lại **200** |
| `POST /api/categories` | trả `{message}` — **không có id** ⇒ phải `GET /api/categories` rồi tìm theo tên |
| `POST /api/restaurant-tables` | trả `{message, data:{tableId,…}}` — id nằm trong `data` |
| `GET /api/reports/dashboard` | **không** phải `/api/management/dashboard` |
| Tên field | `quanlity` (giữ lỗi gõ cũ) ở kho/ledger; `quantity` ở đơn/bếp. Đã có alias JSON ở chỗ quan trọng |

---

## A. Bảng công việc tổng

| # | Màn hình / file FE | Hiện trạng FE (đã kiểm tra code) | Việc cần làm | Endpoint BE phải gọi | Phase |
|---|---|---|---|---|---|
| **FE-01** | Hạ tầng `js/common/api.js` | Chỉ có `get/post/put/patch/delete`, **không** gửi `Authorization`, không tải blob, không phân biệt 401/403 | Lưu token; gắn `Authorization: Bearer`; `401` → về login; `403` → báo không đủ quyền; thêm `download()` cho CSV; cho phép header tuỳ chọn (`Idempotency-Key`) | — (hạ tầng) | **F0** |
| **FE-02** | `pages/login.js` | `GET /api/setup/status`, `POST /api/login` → không lưu token, không gửi Bearer | Lưu token (`localStorage`), gửi kèm mọi request, nút đăng xuất, hết hạn token → bắt nhập lại, hiện role đang đăng nhập | `POST /api/auth/login` (đã có) · `POST /api/auth/change-password` (chưa dùng) | **F0** |
| **FE-03** | `pages/account.html` | **Không có file JS** | Tạo `js/pages/account.js`: thông tin tôi, sửa hồ sơ, đổi mật khẩu | `GET /api/users/me` · `PUT /api/users/me` · `POST /api/auth/change-password` | F0 / F6 |
| **FE-04** | `pages/dashboard.js` | **0 lần gọi API** — dùng `js/data/dashboard.data.js` (dữ liệu tĩnh) | Bỏ dữ liệu tĩnh, nối API thật: doanh thu hôm nay, đơn mở, bàn, bếp, cảnh báo tồn thấp, 10 đơn gần nhất | `GET /api/reports/dashboard` | **F5** |
| **FE-05** | `pages/tables.js` | 6 call: list/create/delete/bảo trì bàn; `GET /api/table-areas?includeInactive=true` | Sửa bàn (thiếu `PUT`), CRUD khu vực đầy đủ, bật/tắt khu vực, **xem đơn đang mở của bàn** (để bấm vào bàn → mở đơn) | `PUT /api/restaurant-tables/{id}` · `POST/PUT/DELETE /api/table-areas` · `PATCH /api/table-areas/{id}/active` · `GET /api/orders?tableId={id}` | F6 (đơn của bàn: F1) |
| **FE-06** | `pages/order.js` (POS) | **2 call**: `GET /api/menu` + `POST /api/orders` ⇒ **chỉ tạo được đơn, không sửa được đơn** | Mở đơn đang có theo bàn; **gọi thêm**; **đổi số lượng** (cả khi bếp đã nhận); **huỷ món** (có lý do + reasonCode); áp khuyến mãi; xem lịch sử đơn/món; huỷ đơn | `GET /api/orders?tableId=` · `GET /api/orders/{id}` · `POST /api/orders/{id}/items` · `PATCH /api/orders/{id}/items/{itemId}/quantity` · `POST /api/orders/{id}/items/{itemId}/cancel` · `PATCH /api/orders/{id}/promotion/{promotionId}` · `PATCH /api/orders/{id}/status` · `GET /api/orders/{id}/history` · `GET /api/orders/{id}/item-history` | **F1** |
| **FE-07** | `pages/kitchen.js` | 6 call; bếp chỉ bấm được "Preparing" và "xong/ready" | Báo **đã nấu được mấy phần** (prepared-quantity); báo **thiếu nguyên liệu** (shortage, có lý do); **mở lại món đã xong** (reopen); hiện lý do huỷ trên UI | `PATCH /api/management/kitchen/items/{id}/prepared-quantity` · `POST /api/management/kitchen/items/{id}/shortage` · `POST /api/management/kitchen/items/{id}/reopen` | **F2** |
| **FE-08** | `pages/payment.js` | 3 call: lấy đơn, lấy bàn, `POST /api/payments` | Danh sách phiếu thu; lịch sử thu của đơn; chọn phương thức thanh toán (Cash/Card/Bank/EWallet/Transfer); in bill; sau khi thu → bàn tự trả | `GET /api/payments` · `GET /api/payments/order/{orderId}` · `GET /api/orders?status=Ready` (danh sách đơn chờ thu) | **F3** |
| **FE-09** | `pages/foods.js` | 10 call: category list/create, food list/create/edit/delete/active/availability, food size list/create | Quản lý **topping** (chưa có màn hình nào gọi `/api/toppings`), gán topping cho món, xoá/sửa/đặt mặc định/khoá size, khoá & xoá category | `GET/POST/PUT/PATCH/DELETE /api/toppings` · `GET/POST/DELETE /api/foods/{foodId}/toppings` · `PUT/DELETE /api/foods/{foodId}/sizes/{sizeId}` · `PATCH /api/foods/{foodId}/sizes/{sizeId}/default` · `PATCH /api/foods/{foodId}/sizes/{sizeId}/active` · `PUT/DELETE /api/categories/{id}` · `PATCH /api/categories/{id}/active` | **F6** |
| **FE-10** | `pages/recipes.js` | 10 call; nguyên liệu **chỉ tạo được**, không sửa/xoá/khoá | Sửa / xoá / khoá nguyên liệu; xem chi tiết định lượng khi sửa công thức | `PUT /api/management/ingredients/{id}` · `DELETE /api/management/ingredients/{id}` · `PATCH /api/management/ingredients/{id}/active` · `GET /api/management/ingredients/{id}` | **F6** |
| **FE-11** | `pages/inventory.js` | **2 call**: `GET /api/management/inventory`, `GET /api/management/ingredients` ⇒ chỉ xem | **Điều chỉnh tồn** (có lý do); xem **biến động kho**; quản lý **nhà cung cấp**; xem **tồn kho thấp** | `POST /api/management/inventory/{ingredientId}/adjust` · `GET /api/management/inventory-transactions` · `GET/POST/PUT/DELETE /api/management/suppliers` · `PATCH /api/management/suppliers/{id}/active` · ✅ `GET /api/management/inventory/low-stock` (**Đã có** — Đợt 3) | **F4** |
| **FE-12** | `pages/purchases.js` | **1 call**: `GET /api/management/purchase-orders` ⇒ chỉ xem danh sách | Tạo phiếu nhập (NCC + dòng hàng + giá), xem chi tiết, **nhập hàng** (cộng kho), huỷ phiếu | `POST /api/management/purchase-orders` · `GET /api/management/purchase-orders/{id}` · `POST /api/management/purchase-orders/{id}/receive` · `POST /api/management/purchase-orders/{id}/cancel` | **F4** |
| **FE-13** | `pages/employees.js` | 2 call: `GET /api/users`, `POST /api/users` | Sửa nhân viên, khoá/mở khoá, đặt lại mật khẩu, **gán/thu role**, xem danh sách role | `GET/PUT /api/users/{id}` · `PATCH /api/users/{id}/active` · `POST /api/users/{id}/reset-password` · `POST /api/users/{userId}/roles` · `DELETE /api/users/{userId}/roles/{roleName}` · `GET /api/roles` | **F6** |
| **FE-14** | `pages/roles.html` | **Không có JS** | Tạo `js/pages/roles.js` hiển thị 5 vai trò + mô tả quyền | `GET /api/roles` | **F6** |
| **FE-15** | `pages/reports.html` | **Không có JS** | Tạo `js/pages/reports.js`: chọn kỳ (day/week/month) + ngày, xem bảng doanh thu theo nhóm, **tải CSV** | `GET /api/reports/revenue?period=&date=` · `GET /api/reports/revenue/export.csv?period=&date=` (cần `blob` trong api.js) | **F5** |
| **FE-16** | Khuyến mãi | **Chưa có màn hình nào** | Tạo `pages/promotions.html` + `js/pages/promotions.js`: CRUD khuyến mãi, bật/tắt, xem khuyến mãi đang áp cho đơn và **gỡ khuyến mãi** | `GET/POST/PUT/DELETE /api/management/promotions` · `PATCH /api/management/promotions/{id}/active` · `PATCH /api/orders/{id}/promotion/{promotionId}` · ✅ `DELETE /api/orders/{id}/promotion/{promotionId}` (**Đã có** — C-14) · ✅ `PUT /api/management/promotions/{id}/targets` (**Đã có** — C-18) | **F6** |
| **FE-17** | Thiết lập quán / VAT | Chưa có | Chờ BE có settings + thuế | `GET/PUT /api/management/settings` | F7 |
| **FE-18** | Chuyển bàn / gộp bàn | Chưa có | Chờ BE | `PATCH /api/orders/{id}/table` | F7 |
| **FE-19** | Chia bill / gộp bill | Chưa có | Chờ BE | `POST /api/orders/{id}/split` | F7 |
| **FE-20** | Ca làm việc / két tiền | Chưa có | Chờ BE | `GET/POST /api/management/shifts/*` | F7 |
| **FE-21** | Thanh toán MoMo / QR | Chưa có | Chờ BE làm xong provider (**cuối cùng**) | endpoint payment provider (chưa có) | F8 |

---

## B. Chi tiết theo phase (kèm endpoint & body tối thiểu)

### F0 — Hạ tầng auth (BLOCKER cho mọi phase sau)
| Bước | Nội dung | Ghi chú BE |
|---|---|---|
| F0-1 | `api.js`: đọc token từ `localStorage`, gắn header `Authorization: Bearer` cho **mọi** request | BE đã có JWT + claim `role`; `GET /api/auth/login` trả token |
| F0-2 | `api.js`: bắt `401` → xoá token + chuyển về `login.html`; `403` → hiện "không đủ quyền" (không phải lỗi mạng) | BE trả `401` (thiếu/sai token) và `403` (sai role) |
| F0-3 | `api.js`: thêm `download(path, filename)` dùng `blob` (cho CSV) | CSV trả `text/csv; charset=utf-8` + BOM |
| F0-4 | `api.js`: cho phép truyền header tuỳ chọn (chuẩn bị cho `Idempotency-Key` của `POST /api/orders`) | BE đã hỗ trợ `Idempotency-Key` (≤128 ký tự) cho tạo đơn |
| F0-5 | Lưu & hiển thị role hiện tại; nút đăng xuất (xoá token) | Role lấy từ claim trong token |
| F0-6 | **Bật `Auth:Enforce=true` ở BE** (sau khi F0-1 xong) ⇒ mọi policy bắt đầu có hiệu lực | C-04: hiện BE cố tình để `false` vì FE chưa gửi token |

> ⚠️ Sau khi bật enforce: các trang cũ gọi API mà chưa qua `api.js` sẽ 401 — phải rà lại toàn bộ 12 file `js/pages`.

### F1 — POS: gọi thêm / đổi số lượng / huỷ món (ưu tiên cao nhất về nghiệp vụ)
| Bước | Việc | Endpoint + body tối thiểu |
|---|---|---|
| F1-1 | Màn hình bàn → bấm bàn đang có đơn → mở chi tiết đơn | `GET /api/orders?tableId={id}&status=...` rồi `GET /api/orders/{orderId}` |
| F1-2 | Nút **"Gọi thêm"** (mở lại giỏ hàng cho đơn đang mở) | `POST /api/orders/{orderId}/items` body `{ changedBy, items:[{ sizeId, quantity, note?, toppings:[{ toppingId, quantity }] }]}` → BE **luôn tạo vòng bếp mới**, đơn `Completed` được mở lại, bàn vẫn `Occupied` |
| F1-3 | Nút **"Sửa số lượng"** trên từng dòng món (kể cả khi bếp đã nhận) | `PATCH /api/orders/{orderId}/items/{itemId}/quantity` body `{ quantity | delta, changedBy, reason?, reasonCode?, allowWastage? }` — gửi **đúng một** trong hai: `quantity` (đích) hoặc `delta` (lệch). Về 0 thì **bắt buộc có `reason`** |
| F1-4 | Nút **"Huỷ món"** trên từng dòng | `POST /api/orders/{orderId}/items/{itemId}/cancel` body `{ reason (bắt buộc ≤500), reasonCode?, changedBy }`. `reasonCode`: `CustomerChange` (mặc định, **hoàn kho** phần chưa nấu) hoặc `OutOfStock`/`PhysicalShortage`/`KitchenError` (**ghi hao hụt**, không hoàn kho) |
| F1-5 | Áp khuyến mãi cho đơn | `PATCH /api/orders/{orderId}/promotion/{promotionId}` (body rỗng) — BE tự kiểm tra thời hạn/min-amount/targeting |
| F1-6 | Xem lịch sử đơn & lịch sử từng món (audit) | `GET /api/orders/{orderId}/history` · `GET /api/orders/{orderId}/item-history` |
| F1-7 | Huỷ cả đơn (Admin) | `PATCH /api/orders/{orderId}/status` body `{ status:"Cancelled", changedBy, note?, reasonCode? }` |
| F1-8 | **Cấm dùng** `PUT /api/orders/{id}/items` | Route đã bị chặn, trả `410 Gone` — nếu FE còn code cũ thì sửa ngay |

### F2 — Bếp: prepared / shortage / reopen
| Bước | Việc | Endpoint + body tối thiểu |
|---|---|---|
| F2-1 | Bấm "nấu được mấy phần" (ví dụ món 3 phần mới nấu được 1) | `PATCH /api/management/kitchen/items/{id}/prepared-quantity` body `{ preparedQuantity (≥1), changedBy, reason? }` — BE tách dòng thành phần `Completed` + phần còn lại, tổng tiền/kho **không đổi** |
| F2-2 | Báo thiếu nguyên liệu (không hard-block bếp) | `POST /api/management/kitchen/items/{id}/shortage` body `{ preparedQuantity (0..số lượng món), reasonCode: "OutOfStock"\|"PhysicalShortage", changedBy, reason? }` — phần thiếu bị huỷ + ghi `Wastage` (không hoàn kho ảo) |
| F2-3 | Bấm "lỡ tích xong" → mở lại món | `POST /api/management/kitchen/items/{id}/reopen` body `{ reason? }` — chỉ khi đơn chưa `Paid/Cancelled` và phiếu chưa `Completed/Cancelled`; món/ticket/đơn về `Preparing` |
| F2-4 | Hiển thị lý do huỷ món trên bảng bếp | Đọc từ `GET /api/management/kitchen/orders/{ticketId}/items` + `GET /api/orders/{orderId}/item-history` |

### F3 — Thu ngân
| Bước | Việc | Endpoint + body tối thiểu |
|---|---|---|
| F3-1 | Danh sách đơn chờ thu (nút bấm từ bàn) | `GET /api/orders?status=Ready` / `status=Completed` + `GET /api/orders/{id}` |
| F3-2 | Chọn phương thức thanh toán | `POST /api/payments` body `{ orderId, paymentMethod: "Cash"\|"Card"\|"Bank"\|"EWallet"\|"Transfer", transactionCode?, createdBy }` |
| F3-3 | Lịch sử thu của đơn + danh sách phiếu thu | `GET /api/payments/order/{orderId}` · `GET /api/payments?page=&pageSize=&method=&from=&to=` |
| F3-4 | In bill | Dữ liệu đã có ở `GET /api/orders/{id}` + `GET /api/restaurant-tables/{tableId}`; **chưa có** endpoint bill hợp nhất (xem mục C) |
| F3-5 | **Không** gọi `POST /api/payments` khi đơn chưa `Ready`/`Completed` hoặc còn món chưa xong — BE sẽ trả `409` | |

### F4 — Kho & Mua hàng
| Bước | Việc | Endpoint + body tối thiểu |
|---|---|---|
| F4-1 | Điều chỉnh tồn (thêm/bớt khi kiểm kê, hết hạn…) | `POST /api/management/inventory/{ingredientId}/adjust` body `{ quantity (+/-), createdBy (≥1), note? }` — số âm làm tồn âm sẽ bị `409` |
| F4-2 | Xem biến động kho (có lọc theo nguyên liệu/loại/đơn hàng/thời gian) | `GET /api/management/inventory-transactions?ingredientId=&type=&referenceType=&referenceId=&from=&to=&page=&pageSize=` — lưu ý: `referenceId` **bắt buộc** kèm `referenceType` |
| F4-3 | Tạo phiếu nhập | `POST /api/management/purchase-orders` body `{ supplierId, createdBy (≥1), items:[{ ingredientId, quantity (>0), unitPrice }] }` |
| F4-4 | Nhập hàng (cộng kho) | `POST /api/management/purchase-orders/{id}/receive` body `{ receivedBy (≥1) }` — nhập 2 lần sẽ bị chặn |
| F4-5 | Huỷ phiếu nhập | `POST /api/management/purchase-orders/{id}/cancel` — phiếu đã nhập thì không huỷ được (`409`) |
| F4-6 | Quản lý nhà cung cấp | `GET/POST/PUT/DELETE /api/management/suppliers` · `PATCH /api/management/suppliers/{id}/active` |

### F5 — Báo cáo & Tổng quan
| Bước | Việc | Endpoint |
|---|---|---|
| F5-1 | Bỏ dữ liệu tĩnh ở `dashboard.js`, nối API thật | `GET /api/reports/dashboard` |
| F5-2 | Báo cáo doanh thu theo kỳ | `GET /api/reports/revenue?period=day\|week\|month&date=yyyy-MM-dd` |
| F5-3 | Tải CSV | `GET /api/reports/revenue/export.csv?period=&date=` (cần `blob` — F0-3) |

### F6 — Quản trị
Nhân viên (F6-1), vai trò (F6-2), tài khoản (F6-3), danh mục món/topping/size/category (F6-4), khu vực bàn (F6-5), khuyến mãi (F6-6) — endpoint đã liệt kê ở bảng A.

---

## C. Việc FE **chưa làm được** cho tới khi BE bổ sung (mục C của audit backend)

| # | Tính năng | BE còn thiếu | Ưu tiên BE |
|---|---|---|---|
| FE-C1 | Gỡ khuyến mãi khỏi đơn | `DELETE /api/orders/{id}/promotion/{promotionId}` | **P0** |
| FE-C2 | VAT + phí dịch vụ hiển thị trên bill | bảng `settings` + tính lại `TotalAmount` | **P0** |
| FE-C3 | Bill theo bàn (gom mọi đơn của bàn) | `GET /api/orders/table/{tableId}/bill` | P1 |
| FE-C4 | Chuyển bàn | `PATCH /api/orders/{id}/table` | P1 |
| FE-C5 | Chia / gộp bill | `POST /api/orders/{id}/split` | P1 |
| FE-C6 | Danh sách tồn kho thấp | `GET /api/management/inventory/low-stock` | P1 |
| FE-C7 | Cấu hình quán (tên, MST, ĐT, giờ, tiền tệ) | `GET/PUT /api/management/settings` | P1 |
| FE-C8 | Ca làm việc / két tiền | `/api/management/shifts/*` | P1 |
| FE-C9 | Báo cáo vận hành (theo phương thức thu, món bán chạy, hao hụt, tồn kho, mua hàng) | `/api/reports/*` mới | P1 |
| FE-C10 | Ghi chú ở mức đơn | `Order.Note` + `PATCH /api/orders/{id}/note` | P1 |
| FE-C11 | Thanh toán MoMo / QR / hoàn tiền | payment provider | **cuối cùng** |

---

## D. Quy tắc xử lý lỗi từ BE (dùng chung cho mọi màn hình)

| HTTP | Ý nghĩa | FE nên làm |
|---|---|---|
| `400` | Dữ liệu sai (thiếu lý do, sai định dạng, quá độ dài) | Hiện `message` của BE ngay dưới form, **không** clear form |
| `401` | Chưa đăng nhập / token hết hạn / token sai | Xoá token → chuyển `login.html` |
| `403` | Đúng vai trò nhưng không đủ quyền với nghiệp vụ (VD: huỷ món đã xong mà không phải Admin) | Hiện message, ẩn nút thay vì chỉ báo lỗi |
| `404` | Không tìm thấy đơn/món/bàn | Quay lại danh sách + refresh |
| `409` | Xung đột nghiệp vụ (đơn đã `Paid`, bếp đã bắt đầu, thiếu kho, đơn không sẵn sàng thu) | Hiện message + refresh dữ liệu |
| `410` | Route đã bị bỏ (`PUT /api/orders/{id}/items`) | Báo "luồng này đã bị thay bằng …" + hướng dẫn dùng route mới |

**Body lỗi của BE luôn là `{ "message": "..." }`** → `api.js` lấy `payload.message` được trên **mọi** lỗi.
(Trước 2026-10-21, ~50 lỗi trả **text thuần** nên `payload.message` là `undefined` — nay đã sửa hết.)

> **Lưu ý 2026-10-21:** "không tìm thấy" giờ trả **`404`** ở **mọi** nhóm endpoint (trước một số chỗ trả `400`).
> Nếu màn hình nào đang bắt `400` để hiện "không tìm thấy món/bàn", cần đổi sang `404`.

---

## E. Checklist nghiệm thu FE

- [ ] Mọi request đều có `Authorization: Bearer` (trừ `login`, `setup/status`, `setup/first-admin`).
- [ ] `401` → về trang đăng nhập; `403` → báo đúng quyền.
- [ ] Không còn gọi `PUT /api/orders/{id}/items` (đã `410`).
- [ ] Gọi thêm / đổi số lượng / huỷ món đều **truyền `reason` / `reasonCode`** đúng, và hiển thị lại lý do do BE trả về.
- [ ] Bếp có đủ 3 nút: prepared / shortage / reopen, và hiển thị được lý do huỷ món.
- [ ] Thu ngân: chỉ gọi `POST /api/payments` khi đơn đủ điều kiện (để tránh 409) và hiện rõ thông báo lỗi.
- [ ] Tổng quan & báo cáo lấy từ API thật, không còn dữ liệu tĩnh.
- [ ] CSV tải đúng tên file + mở bằng Excel không lỗi dấu.
- [ ] Không tự tính tiền/tax ở FE — mọi con số hiển thị lấy từ response BE (`subtotal`, `discountAmount`, `taxAmount`, `totalAmount`).
- [ ] Mọi màn hình dùng chung `api.js` (không tự `fetch`).

---

## G. Thay đổi BE 2026-10-21 mà FE cần biết (C-13 + đợt sửa P0)

> Backend đã sửa xong. Tài liệu chi tiết: `docs/architecture/kitchen-stock-rule-2026-10-21.md` +
> `docs/architecture/endpoint-baseline.md` (mục "C-13 + Đợt sửa P0").
> Build 0 warning/0 error · test 550/550 pass.

### G.1 Endpoint mới cho FE dùng ngay

| Verb | Route | Body | Mục đích |
|---|---|---|---|
| PATCH | `/api/management/ingredients/{id}/status` | `{ "stockStatus": "Available\|OutOfStock\|Spoiled", "note": "..." }` | Kho đánh dấu nguyên liệu hết hàng / hư hỏng. **Không** xoá nguyên liệu |
| GET | `/api/management/kitchen/orders?status=open` | - | Chỉ lấy phiếu **đang mở** → dùng để **poll màn hình bếp 3–5s** |

### G.2 Response có thêm field (không phá JSON cũ)

| DTO | Field mới | Dùng để |
|---|---|---|
| `KitchenOrderResponse` | `tableNumber`, `waiterName`, `itemCount` | Bếp nhìn 1 màn hình là đủ, **không** phải gọi thêm `GET /orders/{id}` mỗi phiếu |
| `IngredientResponse` | `stockStatus` | Kho hiển thị cảnh báo "hết hàng / hư hỏng" |

### G.3 Body mở rộng

| Route | Field mới |
|---|---|
| `POST /api/management/kitchen/items/{id}/shortage` | `ingredientId` (nguyên liệu nào hư/hết) · `deactivateDish` (ngừng bán món này) · `inactiveReason` (lý do) |
| `PATCH /api/management/kitchen/orders/{id}/status` | `changedBy` |
| `PATCH /api/management/kitchen/items/{id}/status` | `changedBy` |
| `POST /api/management/kitchen/items/{id}/reopen` | `changedBy` |

> `changedBy` dùng khi request **không** có token (chế độ dev hiện tại). Khi có token, backend lấy actor từ token và bỏ qua body.

### G.4 Hành vi mới FE phải xử lý

| Tình huống | Hành vi mới | FE cần làm |
|---|---|---|
| Món `Preparing` (bếp đã bấm) | **KHÓA với POS** — không huỷ/giảm được | Hiện thông báo "món đang nấu, liên hệ bếp"; **không** cho bấm nút huỷ/giảm; chỉ Admin mới thao tác được (API trả **403**) |
| Bếp huỷ món `Preparing` | Món hiện `Cancelled` + `CancelReason` + `CancelledBy` | **Poll** `GET /api/orders/{id}` (đã có) hoặc `GET /api/orders/{id}/item-history` để báo khách. Có thể poll `GET /api/management/kitchen/orders?status=open` |
| Lịch sử đơn | Có thêm dòng **"đơn được tạo"** và dòng **"→ Paid"** | Đọc `GET /api/orders/{id}/history` — không cần đổi code, chỉ dữ liệu nhiều hơn 1 dòng |
| Lịch sử món | Có thêm dòng **"dòng món được tạo"** (`oldStatus` rỗng) | `GET /api/orders/{id}/item-history` — `oldStatus` có thể là **chuỗi rỗng** ở dòng đầu tiên |
| Huỷ đơn | Phiếu bếp đã đóng **giữ nguyên** `Completed` | Không cần xử lý gì thêm |
| Xung đột ghi đồng thời | `409` thay vì `500` | Hiện thông báo "dữ liệu vừa bị người khác sửa, tải lại" thay vì "lỗi hệ thống" |

### G.5 Màn hình bếp — khuyến nghị

- **Poll** `GET /api/management/kitchen/orders?status=open` mỗi **3–5 giây** + `GET /api/management/kitchen/orders/{id}/items`.
- Không cần SignalR/WebSocket — BE **không** làm realtime push (đã chốt 2026-10-21).
- Nhờ `tableNumber`/`waiterName`/`itemCount` mỗi phiếu, 1 màn hình bếp chỉ cần **2 request/lần poll**.

### G.6 Việc FE vẫn còn (không đổi so với trước)

- **Gửi `Authorization: Bearer <token>`** — BE đã sẵn sàng; bật `Auth:Enforce=true` chỉ còn thiếu bước này.
- Dùng các route lệnh (gọi thêm / đổi SL / huỷ món / prepared / shortage / reopen / item-history) — BE là nguồn chuẩn.
- `PUT /api/orders/{id}/items` vẫn trả **410** ⇒ **không** dùng.

---

## H. Thay đổi BE 2026-10-21 (đợt "Tiền đúng") — FE cần biết

> Backend đã sửa xong. Chi tiết: `docs/architecture/endpoint-baseline.md` (mục "C-14 — Tiền đúng").
> Build 0 warning/0 error · test 569/569 pass · 1 migration duy nhất.

### H.1 Endpoint mới (3)

| Verb | Route | Body | Mục đích |
|---|---|---|---|
| GET | `/api/management/settings` | - | Đọc cấu hình quán (VAT, phí dịch vụ, tên quán, MST) |
| PUT | `/api/management/settings` | `{ restaurantName, address, phone, taxCode, currency, taxRate, taxIncluded, serviceChargeRate }` | Lưu cấu hình quán (Admin) |
| DELETE | `/api/orders/{id}/promotion/{promotionId}` | - | **Gỡ khuyến mãi** khỏi đơn |

### H.2 Header mới `Idempotency-Key` (3 route)

| Route | Tác dụng |
|---|---|
| `POST /api/orders/{id}/items` | bấm "gọi thêm" 2 lần chỉ tạo **1** round bếp, trừ kho **1** lần |
| `PATCH /api/orders/{id}/items/{itemId}/quantity` | bấm "giảm SL" 2 lần chỉ áp dụng **1** lần |
| `POST /api/payments` | bấm "thu tiền" 2 lần chỉ thu **1** lần |

- Cùng key + cùng payload ⇒ trả lại **kết quả cũ** (không thực thi lại).
- Cùng key + **payload khác** ⇒ **`409`** `{ "message": "... different request payload" }`.
- **Không** gửi header ⇒ hành vi cũ (không chống trùng).
- **FE nên** tạo 1 UUID cho mỗi thao tác và gửi **lại đúng key đó** khi retry.

### H.3 Response thêm field

| DTO | Field mới | Dùng để |
|---|---|---|
| `OrderDetailResponse` | `serviceChargeAmount`, `promotionId`, `promotionDiscountType`, `promotionDiscountValue` | Hiện dòng phí DV + biết có khuyến mãi để gỡ |
| `OrderListItemResponse` | `serviceChargeAmount` | Danh sách đơn |
| `OrderCreatedResponse` | `serviceChargeAmount`, `promotionId`, `promotionDiscountType`, `promotionDiscountValue` | Sau khi tạo đơn |
| `OrderStatusHistoryResponse` | `reasonCode` | Phân biệt "khách đổi" vs "hết hàng" |
| `PromotionResponse` | `startAtUtc`, `endAtUtc` | Xem cả 2 múi giờ |
| `CategoryDTO` | `displayOrder` đổi từ **`bool` → `int`** | Sắp danh mục bằng số |

### H.4 Công thức tiền — FE **không** tự tính

- Server đã tính sẵn: `subtotal`, `discountAmount`, `taxAmount`, `serviceChargeAmount`, `totalAmount` — **cứ hiển thị đúng số server trả về**.
- `taxIncluded = true` (mặc định) ⇒ giá menu **đã gồm VAT**: `taxAmount` là phần VAT **tách ra để in**, **không** cộng thêm.
  `totalAmount` = tiền hàng − giảm giá + phí DV.
- `taxIncluded = false` ⇒ `totalAmount` = tiền hàng − giảm giá + VAT + phí DV.
- Phí dịch vụ tính trên **tiền hàng sau giảm giá**.
- Mặc định `taxRate = 0`, `serviceChargeRate = 0` ⇒ tổng = tiền hàng − giảm giá (đúng như cũ).

### H.5 Khuyến mãi — quy ước múi giờ (quan trọng)

- `POST`/`PUT /api/management/promotions`: `startAt`/`endAt` gửi theo **giờ Việt Nam (UTC+7)**,
  ví dụ `"2026-12-01T08:00:00"` (**không** kèm offset). Server tự quy đổi sang UTC khi lưu.
- Nếu gửi kèm offset (ISO-8601 `"2026-11-30T17:00:00Z"`) thì server giữ nguyên mốc đó.
- Response: `startAt`/`endAt` = **giờ VN** (gửi lại nguyên vẹn khi sửa) · `startAtUtc`/`endAtUtc` = UTC.
- Đơn đã áp khuyến mãi giờ có `promotionId` + snapshot ⇒ sửa/xoá khuyến mãi **không** làm đổi đơn cũ.

### H.6 Hành vi mới FE cần xử lý

| Tình huống | Hành vi mới | FE cần làm |
|---|---|---|
| Sửa món / gọi thêm / huỷ món trên đơn đang có khuyến mãi | Discount và `promotionId` bị **bỏ** (cơ sở khuyến mãi đã đổi) | Cập nhật UI: hiện "khuyến mãi đã bị bỏ" hoặc tự áp lại nếu nghiệp vụ yêu cầu |
| Áp / gỡ khuyến mãi | Ghi thêm 1 dòng audit trong lịch sử đơn | Không cần xử lý (chỉ nhiều dòng hơn) |
| Lịch sử đơn | Dòng huỷ có `reasonCode` | Dùng `reasonCode` để hiện nhãn tiếng Việt, đừng parse `note` |
| Danh mục | `displayOrder` là **số** | Sửa UI: gửi số thứ tự thay vì `true`/`false` |
| Bấm 2 lần nhanh | `Idempotency-Key` chặn ghi 2 lần | Gửi header `Idempotency-Key` |

### H.7 Việc FE vẫn còn

- **Gửi `Authorization: Bearer <token>`** — BE đã sẵn sàng; bật `Auth:Enforce=true` chỉ còn thiếu bước này.
- Thêm màn hình **cấu hình quán** (Admin) dùng `GET`/`PUT /api/management/settings`.
- Nếu mở `taxRate`/`serviceChargeRate` > 0 thì **tiền thu** và **doanh thu báo cáo** đều là *tiền khách trả*
  (đã gồm VAT + phí DV) — BE chưa tách "doanh thu thuần".

---

## I. Thay đổi BE 2026-10-21 (C-15) — FE cần biết: chuyển bàn + MoMo

> Backend đã sửa xong. Chi tiết: `docs/architecture/endpoint-baseline.md` (mục "C-15 — Chuyển bàn + thanh toán MoMo").
> Build 0 warning/0 error · test 594/594 pass · migration `20261021052642_AddMomoPaymentProvider`.

### I.1 Endpoint mới (5)

| Verb | Route | Body | Mục đích |
|---|---|---|---|
| PATCH | `/api/orders/{id}/table` | `{ tableId, changedBy }` | **Chuyển bàn** (khách đổi chỗ ngồi) |
| POST | `/api/payments/momo` | `{ orderId, createdBy }` | Tạo giao dịch MoMo ⇒ trả `redirectUrl` + `qrCodeUrl` |
| POST | `/api/payments/momo/notify` | payload IPN của MoMo | **MoMo gọi về** — FE không gọi route này |
| POST | `/api/payments/momo/mock/{paymentId}?success=true` | - | **Giả lập thanh toán** (chỉ khi BE chạy chế độ `Mock`) |
| POST | `/api/payments/{paymentId}/refund` | `{ reason, createdBy }` | **Hoàn tiền** (toàn bộ) |

### I.2 Chuyển bàn — luật đã chốt

| Tình huốp | Kết quả | FE cần làm |
|---|---|---|
| Bàn đích **đang có đơn mở** | `409` `{message}` | Hiện "Bàn này đang có khách, chọn bàn khác" |
| Bàn đích bảo trì / đã xoá | `400` | Hiện lý do |
| Chuyển sang chính bàn cũ | `400` | Disable nút khi đã ở bàn đó |
| Đơn đã `Paid`/`Cancelled` | `409` | Ẩn nút chuyển bàn |
| Thành công | `204` | Bàn cũ về `Available`, bàn mới về `Occupied` — **poll lại** `GET /api/restaurant-tables` (không phải `/api/management/...`) |

- Quyền: **Waiter + Admin** (chính sách chưa bật `Auth:Enforce` thì mọi role gọi được).
- Đơn có ghi thêm 1 dòng lịch sử "Moved from table X to table Y" — `GET /api/orders/{id}/history` sẽ có dòng này.

### I.3 Thanh toán MoMo — luồng FE

```
1. Thu ngân bấm "Thanh toán MoMo"
   → POST /api/payments/momo  { orderId, createdBy }
   ← { paymentId, status: "Pending", redirectUrl, qrCodeUrl, isMock }
2. Hiện QR (qrCodeUrl) hoặc chuyển khách tới redirectUrl
3. FE POLL GET /api/payments/order/{orderId}   (2–3 giây/lần)
   → payment.status chuyển "Pending" → "Paid" ⇒ đơn đã đóng, in hoá đơn
   → nếu "Failed" ⇒ báo khách thanh toán không thành công
4. Hoàn tiền: POST /api/payments/{paymentId}/refund { reason, createdBy }
```

- **Đơn KHÔNG tự đóng** khi tạo intent — chỉ đóng khi có IPN. FE **phải poll** trạng thái.
- `status` của payment: `Pending` → `Paid` | `Failed` | `Refunded`.
- `paidAt` có thể **`null`** (intent chưa thu) — đừng giả định có giá trị.
- Bấm "tạo giao dịch" 2 lần ⇒ BE trả lại **cùng** intent (cùng `paymentId`, cùng QR) — không cần xử lý gì.

### I.4 Chế độ giả lập — test KHÔNG cần tiền thật

Khi BE chạy `Payments:Provider = Mock` (mặc định ở môi trường test):

- `isMock = true` trong response.
- `redirectUrl` trỏ về `POST /api/payments/momo/mock/{paymentId}?success=true` — gọi nó là **đóng tiền thành công**,
  không tốn tiền thật, không cần mạng. Thêm `?success=false` để giả lập **thất bại** (kiểm tra luồng lỗi).
- Payload IPN giả lập được ký chữ ký **đúng** ⇒ BE vẫn kiểm tra đầy đủ (sai chữ ký / sai số tiền / trùng IPN đều bị chặn).

> Khi bật MoMo thật, `redirectUrl` là trang thanh toán của MoMo và `qrCodeUrl` là ảnh QR thật.

### I.5 Response đổi field

| DTO | Thay đổi |
|---|---|
| `PaymentResponse` | **+** `provider`, `requestId`, `providerTransactionId`, `note`, `providerResponse`, `createdAt`, `refundedAt`; `paidAt` **→ nullable** |
| `MomoIntentResponse` (mới) | `paymentId`, `orderId`, `amount`, `status`, `provider`, `requestId`, `redirectUrl`, `qrCodeUrl`, `isMock` |

### I.6 Hoàn tiền

- `POST /api/payments/{paymentId}/refund` ⇒ payment `status = Refunded`, **đơn mở lại về `Ready`** (chờ thu lại),
  bàn trở lại `Occupied`.
- Chỉ hoàn **toàn bộ**; chưa hỗ trợ hoàn từng phần.
- `409` nếu giao dịch không ở trạng thái `Paid` (đã hoàn rồi).

### I.7 Việc FE vẫn còn

- **Gửi `Authorization: Bearer <token>`** — BE đã sẵn sàng; bật `Auth:Enforce=true` chỉ còn thiếu bước này.
- Màn hình **cấu hình quán** (Admin) — `GET`/`PUT /api/management/settings`.
- Màn hình **chuyển bàn** (POS) — `PATCH /api/orders/{id}/table`.
- Màn hình **thanh toán MoMo** (thu ngân) — `POST /api/payments/momo` + poll trạng thái + nút hoàn tiền.
- Điều khoản `POST /api/payments/momo/notify` là của **MoMo**, không phải của FE.

---

## J. Thay đổi BE 2026-10-21 (C-16) — hợp đồng lỗi

> Không có endpoint mới, **không** có field mới, **không** đổi request.
> Chỉ thay **status code** của lỗi và **dạng body lỗi**.

### J.1 Bảng ánh xạ lỗi (giờ đã thống nhất toàn API)

| Tình huống | HTTP | Body |
|---|---|---|
| Không tìm thấy món / danh mục / bàn / đơn | **`404`** | `{ "message": "..." }` |
| Dữ liệu đầu vào sai | **`400`** | `{ "message": "..." }` |
| Xung đột nghiệp vụ (trùng tên, vi phạm luật, xung đột ghi) | **`409`** | `{ "message": "..." }` |
| Lỗi hệ thống (lỗi DB, mất kết nối) | **`500`** | `{ "message": "An unexpected error occurred. Please try again." }` |

### J.2 FE cần sửa gì

| Việc | Chi tiết |
|---|---|
| ✅ `payload.message` luôn có | Trước đây ~50 lỗi trả **text thuần** ⇒ `payload.message` là `undefined` ⇒ FE hiện message rỗng. Nay **mọi** lỗi đều có `message` |
| ⚠️ Đổi `400` → `404` | Các màn hình đang bắt `400` để hiện "không tìm thấy món/bàn" cần chuyển sang `404` |
| ✅ `500` = lỗi hệ thống | Trước đây lỗi SQL cũng trả `400`/`404` kèm message thô. Nay trả `500` + message chung ⇒ **hiện "lỗi hệ thống, thử lại"**, đừng hiện message kỹ thuật |
| ➕ Màn hình dùng `409` | "Topping already exists" / "This size already exists for this food" / "Topping already assigned to this food" nay trả **`409`** (trước `400`) |

### J.3 Không đổi gì

Route, tên field JSON, body request, định dạng lỗi 401/403/410 — **giữ nguyên**.

---

## K. Thay đổi BE 2026-10-21 (C-17) — ledger kho có đơn vị + khoá đơn vị nguyên liệu

> Không thêm route, **không** đổi request. **1 field mới** (`unit`) + **1 trường hợp `409` mới**.

### K.1 Bảng biến động kho — thêm cột "Đơn vị"

`GET /api/management/inventory-transactions` ⇒ mỗi dòng có thêm **`unit`** (string, có thể `null`).
Đây là **snapshot lúc ghi dòng** ⇒ đọc đúng cả với dòng cũ.

### K.2 Form nguyên liệu — đơn vị có thể bị khoá

`PUT /api/management/ingredients/{id}` — nếu đổi `unit` của nguyên liệu **đã dùng** (có trong công thức /
đã phát sinh biến động kho / đã có trong phiếu mua) ⇒ **`409`**:

```json
{ "message": "Cannot change unit from 'kg' to 'g' because this ingredient is already used in recipes, stock history. Create a new ingredient instead." }
```

| Việc | Chi tiết |
|---|---|
| ➕ Hiện cột "Đơn vị" | Bảng biến động kho (`pages/inventory.js`) — đọc `item.unit` |
| ⚠️ Bắt `409` khi đổi đơn vị | Disable ô "Đơn vị" + báo *"Nguyên liệu đã dùng nên không đổi được đơn vị — hãy tạo nguyên liệu mới"* |
| ✅ Gửi lại đơn vị cũ | Vẫn `204` — kể cả khác hoa/thường (`kg` vs `KG`) |
| ✅ Nguyên liệu mới, chưa dùng | Vẫn sửa đơn vị bình thường |

### K.3 Không đổi gì

Route, tên field JSON của request (`unit` vẫn bắt buộc), body request — **giữ nguyên**.

---

## L. Thay đổi BE 2026-10-21 (C-18) — target khuyến mãi

> Thêm **1 endpoint** + **2 field** trong response. Không đổi request cũ.

### L.1 Gắn target cho khuyến mãi

`PUT /api/management/promotions/{id}/targets` — body `{ "sizeIds": [1,2], "categoryIds": [3] }`.

**Semantics: thay toàn bộ** — body là danh sách target **sau khi sửa**, không phải phần thêm.
Gửi `{"sizeIds": [], "categoryIds": []}` ⇒ bỏ giới hạn ⇒ khuyến mãi áp cho toàn bộ hoá đơn.

| Lỗi | HTTP |
|---|---|
| Khuyến mãi không tồn tại | `404` |
| Size/danh mục không tồn tại | `404` (message nêu rõ "Food size not found: 999") |

### L.2 Đọc target

`GET /api/management/promotions` và `GET /api/management/promotions/{id}` ⇒ thêm `sizeIds` / `categoryIds`
(mảng rỗng = áp cho toàn bộ hoá đơn).

### L.3 Việc FE cần làm

| Việc | Chi tiết |
|---|---|
| ➕ Form khuyến mãi: chọn món/danh mục được giảm | Checkbox list, lưu bằng `PUT .../targets` với danh sách đầy đủ |
| ➕ Hiện phạm vi giảm giá trong danh sách khuyến mãi | Đọc `sizeIds` / `categoryIds` |
| ⚠️ Đừng hiển thị "giảm toàn bộ hoá đơn" khi có target | Target rỗng = áp toàn bộ; có target = chỉ trong phạm vi |

---

## M. Thay đổi BE 2026-10-21 (C-19) — mã giao dịch + phiên bản công thức

### M.1 Mã giao dịch thanh toán

| Việc | Chi tiết |
|---|---|
| ➕ Hiện mã giao dịch cho thu ngân | `POST /api/payments` không gửi `transactionCode` thì **server sinh mã** (`PAY20261021153012-a1b2c3d4`) ⇒ `response.transactionCode` luôn có giá trị |
| ⚠️ Bắt `409` khi mã trùng | `{ "message": "Transaction code '...' already exists" }` — hiện "mã giao dịch đã tồn tại" |
| ➕ Cho nhập mã ngân hàng (tuỳ chọn) | Gửi `transactionCode` trong body; gửi trùng ⇒ `409` |

### M.2 Sửa công thức — `recipeId` đổi

| Việc | Chi tiết |
|---|---|
| ✅ Gọi lại danh sách sau khi lưu | Form hiện tại **đã** làm (`loadData`) ⇒ không cần sửa |
| ⚠️ Bắt `409` khi sửa phiên bản cũ | `{ "message": "This recipe version is no longer active; ..." }` — hiện "công thức này đã được thay thế, hãy sửa phiên bản mới nhất" |
| ℹ️ Version tự tăng | Gửi lại đúng version cũ ⇒ server tự tăng (`1` → `2`); muốn đặt tên riêng thì gửi version khác |

### M.3 Không đổi gì

Route (`PUT /api/management/recipes/{id}`), tên field JSON của request, body request — **giữ nguyên**.

---

## N. Thay đổi BE 2026-10-21 (Đợt 3) — đủ vận hành

> Thêm **3 endpoint** + **3 query param** + **1 cột**. Không phá shape cũ.

### N.1 Hoá đơn theo bàn (thu ngân)

`GET /api/orders/table/{tableId}/bill` — 1 call ra **đủ** hoá đơn của bàn: tổng tiền, dòng món đã gộp,
ghi chú, và `orderIds` để thanh toán lần lượt.

| Việc | Chi tiết |
|---|---|
| ➕ Màn hình thu ngân: nút "Hoá đơn bàn" | Gọi 1 endpoint thay vì tự gọi `GET /api/orders?tableId=` rồi cộng tay |
| ➕ In hoá đơn | Dùng `items[]` (đã gộp) + `totalAmount` |
| ⚠️ Thanh toán vẫn **từng đơn** | Dùng `orderIds` để gọi `POST /api/payments` cho từng đơn |
| ⚠️ Bàn không có đơn mở | `404` ⇒ hiện "bàn này chưa có gì để thu" |

### N.2 Ghi chú hoá đơn

`PATCH /api/orders/{id}/note` — body `{ "note": "...", "changedBy": 0 }`; gửi rỗng để xoá.

| Việc | Chi tiết |
|---|---|
| ➕ Ô "Ghi chú hoá đơn" ở màn hình thu ngân / POS | "Xuất hoá đơn công ty ABC", "chia đôi với bàn 6"… |
| ➕ `note` trong `POST /api/orders` | Ghi lúc tạo đơn (tuỳ chọn) |
| ➕ Đọc `note` | `GET /api/orders/{id}`, `GET /api/orders` (mỗi dòng), `POST /api/orders` |

### N.3 Tồn kho thấp (kho)

`GET /api/management/inventory/low-stock` — danh sách nguyên liệu sắp hết kèm mức tối thiểu và số lượng thiếu.

| Việc | Chi tiết |
|---|---|
| ➕ Màn hình kho: bảng "Sắp hết" | Đọc `ingredientName`, `quanlity`, `minimumStock`, `shortage`, `unit`, `isOutOfStock` |
| ✅ Bỏ đoạn tự ghép 2 danh sách | `inventory.js` hiện tự tính `quantity <= minimum` từ 2 endpoint ⇒ thay bằng endpoint mới |

### N.4 Bảng bếp — poll nhẹ

`GET /api/management/kitchen/orders` thêm `?since=` + `?page`/`?pageSize`. Response **vẫn là mảng**.

| Việc | Chi tiết |
|---|---|
| ➕ Poll 3–5s thay vì bấm tay | Lần đầu gọi **không** kèm `since`; các lần sau kèm `since` = mốc lần trước |
| ⚠️ Lần đầu **không** được kèm `since` | Nếu kèm thì phiếu cũ chưa xử lý sẽ không hiện |
| ➕ Gọi `?status=open` | Lọc phiếu đang mở (backend đã hỗ trợ từ C-13, FE chưa dùng) |
