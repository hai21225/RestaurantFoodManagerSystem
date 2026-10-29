# Mục lục tài liệu dự án

## Phase 2 (2026-10-20) — ✅ đã xong

- Backend-only: gọi thêm, điều chỉnh số lượng, prepared-quantity, shortage; Cách A (`OrderItemGroupId`).
- C-07/C-08: chặn huỷ ticket sau `Paid`; huỷ theo `ReasonCode` (hoàn kho vs wastage).
- C-06/C-10/C-11: mở lại món đã xong, audit từng dòng món, bỏ `Accepted` ở cấp món + CHECK constraint.
- C-09: `PUT /api/orders/{id}/items` (thay toàn bộ dòng món) đã bị chặn → `410 Gone`; dùng 3 route lệnh.
- Test **538/538 pass**; build **0 warning / 0 error**.
- Chi tiết: `docs/architecture/status-matrix-cancellation-audit-2026-10-20.md` mục 13 và `docs/architecture/progress-summary-2026-10-20.md` phần đầu.

---
Cập nhật: **2026-10-21**  Dùng file này làm điểm vào khi bắt đầu phiên làm việc mới.> **Quy tắc bắt buộc:** chỉ làm **backend**. Không sửa `frontend/`, `wwwroot/` hoặc file FE nào nữa, trừ khi có yêu cầu rõ ràng.---## 1. Đọc trước khi làmThứ tự khuyến nghị:1. **`docs/architecture/progress-summary-2026-10-21.md`** — trạng thái mới nhất: đã làm gì, còn gì, build/test, quy tắc làm việc.2. **`docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md`** — P1c/P1d đã triển khai: ma trận trạng thái, phân quyền hủy, hoàn kho, API hủy món, migration và test.3. **`docs/architecture/refactor-plan.md`** — bối cảnh các giai đoạn GĐ0–GĐ8, Batch A/A2, các việc còn treo.4. **`docs/architecture/endpoint-baseline.md`** — hợp đồng API và các hành vi phải giữ.5. **`docs/architecture/data-access.md`** — quy tắc `AppDbContext`, tracking/`AsNoTracking`, không đổi tùy tiện.6. **`docs/architecture/backend-frontend-audit.md`** — audit chi tiết backend ↔ frontend, defect và trạng thái xử lý.7. **`docs/architecture/status-matrix-cancellation-audit-2026-10-20.md`** — rà ma trận trạng thái và luồng hủy món sau P1c/P1d: lỗi đồng bộ POS/bếp/kho/bàn/thanh toán; quyết định **order mở tới Paid**, **gọi thêm bằng round mới**, **giảm/tăng số lượng sau khi bếp nhận**, **thiếu nguyên liệu thực tế**, hướng sửa theo giai đoạn.Nếu chỉ có thời gian đọc một file: đọc **`progress-summary-2026-10-21.md`** trước.---## 2. Danh mục file| File | Nội dung | Khi nào đọc | Trạng thái ||---|---|---|---|| `docs/architecture/progress-summary-2026-10-21.md` | **Tổng kết mới nhất**: 8 đợt (C-13 luật bếp↔kho → sửa 12/12 lỗi P0 → "Tiền đúng" → chuyển bàn/MoMo → hợp đồng lỗi → ledger+đơn vị → target khuyến mãi → mã giao dịch+phiên bản công thức → "Đủ vận hành"), build/test, hợp đồng API đổi, việc còn lại, bài học | **Mỗi phiên mới** | ✅ **Mới nhất** |
| `docs/architecture/backend-capabilities-2026-10-21.md` | **Kiểm kê năng lực backend**: 11 module × đã có gì, 30 bảng, luật đã cài, độ tin cậy, 2 mục còn thiếu, P2 đã hoãn, cách vận hành | **"Backend đã có gì?"** | ✅ **Mới nhất** |
| `docs/architecture/e2e-smoke-test-2026-10-21.md` | **Smoke test E2E trên API thật**: script `scripts/e2e-smoke.ps1` (100 case / 0 fail) + script dọn `scripts/cleanup-e2e.sql` + bẫy PowerShell/SQL | Kiểm chứng backend trước khi bàn giao | ✅ **Mới nhất** |
| `docs/architecture/kitchen-stock-rule-2026-10-21.md` | **Luật chuẩn** bếp ↔ kho ↔ hoàn kho (mốc `Preparing` = khóa), ma trận 9 dòng, delta, endpoint mới | Khi làm việc với đơn/bếp/kho | ✅ Đã triển khai |
| `docs/architecture/backend-gap-review-2026-10-21.md` | Gap review P0/P1/P2 + trạng thái sửa từng mục P0 | Khi cần biết còn lỗi gì / còn thiếu tính năng gì | ✅ **12/12 P0 đã sửa** |
| `docs/architecture/progress-summary-2026-10-20.md` | Tổng kết đợt trước: P1c/P1d, C-06/C-09/C-10/C-11 | Khi tra lịch sử trước 2026-10-21 | ✅ Lịch sử || `docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md` | P1c/P1d: ma trận trạng thái đơn/món/phiếu bếp, phân quyền hủy, API hủy món, hoàn kho/ledger, thanh toán, migration và test | Khi làm việc với đơn/bếp/kho | ✅ Đã triển khai 2026-10-20 || `docs/architecture/refactor-plan.md` | Kế hoạch refactor, lộ trình GĐ0–GĐ8, Batch A/A2, các quyết định còn treo | Cần bối cảnh/lộ trình | 🟡 Còn dùng; số test cũ || `docs/architecture/endpoint-baseline.md` | Baseline hành vi API, route/verb/status/body lỗi, các quirk phải giữ | Trước khi sửa API | 🟡 Hợp đồng còn giá trị; số test cũ || `docs/architecture/data-access.md` | Quy tắc truy cập dữ liệu sau GĐ5, ánh xạ tracking/`AsNoTracking` | Trước khi sửa service/EF | ✅ Quy tắc còn giá trị || `docs/architecture/backend-frontend-audit.md` | Audit backend ↔ frontend, 32 defect, field thiếu, dead code | Cần tra chi tiết lỗi/lịch sử | 🟡 Audit lịch sử; trạng thái mới ghi ở đầu file || `docs/architecture/backend-audit-2026-10-20.md` | Audit backend sau P1c/P1d: 3 High, 11 Medium, 12 Low, kèm file/dòng, tác động và hướng sửa | Khi cần biết "đang lỗi gì, sửa gì" | ✅ Mới nhất 2026-10-20 || `docs/architecture/status-matrix-cancellation-audit-2026-10-20.md` | Rà ma trận trạng thái đơn/món/phiếu bếp, luồng hủy từ POS/bếp, đồng bộ kho-bàn-thanh toán; order mở tới Paid, gọi thêm bằng round mới, giảm/tăng số lượng sau khi bếp nhận, thiếu nguyên liệu thực tế; 5 lỗi ưu tiên + lộ trình sửa | Khi làm hủy món/trạng thái/gọi thêm/thanh toán | ✅ Mới nhất 2026-10-20 |> Các con số test trong `refactor-plan.md`, `endpoint-baseline.md` và `backend-frontend-audit.md` là **mốc lịch sử tại thời điểm viết**, không phải số hiện tại. Số hiện tại nằm trong `progress-summary-2026-10-20.md`.---## 3. Trạng thái hiện tại (tóm tắt nhanh)- Nhánh: **`hai`**- Commit gần nhất: **`c162385`**- Working tree: **chưa commit** (gồm Batch A2, auth, soft-delete, bàn/khu vực bàn, P1c/P1d và đợt rà soát hồi quy 2)- Build: **0 warning / 0 error**- Test: **505/505 pass** (tăng 7 test so với 498 test cũ; sau đợt sửa H-01, M-01–M-06, M-08, M-10, L-01 và C-01/C-02/C-03 ngày 2026-10-20)- EF: **không có pending model changes**- Migration mới: **`20261020075758_AddTableAreas`** (đã apply vào SQL Server thật ngày 2026-10-20)- Migration P1c/P1d: **`20261020093304_AddOrderItemCancellationAndItemLedger`** (đã apply vào SQL Server thật ngày 2026-10-20)- Auth: **`Auth:Enforce=false` tạm thời** (đã bật `true` để test ngày 2026-10-20, sau đó tắt lại để frontend dùng được); JWT key dev vẫn cấu hình trong `appsettings.json`. App log cảnh báo lúc khởi động khi `Enforce=false`, và **fail-fast** nếu JWT key < 32 byte.- Frontend: đã sửa trong đợt trước, **từ nay không sửa nữa nếu không được yêu cầu**. Lưu ý: FE hiện **chưa gửi Bearer token**, nên khi bật lại `Enforce=true` các API gọi từ UI sẽ trả `401` cho tới khi FE được cập nhật.---## 4. Việc còn treo (đọc chi tiết trong progress summary)- **Phase 0 (2026-10-20)**: đã thêm `OrderLifecycleCoordinator`, sửa C-01/C-02/C-03, thêm 7 test hồi quy; build 0 warning / 0 error, test 505/505 pass. Không đổi schema/API/FE.
- **P1c/P1d**: đã triển khai xong (xem `docs/architecture/p1c-p1d-state-machine-design-2026-10-20.md`). Còn lại:  frontend gửi `note`/`reason` khi hủy; refund cho đơn `Paid`; gọi thêm bằng round/ticket mới; giảm/tăng số lượng sau khi bếp nhận (tách dòng/`PreparedQuantity`); thiếu nguyên liệu thực tế; ledger theo món cho  luồng `PUT /orders/{id}/items`.- **Rà soát hồi quy đợt 2** (2026-10-20): đã sửa lỗi tiền / mật khẩu / actor-FK / hợp đồng HTTP.  Còn lại (cần quyết định nghiệp vụ hoặc đổi schema): xem `progress-summary-2026-10-20.md` mục 2.8.- ~~Lưu `PromotionId` trên order~~ — ✅ **đã làm** (Đợt "Tiền đúng", 2026-10-21): `orders.PromotionId` + snapshot.- ~~`Category.DisplayOrder` đang là `bool`~~ — ✅ **đã đổi thành `int`**.- ~~Múi giờ của `promotions.StartAt/EndAt`~~ — ✅ **đã chốt: API nhận giờ VN → server lưu UTC**.- Frontend gửi Bearer token + xử lý `401/403` (ngoài phạm vi backend); hiện đang tạm `Auth:Enforce=false` để UI chạy.- Rate limit / lockout đăng nhập; thu hồi token cũ sau khi khoá tài khoản (cần `security stamp` ⇒ đổi schema).- Test concurrency thật trên SQL Server.- Dashboard frontend vẫn dùng dữ liệu tĩnh.- Chưa có UI quản lý khu vực bàn; API CRUD đã có.---## 5. Cách yêu cầu trong phiên mớiCó thể nói ngắn gọn:```textĐọc docs/README.md trước.Chỉ làm backend, không sửa frontend.Sau đó đọc progress-summary, endpoint-baseline và data-access.```Agent sẽ biết phải đọc file nào, trạng thái hiện tại ra sao và giới hạn phạm vi.---## 6. Lưu ý về GitThư mục `docs/` **không** nằm trong `.gitignore` ⇒ các file này đi kèm repository khi commit.`nếu máy khác chưa có docs: `git pull` (hoặc copy thủ công thư mục `docs/` + `docsforfe/` + `scripts/`).

