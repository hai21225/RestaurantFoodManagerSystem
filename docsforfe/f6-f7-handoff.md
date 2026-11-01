# F6 / F7 — bàn giao theo người phụ trách

Cập nhật 2026-11-01, nhánh `dong`, tiếp tục từ `123005d`.
Tài liệu này mô tả phần frontend mới của Đông và công việc backend cần Hải xử lý.
Các kết quả kiểm tra ở đây thuộc môi trường Integration riêng; không đồng nghĩa database gốc đã chạy được luồng đặt món.
Các route rút gọn bên dưới đều có tiền tố `/api`.

**Code để đọc:** [quản trị + WriteGuard](../frontend/js/common/admin-session.js),
[hồ sơ cá nhân](../frontend/js/common/account-session.js), [tiện ích đơn](../frontend/js/common/order-extras-session.js),
[giao diện quản trị](../frontend/pages/administration.html), [giao diện F7](../frontend/pages/order-tools.html).

## 1. Ai phụ trách việc nào

| Người | Phạm vi | Trách nhiệm |
|---|---|---|
| **Đông — frontend** | `frontend/**`, hướng dẫn sử dụng và ghi chú `docsforfe/**` | Form, điều hướng, kiểm tra dữ liệu nhập, phân quyền giao diện, gọi API, hiển thị kết quả/lỗi, đối chiếu mất phản hồi; kiểm tra UI và frontend tests |
| **Hải — backend** | C# services/controllers/DTO, migrations, quyền server, transaction và JWT | Bảo đảm nghiệp vụ và quyền ở server; sửa các ticket có tên Hải ở mục 4; cung cấp migration tương thích DB cũ và kết quả test backend |
| **Đông + Hải — nghiệm thu** | Luồng qua API thật | Sau khi Hải sửa, Đông nối/kiểm tra UI lại bằng tài khoản đúng vai trò; chỉ đổi trạng thái ticket khi có bằng chứng thực chạy |

**Không giao Hải làm lại toàn bộ F6/F7.** Các API có sẵn đã được frontend sử dụng. Hải xử lý đúng các giới hạn server phía dưới.
Chưa sửa source backend, migration, appsettings hoặc cấu trúc database gốc trong phần F6/F7 này.

## 2. Đông đã triển khai frontend gì, mở ở đâu

### F6-3 — Tài khoản cá nhân

- **Người làm:** Đông. File: `pages/account.html`, `js/pages/account.js`, `js/common/account-session.js`.
- Mở **Quản trị → Tài khoản**. Mọi nhân viên đã đăng nhập được sửa hồ sơ của chính mình.
- `GET /api/users/me` đọc hồ sơ; `PUT /api/users/me` chỉ gửi `{fullName,email,phone}`.
  Không gửi/sửa `userName`, `roles`, `isActive`, mã nhân viên hoặc ca làm việc qua form này.
- Đổi mật khẩu dùng `POST /api/auth/change-password` với
  `{currentPassword,newPassword,confirmNewPassword}`. Lưu ý trường xác nhận khác tên `confirmPassword` của chức năng tạo/reset nhân viên.
- Mật khẩu không nằm trong storage/pending, log hoặc tài liệu. Form xóa các ô mật khẩu sau khi xử lý.
- Sau lưu hồ sơ, đọc lại server và cập nhật tên ở thanh trên. Hồ sơ bị sửa ở phiên khác thì chặn lưu bản cũ.
- **Hải còn việc:** FE-C21. Frontend thông báo phiên khác chưa bị thu hồi ngay; không tuyên bố nút đổi mật khẩu đã đăng xuất mọi thiết bị.

### F6-4 — Danh mục, topping, kích cỡ và topping của món

- **Người làm:** Đông. Dùng `pages/administration.html?tab=categories|toppings|sizes|links`,
  `js/pages/administration.js`, `js/common/admin-session.js`, `css/pages/administration.css`.
