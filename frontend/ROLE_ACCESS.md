# Menu và màn hình theo vai trò — 2026-11-05

Lượt triển khai menu ngày 05/11 thực hiện theo yêu cầu chỉ sửa frontend: không thay backend/database/JWT,
không tạo tài khoản hoặc đăng nhập nhanh và chưa commit/push lúc đó. Các yêu cầu đăng ký tài khoản
và bàn giao Git riêng ngày 10/11 được ghi bổ sung ở cuối tài liệu.

## Màn được hiển thị

| Role | Màn mặc định sau đăng nhập | Phạm vi màn hình |
|---|---|---|
| Waiter | Bàn ăn | Bàn ăn, Đặt món, tiện ích của đơn được phép xem |
| Chef | Bếp | Bếp |
| Cashier | Thanh toán | Thanh toán, tiện ích đơn ở mức quyền sẵn có |
| WarehouseManager | Kho hàng | Kho, Nhập hàng/NCC, Nguyên liệu/Công thức, Món ăn, Danh mục/Topping/Kích cỡ/Liên kết món |
| Admin | Tổng quan | Tất cả các màn hiện có |

Mọi tài khoản đã đăng nhập có Tài khoản của tôi/Đăng xuất. Tài khoản nhiều role nhận hợp quyền; khi không có returnUrl hợp lệ, chọn màn mặc định theo thứ tự Admin → Waiter → Chef → Cashier → WarehouseManager. Role trống/lạ chỉ mở hồ sơ, không tự coi Manager là Admin.

## Các file Đông đã sửa

- `js/common/auth.js`: một ma trận `pageRoles`, danh sách tab quản trị, `canAccessPage`, `getHomeUrl`, `enforcePageAccess`; lọc returnUrl theo quyền; chặn trang/tab không biết; kiểm tra lại khi Back/Forward hoặc đổi phiên. Quyền frontend phục vụ điều hướng, không thay kiểm tra JWT/policy của server.
- `js/common/api.js`: đợi hồ sơ thật từ `/users/me`, kiểm tra quyền màn trước khi tải API nghiệp vụ; không để trang cũ tải ngay bằng role Admin trong cache. `/users/me` bootstrap không chờ chính promise của nó. Bootstrap lỗi: không mở nội dung bằng hồ sơ cũ; hiện hướng tải lại/đăng xuất.
- `js/common/navigation.js`: lọc toàn bộ mục bằng cùng ma trận, không chỉ mục Quản trị; ẩn nhóm rỗng; nhóm một mục có nút trực tiếp trên điện thoại; giữ menu hai nhóm của Admin. Có thông báo khi chuyển về màn làm việc do URL ngoài quyền. Nghe thêm `restaurant:page-access-changed` để cập nhật menu sau khi hồ sơ đã được chốt quyền, kể cả HTML cũ nạp navigation trước api.js.
- `css/base.css`: ẩn `.app-shell` trong khi chưa xác minh quyền; thông báo đang kiểm tra và panel lỗi có nút phục hồi.
- `css/layout.css`: ẩn nhóm/toggle đúng khi có thuộc tính hidden; bố trí nút trực tiếp cho nhóm một mục, một cột nếu chỉ còn một nhóm.
- `js/pages/order-tools.js`: ẩn đường sang Thu ngân đối với Waiter; ẩn đường sang POS đối với Cashier. Các thao tác sửa/hủy vẫn theo guard hiện có.
- `index.html` và `pages/*.html`: cập nhật version các tài nguyên dùng chung để trình duyệt nhận đồng bộ auth/API/menu/CSS mới; chuẩn hóa thứ tự auth → api → navigation, không viết lại giao diện các màn.
- `tests/api-auth.test.cjs`: mở rộng kiểm tra đủ role/tab, returnUrl, nhiều role, không-role, chờ hồ sơ, hồ sơ lỗi và Back/Forward.

## Kiểm tra

- Bộ test frontend sau sửa bootstrap menu: **231/231 đạt**; bao gồm bộ cũ và các trường hợp bổ sung. Không chạy lại hoặc viện kết quả test backend.
- JavaScript đã sửa được kiểm tra bằng `node --check`; `git diff --check` đạt.
- Browser thật: đăng nhập Admin local hiện có; vào Tổng quan rồi Tài khoản; menu Admin giữ đủ chức năng. Không lưu form hoặc thực hiện giao dịch nghiệp vụ.
- Browser fixture riêng ngoài repository: dùng navigation.js và CSS thật với hồ sơ giả độc lập, kiểm tra năm menu. Kết quả Bếp: Bếp/Tài khoản; Phục vụ: Bàn/Đặt món/Tài khoản; Thu ngân: Thanh toán/Tài khoản; Kho: sáu mục phù hợp; Admin: đầy đủ 17 mục. Fixture không gọi backend hoặc lưu phiên thật; không được coi là đã đăng nhập thực tế từng role.
- Trên fixture 390×844: Bếp và Tài khoản là nút trực tiếp cao 48 CSS px; không tràn ngang. Đã kiểm tra thêm menu hai mục của Phục vụ và popup nghiệp vụ Kho. Các kiểm tra này không phải audit responsive toàn bộ sản phẩm.