---

## Nếu bạn là AI agent làm phần BACKEND

> `AGENTS.md` ở gốc repo hiện hướng cho **agent làm FRONTEND** (giai đoạn hiện tại của dự án).
> Nếu bạn được giao làm **backend**, đọc theo thứ tự:

1. **`docs/architecture/backend-capabilities-2026-10-21.md`** — backend hiện có gì (11 module, 30 bảng, luật đã cài, độ tin cậy, cách vận hành).
2. `docs/architecture/progress-summary-2026-10-21.md` — 8 đợt đã làm + **việc còn lại**.
3. `docs/architecture/endpoint-baseline.md` — hợp đồng API (đọc trước khi sửa API).
4. `docs/architecture/kitchen-stock-rule-2026-10-21.md` — luật bếp ↔ kho ↔ hoàn kho (đọc trước khi đụng trừ/hoàn kho).
5. `docs/architecture/backend-gap-review-2026-10-21.md` — gap P0/P1/P2 + trạng thái từng mục.

**Quy tắc backend:** chỉ sửa `RestaurantFoodManagerSystem/` + `RestaurantFoodManagerSystem.Tests/` ·
**không** sửa `frontend/` (trừ khi chủ dự án yêu cầu rõ ràng) · mỗi thay đổi schema = 1 migration +
apply trên DB thật · build phải **0 warning / 0 error** · test phải **xanh hết** ·
`dotnet ef migrations has-pending-model-changes` phải = **"No changes"**.