- Mở **Quản trị → Danh mục / Topping**. Admin và WarehouseManager được dùng theo policy WarehouseDesk thực tế của backend.
- **Danh mục:** thêm/sửa tên, mô tả, thứ tự; ngừng dùng và khôi phục.
  `GET/POST /categories`, `PUT/DELETE /categories/{id}`, `PATCH /categories/{id}/active`.
  POST không có ID nên đọc lại danh sách. PUT phải giữ `isActive` hiện tại, tránh DTO mặc định false làm ngừng danh mục ngoài ý muốn.
- **Topping:** thêm/sửa tên, giá, trạng thái bán; ngừng dùng/khôi phục; bật bán/tạm hết.
  `GET/POST /toppings`, `PUT/DELETE /toppings/{id}`, `PATCH .../active`, `PATCH .../availability`.
- **Kích cỡ:** chọn món, thêm/sửa kích cỡ và giá, đặt mặc định, ngừng dùng/khôi phục.
  `GET/POST /foods/{foodId}/sizes`, `PUT/DELETE .../{sizeId}`, `PATCH .../{sizeId}/default`, `PATCH .../{sizeId}/active`.
  Danh sách đọc `includeInactive=true`; giá/default/active hiển thị lại theo server.
- **Topping của món:** chọn món, gán hoặc gỡ từng topping bằng `GET/POST /foods/{id}/toppings`,
  `DELETE /foods/{id}/toppings/{toppingId}`. Không tự đổi giá dòng món đã đặt trước đó.
- Thêm/sửa/xóa mềm món ăn cơ bản vẫn dùng màn **Món ăn** hiện có; không tạo một nguồn danh sách món giả khác.
- Đông đã sửa màn Món ăn để đọc topping thật theo từng món và hiển thị cả kích cỡ tên riêng, không để topping rỗng cố định/lọc mất size ngoài Nhỏ-Vừa-Lớn.
  Có lối mở quản lý kích cỡ/topping của món đang chọn. Nút Nổi bật/Món mới chỉ đổi local đã được bỏ vì không có API trong contract;
  không giao Hải làm thêm hai chức năng ngoài phạm vi này. Giá vốn/thời gian chế biến vẫn ghi Chưa có dữ liệu.
- Để đặt được món mới, cần món/kích cỡ hoạt động, công thức và tồn nguyên liệu phù hợp. POS tính theo **giá kích cỡ**;
  sửa giá cơ bản của món không tự đổi toàn bộ giá kích cỡ. Tạo món → thêm kích cỡ → thiết lập công thức/tồn → bật bán.
- Các form món ăn cơ bản có sẵn trong `foods.js` giữ luồng cũ; không tuyên bố chúng đã có request receipt/version của server.
- **Hải còn việc:** FE-C26 (bảo đảm ghi đồng thời/mất ACK), FE-C27 (liên kết topping đã ngừng bị ẩn khỏi API quản trị).

### F6-5 — Khu vực và sửa bàn

- **Người làm:** Đông. Mở **Quản trị → Khu vực / Bàn**.
- Tab **Khu vực bàn** (`?tab=areas`): tên, mô tả, thứ tự, thêm/sửa/ngừng/khôi phục.
  `GET/POST /table-areas`, `PUT/DELETE /table-areas/{id}`, `PATCH .../{id}/active`.
- Tab **Thông tin bàn** (`?tab=tables`): sửa tên/số bàn, số chỗ và khu vực qua `PUT /restaurant-tables/{id}`.
  Chọn **Chưa phân khu** gửi `areaId:null`; không gửi số 0 thay null.
- Thêm bàn, bảo trì, xóa mềm/khôi phục vẫn ở màn **Bàn ăn**. Không dùng PUT sửa thông tin để tự đặt bàn thành Trống/Có khách.
- Đọc bằng danh sách `GET /restaurant-tables`, tránh GET bàn theo ID có tác dụng ghi DB.
- Ngừng khu vực không xóa bàn/lịch sử hoặc tự giấu bàn trong khu vực đó. Chọn bộ lọc Ngừng dùng để khôi phục khu vực.
- **Hải còn việc:** FE-C26; FE-C28 về lỗi validation tên bàn quá dài và phản hồi khi lưu không thay đổi.

