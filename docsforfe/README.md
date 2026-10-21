# docsforfe — Tài liệu phía FRONTEND

**Mục đích:** đây là nơi **duy nhất** mô tả phần việc FRONTEND của hệ thống Quản lý Nhà hàng.

> **Bắt đầu ở đâu?** Nếu bạn là AI agent: đọc **`AGENTS.md` ở gốc repo** trước — nó chỉ đường dẫn
> đọc, quy tắc làm FE, và những gì **không được** đụng (backend). Nếu bạn là người: đọc
> **`api-contract.md`** (hợp đồng API) rồi `fe-work-plan.md` (bảng công việc).

**Quy tắc bất di bất dịch:**

1. **BACKEND là nguồn chuẩn.** FE không được tự đặt giá, tự tính tổng tiền, tự quyết định trạng thái đơn/món, tự gửi `role`/`actor` để tin là được phép.
2. **FE không được sửa backend.** Muốn thêm API ⇒ ghi vào `docs/architecture/backend-completeness-audit-2026-10-21.md` rồi báo chủ dự án duyệt; sau đó mới cập nhật bảng ở đây.
3. **Mọi thay đổi trạng thái phải gọi API, không sửa state cục bộ.** Ví dụ bếp bỏ tích "xong" ⇒ `PATCH /api/management/kitchen/items/{id}/status`, không được tự set `status` trong JS.
4. **`PUT /api/orders/{id}/items` đã bị bỏ (C-09).** Dùng 3 route lệnh: gọi thêm / đổi số lượng / huỷ món. Route cũ trả `410 Gone`.
5. **Tiêu chí chấp nhận:** đúng + đủ trước, hiệu năng sau. Ưu tiên nghiệp vụ thu ngân/bếp/kho.

---

## Cấu trúc thư mục

| File | Nội dung | Khi nào đọc |
|---|---|---|
| **`api-contract.md`** | **⭐ HỢP ĐỒNG API — file nên mở đầu tiên**: endpoint theo màn hình, body, status code, lỗi, luật nghiệp vụ, checklist bàn giao | **Bắt đầu code FE** |
| `README.md` | Quy tắc + mục lục + trạng thái tổng | Mỗi phiên làm FE |
| `fe-work-plan.md` | **Bảng công việc FE**: màn hình nào, đang thiếu gì, gọi endpoint nào, ưu tiên · mục 0 "backend đã sẵn sàng chưa?" | Lập kế hoạch & làm |
| `fe-api-reference.md` | Danh mục endpoint backend (128 action), đánh dấu FE đã dùng / chưa dùng | Tra cứu nhanh khi code |
| `fe-payloads.md` | Body/response mẫu cho các lệnh quan trọng (gọi thêm, đổi SL, huỷ món, bếp, thanh toán) | Khi tích hợp API |

> **`api-contract.md` là bản tổng hợp, tự chứa đủ.** `fe-api-reference.md` + `fe-payloads.md` là
> tài liệu chi tiết theo từng đợt — dùng để tra cứu sâu, không bắt buộc đọc hết.

---

## Trạng thái hiện tại (quét FE ngày 2026-10-21)

| Hạng mục | Số |
|---|---|
| Trang HTML | 15 (1 `index.html` + 14 trang con) |
| Trang có JS hoạt động | 11 |
| Trang **chưa có JS** | 3 (`reports.html`, `roles.html`, `account.html`) |
| Tổng số lần gọi API trong FE | **46** (file `js/common/*.js` + `js/pages/*.js`) |
| Tổng action backend | **128** ⇒ FE mới dùng **38** (30%) |

**Backend đã sẵn sàng cho FE chưa?** → **CÓ.** Đã kiểm chứng bằng smoke test E2E trên API thật:
**100/100 case / 0 FAIL** (`scripts\e2e-smoke.ps1`).
Chỉ còn **2 mục chưa có API**: `POST /api/orders/{id}/split` (chia bill) ·
`GET/POST /api/management/shifts/*` (ca làm việc). Chi tiết: `api-contract.md` mục 0.