**Bẫy đã gặp:** đừng `dotnet build` khi API đang chạy (file `.exe`/`.dll` bị khoá ⇒ `MSB3021`) ·
script PowerShell phải lưu **UTF-8 có BOM**; gửi body JSON tiếng Việt phải gửi **byte UTF-8** ·
`sqlcmd` cần `SET QUOTED_IDENTIFIER ON` khi xoá trên bảng có filtered index.

---

## Kiểm kê năng lực backend (2026-10-21)

> **File này trả lời: "backend hiện đã có gì?"** — theo từng phần nghiệp vụ, kèm những gì **chưa** làm.

- `docs/architecture/backend-capabilities-2026-10-21.md` — **năng lực theo 11 module** (menu · kho · công thức · bàn ·
  đơn · bếp · thanh toán · khuyến mãi · cấu hình tiền · báo cáo · nhân viên) + **30 bảng** + các luật đã cài +
  độ tin cậy (transaction / concurrency / idempotency / audit) + **2 mục còn thiếu** + **P2 đã hoãn** + cách vận hành.
- Bản bàn giao riêng cho FE: `docsforfe/api-contract.md`.

---

## E2E — smoke test trên API thật (2026-10-21)

- **Chạy:** `dotnet run --project RestaurantFoodManagerSystem` → rồi
  `powershell -ExecutionPolicy Bypass -File scripts\e2e-smoke.ps1` ⇒ **100 OK / 0 FAIL**
- **Dọn dữ liệu test:** `sqlcmd -S HAI2\SQLEXPRESS -d RestaurantFoodManager -E -i scripts\cleanup-e2e.sql`
  (chỉ xoá bản ghi có chuỗi **"E2E"** trong tên + các bản ghi con; dữ liệu thật không bị đụng).
- Chi tiết 20 nhóm case + các bẫy kỹ thuật PowerShell/SQL gặp phải:
  `docs/architecture/e2e-smoke-test-2026-10-21.md`.

---

## Đợt 3 — "Đủ vận hành" (2026-10-21)

4 API mà quán thật dùng hằng ngày mà backend còn thiếu.

- **B2 — hoá đơn gộp theo bàn:** `GET /api/orders/table/{tableId}/bill` — gom mọi đơn **đang mở** của bàn
  thành 1 bản tổng hợp (tiền hàng / giảm giá / VAT / phí DV / tổng + dòng món đã gộp + ghi chú + chi tiết từng đơn).
  Tổng = **tổng của từng đơn** ⇒ không lệch tiền thật. `404` khi bàn không có đơn đang mở.
- **B3 — ghi chú hoá đơn:** cột mới `orders.Note` + `PATCH /api/orders/{id}/note` (gửi rỗng = xoá) +
  `note` trong request tạo đơn và trong 3 response đơn. Mỗi lần sửa ghi 1 dòng lịch sử đơn.
- **B4 — tồn kho thấp:** `GET /api/management/inventory/low-stock` — danh sách nguyên liệu sắp hết kèm
  `minimumStock` / `shortage` / `unit` / `isOutOfStock`, sắp theo số lượng thiếu giảm dần.
- **B6 — bảng bếp:** `GET /api/management/kitchen/orders` thêm `?since=` (chỉ phiếu **có thay đổi** kể từ mốc)
  + `?page` / `?pageSize` tuỳ chọn. Response **vẫn là mảng** ⇒ không phá FE.
- **Migration:** `20261021070215_AddOrderBillNote` (đã apply trên DB thật) — chỉ thêm cột `orders.Note`.
- **Kiểm chứng:** build **0 warning / 0 error**; test **670/670 pass**; `has-pending-model-changes` = **No changes**; **không** đụng FE.
- **Phía FE:** `docsforfe/fe-work-plan.md` mục **N** + `docsforfe/fe-api-reference.md` phụ lục 2026-10-21 (đợt 7).

---

## C-23 — Rà soát sâu `OrderService` + sửa 8 điểm (2026-10-21)

> File `OrderService.cs` đã qua một lần splice dòng (đợt C-20) ⇒ rà soát lại toàn bộ: quét cân bằng
> ngoặc, kiểm kê 29 thành viên, đối chiếu từng luồng nghiệp vụ với `kitchen-stock-rule` §2.2–§2.4.1.