### F6-6 — Quản lý khuyến mãi và phạm vi

- **Người làm:** Đông. Mở **Quản trị → Khuyến mãi**, `administration.html?tab=promotions`. Chỉ Admin.
- Thêm/sửa tên, mô tả, `Percent`/`Fixed`, mức giảm, thời gian bắt đầu/kết thúc, đơn tối thiểu và giảm tối đa.
  `GET/POST /management/promotions`, `PUT/DELETE .../{id}`.
- Thời gian nhập/hiển thị là **giờ Việt Nam**; gửi chuỗi không offset theo hợp đồng API, không cộng/trừ múi giờ hai lần.
  Mức phần trăm phải >0 và <=100; kết thúc phải sau bắt đầu; ngày không tồn tại bị chặn.
- Ngừng dùng dùng DELETE mềm. Bật lại dùng **query** `PATCH .../{id}/active?active=true`, không dùng body `{isActive:true}` ở route này.
- **Chọn phạm vi**: `PUT .../{id}/targets`, `{sizeIds:[...],categoryIds:[...]}`.
  Chọn danh mục và/hoặc kích cỡ; hai mảng rỗng nghĩa là áp dụng toàn hóa đơn, phải đánh dấu xác nhận trước khi lưu.
- Tạo khuyến mãi và chọn phạm vi là **hai lệnh riêng**, không giả đây là một giao dịch nguyên tử.
  Khuyến mãi vừa tạo mặc định đang active và toàn hóa đơn; nên tạo với ngày bắt đầu tương lai nếu cần hoàn thiện phạm vi trước khi sử dụng.
- Sửa cấu hình/phạm vi không tự tính lại các đơn đã áp trước đó; frontend hiển thị lại snapshot và số tiền trên đơn.
- **Hải còn việc:** FE-C24 (lookup cho Phục vụ), FE-C26 (concurrency/receipt; cân nhắc tạo draft + targets nguyên tử).

### F7 — Thiết lập quán, VAT/phí dịch vụ

- **Người làm:** Đông. Mở **Quản trị → Thiết lập quán**, `administration.html?tab=settings`. Chỉ Admin.
- `GET/PUT /management/settings`: `{restaurantName,address,phone,taxCode,currency,taxRate,taxIncluded,serviceChargeRate}`.
- VND theo yêu cầu dự án; VAT/phí dịch vụ 0..100. `taxIncluded=true` là giá menu đã gồm VAT.
- Frontend không nhân/cộng thuế hoặc tự cập nhật tổng đơn. Chỉ đọc các trường tiền từ server.
- Lưu cấu hình không tự tính lại tất cả đơn đã mở. Kiểm tra đơn sau lệnh nghiệp vụ để biết số server đang ghi.
- Xem trước phiếu bằng Admin đọc thông tin quán từ settings và escape khi hiển thị.
- **Hải còn việc:** FE-C25 để Thu ngân in được thông tin quán; FE-C26 cho ghi cấu hình đồng thời.

### F7 + F1-5 — Tiện ích của đơn

- **Người làm:** Đông. Tại **Đặt món → đơn đã gửi → Chuyển bàn / Khuyến mãi / Ghi chú**.
  Mở `pages/order-tools.html?orderId={id}`; file `js/pages/order-tools.js`, `js/common/order-extras-session.js`.
- Admin/Waiter được sửa; Cashier chỉ đọc. API là chốt quyền cuối cùng.
- **Ghi chú:** `PATCH /orders/{id}/note`, `{note,changedBy}`. Chuỗi rỗng xóa ghi chú; tối đa 500 ký tự.
- **Chuyển bàn:** `PATCH /orders/{id}/table`, `{tableId,changedBy}`.
  Chỉ chọn bàn khác đang hoạt động và Trống; đọc lại bàn/đơn đích trước lệnh. Backend vẫn phải kiểm tra và khóa nguyên tử.
  Sau lệnh đọc lại đơn theo **orderId**, cập nhật liên kết Về Đặt món bằng tableId mới. Không tìm lại bằng bàn cũ rồi tạo đơn mới.
