# AGENTS.md — hướng dẫn cho AI agent làm FRONTEND trong repo này

> **Vai trò của bạn: lập trình viên FRONTEND.**
> Backend đã **hoàn thành và đóng băng**. Nhiệm vụ của bạn là nối frontend vào API thật.
>
> **Đọc file này trước.** Nó chỉ để bạn biết đọc gì tiếp theo và **không được** làm gì.

---

## 1. Dự án này là gì

Hệ thống **quản lý nhà hàng**: menu · kho · công thức · bàn · đặt món (POS) · bếp · thu ngân ·
khuyến mãi · báo cáo · nhân viên.

| | |
|---|---|
| **Phần bạn làm** | `frontend/` — HTML + CSS + JavaScript thuần, **không có bước build** (không npm/webpack) |
| **Phần KHÔNG đụng** | `RestaurantFoodManagerSystem/` (backend C#) · `RestaurantFoodManagerSystem.Tests/` · `Migrations/` · `appsettings*.json` |
| Backend API | `http://localhost:5225/api` · Swagger: `http://localhost:5225/swagger` |
| Trạng thái API | **128 route** · đã kiểm chứng **100/100 case E2E trên API thật** ⇒ API đáng tin |

---

## 2. Quy tắc bất di bất dịch

1. **KHÔNG sửa backend.** Không đụng `RestaurantFoodManagerSystem/**`, `RestaurantFoodManagerSystem.Tests/**`,
   `Migrations/**`, `appsettings*.json`. Nếu cần backend thêm gì ⇒ **ghi ra `docsforfe/fe-work-plan.md`
   mục C rồi báo chủ dự án**, đừng tự sửa.
2. **Không tự tính tiền.** Server đã tính sẵn `subtotal` / `discountAmount` / `taxAmount` /
   `serviceChargeAmount` / `totalAmount`. FE **chỉ hiển thị** số server trả về — không cộng, không nhân.
3. **Không tự đổi trạng thái.** Trạng thái đơn / món / phiếu bếp **chỉ** đổi qua API.
   Không `state.status = "Ready"` trong JS.
4. **Mọi lỗi hiện `payload.message`.** Backend trả `{ "message": "..." }` cho mọi lỗi — dùng thẳng message đó.
5. **Đơn vị tiền: VND**, số nguyên, format `vi-VN` + `toLocaleString("vi-VN")`.
6. **Không hard-code** endpoint. Tất cả qua `window.restaurantApi` (xem `frontend/js/common/api.js`).
7. **Tiêu chí chấp nhận:** đúng + đủ trước, hiệu năng sau. Ưu tiên nghiệp vụ thu ngân / bếp / kho.

---

## 3. Đọc gì, theo thứ tự

| # | File | Để làm gì |
|---|---|---|
| 1 | **`docsforfe/api-contract.md`** | **⭐ BẮT BUỘC ĐỌC TRƯỚC KHI CODE.** Hợp đồng API tự chứa đủ: 81 route theo màn hình, body, status code (200/201/204), bảng lỗi, **10 luật nghiệp vụ UI phải tôn trọng**, checklist bàn giao |
| 2 | `docsforfe/fe-work-plan.md` | Bảng công việc theo phase **F0–F8**: màn hình nào, hiện đang thiếu gì, gọi endpoint nào, ưu tiên. Mục 0 nói rõ backend đã sẵn sàng chưa |
| 3 | `docsforfe/README.md` | Quy tắc + mục lục + trạng thái FE hiện tại (14 trang, 11 có JS, 3 trang rỗng) |
| 4 | `docsforfe/fe-payloads.md` | Body/response mẫu chi tiết cho các lệnh quan trọng |
| 5 | `docsforfe/fe-api-reference.md` | Danh mục 128 action backend + đánh dấu FE đã dùng/chưa dùng (tra cứu nhanh) |

> Chỉ cần **file 1** là đủ để code. File 2 để biết làm cái nào trước. File 3–5 để tra cứu sâu.

---

## 4. Cấu trúc frontend

```
frontend/
  index.html              ← màn "Tổng quan" + trang điều hướng (data-page="pages/xxx.html")
  pages/*.html            ← 14 trang con, mỗi trang tự nạp script của nó
  css/                    base · layout · components + css/pages/<màn>.css
  js/
    common/               api.js (LỚP HTTP DUY NHẤT) · navigation.js · management.js · dialogs.js · food-*.js
    data/*.data.js        ⚠️ DỮ LIỆU GIẢ TĨNH — cần thay bằng API thật
    pages/*.js            logic từng màn hình
```

**Quy ước đang dùng:**
- JavaScript thuần, **IIFE** `(function () { ... })();` — không framework, không module.
- State nằm trong object `state` đầu file; hàm `load*()` gọi API rồi render.
- Render bằng `innerHTML` + template literal, có `escapeHtml()` cho dữ liệu người dùng.
- Mỗi trang HTML tự nạp: `<script src="../js/common/navigation.js">` rồi `<script src="../js/pages/<màn>.js">`.
- Ghi chú trong code bằng **tiếng Việt**, khớp giọng với file hiện có.

---

## 5. Việc cần làm — theo đúng thứ tự

### F0 — Hạ tầng `api.js` (**làm TRƯỚC, mọi phase sau đều cần**)

`frontend/js/common/api.js` hiện chỉ có `get/post/put/patch/delete`. Cần bổ sung:

| Việc | Ghi chú |
|---|---|
| Đọc token từ `localStorage`, gắn `Authorization: Bearer <token>` cho **mọi** request | token lấy từ `POST /api/auth/login` (`data.token`) |
| `401` → xoá token, chuyển về trang đăng nhập | |
| `403` → báo "không đủ quyền" (không phải "hết phiên") | |
| `download()` dùng `blob` cho CSV | `GET /api/reports/revenue/export.csv` |
| Cho phép header tuỳ chọn | `Idempotency-Key` cho tạo đơn / gọi thêm / đổi SL / thanh toán |

> `GET`/`PUT /api/users/me` **luôn cần token** (kể cả khi backend đang chạy chế độ dev) —
> còn mọi endpoint khác thì chưa bắt buộc cho tới khi bật `Auth:Enforce=true`.

### F1 → F7 — bảng công việc chi tiết trong `docsforfe/fe-work-plan.md`

| Phase | Nội dung |
|---|---|
| **F1** | **POS**: mở đơn theo bàn, gọi thêm, đổi SL, huỷ món, áp khuyến mãi, lịch sử |
| **F2** | **Bếp**: nấu được mấy phần, báo thiếu nguyên liệu, mở lại món |
| **F3** | **Thu ngân**: đơn chờ thu, phương thức thanh toán, in bill, hoàn tiền, MoMo |
| **F4** | **Kho & mua hàng**: điều chỉnh tồn, biến động kho, phiếu mua, NCC, tồn kho thấp |
| **F5** | **Báo cáo & tổng quan**: nối `reports.html` + `dashboard.js` vào API thật (bỏ dữ liệu tĩnh) |
| **F6** | **Quản trị**: topping, cỡ món, nguyên liệu, nhân viên, vai trò, khuyến mãi, khu vực bàn |
| **F7** | Cấu hình quán + VAT, chuyển bàn, bill theo bàn, ghi chú hoá đơn, MoMo + hoàn tiền |
| **F8** | QR MoMo + poll trạng thái (làm **cuối cùng**) |

### 3 trang đang **rỗng** (chưa có file JS)
`pages/reports.html` · `pages/roles.html` · `pages/account.html` ⇒ cần tạo `js/pages/<tên>.js` tương ứng.

### Dữ liệu giả tĩnh cần thay
`js/data/dashboard.data.js` (đang dùng cho màn Tổng quan) · `foods.data.js` · `kitchen.data.js` ·
`order.data.js` · `payment.data.js` · `tables.data.js` ⇒ nối API thật thay vì đọc file giả.

### 2 màn hình **chưa có API** — đừng làm vội
| Màn hình | Endpoint còn thiếu |
|---|---|
| Chia / gộp bill | `POST /api/orders/{id}/split` |
| Ca làm việc / két tiền | `GET/POST /api/management/shifts/*` |

⇒ Cứ bỏ trống hoặc để nút ở trạng thái disabled kèm chú thích *"chờ backend"*. **Đừng** giả lập bằng
dữ liệu tĩnh — sẽ gây hiểu nhầm.

---

## 6. Luật nghiệp vụ UI phải tôn trọng (rút gọn — chi tiết ở `api-contract.md` §5)

| Luật | Hệ quả cho UI |
|---|---|
| **1 bàn = 1 đơn đang mở** | Tạo đơn ở bàn đã có đơn ⇒ `409` ⇒ dùng nút **"Gọi thêm"** (`POST /api/orders/{id}/items`), đừng tạo đơn mới |
| **Gọi thêm ⇒ đơn quay lại `SentToKitchen`** | Thu ngân **không** thu được tiền cho tới khi bếp nấy xong ⇒ hiện "đang chờ bếp", **đừng** báo lỗi |
| **Tiền luôn do server tính** | Hiển thị `totalAmount` server trả về; không cộng tay |
| **Đơn vị nguyên liệu bị khoá** | Sửa nguyên liệu đã dùng mà đổi `unit` ⇒ `409` ⇒ disable ô "Đơn vị" + báo *"Nguyên liệu đã dùng nên không đổi được đơn vị — hãy tạo nguyên liệu mới"* |
| **Sửa công thức ⇒ `recipeId` đổi** | Sau khi lưu **phải** gọi lại `GET /api/management/recipes`; sửa bản đã bị thay thế ⇒ `409` |
| **Khuyến mãi có phạm vi** | Có target ⇒ chỉ giảm trong phạm vi; đọc `sizeIds`/`categoryIds` để hiện phạm vi |
| **Mã giao dịch** | Client không gửi ⇒ server sinh mã; gửi mã trùng ⇒ `409` ⇒ báo "mã đã tồn tại" |
| **Hoàn tiền** | Đơn mở lại `Ready` (chờ thu lại) · đã thu rồi hoàn ⇒ **không còn tính doanh thu** |
| **Món `Preparing` = đã tiêu thật** | POS **không** tự huỷ/giảm món `Preparing` (chỉ Chef/Admin) |
| **Mã lý do huỷ** | `CustomerChange` (mặc định) = hoàn kho · `OutOfStock`/`PhysicalShortage`/`KitchenError` = ghi hao hụt |
| **Quirk `GET /api/restaurant-tables/{id}`** | Endpoint này **ghi DB** ⇒ **đừng** gọi chỉ để xem bàn; dùng `GET /api/restaurant-tables` |

---

## 7. Chạy & kiểm tra

```powershell
# 1) Chạy backend (cần để FE có API)
dotnet run --project RestaurantFoodManagerSystem        # http://localhost:5225/api

# 2) Chạy frontend — file tĩnh, KHÔNG có bước build.
#    Mở trực tiếp: frontend\index.html
#    Hoặc phục vụ tĩnh (khuyến nghị, để tránh lỗi CORS/file://):
python -m http.server 5500 --directory frontend
#    → http://localhost:5500/index.html
```

| | |
|---|---|
| Base URL API | `http://localhost:5225/api` (đọc từ `localStorage.restaurant.apiBaseUrl`) |
| Tài khoản test | `e2e-runner` / `E2E@123456` |
| Swagger | `http://localhost:5225/swagger` |
| Kiểm tra contract | đối chiếu mọi endpoint bạn gọi với `docsforfe/api-contract.md` §4 |
| Kiểm chứng backend (không sửa FE) | `powershell -ExecutionPolicy Bypass -File scripts\e2e-smoke.ps1` ⇒ 100/100 |

---

## 8. Bẫy đã gặp

| Bẫy | Cách tránh |
|---|---|
| CORS khi mở FE bằng `file://` | dùng static server (mục 7) |
| `payload.message` = `undefined` | backend **luôn** trả JSON `{message}`; nếu thấy `undefined` ⇒ kiểm tra `response.ok` trước khi đọc |
| Status code lẫn lộn | `201` (tạo món/cỡ/topping) · `204` (PATCH/PUT cập nhật) · `200` (còn lại) — `api.js` đã xử lý `204` ⇒ `null` |
| `POST /api/categories` không trả id | phải `GET /api/categories` rồi tìm theo tên |
| `POST /api/restaurant-tables` trả `{message, data}` | id nằm ở `data.tableId` |
| Tên field `quanlity` ở kho/ledger | giữ nguyên lỗi gõ cũ; đơn/bếp dùng `quantity` |
| `GET /api/reports/dashboard` | **không** phải `/api/management/dashboard` |
| Bếp poll `?since=` | lần **đầu** gọi **không** kèm `since`, các lần sau mới kèm |

---

## 9. Nếu cần hiểu sâu hơn phía backend

Đọc (không sửa): `docs/architecture/backend-capabilities-2026-10-21.md` (backend có gì) ·
`docs/architecture/kitchen-stock-rule-2026-10-21.md` (luật bếp ↔ kho) ·
`docs/architecture/endpoint-baseline.md` (hợp đồng API phía server).

**Cần backend thêm gì?** Ghi vào `docsforfe/fe-work-plan.md` mục C (bảng "API còn thiếu") rồi báo chủ dự án.