- **Kết luận cấu trúc: file NGuyÊN vẹn** — dấu vết splice chỉ là mã hoá lại comment
  (mojibake → UTF-8), **không dòng lệnh nào thay đổi** (1763 dòng code trước = 1763 sau).
- **Sửa 8 điểm:** (1) chuyển bàn — bàn đích lỗi đầu vào trả **400** thay vì 404/409 ·
  (2) cơ sở khuyến mãi dùng LEFT JOIN (không còn âm thầm từ chối khuyến mãi khi món bị xoá cứng) ·
  (3) lưu dòng bếp mới **trước** khi tính lại trạng thái phiếu · (4) `CancelItemAsync` dùng chung
  `RecalculateTotals` · (5) điều chỉnh SL không đổi gì thì không để lọt group id ·
  (6) áp khuyến mãi lên đơn `Paid` trả **409** (giống gỡ khuyến mãi) ·
  (7) gắn lại 2 khối XML doc bị bỏ trôi · (8) lỗi áp khuyến mãi trả **JSON `{message}`** thay vì text thuần.
- **Vệ sinh mojibake:** quét lại **toàn repo** (272 file) ⇒ **0 dòng còn lại**.
- **Kiểm chứng:** build **0 warning / 0 error**; test **697/697 pass**; E2E **100/100**;
  `has-pending-model-changes` = **No changes**; **không** đụng FE.
- **Chi tiết:** `docs/architecture/endpoint-baseline.md` mục "C-23" · test `Tests/C23OrderContractTests.cs`.

## C-22 — Khôi phục cấu hình quán sau khi chạy E2E (2026-10-21)

- **Vấn đề:** `restaurant_settings` là **singleton dùng chung** (VAT, phí dịch vụ, tên quán).
  `scripts\e2e-smoke.ps1` đổi VAT 10% / phí DV 5% / tên "Quán E2E Test" để kiểm tra công thức
  tiền — nhưng **không khôi phục lại**, và `scripts\cleanup-e2e.sql` cũng không đụng bảng này.
  ⇒ **chạy E2E là đổi luôn tiền thật của quán** (VAT + phí DV) và tên quán.
- **Sửa:** script E2E nay **lưu cấu hình cũ trước** khi `PUT` và **khôi phục lại ở cuối**
  (in dòng "Đã khôi phục cấu hình quán (VAT …, phí DV …)"). Nếu script bị tắt giữa chừng
  thì phải `PUT /api/management/settings` lại bằng tay.
- **Kiểm chứng:** đặt cấu hình "QUAN THAT CUA TOI" (VAT 8%, phí DV 3%) → chạy E2E
  ⇒ **100 OK / 0 FAIL** và cấu hình được khôi phục **đúng như cũ**.

---

## C-21 — Sửa 6 lỗi làm **báo cáo sai** (2026-10-21)

> Đợt sửa **trước** khi làm thêm báo cáo vận hành (B5) — báo cáo trên nền dữ liệu sai thì ra báo cáo sai.

- **C-21.1** `ReasonCode` mất ở cấp **đơn** khi huỷ 1 món / bếp huỷ món cuối ⇒ báo cáo "đơn huỷ vì
  hết hàng" bỏ sót. Nay `reasonCode` được truyền xuống tới `ApplyOrderStatusAsync`.
- **C-21.2** Ledger `Before/After` sai khi **tách dòng** ⇒ dựng lại tồn kho từ ledger ra số sai.
  Nay giữ **chuỗi running-balance liên tục** (dòng gốc `After = Before − phần còn lại`, dòng tách nối tiếp).
- **C-21.3** Ledger **điều chỉnh tay** giờ **bắt buộc có lý do** (`note` → `400` nếu thiếu) + có
  `ReferenceType = "ManualAdjustment"`; ledger **nhập phiếu mua** có `Note` ("Nhập phiếu mua #<id> từ <NCC>").
- **C-21.4** `GET /api/management/inventory/low-stock` thêm field `stockStatus` và **gồm cả** nguyên liệu
  `OutOfStock`/`Spoiled` (trước chỉ lọc tồn ≤ mức tối thiểu ⇒ nguyên liệu bếp đánh dấu hư mà còn
  tồn nhiều thì **không hiện ở đâu cả**).
- **C-21.5** `?since=` của bảng bếp lọc **cả** lịch sử trạng thái **món** ⇒ món `Ready` trong khi món khác
  còn `Preparing` (phiếu không đổi mốc) cũng được trả về ⇒ bếp không bỏ sót món vừa xong.
- **C-21.6** CSV **chặn formula injection** (ô bắt đầu `=`/`+`/`-`/`@`); số âm (`-5`) không bị chặn.
- **Hợp đồng API đổi:** `note` bắt buộc khi điều chỉnh tồn · `low-stock` có `stockStatus` ·
  `inventory-transactions` có `referenceType = ManualAdjustment` · `?since=` trả nhiều hơn trước.
- **Kiểm chứng:** build **0 warning / 0 error**; test **690/690 pass**; E2E **100/100**;
  `has-pending-model-changes` = **No changes**; **không** đụng FE.
- **Chi tiết:** `docs/architecture/endpoint-baseline.md` mục "C-21" · test `Tests/C21ReportIntegrityTests.cs`.

## C-20 — "Tăng SL món đang nấu ⇒ tách dòng Pending mới" (2026-10-21)

- **Luật (chủ dự án chốt):** POS tăng số lượng một món mà món đó **đã vào nấu / đã ra đĩa** ⇒
  **tách thành dòng `Pending` MỚI**, nằm **ngay dưới** dòng cũ trong **cùng phiếu bếp**
  (màn bếp thấy `Gà rán 2 [Preparing]` rồi ngay dưới `Gà rán 3 [Pending]`).
  Món còn dòng `Pending` (bếp chưa bắt đầu) ⇒ **gộp chung** vào dòng Pending, không tách.