**Ba trang "rỗng":**
- `pages/reports.html` — chỉ nạp `navigation.js` + `management.js`, **không có** `js/pages/reports.js` ⇒ toàn bộ báo cáo doanh thu + dashboard API chưa dùng.
- `pages/roles.html` — không có JS ⇒ `GET /api/roles` chưa dùng.
- `pages/account.html` — không có JS ⇒ `GET/PUT /api/users/me`, `POST /api/auth/change-password` chưa dùng.

**Hạ tầng còn thiếu trong `frontend/js/common/api.js`:**
- ❌ Không gửi `Authorization: Bearer <token>` ⇒ **chưa kích hoạt được phân quyền** (BE đang chạy `Auth:Enforce=false`).
- ❌ Không hỗ trợ tải file (CSV) — cần `blob`.
- ❌ Không hỗ trợ header tùy chọn (`Idempotency-Key`).
- ❌ Không phân biệt 401 (token hết hạn → về trang đăng nhập) với 403 (không đủ quyền → báo người dùng).

---

## Ưu tiên tổng thể (làm theo thứ tự)

| Phase | Nội dung | Lý do |
|---|---|---|
| **F0** | Hạ tầng auth + `api.js` (Bearer, 401/403, blob, header) | Mọi phase sau đều cần token hợp lệ; đây là tiền đề để bật `Auth:Enforce=true` |
| **F1** | **POS**: xem đơn theo bàn, gọi thêm, đổi số lượng, huỷ món, áp khuyến mãi, xem lịch sử món | Lõi vận hành sàn |
| **F2** | **Bếp**: prepared-quantity, shortage, reopen, chi tiết món | Bếp hiện chỉ bấm 2 nút |
| **F3** | **Thu ngân**: danh sách payment, nhiều phương thức, in bill | Sau F1/F2 |
| **F4** | **Kho & Mua hàng**: điều chỉnh tồn, biến động kho, tạo/nhận/huỷ phiếu nhập, NCC, tồn thấp | |
| **F5** | **Báo cáo & Tổng quan**: nối `reports.html` + `dashboard.js` vào API thật | Dashboard hiện là dữ liệu tĩnh |
| **F6** | **Quản trị**: nhân viên (đầy đủ), vai trò, tài khoản, danh mục/topping/size đầy đủ, khu vực bàn, khuyến mãi (+ **gắn phạm vi giảm**) | |
| **F7** | **Đã có API** → làm ngay: cấu hình quán + VAT, chuyển bàn, **bill theo bàn**, **ghi chú hoá đơn**, MoMo + hoàn tiền. **Còn chờ BE:** *chia bill* (`/orders/{id}/split`) · *ca làm việc* (`/management/shifts/*`) | 2 mục cuối chưa có API |
| **F8** | Sau khi BE làm xong payment provider (MoMo): QR, redirect, trạng thái, hoàn tiền | Làm **cuối cùng** |

---

## Liên quan

- **`api-contract.md`** — **hợp đồng API tổng hợp cho FE** (mở đầu tiên).
- `fe-work-plan.md` — bảng công việc FE theo phase F0–F8 + mục 0 "backend đã sẵn sàng chưa?".
- `docs/architecture/backend-capabilities-2026-10-21.md` — **kiểm kê năng lực backend** (có gì / chưa có gì).
- `docs/architecture/backend-completeness-audit-2026-10-21.md` — audit độ đầy đủ backend, danh sách API còn thiếu (nguồn của F7).
- `docs/architecture/endpoint-baseline.md` — hợp đồng API chi tiết phía backend (128 action).
- `docs/architecture/kitchen-stock-rule-2026-10-21.md` — luật bếp ↔ kho ↔ hoàn kho mà FE phải hiển thị đúng.
- `docs/architecture/status-matrix-cancellation-audit-2026-10-20.md` — quy tắc trạng thái đơn/món/phiếu mà FE phải hiển thị đúng.