- **Áp/gỡ khuyến mãi:** `PATCH/DELETE /orders/{id}/promotion/{promotionId}`.
  Admin có danh sách gợi ý; Waiter hiện nhập mã do Admin cung cấp do FE-C24. Không mở quyền Admin để lấy danh sách.
  Số tiền giảm, thuế, phí dịch vụ và tổng đều lấy từ lần GET đơn sau lệnh; không tự ước tính tiền giảm.
- Paid/Cancelled bị khóa sửa; thay đổi ở phiên khác phải tải lại và xác nhận trước.
- Pending POS/Thanh toán chặn lệnh F7; pending F7 cũng chặn sửa món và thu/hoàn tiền ở frontend trong cùng phiên.
- **Hải còn việc:** FE-C24, FE-C26. FE-C23 vẫn chặn sử dụng trên DB gốc của Đông.

### F7 đã có từ F3 và phần chưa có API

- Bill theo bàn, hoàn tiền và MoMo Mock tiếp tục dùng màn Thanh toán F3; không sửa appsettings/provider để giả MoMo thật.
- Chia/gộp hóa đơn **FE-C5** và ca/két **FE-C8** chưa có API, chỉ có nút không hoạt động kèm thông báo chưa hỗ trợ.
  **Hải** bổ sung backend trước; **Đông** làm UI/API sau khi có contract và dữ liệu kiểm thử.
- In giấy/PDF chưa được nghiệm thu trên máy in/Excel/Chrome thực tế. F3 có bản xem trước; không ghi “in thành công” khi chỉ mở preview.

## 3. Cơ chế frontend đã làm và giới hạn

- `WriteGuard` trong `admin-session.js` khóa bấm kép, đọc dữ liệu mới trước ghi, so snapshot, ghi pending trước gửi lệnh và ghi ACK trước đọc lại.
- Pending gồm userId, API base URL, ngữ cảnh mục/món/đơn, mô tả và ACK; không chứa mật khẩu hay nội dung để tự phát lại lệnh.
- Sau lỗi mạng/5xx hoặc có ACK nhưng GET lỗi: khóa thao tác tiếp theo. **Tải dữ liệu để đối chiếu** chỉ GET;
  người dùng kiểm tra rồi bấm **Tôi đã đối chiếu, tiếp tục**. Không tự gửi thêm POST/PUT/PATCH/DELETE.
- Mật khẩu không thể xác nhận bằng GET hồ sơ. Nếu mất phản hồi đổi mật khẩu, đăng xuất/đăng nhập bằng mật khẩu mới để kiểm tra.
- Logout/401/đổi tài khoản xóa ngữ cảnh pending khỏi trình duyệt; đóng phiên cũng có thể mất sessionStorage.
  Đây không thay thế request receipt bền vững ở server. Trước khi tạo lại thao tác phải kiểm tra dữ liệu hiện tại.
- Snapshot trước ghi chỉ phát hiện thay đổi **đã xảy ra trước lần đọc đó**, không khóa được hai máy cùng đọc rồi cùng ghi.
  Không mô tả frontend là đã giải quyết ghi đồng thời nguyên tử.

## 4. Ticket cụ thể giao Hải

### FE-C23 — DB gốc lệch kiểu số lượng — Hải, P0 local, chưa sửa