- **Trước:** cộng thẳng vào dòng `Preparing` ⇒ bếp thấy "5 phần" dù mới có 2, và khi huỷ/giảm
  **cả** 5 phần đều bị ghi `Wastage` ⇒ mất nguyên liệu oan.
- **Nay:** dòng mới giữ trạng thái `Pending` ⇒ sau đó huỷ/giảm thì **phần mới còn hoàn kho được**
  (`AdjustmentIn`), chỉ phần đang nấu mới thành `Wastage`. Ledger ghi theo từng dòng, `OrderItemId` riêng.
- **Sửa:** `OrderService.AdjustItemQuantityAsync` (nhánh tăng) · `KitchenService.GetItemsAsync`
  thêm `orderby ki.Id` (để dòng mới **luôn** nằm dưới) · `OrderService.GetAsync` thêm `orderby oi.Id`.
- **Không** đổi route · **không** đổi tên field JSON · **không** đổi schema.
- **Kiểm chứng:** build **0 warning / 0 error**; test **673/673 pass**; E2E **100/100**;
  `has-pending-model-changes` = **No changes**; **không** đụng FE.
- **Luật chi tiết:** `docs/architecture/kitchen-stock-rule-2026-10-21.md` §2.4.1 ·
  **hợp đồng API:** `docs/architecture/endpoint-baseline.md` mục "C-20".

## C-19 — "Mã giao dịch + phiên bản công thức" (2026-10-21)

- **C5 — mã giao dịch phải luôn có & luôn duy nhất:** client không gửi `transactionCode` ⇒ **server sinh mã**
  `PAY{yyyyMMddHHmmss}-{8 hex}`; client gửi mã **trùng** ⇒ **`409`**; index `IX_payments_TransactionCode`
  giờ **unique** (filter `TransactionCode <> ''`). Migration `20261021064129_AddPaymentTransactionCodeUnique`
  (đã apply) **backfill** mã cho dòng cũ rỗng / mã trùng trước khi tạo index.
- **C3 — sửa công thức = tạo phiên bản mới, không ghi đè bản cũ:** bản cũ chuyển `IsActive = false`
  (giữ nguyên định lượng làm minh chứng lịch sử) + bản mới `IsActive = true`; unique index
  `(SizeId) WHERE IsActive = 1` đã có sẵn ⇒ **không** cần đổi schema. Version **tự tăng** khi client
  gửi rỗng / trùng version đang chạy; sửa bản đã bị thay thế ⇒ **`409`**.
- **Kiểm chứng:** build **0 warning / 0 error**; test **643/643 pass**; `has-pending-model-changes` = **No changes**; **không** đụng FE.
- ⚠️ **2 test cũ đang pin hành vi sai đã sửa lại:** `Create_UsesEmptyString_WhenTransactionCodeIsMissing` → nay sinh mã ·
  `UpdateRecipe_ReplacesIngredients_AndUpdatesFields` → nay tạo bản mới, giữ bản cũ.
- **Phía FE:** `docsforfe/fe-work-plan.md` mục **M** + `docsforfe/fe-api-reference.md` phụ lục 2026-10-21 (đợt 6).

---

## C-18 — "Target khuyến mãi" (2026-10-21)

- **Vì sao:** `promotion_foods` + `promotion_categories` được `OrderService.ResolveDiscountBaseAsync` **đọc**
  nhưng **không API nào ghi** ⇒ 2 bảng **luôn rỗng** ⇒ mọi khuyến mãi âm thầm giảm trên **toàn bộ** hoá đơn.
- **Endpoint mới (1):** `PUT /api/management/promotions/{id}/targets` — **thay toàn bộ** target (idempotent);
  body `{ "sizeIds": [...], "categoryIds": [...] }`; gửi mảng **rỗng** = bỏ giới hạn ⇒ áp cho toàn bộ hoá đơn.
- **Response thêm field (2):** `PromotionResponse` += `sizeIds` / `categoryIds` (có trong list **và** detail).
- **Dead code xoá:** `PromotionLinkRequest`. **Migration:** **không** — 2 bảng đã có sẵn từ `InitialCreate`.
- **Kiểm chứng:** build **0 warning / 0 error**; test **633/633 pass**; **không** đụng FE.
- **Phía FE:** `docsforfe/fe-work-plan.md` mục **L** + `docsforfe/fe-api-reference.md` phụ lục 2026-10-21 (đợt 5).

---

## C-17 — "Ledger kho có đơn vị + khoá đơn vị nguyên liệu" (2026-10-21)

- **Ledger ghi đơn vị:** cột mới `inventory_transactions.unit` (nvarchar(50), nullable) = **snapshot** đơn vị lúc ghi dòng.
  Ghi ở **mọi** luồng: `Sale` (trừ kho) · `Purchase` (nhận kho) · `AdjustmentIn/Out` (điều chỉnh tay) ·
  `AdjustmentIn` (hoàn kho) · `Wastage` (hao hụt) · dòng **tách** (giữ đơn vị dòng gốc).
- **Chặn sửa `Ingredient.Unit` (C2):** `PUT /api/management/ingredients/{id}` đổi đơn vị (khác hoa/thường) khi
  nguyên liệu đã có **công thức / ledger / phiếu mua** ⇒ **`409`** kèm message nêu rõ đã dùng ở đâu.
  Nguyên liệu **chưa** dùng ở đâu thì vẫn sửa được (chữa typo lúc mới tạo).
  Lý do: đổi `kg → g` sau khi đã dùng sẽ **âm thầm sai** mọi công thức, ledger và báo cáo cũ.
  Không cần bảng quy đổi đơn vị — muốn đổi đơn vị thì tạo nguyên liệu mới.
