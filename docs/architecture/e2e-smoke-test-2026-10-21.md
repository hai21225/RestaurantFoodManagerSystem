# Smoke test end-to-end (E2E) — 2026-10-21

> Chạy **API thật** (SQL Server thật) qua HTTP, đi hết một ca vận hành của quán từ lúc
> dựng menu đến lúc thu tiền + hoàn tiền + báo cáo.

## 1. Chạy

```powershell
# 1) Chạy API (cổng 5225 — đúng cổng FE đang trỏ tới)
dotnet run --project RestaurantFoodManagerSystem

# 2) Chạy smoke test (PowerShell 5.1 hoặc 7+)
powershell -ExecutionPolicy Bypass -File scripts\e2e-smoke.ps1
```

Kết quả cuối:

```
KẾT QUẢ E2E: 100 OK / 0 FAIL
```

## 2. Script dọn dữ liệu test

```powershell
sqlcmd -S HAI2\SQLEXPRESS -d RestaurantFoodManager -E -i scripts\cleanup-e2e.sql
```

Chỉ xoá bản ghi **liên quan tới chuỗi "E2E"** trong tên (danh mục / món / nguyên liệu /
khuyến mãi / nhà cung cấp / khu vực / số bàn) và mọi bản ghi con của chúng. Dữ liệu thật
của quán **không** bị đụng tới.

> Lưu ý kỹ thuật: SQL Server cần `SET QUOTED_IDENTIFIER ON` khi xoá trên bảng có
> **filtered index** (unique) — `sqlcmd` mặc định để OFF. Script đã bật sẵn.

## 3. 20 nhóm case được kiểm tra

| # | Nhóm | Case chính | Kết quả mong đợi |
|---|---|---|---|
| 0 | Khởi tạo & đăng nhập | `/setup/status`, đăng ký, đăng nhập, sai mật khẩu | `200` / `401` |
| 1 | Danh mục · món · cỡ · topping | tạo 2 danh mục, 2 món, 2 cỡ, 1 topping, gắn topping | `200`/`201` |
| 2 | Kho | 2 nguyên liệu, nhập kho tay, phiếu mua + nhận, **nhận 2 lần bị chặn** | `204` / `404` |
| 3 | Công thức | tạo 2 công thức, sửa ⇒ **2 phiên bản**, chỉ 1 bản active | `204`, 2 bản |
| 4 | Khu vực + bàn | tạo khu vực + 2 bàn | `200` |
| 5 | Cấu hình quán | VAT 10% + phí DV 5% ⇒ đọc lại khớp | `200` |
| 6 | Khuyến mãi | tạo khuyến mãi, gắn target, đọc lại target, **target sai ⇒ 404** | `204` / `404` |
| 7 | POS tạo đơn | 2 món (1 kèm topping) ⇒ trừ kho đúng, tiền hàng 150.000, tổng 157.500 | `200` |
| 8 | Bếp vòng 1 | phiếu có số bàn/tên phục vụ/số món · Preparing → Ready ⇒ đơn `Ready` | `204` |
| 9 | Gọi thêm | thêm 1 phần ⇒ tiền hàng 210.000, đơn quay lại `SentToKitchen`, **vòng bếp mới**, bếp nấy xong ⇒ `Ready` | `200`/`204` |
| 10 | Bảng bếp `?since=` | không lọc / `since` cũ / `since` mới / phân trang | `200` |
| 11 | Khuyến mãi 2 chiều | giảm **đúng phạm vi target** (190.000 × 10% = 19.000), món ngoài target không bị giảm | `204` |
| 12 | Hoá đơn theo bàn | `GET /orders/table/{id}/bill` ⇒ tổng + dòng món gộp + ghi chú; bàn trống ⇒ `404` | `200`/`404` |
| 13 | Ghi chú hoá đơn | ghi, đọc lại, có dòng lịch sử, xoá | `204` |
| 14 | Chuyển bàn | bàn cũ → `Available`, bàn mới → `Occupied`, chuyển lại | `204` |
| 15 | Thanh toán tiền mặt | mã giao dịch giữ nguyên, đơn → `Paid`, **thu trùng ⇒ 409**, bàn → `Available`, **doanh thu đã ghi nhận** | `200`/`409` |
| 16 | Hoàn tiền | giao dịch → `Refunded`, đơn mở lại `Ready` | `200` |
| 17 | MoMo (giả lập) | intent `Pending`, `isMock=true`, **IPN chữ ký sai bị từ chối**, đơn không đổi | `200` |
| 18 | Báo cáo | doanh thu sau hoàn = 0, CSV có dòng `TOTAL` | `200` |
| 19 | Tồn kho thấp + ledger | nguyên liệu sắp hết có `minimumStock`/`shortage`, ledger có `unit` | `200` |
| 20 | Lịch sử + dashboard | 13 dòng lịch sử đầy đủ, số bàn có khách | `200` |