Đọc [ghi chú DB gốc](original-database-and-delete-progress.md#fe-c23--lỗi-đọc-đơn-và-phiếu-bếp-trên-database-gốc).
Hai cột `order_items.Quanlity`, `kitchen_order_items.Quanlity` còn decimal(18,3), model hiện tại là int.
`GET /orders/1` và chi tiết phiếu bếp 1 trả 500; đơn đã lưu. Hải cung cấp migration tương thích DB cũ,
kiểm tra số lượng lẻ/range và bảo toàn dữ liệu. Đông xác nhận lại đúng pending cũ sau khi hai GET đọc được.

### FE-C21 — Thu hồi phiên JWT — Hải, P0, chưa sửa

Đọc [ticket JWT có bước tái hiện và checklist](fe-c21-jwt-session-revocation.md).
Phải xử lý cả khóa, thu role, reset và **tự đổi mật khẩu**; frontend không thể thu hồi các thiết bị khác.
Đông kiểm tra lại luồng hồ sơ/đăng nhập sau khi backend đã có cơ chế thu hồi.

### FE-C24 — Lookup khuyến mãi cho Phục vụ — Hải, P1, đã xác minh 403

**Điểm đang vướng:** `ManagementController.Promotions/PromotionDetails` là AdminOnly,
nhưng `OrdersController.Promotion/RemovePromotion` cho PosOperator.
Waiter áp dụng được mã đã biết nhưng không thể xem danh sách/tên/thời hạn/phạm vi để chọn trên POS.

**Tái hiện:** Admin tạo khuyến mãi hợp lệ → Waiter login → `GET /api/management/promotions` trả 403 →
`PATCH /api/orders/{đơn-mở}/promotion/{mã-hợp-lệ}` vẫn áp dụng được.

**Đề xuất Hải:** thêm lookup chỉ đọc cho PosOperator (route mới cần chốt, ví dụ `/api/promotions/available?orderId=...`),
trả id/name/loại/mức giảm/thời hạn VN/phạm vi/điều kiện. Giữ CRUD quản trị AdminOnly; không bỏ policy toàn controller.

**Nghiệm thu:** Waiter chọn được khuyến mãi hợp lệ; inactive/hết hạn không bị gợi ý sai;
áp dụng vẫn kiểm tra tiền/phạm vi nguyên tử ở server; Waiter không được tạo/sửa/xóa promotion.
**Đông sau đó:** thay nhập mã thủ công bằng selector, hiển thị tên/điều kiện và kiểm tra API/UI lại.

### FE-C25 — Thông tin quán trên phiếu của Thu ngân — Hải, P1, đã xác minh 403

**Điểm đang vướng:** `GET /management/settings` là AdminOnly; order detail/table bill không có tên quán/địa chỉ/điện thoại/MST.
Cashier có quyền xem/in bill nhưng không có nguồn dữ liệu hợp lệ để in những thông tin quán đã cấu hình.

**Tái hiện:** Admin lưu tên quán/MST → Cashier login → `GET /api/management/settings` trả 403.
Hiện frontend chỉ tải merchant info khi Admin xem trước phiếu; Cashier vẫn xem được món/tiền/ghi chú, không bịa thông tin quán.

**Đề xuất Hải:** bổ sung metadata phiếu vào DTO đọc đơn/bill hoặc endpoint đọc riêng cho PosOrCashier,
chỉ gồm `{restaurantName,address,phone,taxCode,currency}`. Chốt lấy thông tin hiện tại hay snapshot tại lúc thu.
Không cấp Cashier quyền sửa settings và không đưa khóa MoMo/JWT/connection string vào metadata.

**Nghiệm thu:** Cashier xem/in đúng thông tin quán, vẫn 403 khi PUT settings; không lộ cấu hình nội bộ.
**Đông sau đó:** bỏ nhánh Admin-only lấy merchant info, dùng contract mới chung cho Admin/Cashier.

### FE-C26 — Ghi đồng thời và đối chiếu lệnh F6/F7 — Hải, P1

**Phạm vi mới:** category, topping, size, liên kết món-topping, khu vực, sửa bàn, promotion/targets,
settings, hồ sơ cá nhân, note/chuyển bàn/áp-gỡ promotion. FE-C18/22 vẫn là ticket riêng cho recipe/ingredient và nhân viên/role.

**Nguồn cần rà:** controllers/services tương ứng dưới `Food`, `Restaurant`, `Promotion`, `Settings`, `User`, `Order`.
Các request này chưa có contract rowVersion/ETag và request receipt bền vững thống nhất để frontend sử dụng.

**Tình huống:** A/B cùng mở một bản → cùng GET trước ghi → A lưu, B lưu sau có thể ghi đè;
POST đã commit nhưng mất phản hồi thì FE không chứng minh kết quả bằng tên giống nhau.
Đây là giới hạn từ contract/source; chưa chạy bài tải cạnh tranh nhiều máy để tuyên bố mọi route đều bị lỗi.

**Đề xuất Hải:** chốt token phiên bản cho từng nhóm ghi, trả 409 khi phiên bản cũ; receipt/idempotency bền vững
cho lệnh tạo và các lệnh cần gửi lại; receipt có user/phạm vi/payload và kết quả, tồn tại sau restart.
Promotion nên có cách tạo draft hoặc tạo kèm targets nguyên tử để không có khoảng active toàn hóa đơn trước khi chọn phạm vi.

**Nghiệm thu:** hai client sửa cùng bản chỉ một bản hợp lệ hoặc xung đột rõ; cùng request không tạo trùng sau timeout/restart;
không nhận cùng key với payload/actor khác. **Đông sau đó:** gửi version/key theo contract mới và thêm test UI đối chiếu.

### FE-C27 — Liên kết topping đã ngừng không xem được — Hải, P2

**Nguồn:** `Application/Services/Food/FoodToppingService.cs`, `GetToppingsByFoodIdAsync` lọc `x.IsActive`.
GET dùng cho quản trị cũng không trả topping inactive đã gán, nên không có đủ dữ liệu để gỡ liên kết ẩn.
Đã tái hiện bằng API thật trên fixture riêng: hiện khi active → ẩn khi ngừng → hiện lại khi khôi phục; topping đã được khôi phục sau ca thử.

**Cách tái hiện:** gán topping T cho món M → ngừng T → GET `/foods/M/toppings` không có T → khôi phục T thì liên kết cũ xuất hiện lại.
Frontend hiện hướng dẫn khôi phục T rồi gỡ, không coi danh sách rỗng là chưa từng có liên kết.

**Đề xuất Hải:** thêm `includeInactive=true` cho lookup quản trị WarehouseDesk hoặc route riêng;
giữ POS/menu chỉ thấy topping đang được bán theo nghiệp vụ. DTO cần có toppingId, isActive, isAvailable, tên/giá.
**Đông sau đó:** hiện liên kết inactive và nút gỡ ngay tại tab Topping của món.

### FE-C28 — Validation tên bàn — Hải, P2

**Đã gặp khi kiểm tra:** tên bàn kiểm thử dài hơn 20 ký tự gây SQL 2628, controller trả 409 chung chung.
`AppDbContext` giới hạn `TableNumber` 20 ký tự, nhưng `RestaurantTableDto` chưa khai báo Required/MaxLength tương ứng.
Frontend tạo/sửa đều giới hạn 20; Hải cần validation phía server để client khác cũng nhận lỗi nhập liệu 400 rõ ràng.

**Nghiệm thu:** rỗng/quá 20 ký tự nhận 400 có message trước khi chạm DB; không ghi log SQL exception cho lỗi nhập liệu.
Đổi tên hợp lệ/trùng tên vẫn giữ quy tắc hiện tại và không thay trạng thái phục vụ ngoài ý muốn.

### Các ticket cũ vẫn do Hải xử lý, không bị đóng bởi phần frontend mới

| Ticket / người làm | Tình huống cần sửa, nguồn và điều kiện nghiệm thu |
|---|---|
| **FE-C14 — Hải, P1** | `Application/Services/Payment/PaymentService.cs`, `RefundAsync`: hoàn đơn cũ có thể mở lại Ready ở bàn đã có đơn mới. Hải kiểm tra đơn mở khác và khóa cùng transaction với tạo/chuyển đơn, không chỉ dựa GET trước đó. Kiểm tra hai client: một hoàn tiền, một mở đơn trên cùng bàn; không được có hai đơn mở hoặc hoàn tiền nửa chừng. Đông giữ guard F3 và kiểm tra lại sau bản sửa. |
| **FE-C15 — Hải, P1 / provider thật** | `MomoPaymentService.cs`, `PaymentService.cs`, `PaymentsController`: cần contract hủy/đóng intent MoMo Pending có đối chiếu provider. Không đổi sang Cash bằng cách tự đánh Failed trong FE. Nghiệm thu: provider đã thu thì ghi nhận/đối chiếu đúng, chưa thu và hủy thành công mới mở phương thức khác; không phát sinh hai khoản thu cho một đơn. Đông bổ sung nút chuyển phương thức sau contract mới. |
| **FE-C19 — Hải, P1** | `Application/Services/Recipe/RecipeService.cs`, route `PATCH /management/recipes/{id}/active`: khôi phục công thức phải kiểm tra món/kích cỡ/nguyên liệu còn hợp lệ trong cùng lệnh server. Nghiệm thu gọi trực tiếp API với size/ingredient đã ngừng phải bị chặn; dữ liệu không đổi nửa chừng. Đông đã preflight, không thể thay validation server. |
| **FE-C20 — Hải chốt cùng Đông, P1** | `IngredientService` và luồng tiêu hao khi tạo/gọi thêm món: hiện ngừng nguyên liệu/đánh Spoiled không tự ngừng món/công thức và chưa chắc chặn tiêu hao. Hải chốt ý nghĩa từng cờ với Đông, ghi quy tắc rồi thực thi nguyên tử ở server; lưu note/audit của thay đổi trạng thái. Nghiệm thu đặt món trực tiếp qua API trong các trường hợp inactive/hỏng/hết và xác minh kho/ledger theo đúng quy tắc đã chốt. Không tự hoàn kho cho hao hụt đã tiêu thật. |
| **FE-C18/22 — Hải, P1** | Version/receipt cho nguyên liệu-công thức và nhân viên/role. Ghi đè phiên bản cũ phải 409; mất ACK phải tra được kết quả bền vững. Việc bảo vệ Admin cuối cần nguyên tử khi hai phiên cùng khóa/thu role. Đông đã có pending/snapshot; sẽ gửi version/key và kiểm tra lại khi Hải cung cấp contract. FE-C26 mở rộng sang các nhóm mới, không thay thế hai ticket này. |
| **FE-C13/16 — Hải, P1** | Lệnh Bếp và Kho/phiếu mua/NCC chưa có receipt bền vững cho tự retry. Tình huống lệnh đã ghi nhưng mất phản hồi: cùng request phải đọc lại được kết quả sau restart, không nấu/ghi hao hụt/nhận hàng/điều chỉnh tồn hai lần. Đông đang khóa và đối chiếu thủ công; chỉ bật retry khi có bảo đảm server. |
| **FE-C12 — Hải, P2** | Lookup nguyên liệu theo món bếp cho Chef/Admin, không mở quyền WarehouseDesk. Nghiệm thu Chef đọc được nguyên liệu của dòng bếp mình đang xử lý nhưng không được sửa danh mục kho. Đông bổ sung lựa chọn nguyên liệu cụ thể thay cho báo thiếu ở mức công thức hiện tại. |
| **FE-C17 — Hải, P2** | `IngredientResponse` cần metadata như `unitLocked`/lý do để FE biết đơn vị đã bị khóa; server vẫn giữ validation đổi đơn vị. Đông hiện phải đọc nhiều lịch sử/chi tiết để preflight, sẽ dùng metadata mới để giảm số request. Không coi đây là lỗi làm mất dữ liệu hiện tại. |

## 5. Bằng chứng và cách xác nhận hoàn thành

- **214/214 test frontend đạt**, gồm 183 test cũ và 31 test mới cho role, hồ sơ/mật khẩu, snapshot,
  pending/ACK, ngày VN không hợp lệ, kích cỡ tên riêng, giới hạn promotion, chuyển bàn và khóa chéo POS/Thu ngân/F7.
  Lưu bàn không thay đổi chỉ đọc đối chiếu, không gửi PUT vô ích có thể bị server trả 404 khi số dòng thay đổi bằng 0.
- **22/22 nhóm kiểm tra API thật đạt** trên `RestaurantFoodManager_Integration_20261029`, API tách riêng cổng 5227.
  Bao gồm tạo/sửa/ngừng/khôi phục danh mục/topping/size/khu vực; gán/gỡ topping; sửa bàn/bỏ phân khu;
  promotion + phạm vi + giờ VN; settings; VAT/phí do server tính; note; áp/gỡ promotion; chuyển bàn/bill;
  profile Waiter; sai/đúng mật khẩu cũ và login mới; xác minh FE-C24/25 bằng 403.
- Có lỗi fixture ban đầu: tên bàn quá 20 ký tự. Đã chỉnh frontend giới hạn theo DB và giữ ticket FE-C28;
  không ghi lỗi fixture đó thành “toàn bộ kiểm tra đều đạt ngay lần đầu”.
- UI đã kiểm tra: sửa danh mục; chặn phạm vi rỗng khi chưa xác nhận; lưu phạm vi; lưu thiết lập trên 390 px;
  Waiter sửa hồ sơ/tên trên header; note/chuyển bàn/áp promotion; các kích thước F7 320/390/768/1280 không tràn ngang.
- Thêm/sửa promotion giữ được giây trong datetime-local (`step=1`), đã lưu qua UI với thời gian có giây;
  bản xem trước phiếu Admin hiển thị tên quán đã cấu hình và ghi chú đúng dữ liệu server.
  Form promotion và trang quản trị không tràn ngang ở 320/390/768/1280; hộp thoại cuộn trong giới hạn chiều cao màn hình.
- Màn Món ăn đã kiểm tra bằng fixture thật: kích cỡ **Phần riêng F6**, topping/giá/trạng thái bán hiện đúng,
  lối mở quản lý giữ đúng foodId; không còn nút đánh dấu local Nổi bật/Món mới.
- Đổi mật khẩu đã kiểm tra qua API/session thật; không tự thay mật khẩu người dùng gốc qua UI.
- Report/script/fixture và ảnh ở `_codex_reports/20261101`, `_codex_runtime` ngoài Git; không push dữ liệu/backup/mật khẩu.
- Đã dọn các fixture của lượt F6/F7 qua API: hủy đơn thử, gỡ topping, ngừng món/kích cỡ/topping/nguyên liệu/công thức/
  khu vực/khuyến mãi/bàn/danh mục, khóa tài khoản thử và khôi phục các giá trị settings của DB Integration.
  Giữ lịch sử kiểm thử có nhãn TEST; không hard delete hoặc đụng dữ liệu nghiệp vụ gốc.
- Kiểm tra tĩnh cuối: **40 JS** đúng cú pháp, **17 HTML** không trùng ID, **168 đường dẫn CSS/script** tồn tại,
  `git diff --check` đạt với cấu hình line ending của repository. Màn Món ăn/Bàn ăn thêm lối quản lý vẫn vừa ở 320 px.
  Trang quản trị dùng dữ liệu gốc đã mở được bằng tài khoản Đông; chỉ đọc, không dùng DB gốc để thử thêm/sửa/xóa.
- Không sửa hoặc chạy lại bộ test C#. Baseline cũ 518 đạt/179 lỗi cần Hải chạy lại sau bản sửa, không phải kết quả kiểm tra backend của phần F6/F7 này.

Khi Hải giao lại, mỗi ticket cần: commit, file đã sửa, thay đổi contract, migration/cách nâng cấp nếu có,
test đã chạy và kết quả, cách Đông tái hiện UI, phần còn chưa xử lý. Không ghi chung “backend xong” khi các điều kiện trên chưa đạt.