- **Migration:** `20261021061510_AddInventoryTransactionUnit` (đã apply trên DB thật) — có **backfill** đơn vị cho dòng ledger cũ.
- **Response thêm field (1):** `unit` trong `GET /api/management/inventory-transactions`.
- **Kiểm chứng:** build **0 warning / 0 error**; test **618/618 pass**; `has-pending-model-changes` = **No changes**; **không** đụng FE.
- **Phía FE:** `docsforfe/fe-work-plan.md` mục **K** + `docsforfe/fe-api-reference.md` phụ lục 2026-10-21 (đợt 4).

---

## C-16 — "Hợp đồng lỗi thống nhất" (2026-10-21)

- **Lọc lỗi toàn cục** (`Infrastructure/Errors/ApiExceptionFilter.cs`): mọi exception lọt ra khỏi controller
  được đổi thành `{ "message": "..." }` + status chuẩn — `KeyNotFoundException`→**404**, `ArgumentException`→**400**,
  `InvalidOperationException`→**409**, còn lại→**500** (message chung, chi tiết chỉ ghi log server-side).
- Trước đây 5 controller danh mục `catch (Exception ex)` ⇒ **lỗi SQL/mất kết nối cũng trả 400/404 kèm `ex.Message` thô**.
- 18 chỗ `throw new Exception("...")` → exception có ngữ nghĩa; 13 `catch (Exception)` đã bỏ.
- **50** chỗ trả lỗi dạng **text thuần** ⇒ nay là **JSON `{ "message": ... }`** (FE đọc `payload.message` được).
- 7 chỗ "không tìm thấy" chuyển `400` → **`404`**.
- CHECK constraint `CK_payments_Status` trên `payments.Status`.
- **Kiểm chứng:** build **0 warning / 0 error**; test **602/602 pass**; **không** đụng FE.

---

## C-15 — "Chuyển bàn + MoMo" (2026-10-21)

- **Chuyển bàn (B1):** `PATCH /api/orders/{id}/table` (Waiter + Admin). Bàn đích **có đơn mở ⇒ `409`**;
  bàn cũ → `Available`, bàn mới → `Occupied`; ghi 1 dòng lịch sử (dùng lại helper sẵn có, 0 schema).
- **Thanh toán MoMo:** `POST /api/payments/momo` (intent) · `POST /api/payments/momo/notify` (IPN, verify chữ ký
  + kiểm tra số tiền, idempotent) · `POST /api/payments/{id}/refund` (hoàn toàn bộ, đơn mở lại `Ready`).
  `Payment.Status` có thêm `Pending`/`Failed`/`Refunded`; `PaidAt` **nullable**.
- **Chế độ giả lập (mặc định):** `Payments:Provider = Mock` ⇒ **không gọi ra ngoài**, backend tự sinh payload IPN
  **đã ký chữ ký đúng** ⇒ chạy đúng đường verify thật, test được cả offline. Bật MoMo thật: đặt
  `Payments__MoMo__PartnerCode/AccessKey/SecretKey` (biến môi trường/user-secrets, **không commit**) + `Provider = MoMo`.
- **Migration:** `20261021052642_AddMomoPaymentProvider` (đã apply trên DB thật).
- **Kiểm chứng:** build **0 warning / 0 error**; test **594/594 pass**; **không** đụng FE.
- **Phía FE:** `docsforfe/fe-work-plan.md` mục **I** + `docsforfe/fe-api-reference.md` phụ lục 2026-10-21 (đợt 3).

---

## C-14 — "Tiền đúng" (2026-10-21)

- **Một migration duy nhất** cho cả đợt: `20261021041728_AddSettingsTotalsPromotionSnapshot` (**đã apply trên DB thật**).
- **A1 — settings + VAT + phí dịch vụ:** bảng `restaurant_settings` (1 dòng, `Id = 1`) + `GET/PUT /api/management/settings` (Admin) +
  `OrderTotalsCalculator` — **một** nguồn sự thật cho công thức tiền, thay cho 6 chỗ gán `TotalAmount` lệch nhau.
  **Luật đã chốt:** giá menu **đã bao gồm VAT** ⇒ VAT chỉ tách ra để in, **không** cộng thêm vào tổng;
  **phí dịch vụ** tính trên tiền hàng **sau giảm giá** và luôn cộng thêm. Mặc định 0%/0% ⇒ hành vi cũ giữ nguyên.
- **A2/A3 — khuyến mãi 2 chiều:** thêm `DELETE /api/orders/{id}/promotion/{promotionId}`; đơn lưu `PromotionId` + snapshot
  (`PromotionDiscountType`/`PromotionDiscountValue`) ⇒ sửa/xoá khuyến mãi sau này không đổi nghĩa của đơn cũ.
- **A5 — idempotency:** `Idempotency-Key` cho `POST /orders/{id}/items`, `PATCH /orders/{id}/items/{itemId}/quantity`, `POST /payments`.
- **C1 — `Category.DisplayOrder`** `bool` → `int` (menu sắp được theo số).
- **P0-05 — múi giờ khuyến mãi:** API nhận **giờ VN** → lưu **UTC**; response trả cả giờ VN lẫn UTC; migration đã trừ 7 giờ cho dữ liệu cũ.
- **P0-12 — `ReasonCode`** cho `order_status_histories` + `OrderStatusHistoryResponse.reasonCode`.
- **Kiểm chứng:** build **0 warning / 0 error**; test **569/569 pass**; `has-pending-model-changes` = **No changes**; **không** đụng FE.
- **Phía FE:** `docsforfe/fe-work-plan.md` mục **H** + `docsforfe/fe-api-reference.md` phụ lục 2026-10-21 (đợt 2).
- **Còn lại:** Đợt 3 "đủ vận hành" (bill theo bàn, bill note, tồn kho thấp) · Đợt 4 (báo cáo + ca làm việc) ·
  `Auth:Enforce=true` (chỉ thiếu FE gửi Bearer) · MoMo (chủ dự án tự làm).