## Phần Hải và giới hạn

### Lỗi bootstrap đã sửa bổ sung

Chrome thực tế đã hiển thị tài khoản Phục vụ `phucvu` cùng các mục không liên quan ở bản đang mở. Khi rà source, Bàn/Bếp và một số trang nạp navigation trước api.js: `auth.ready` chưa có khi menu đăng ký callback. Sự kiện auth-changed phát lúc cập nhật user nhưng quyền màn chưa được đánh dấu allowed, vì vậy bản script mới cũng có thể ẩn hết menu mà không cập nhật lại. Fixture giao diện cũ đặt ready/allowed sẵn nên đã bỏ sót trường hợp này.

Đã sửa cả thứ tự tải và sự kiện chốt quyền. `tests/navigation.test.cjs` chạy auth/api/navigation thật với hồ sơ trả về trễ, DOM và HTTP giả độc lập; kiểm tra năm role ở thứ tự cũ và Phục vụ ở thứ tự chuẩn. Không biến test này thành tuyên bố đã đăng nhập thật từng role. Menu hiển thị đủ màn hợp quyền ngay sau xác thực, không mở khóa tuần tự theo luồng. Khi kiểm tra trên tab Chrome đã mở từ trước, cần tải lại không dùng cache (`Ctrl + Shift + R`) để nhận HTML và tài nguyên phiên bản mới.

Không có thay đổi backend cần áp dụng cho tính năng menu này. Hải vẫn sở hữu kiểm tra quyền API và giới hạn JWT cũ khi thu quyền/khóa tài khoản. Frontend không tự khẳng định token cũ đã bị thu hồi; trang mới xác minh hồ sơ, nhưng đây không phải cơ chế thu hồi JWT phía server.

Chưa đăng nhập thật Waiter/Chef/Cashier/WarehouseManager vì không tạo/reset tài khoản trong phạm vi frontend. Cần dùng tài khoản được giao để nghiệm thu role thực tế; các test dùng mock/fixture đã được phân biệt ở trên. Không sửa dữ liệu, giá, trạng thái món/đơn hoặc xử lý kho.

## Rà soát Phục vụ / Bếp / Thu ngân — 2026-11-09

### Đông đã sửa frontend

1. **Quyền thao tác Bàn ăn** — `js/pages/tables.js`, `pages/tables.html`, `css/pages/tables.css`:
   - Phục vụ chỉ chọn bàn và mở Đặt món. Không hiện Thêm bàn, Sửa bàn/Khu vực, Bảo trì, Xóa hoặc Khôi phục.
   - Hai nút quản lý trong HTML có `hidden` ngay từ đầu; chỉ mở khi đã xác minh quyền màn và có Admin.
   - Chặn ngay trong handler mở form, submit tạo bàn và xác nhận vòng đời; không chỉ ẩn nút bằng CSS.
   - Khi quyền đổi hoặc trang bị chặn: đóng form quản lý đang mở và kiểm tra lại quyền trước khi gửi lệnh.
   - Phục vụ không có bộ lọc bàn đã xóa; Admin vẫn có đầy đủ bộ lọc và thao tác theo API hiện hữu.
2. **Không hiển thị số liệu chưa tải** — `js/pages/tables.js`:
   - Trước đây dữ liệu danh sách bàn được gán cứng `guestCount=0`, `totalAmount=0`, mã đơn trống. Bàn có khách vì vậy vẫn hiện “Chưa có đơn / 0đ”. Đây không phải kết quả đọc chi tiết đơn.
   - Bỏ các giá trị và ô thông tin chưa có nguồn đó. Thẻ bàn hiện sức chứa/trạng thái; bàn đang dùng có “Mở đơn để xem món” và nút “Xem / thêm món”. Chi tiết món và tiền vẫn tải tại Đặt món từ API của đơn.
   - Không gọi GET bàn theo ID để lấy thêm dữ liệu, vì tài liệu ghi route đó có tác dụng cập nhật trạng thái bàn.
3. **Điện thoại** — `css/components.css`, `css/pages/tables.css`, `css/pages/order.css`, `css/pages/kitchen.css`:
   - Tách tiêu đề và thông tin nhân viên thành hai hàng ở màn hẹp; tên dài xuống dòng trong ô của nó, không ép tiêu đề sang cột hẹp.
   - Tăng vùng bấm bộ lọc bàn, đóng chi tiết, tăng/giảm món, đóng giỏ/hộp thoại, sửa/hủy dòng món và nút Bếp lên ít nhất 44 CSS px ở breakpoint áp dụng.
   - Các trường nhập trong hộp thoại món/Bếp dùng font 16px ở màn nhỏ. Không đổi trạng thái, số lượng hoặc tổng tiền nghiệp vụ bằng CSS.
   - Cập nhật version URL CSS dùng chung ở 17 HTML và tài nguyên riêng của Bàn/Đặt món/Bếp để tải đồng bộ bản mới.

### Kiểm tra đã thực hiện

