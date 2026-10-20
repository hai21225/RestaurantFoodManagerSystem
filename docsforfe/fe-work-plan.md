# Bảng công việc FRONTEND — hệ thống Quản lý Nhà hàng

**Cập nhật:** 2026-10-21 · **Nguồn chuẩn:** backend `RestaurantFoodManagerSystem` (115 action)
**Quy tắc:** xem `docsforfe/README.md`. FE không sửa backend; backend không sửa FE.

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
| **FE-11** | `pages/inventory.js` | **2 call**: `GET /api/management/inventory`, `GET /api/management/ingredients` ⇒ chỉ xem | **Điều chỉnh tồn** (có lý do); xem **biến động kho**; quản lý **nhà cung cấp**; xem **tồn kho thấp** | `POST /api/management/inventory/{ingredientId}/adjust` · `GET /api/management/inventory-transactions` · `GET/POST/PUT/DELETE /api/management/suppliers` · `PATCH /api/management/suppliers/{id}/active` · *(GET tồn thấp — BE chưa có, xem mục C)* | **F4** |
| **FE-12** | `pages/purchases.js` | **1 call**: `GET /api/management/purchase-orders` ⇒ chỉ xem danh sách | Tạo phiếu nhập (NCC + dòng hàng + giá), xem chi tiết, **nhập hàng** (cộng kho), huỷ phiếu | `POST /api/management/purchase-orders` · `GET /api/management/purchase-orders/{id}` · `POST /api/management/purchase-orders/{id}/receive` · `POST /api/management/purchase-orders/{id}/cancel` | **F4** |
| **FE-13** | `pages/employees.js` | 2 call: `GET /api/users`, `POST /api/users` | Sửa nhân viên, khoá/mở khoá, đặt lại mật khẩu, **gán/thu role**, xem danh sách role | `GET/PUT /api/users/{id}` · `PATCH /api/users/{id}/active` · `POST /api/users/{id}/reset-password` · `POST /api/users/{userId}/roles` · `DELETE /api/users/{userId}/roles/{roleName}` · `GET /api/roles` | **F6** |
| **FE-14** | `pages/roles.html` | **Không có JS** | Tạo `js/pages/roles.js` hiển thị 5 vai trò + mô tả quyền | `GET /api/roles` | **F6** |
| **FE-15** | `pages/reports.html` | **Không có JS** | Tạo `js/pages/reports.js`: chọn kỳ (day/week/month) + ngày, xem bảng doanh thu theo nhóm, **tải CSV** | `GET /api/reports/revenue?period=&date=` · `GET /api/reports/revenue/export.csv?period=&date=` (cần `blob` trong api.js) | **F5** |
| **FE-16** | Khuyến mãi | **Chưa có màn hình nào** | Tạo `pages/promotions.html` + `js/pages/promotions.js`: CRUD khuyến mãi, bật/tắt, xem khuyến mãi đang áp cho đơn và **gỡ khuyến mãi** | `GET/POST/PUT/DELETE /api/management/promotions` · `PATCH /api/management/promotions/{id}/active` · `PATCH /api/orders/{id}/promotion/{promotionId}` · *(DELETE gỡ khuyến mãi — BE chưa có, xem mục C)* | **F6** |
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

Body lỗi của BE luôn là `{ "message": "..." }` → `api.js` đã lấy `payload.message`; chỉ cần phân biệt status code.

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