---

## C-13 + Đợt sửa P0 — cập nhật 2026-10-21

- **2 đợt trong 1 phiên:** (1) chốt **luật bếp ↔ kho ↔ hoàn kho** với chủ dự án; (2) sửa **10/12 lỗi P0** + dọn dead code.
- **Kiểm chứng:** build **0 warning / 0 error**; test **550/550 pass**; `has-pending-model-changes` = No changes; **không** đụng FE.
- **Migration:** `20261021025953_AddIngredientStockStatusAndFoodInactiveReason` (đã apply trên DB thật) — `ingredients.StockStatus`, `foods.InactiveReason`, `foods.InactiveAt`.
- **Luật cốt lõi:** trừ kho lúc POS gọi món · **`Preparing` = mốc KHÓA** (không hoàn kho, mọi thay đổi ghi `Wastage`) · POS không huỷ/giảm món `Preparing` (chỉ Chef/Admin) · bếp được huỷ đơn phương nhưng phải báo POS · nguyên liệu có status `Available/OutOfStock/Spoiled` · nguyên liệu **giữ nguyên** làm minh chứng báo cáo.
- **Hợp đồng API đổi:** +1 endpoint (`PATCH /api/management/ingredients/{id}/status`) · +1 query param (`?status=open`) · +5 field vào response · +4 request mở rộng (`changedBy`, `ingredientId`, `deactivateDish`, `inactiveReason`) · 5 status code `500` → `409`. Chi tiết: `docs/architecture/endpoint-baseline.md`.
- **Chi tiết luật:** `docs/architecture/kitchen-stock-rule-2026-10-21.md` · **Chi tiết gap:** `docs/architecture/backend-gap-review-2026-10-21.md` · **Tổng kết:** `docs/architecture/progress-summary-2026-10-21.md`.
- **Phía FE:** `docsforfe/fe-work-plan.md` mục G + `docsforfe/fe-api-reference.md` phụ lục 2026-10-21.
- **Còn lại:** P0-05 (múi giờ promotion — **cần chốt quy ước**) · P0-12 (`ReasonCode` — gộp Phase A) · nhóm P1 (settings/VAT, refund, chuyển bàn, bill theo bàn, tồn thấp, 5 báo cáo, ca làm việc) · P2 (cố ý hoãn).
- **MoMo:** backend **không** đụng provider/thanh toán tích hợp — `Payment` entity / `Payment.Status` / cột `payments` **không đổi**.

---

## C-06 / C-09 / C-10 / C-11 — cập nhật 2026-10-20

- **C-06 (mở lại món đã xong):** thêm `POST /api/management/kitchen/items/{id}/reopen` (policy `KitchenDesk` = Chef/Admin).
  Chỉ mở được món đang `Ready`/`Completed` khi order chưa `Paid/Cancelled` và ticket chưa `Completed/Cancelled`.
  Món, ticket và order cùng quay về `Preparing`; `CompletedAt` của ticket được xoá khi ticket về `Preparing`; bàn vẫn `Occupied`.
- **C-09 (route thay toàn bộ dòng món):** `PUT /api/orders/{id}/items` đã bị chặn, trả `410 Gone` kèm hướng dẫn route thay thế.
  Lý do: replace-all xoá hẳn `OrderItem` cũ (mất `OrderItemId` ⇒ mất liên kết ledger theo dòng và mất dấu vết huỷ),
  ghi ledger ở cấp đơn (không có `OrderItemId`) nên huỷ món sau đó phải ước tính lại từ công thức, và không ghi history món.
  Thay bằng 3 route lệnh: `POST /api/orders/{id}/items` (gọi thêm), `PATCH /api/orders/{id}/items/{itemId}/quantity` (đổi số lượng),
  `POST /api/orders/{id}/items/{itemId}/cancel` (huỷ món). `OrderService.UpdateItemsAsync` còn giữ cho test, không expose qua HTTP.
- **C-10 (audit từng dòng món):** bảng mới `order_item_status_histories` + entity `OrderItemStatusHistory`
  (old/new status, reason, reasonCode, actor, thời điểm).
  Ghi ở: đổi trạng thái món bếp, huỷ ticket/món (POS và bếp), tăng/giảm số lượng (kể cả khi giảm dưới phần đã nấu),
  prepared split, shortage, huỷ đơn. Đọc qua `GET /api/orders/{id}/item-history`.
  Huỷ ticket từ bếp giờ ghi lý do thật vào history order thay vì `"Updated from kitchen"`.
- **C-11 (`Accepted` chỉ ở cấp phiếu):** tách `KitchenStatuses.AllowedTicket` (có `Accepted`) và `AllowedItem` (không có `Accepted`);
  `PATCH /api/management/kitchen/items/{id}/status` từ chối `Accepted`.
  Migration thêm CHECK constraint cho `orders`, `order_items`, `kitchen_orders`, `kitchen_order_items`
  và chuyển item `Accepted` cũ sang `Preparing` trước khi thêm constraint.
- **Migration:** `20261020155551_AddOrderItemHistoryAndStatusGuards` — **đã apply trên DB thật** (đã kiểm tra `__EFMigrationsHistory`).
- **Kiểm chứng:** build `0 warning / 0 error`; test **538/538 pass**.
- **Còn lại:** C-04 (`Auth:Enforce=false` — cần FE gửi Bearer), C-13 (refund sau `Paid`).
  Phía FE: hiện chưa có UI dùng các route mới (gọi thêm / đổi số lượng / huỷ món / prepared / shortage / reopen / item-history);
  BE là nguồn chuẩn, FE cần bám theo các route lệnh này.