- **236/236 test frontend đạt**, gồm 5 regression mới ở `tests/tables-access.test.cjs`.
- Các test mới chạy nguyên script trang với DOM/API giả: Phục vụ không gửi được POST/PATCH/DELETE quản lý bàn; Admin vẫn thêm/bảo trì; đổi quyền khi form mở chặn lệnh; Admin chưa được xác minh màn cũng bị chặn.
- `node --check js/pages/tables.js` và `git diff --check` đạt.
- Trình duyệt dùng **fixture riêng tại cổng 5508**, có banner vàng “DỮ LIỆU GIẢ / KHÔNG KẾT NỐI DATABASE”. Fixture phục vụ source frontend thật và response giả; tất cả request ghi đều bị chặn, không proxy tới backend.
- Kiểm tra Bàn ăn, Đặt món, Bếp và Thanh toán ở **320, 390, 768, 1366px**: 16 cấu hình không tràn ngang toàn trang, menu đúng role, tên nhân viên không tràn ô. Đây là kiểm tra bốn màn, không phải chứng nhận responsive toàn bộ ứng dụng.
- Trên fixture: chọn bàn → mở POS; chọn món/kích cỡ → thêm giỏ tạm → mở giỏ; mở hộp báo đã làm và báo thiếu; chuyển cột Bếp; Thu ngân chọn đơn chờ bếp thấy nút thu bị khóa, chọn đơn Ready mở được hộp xác nhận rồi Quay lại; Admin mở form bàn; Cashier mở URL Bếp bị đưa về Thanh toán, không có Bếp trong menu.
- Kiểm tra trạng thái rỗng và lỗi tải trên bốn màn. Không xác nhận gửi món, hủy món, hoàn thành, thu hay hoàn tiền trên database thật.
- Bằng chứng ngoài repo: `_codex_runtime/role-ui-review-matrix.json`, `role-ui-review-states.json`, `role-ui-review-frontend-tests.log`, `role-ui-review-evidence/waiter-mobile-after.png` tính từ thư mục workspace `D:\Hoc_Tap\quanliduan1111`.

### Database gốc và phần chưa nghiệm thu

- Theo lựa chọn của Đông, tiếp tục dùng **RestaurantFoodManager** trên `localhost\SQLEXPRESS`. Đã đối chiếu session SQL của tiến trình backend thật: kết nối database này.
- Thời điểm kiểm tra chỉ đọc: database gốc có 4 tài khoản, 5 đơn, 6 món. Database `RestaurantFoodManagerDev` có 1 tài khoản `admin`, 0 đơn, 0 món. Tài khoản không dùng chung giữa hai database.
- Backend được mở từ build đã kiểm tra sau đồng bộ Hải, với `Database:MigrateOnStartup=false` và `Database:SeedOnStartup=false`; không chạy BasicDataSeeder mới vào database gốc.
- Thông tin đăng nhập tài khoản Đông lưu riêng trước đây bị API từ chối đăng nhập. Không reset mật khẩu, tạo user, gán role hay đổi database để vượt qua trong lượt rà UI ngày 09/11. Thông tin được nhóm cung cấp lại và các lần đăng ký riêng sau đó được cập nhật ở mục 10/11 bên dưới.
- Kết quả fixture và unit test ở trên **không chứng minh** Bếp/POS/Thu ngân đã chạy hoàn chỉnh với backend mới. Bộ backend ở lượt đồng bộ trước có 682 đạt, 22 lỗi, 7 bỏ qua; lượt sửa UI này không sửa hay chạy lại bộ C#.
- **Hải phụ trách:** kiểm tra các lỗi backend còn lại và quyền/luật nghiệp vụ phía API. **Đông phụ trách tiếp:** nghiệm thu lại bằng đúng vai trò sau khi đăng nhập được, đặc biệt các thao tác ghi và responsive trên thiết bị thật. Không mở rộng quyền Thu ngân thành Phục vụ/Bếp chỉ bằng frontend.
- Chưa commit/push trong lượt này. Các sửa đổi local trước đó được giữ lại.

## Bổ sung kiểm tra tài khoản — 10/11/2026

- Theo yêu cầu đăng ký riêng sau lượt rà UI, đã có tài khoản `dong` mang Admin và `kho123`
  mang WarehouseManager trong database gốc. Các lần tạo này chỉ thêm người dùng/vai trò,
  không thay source backend hay tạo dữ liệu đơn/kho để thử nghiệp vụ.
- Đã đăng nhập qua giao diện bằng Admin `dong` và mở Tổng quan. Đã kiểm tra đăng nhập qua
  API cho đủ năm vai trò bằng thông tin nhóm cung cấp; cả năm thành công và đúng role.
  Danh sách demo được nhóm yêu cầu chia sẻ nằm trong README, không phải cơ chế tạo tài khoản tự động.
- Việc đăng nhập thành công không thay thế nghiệm thu các lệnh tạo đơn, chế biến, thu/hoàn tiền
  và ghi kho trên backend mới. Các giới hạn fixture và kiểm tra nghiệp vụ phía trên vẫn áp dụng.
- Mẫu `docs/design/pilot-f1-01.html` và công cụ `figma-plugin/` được giữ local theo yêu cầu;
  không thuộc phạm vi bàn giao frontend lần này.