## 4. Những luật nghiệp vụ được kiểm chứng lại bằng API thật

1. **Gọi thêm ⇒ đơn quay lại `SentToKitchen`** ⇒ thu ngân **không** thu được tiền cho tới khi
   bếp nấy xong vòng mới (đúng luật P1e "không thu tiền khi bếp chưa xong").
2. **Khuyến mãi có target chỉ giảm trong phạm vi** — dòng món ngoài target giữ nguyên giá.
3. **Hoàn tiền ⇒ doanh thu về 0** — đã thu rồi hoàn thì không còn tính vào doanh thu.
4. **Nhận phiếu mua 2 lần bị chặn** ⇒ không cộng kho 2 lần.
5. **Sửa công thức giữ lịch sử** — bản cũ `isActive=false`, bản mới `isActive=true`.
6. **Ledger kho có đơn vị** — mọi dòng `Sale`/`Purchase`/`AdjustmentIn` đều kèm `unit`.
7. **IPN MoMo sai chữ ký bị từ chối** ⇒ đơn không bị đóng nhầm.

## 5. Bẫy kỹ thuật gặp khi viết script (để tránh lần sau)

| Bẫy | Nguyên nhân | Cách xử lý |
|---|---|---|
| File `.ps1` tiếng Việt bị lỗi parse | PowerShell 5.1 đọc file **không BOM** theo ANSI | Lưu file **UTF-8 có BOM** |
| `Invoke-WebRequest` lỗi *"NonInteractive mode"* | PS 5.1 cần engine IE để parse HTML | thêm `-UseBasicParsing` |
| JSON tiếng Việt bị server trả `400` | PS 5.1 encode body kiểu Latin-1 | gửi **byte UTF-8**: `[Text.Encoding]::UTF8.GetBytes($json)` + `ContentType "application/json; charset=utf-8"` |
| `(...).Count` trả `$null` | object đơn không có thuộc tính `.Count` trong PS 5.1 | bọc `@(...)` |
| `DELETE` bị chặn bởi filtered index | `sqlcmd` để `QUOTED_IDENTIFIER OFF` | thêm `SET QUOTED_IDENTIFIER ON` |

## 6. Vài khác biệt về **hợp đồng API** phát hiện khi chạy thật

| Endpoint | Response thật | Lưu ý |
|---|---|---|
| `POST /api/categories` | `{ "message": "Category created successfully" }` — **không trả id** | Client phải `GET /api/categories` rồi tìm theo tên (FE hiện reload cả danh sách nên không vướng) |
| `POST /api/foods`, `/toppings`, `/foods/{id}/sizes` | **`201 Created`** + DTO | các endpoint khác trả `200` |
| `POST /api/management/inventory/{id}/adjust` | **`204 No Content`** | không có body |
| `POST /api/management/purchase-orders/{id}/receive` | **`204 No Content`** | không có body |
| `PATCH`/`PUT` cập nhật (bếp, ghi chú, chuyển bàn, khuyến mãi, target, công thức) | **`204 No Content`** | không có body |
| `POST /api/restaurant-tables` | `{ "message": ..., "data": { tableId, ... } }` | id nằm trong `data` |
| `GET /api/reports/dashboard` | — | **không** phải `/api/management/dashboard` |