---

## Luật vòng đời bếp ↔ kho ↔ hoàn kho — chốt 2026-10-21

File mới: **`docs/architecture/kitchen-stock-rule-2026-10-21.md`**

- Nguồn chuẩn cho luật bếp/huỷ/hoàn kho (chủ dự án xác nhận trực tiếp, 2 vòng).
- **Trừ kho lúc POS gọi món** (giữ chặn "order ảo", không thêm khái niệm giữ chỗ).
- **`Preparing` = mốc KHÓA**: nguyên liệu đã tiêu thật ⇒ không hoàn; mọi thay đổi sau đó ghi `Wastage`.
- **Quyền huỷ:** POS **khóa** món `Preparing` (gọi Admin) · **Bếp (Chef) được huỷ đơn phương** kèm lý do hợp lệ · Admin huỷ mọi món.
- **Bếp bắt buộc báo POS** — POS tự poll `GET /orders/{id}` (đã có) là đủ, **không** tạo notification.
- **Hai nhóm lý do bếp huỷ:** (a) hao hụt do bếp làm ẩu → `Wastage` + `Ingredient.StockStatus=OutOfStock`;
  (b) lô hàng hư/hết hạn → `Wastage` + `StockStatus=Spoiled` + **vô hiệu hoá MÓN trong menu** (`Food.IsActive=false`) + lưu lý do. **Nguyên liệu giữ nguyên** làm minh chứng báo cáo.
- **Màn hình bếp: poll `?status=open` 3–5s, KHÔNG SignalR** (lý do kỹ thuật ở mục 4.2; SignalR để P2 nếu cần).
- **Nguyên liệu có 1 status** quản lý "hư/thiếu": `Ingredient.StockStatus` ∈ `Available|OutOfStock|Spoiled`.
- Ma trận 9 dòng + delta code/schema/endpoint cụ thể. Cả 5 câu hỏi đã **chốt**.
- ✅ **ĐÃ TRIỂN KHAI 2026-10-21**: build 0 warning/0 error, test **549/549 pass**.
  Migration `20261021025953_AddIngredientStockStatusAndFoodInactiveReason` (**đã apply trên DB thật**):
  `ingredients.StockStatus` + `foods.InactiveReason` + `foods.InactiveAt`.
  Thêm: `PATCH /api/management/ingredients/{id}/status` · `GET /api/management/kitchen/orders?status=open` (+ `TableNumber`/`WaiterName`/`ItemCount`).
  Sửa: món `Preparing` không hoàn kho (ghi `Wastage`) + **khóa với POS** (Chef/Admin vẫn dùng được) — đây cũng là **P0-1** của gap-review.
  → **Đợt 2 (2026-10-21)**: sửa tiếp **P0-02/03/04/06/07/08/09/10/11** + dọn dead code.
  Build **0 warning / 0 error**, test **550/550 pass**, **không** có migration mới, **không** đụng FE.
  Chi tiết: `docs/architecture/backend-gap-review-2026-10-21.md` mục 2.

---

## Rà soát khoảng trống backend — 2026-10-21

File mới: **`docs/architecture/backend-gap-review-2026-10-21.md`**

- Audit read-only backend (lịch sử: 115 route, 25 service, 29 bảng). Build **0 warning / 0 error**, test **538/538 pass**.
- **P0 — lỗi đúng–sai (12 mục):** hoàn kho cho món đang nấu (tồn kho thổi phồng); thanh toán không ghi `OrderStatusHistory`;
  tạo đơn / tạo dòng món không ghi history; `promotions.StartAt/EndAt` lệch 7 giờ; `ReportShortageAsync` sinh số lượng âm;
  huỷ món chọn nhầm phiếu bếp; huỷ đơn huỷ cả phiếu đã `Completed`; `PaymentService` dùng isolation yếu nhất;
  `ConcurrencyConflictException` trả 500 thay vì 409; bếp ghi sai người thực hiện vào audit; lịch sử đơn mất mã lý do `ReasonCode`.
  → **Đã sửa 10/12** (2026-10-21): P0-01/02/03/04/06/07/08/09/10/11. Còn **P0-05** (múi giờ promotion — cần chốt quy ước) và **P0-12** (`ReasonCode` — gộp Phase A).
- **P1 — thiếu chức năng lõi:** VAT + phí dịch vụ + bảng cấu hình quán (`TaxAmount` hiện **luôn = 0**);
  gỡ khuyến mãi khỏi đơn; lưu `PromotionId` + snapshot; refund đơn đã thu; idempotency cho lệnh tiền/số lượng;
  chuyển bàn; hoá đơn theo bàn; bill note; danh sách tồn kho thấp; 5 báo cáo vận hành; bảng bếp (số bàn + phục vụ); ca làm việc / két tiền.
- **P2 — cố ý hoãn (chống over-engineer):** payment provider, refund từng phần, tách project Domain/Application/Infrastructure,
  CQRS/MediatR, WebSocket cho bếp, kiểm kê tồn kho, multi-kho, bảng đơn vị, đặt bàn, Roles CRUD, refresh token/security stamp.
- **Lộ trình 4 đợt:** (1) sửa P0 → (2) "tiền đúng" + 1 đợt migration duy nhất → (3) "đủ vận hành" → (4) báo cáo + ca làm việc.
- Nguyên tắc: **đúng trước, đủ sau** — không thêm tính năng chưa cần, không đổi hợp đồng API nếu chưa cần.
