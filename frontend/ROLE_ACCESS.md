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

## Ẩn vùng đặt món khi chưa chọn bàn — 10/11/2026

### Đông đã sửa frontend

- `pages/order.html`: thêm khung “Chọn bàn để bắt đầu” và nút liên kết tới Bàn ăn.
  Ẩn ngay trong HTML thanh thao tác đơn, danh mục/thực đơn/giỏ và thanh giỏ mobile,
  tránh xuất hiện thoáng qua trước khi JavaScript tải xong. Cập nhật version CSS/JS của trang.
- `js/pages/order.js`: chỉ mở vùng đặt món sau khi API xác nhận bàn còn hoạt động,
  không bảo trì và người dùng có vai trò Admin/Waiter. Chưa có ID bàn hợp lệ thì không gọi
  API bàn/đơn/thực đơn và không báo đỏ “Hãy chọn một bàn hợp lệ”. Vẫn nhận bàn đã lưu từ
  lần chọn trước; có bàn hợp lệ thì mở thẳng vùng đặt món.
- Nếu tải bàn lỗi: giữ ẩn vùng đặt món, hiện nguyên nhân cùng “Thử lại”/“Chọn bàn khác”.
  Bàn bảo trì có lời giải thích riêng. Khi làm mới nền, giữ bố cục đã xác minh trong lúc
  chờ nhưng vẫn khóa thao tác ghi; nếu tải thất bại thì ẩn dữ liệu cũ và đóng giỏ mobile.
- Giữ thông báo và liên kết quay lại bàn có yêu cầu chưa xác nhận. Không xóa yêu cầu
  đang lưu và không tự gửi lại đơn. Không đổi luật giảm/hủy, gửi bếp hoặc quyền API.
- `css/pages/order.css`: khung chọn bàn gọn ở giữa, nút tối thiểu 44px; trên điện thoại
  nút trải hết chiều rộng. Menu điều hướng Đặt món vẫn hiện cho vai trò được phép,
  không yêu cầu người dùng chạy tuần tự từng luồng để mở quyền màn.

### Kiểm tra và trách nhiệm bàn giao

- **242/242 test frontend đạt**, gồm 6 test mới `tests/order-entry.test.cjs` chạy nguyên
  OrderSession/script trang với DOM/API giả: chưa chọn/sai ID, bàn đã lưu, bàn không tồn
  tại/ngừng hoạt động/bảo trì, lỗi và thử lại, làm mới không nhấp nháy, giữ yêu cầu cũ.
- `node --check frontend/js/pages/order.js` và `git diff --check` đạt.
- Trình duyệt thật, tài khoản Waiter trên localhost:5500: chưa chọn bàn chỉ hiện khung
  chọn; bấm Chọn bàn → chọn bàn x2 trống → Đặt món cho bàn này tải được 3 món.
  Mở/đóng hộp chọn món và giỏ mobile thành công; không thêm/gửi đơn, thanh toán hay ghi kho.
- Quan sát khung chọn bàn ở desktop, 390px và 320px: không tràn ngang toàn trang.
  Khung 320px được kiểm tra bằng URL có `tableId=0` để bỏ qua bàn đã lưu; desktop/390px
  kiểm tra trạng thái chưa chọn bàn sau đăng nhập. Sau đó trả viewport về mặc định.
- Bằng chứng ngoài repo: `_codex_runtime/pos-entry-desktop-20261110.png`,
  `pos-entry-mobile-20261110.png`, `pos-entry-320-20261110.png` và
  `pos-entry-frontend-tests-20261110.log` tính từ workspace `D:\Hoc_Tap\quanliduan1111`.
- **Hải:** không có thay đổi backend cần làm cho yêu cầu ẩn/hiện này. Lỗi tải và thử lại
  được mô phỏng trong test, không gây lỗi server thật. Chưa nghiệm thu thao tác ghi nghiệp vụ
  trong lượt này. **Đông:** thay đổi frontend và ghi chú được đưa vào đợt bàn giao bên dưới.

### Điều chỉnh tiếp: quay lại chọn bàn

- Theo phản hồi của Đông, bỏ việc tự mở bàn từ `restaurant.selectedTable` trong
  `js/pages/order.js`. Nội dung “vẫn nhận bàn đã lưu” ở mục trên được thay thế bằng quy tắc:
  **`order.html` không có `tableId` → khung Chọn bàn; có `tableId` → tải đúng bàn đó.**
  Tải lại URL của bàn đang làm vẫn giữ bàn; bấm mục Đặt món trong menu trở về khung chọn.
- `pages/order.html`: đổi nhãn nút trong đơn thành **Chọn bàn khác**, đi thẳng tới
  sơ đồ Bàn ăn như trước. Không tự điều hướng sau gửi bếp để nhân viên còn kiểm tra đơn.
- Chuyển màn không gửi lệnh hủy/xóa hay đổi bàn của đơn trên server. Yêu cầu chưa xác
  nhận vẫn được giữ và có liên kết quay lại bàn tương ứng. Giỏ tạm chưa gửi vẫn là dữ liệu
  của trang hiện tại, không được lưu khi rời trang; tính năng này chưa bổ sung lưu nháp.
- Test hồi quy bổ sung: dữ liệu bàn cũ không tự mở POS; URL bàn mới được ưu tiên;
  nút Chọn bàn khác về `tables.html` mà không gọi API ghi. Không cần Hải sửa backend.
- Đã chạy lại toàn bộ **243/243 test frontend đạt**, kiểm tra cú pháp và diff đạt.
  Trình duyệt thật với Waiter: bàn x2 → Chọn bàn khác → chọn K1 → mở đúng K1 → bấm
  Đặt món trên menu → khung Chọn bàn, không tự mở K1. Không gửi món hay tạo đơn thử.
  Ảnh ngoài repo: `_codex_runtime/pos-return-table-20261110.png` tính từ workspace.

## Nhãn thanh điều hướng điện thoại theo màn đang mở — 10/11/2026

- **Đông / frontend:** `js/common/navigation.js` đổi nhãn nút nhóm ở breakpoint ≤760px
  thành tên mục đang được chọn trong nhóm đó: Đặt món, Bàn ăn, Kho hàng, Khuyến mãi…
  Nhóm không chứa màn đang mở vẫn dùng tên nhóm; nhóm chỉ có một mục vẫn bấm trực tiếp.
  Popup giữ danh sách đúng quyền như trước, không mở thêm quyền hay màn cho vai trò.
- Xoay/đổi kích thước cập nhật nhãn, desktop giữ bố cục nhóm cũ. Sự kiện đổi module
  quản trị trong cùng trang cũng cập nhật tên và đánh dấu mục hiện tại. Cập nhật version
  URL navigation.js ở 15 HTML có dùng script để tránh trộn tài nguyên cũ.
- **245/245 test frontend đạt**, gồm kiểm tra nhãn mobile/desktop, trang tiện ích đơn,
  kho và đổi tab quản trị; cú pháp JavaScript và `git diff --check` đạt.
- Trình duyệt thật Waiter ở 390px: mở popup từ Đặt món → chọn Bàn ăn → nhãn Bàn ăn;
  chọn lại Đặt món → nhãn Đặt món, popup đóng. Đã trả viewport về mặc định.
  Trang Bàn ăn đang cache cũ cần tải lại để nhận script mới; không sửa dữ liệu nghiệp vụ.
- Ảnh ngoài repo: `_codex_runtime/mobile-navigation-current-screen-20261110.png`;
  log: `_codex_runtime/mobile-navigation-tests-20261110.log` tính từ workspace.
  **Hải / backend:** không có thay đổi cần thực hiện cho yêu cầu nhãn này.

## Làm gọn màn Chọn bàn và bỏ hướng dẫn lặp — 10/11/2026

### Đông đã sửa frontend

- `js/pages/tables.js`: bỏ “Mở đơn để xem món” cùng các dòng hướng dẫn phụ dưới thẻ;
  bỏ câu hướng dẫn mở đơn trong chi tiết. Nút Xem / thêm món và luật thao tác giữ nguyên.
  Nhãn `Occupied` đổi thành **Có khách**, không đổi mã trạng thái hoặc dữ liệu server.
- `pages/tables.html`: bỏ cụm “Sơ đồ bàn / Bàn đang phục vụ” lặp với tiêu đề Chọn bàn.
  Nút Làm mới và nút quản lý Admin chuyển về cạnh tiêu đề Danh sách bàn. Bộ lọc chuyển
  từ nhiều nút nhỏ sang hai select có nhãn Khu vực / Trạng thái; giữ nguyên giá trị lọc.
- `css/pages/tables.css`: thống kê Trống / Có khách / Bảo trì thành một dải chung;
  tổng số bàn vẫn hiện cạnh danh sách. Điện thoại dùng hai cột bàn, tên 18px, số chỗ 14px,
  trạng thái 13px; màu chữ chính rõ, bỏ chữ xanh mặc định trên nút thẻ. Bộ lọc 16px,
  cao ít nhất 46px; ở màn ≤360px xếp dọc để không cắt chữ. Tên và trạng thái tách hàng
  cả trên laptop để tránh ép tên bàn thành nhiều dòng. Dọn CSS nút lọc/tiêu đề cũ.
- `js/data/tables.data.js`: nhãn tùy chọn serving đồng bộ thành Có khách. Cập nhật version
  URL ba tài nguyên của trang. Không đổi role, endpoint hoặc các lệnh tạo/sửa/xóa.
- Khi bộ lọc không có kết quả: “Không có bàn phù hợp / Thử chọn khu vực hoặc trạng thái
  khác”, thay lời nhắc kết nối dữ liệu không đúng ngữ cảnh.

### Kiểm tra và phần Hải

- **245/245 test frontend đạt**. Mở rộng test Bàn hiện có để kiểm tra đổi select trạng
  thái/khu vực lọc đúng, Phục vụ vẫn không thấy bàn đã xóa hoặc nút quản lý.
- Kiểm tra cú pháp hai file JS và `git diff --check` đạt. Đã xem trình duyệt thật bằng
  tài khoản Waiter ở 320px, 390px và desktop 1280px; không tràn ngang toàn trang ở các
  kích thước đã đo. Đã lọc Trống, khu vực Lầu 2, trả về Tất cả, mở/đóng chi tiết bàn.
  Không tạo đơn, sửa bàn, thanh toán hay đổi dữ liệu; Admin được kiểm tra bằng test giả,
  chưa đăng nhập lại Admin để nghiệm thu bố cục nút quản lý trong lượt này.
- Ảnh ngoài repo: `_codex_runtime/tables-clean-mobile-20261110.png`,
  `tables-clean-320-20261110.png`, `tables-clean-desktop-20261110.png`; log:
  `_codex_runtime/tables-clean-tests-20261110.log` tính từ workspace.
- **Hải / backend:** không có yêu cầu sửa backend cho phần bố cục này. **Đông / frontend:**
  phụ trách phần sửa và ghi chú. Các giới hạn nghiệp vụ trước đó giữ nguyên.

## Bàn giao frontend lên nhánh dong — 10/11/2026

- Phạm vi: nhãn menu mobile, chọn/đổi bàn tại POS, bố cục Bàn ăn và các test/ghi chú liên quan.
  Không đưa backend, database, thông tin đăng nhập mới, file thiết kế hoặc figma-plugin vào đợt này.
- Trước commit: fetch xác nhận local khớp origin/dong; 245/245 test frontend đạt;
  kiểm tra cú pháp 40 file JavaScript, 168 đường dẫn tài nguyên không thiếu file và diff đạt.
- Các con số test 242/243 phía trên là các mốc trước khi bổ sung test menu mobile.
  Việc kiểm tra trình duyệt đã mô tả theo từng màn/kích thước; chưa nghiệm thu lệnh ghi
  nghiệp vụ trên database thật trong đợt UI này. Giỏ tạm chưa gửi vẫn mất khi rời trang.
- Ảnh và log nằm ngoài repository, không nằm trong các commit frontend.

## Tìm món không dấu, từ khóa rời và viết tắt — 11/11/2026

- **Đông / frontend:** `js/pages/order.js` chuẩn hóa hoa/thường, dấu tiếng Việt (kể cả
  Unicode tổ hợp và Đ/đ), khoảng trắng và dấu câu khi tìm. Không đổi tên món hiển thị.
  Các từ khóa có thể cách nhau hoặc đảo thứ tự: `tom me` / `me tom` tìm Tôm rang me.
  Chữ đầu theo thứ tự tên món: `bll` tìm Bò lúc lắc, `cchs` tìm Cơm chiên hải sản,
  `lths` tìm Lẩu Thái hải sản. Cho phép tiền tố viết tắt từ hai ký tự, không đoán lỗi chính tả.
- Lọc ngay khi nhập trên danh sách đã tải, không phát request tìm kiếm và không nhấn Enter.
  Giữ phạm vi danh mục đang chọn; muốn tìm toàn thực đơn thì chọn Tất cả. Xóa từ khóa
  trả danh sách của danh mục đó. Kết quả rỗng có hướng dẫn đổi từ khóa/danh mục.
- `pages/order.html`: placeholder minh họa `bo, bll, tom me`; tắt kiểm tra chính tả và
  tự viết hoa cho ô tìm; cập nhật version URL script. Không thay giỏ, giá hoặc API gửi bếp.
- **249/249 test frontend đạt**, gồm bốn test trang mới cho dấu/Unicode, viết tắt,
  từ khóa rời, phạm vi danh mục và xóa từ khóa; test xác nhận không có request/lệnh ghi mới.
  Cú pháp order.js và `git diff --check` đạt.
- Trình duyệt thật bằng Waiter: `bo` trả ba món; `bll`, `cchs`, `tom me`, `lths`, `dau`
  trả đúng món; từ không có kết quả trả 0 và xóa từ khóa trả 21 món. Món mặn + `tom me`
  trả 0, đổi Tất cả trả Tôm rang me. Đã xem `bll` trên viewport 390px và yêu cầu reset sau đó.
  Không gửi bếp hoặc tạo đơn để kiểm thử.
- **Hải / backend:** không có thay đổi cần thực hiện. Database giữ nguyên trong lượt sửa tìm kiếm.
  Bằng chứng ngoài repo: `_codex_runtime/food-search-bll-20261111.png`
  và `_codex_runtime/food-search-tests-20261111.log` tính từ workspace.

### Bổ sung theo ảnh kết quả rỗng 320px — 11/11/2026

- Ảnh gửi sau lượt sửa còn dòng “Thực đơn được tải từ máy chủ…” của script cũ. Đã
  đối chiếu HTTP localhost:5500: file phục vụ khớp source mới, nhưng static server không
  đặt Cache-Control. Có URL mới `order.html?tableId=3&v=20261111-search2` để tải lại HTML.
- Đồng thời API hiện không đưa các món bò vào menu: các món vẫn active/available,
  cỡ và công thức còn active, nhưng thuộc danh mục id 4 (cơm) và 5 (Món mặn) đã ngừng dùng.
  Đông chọn **giữ nguyên trạng thái**; không bật lại danh mục/món hay sửa database.
  Menu thay đổi theo danh mục đang bật, nên số món ở lần kiểm tra trước không cố định.
- `css/pages/order.css`: empty-state thành grid, tách tiêu đề/mô tả cách nhau 8px,
  có padding, chữ 16/14px, xuống dòng trong khung; áp dụng cả đang tải/lỗi tải/rỗng.
  `order.js` giải thích chỉ tìm món đang bán; `order.html` tăng version CSS và JS tương ứng.
- Kiểm tra thật ở 320px: `tom me` và `trm` trả Tôm rang me, `lths` trả Lẩu Thái hải sản;
  `bo` trả 0 đúng trạng thái danh mục. Đo scrollWidth=305 ≤ innerWidth=320, tiêu đề và
  mô tả cách nhau 8px; không tràn ngang toàn trang. 249/249 test frontend, cú pháp và diff đạt.
- Không gửi đơn, không thay dữ liệu. Ảnh ngoài repo:
  `_codex_runtime/search-empty-fixed-320-20261111.png`; log `search-empty-tests-20261111.log`.

### Căn lại màn quản lý Món ăn theo ảnh 320px — 11/11/2026

- **Đông / frontend:** sửa `pages/foods.html`, `css/pages/foods.css`, `js/pages/foods.js`.
  Lỗi cũ: thẻ món dùng grid hai cột trên điện thoại nhưng giá/danh mục bị ép vào
  cột 2, trạng thái tự rơi sang cột 1; chữ thống kê 11px, bộ lọc kéo ngang bị cắt.
- Thẻ mới đặt ảnh viết tắt cạnh tên/mã/danh mục; giá và trạng thái cùng một hàng
  cuối, có đường phân cách. Tên món 16px, giá 15px trên màn nhỏ; tên dài xuống dòng.
  Mô tả dài chỉ hiện trong chi tiết trên màn nhỏ; desktop vẫn hiện mô tả có dữ liệu.
  Không đổi tên món, danh mục, giá, trạng thái hoặc công thức trong database.
- Thống kê hai cột bằng nhau trên màn nhỏ, bốn ô cao 76px ở 320px; nhãn Ngừng bán
  vẫn tính đúng nhóm đã xóa/ngừng dùng. Nút Thêm món hiện riêng; bốn thao tác Công thức,
  Danh mục/Topping, Thêm danh mục, Thêm kích cỡ nằm trong mục mở rộng có nhãn rõ.
- Bộ lọc danh mục/trạng thái thành select có label, giữ nguyên giá trị lọc và quyền.
  320px xếp dọc, 390px trở lên đủ chỗ thì hai cột; ô nhập/chọn cao 44px, font 16px.
  Đổi bộ lọc không làm mất từ tìm kiếm; không thêm API hoặc sửa cách lưu dữ liệu.
  Laptop hẹp xếp danh sách và chi tiết dọc để không ép các cột; desktop rộng giữ hai khung.
  Nút đóng chi tiết tăng lên 44px. HTML tăng version riêng CSS/JS để tải bản mới.
- **Đã chạy:** cú pháp `foods.js`; 249/249 test frontend; `git diff --check`;
  12/12 tài nguyên CSS/JS của trang trả HTTP 200. Bộ test hiện có không có test riêng
  cho màn Foods; phần thay đổi bộ lọc được kiểm tra trực tiếp qua trình duyệt Admin.
- **Kiểm tra trực tiếp:** 320/390/768/1024/1280/1536px, không tràn ngang toàn trang,
  giá/trạng thái không vượt thẻ. Lọc Hải sản trả Tôm rang me và Mực xào sa tế;
  Ngừng bán trả hai món cũ; tìm từ không tồn tại trả 0. Mở/đóng chi tiết và biểu mẫu
  Thêm món; mở/đóng nhóm thao tác phụ. Không bấm Tạo/Lưu/Xóa/đổi trạng thái.
- **Hải / backend:** không có phần cần sửa trong thay đổi này. Không chỉnh backend,
  database hoặc package. Các luồng ghi nghiệp vụ không được nghiệm thu trong lượt sửa này.
  Bằng chứng ngoài repo, tính từ workspace: `_codex_runtime/foods-layout-top-320-20261111.png`,
  `foods-layout-cards-320-20261111.png`, `foods-layout-responsive-20261111.json`,
  `foods-layout-tests-20261111.log` trong cùng thư mục `_codex_runtime`.


## Pilot Sahara cho luồng Phục vụ — 11/11/2026

### Đông / frontend đã thay đổi

- `css/pages/service-workspace.css`: bộ trình bày mới chỉ áp dụng khi body có
  `service-workspace`; ba trang Chọn bàn, Đặt món, Tiện ích đơn chủ động nạp stylesheet.
  Nền kem, màu cam đất đậm #a94f1c, chữ Segoe UI hỗ trợ tiếng Việt, nút/ô nhập 44px.
  Màu nút được làm đậm hơn mẫu Stitch để chữ trắng dễ đọc (tỷ lệ tính được 5.49:1).
  Không nhập nguyên HTML/JS mẫu hoặc thêm thư viện/font/CDN.
- `pages/tables.html`: nhóm thống kê/bộ lọc thành một khối; giữ board và chi tiết.
  Thẻ bàn hiện khu vực/sức chứa/trạng thái từ API. Tablet 761–1100px có ngăn chi tiết
  mở/đóng, mobile giữ ngăn phía dưới; không dựng khách đặt chỗ/thời gian/tổng tiền giả.
- `pages/order.html`: thực đơn/tìm kiếm/danh mục ở trái, giỏ tạm và món đã gửi ở phải
  trên desktop; mobile danh sách một cột, giỏ mở qua thanh dưới. Danh mục đặt sau tìm kiếm,
  các nhãn xuống dòng thay vì bị cắt ngang. Nút Xóa đơn tạm chuyển vào giỏ; nút Gửi bếp
  chính ở giỏ để tránh hai CTA giống nhau trên thanh đầu. Giữ toàn bộ ID và handler cũ.
- `js/pages/order.js`: thẻ món có ảnh từ foodImageUrl HTTP/HTTPS nếu hợp lệ, chữ viết tắt
  khi thiếu/lỗi ảnh; tên/giá/kích cỡ vẫn từ menu. Không lấy ảnh hoặc giá giả trong Stitch.
  Bỏ thông báo lặp lúc tải lần đầu; Làm mới vẫn báo kết quả, pending/lỗi vẫn giữ.
  Không sửa OrderSession, API, công thức tính tiền, điều kiện khóa hoặc lệnh gửi bếp.
- `js/common/navigation.js`: chỉ thêm class trình bày khi trang pilot đã xác minh quyền
  và có 2–4 mục hợp lệ. Mobile Phục vụ mở trực tiếp Bàn ăn/Đặt món/Tài khoản; Admin giữ
  nhóm đầy đủ 17 mục. CSS không mở các nav-item hoặc nhóm bị ẩn do không có quyền.
- `pages/order-tools.html`: nạp cùng bộ trình bày; giữ form/ID/lệnh và xác nhận cũ.
  Chuyển toàn bộ đơn chỉ đến bàn trống/hoạt động; không thêm chia/gộp hoặc ca/két tiền.
  Bếp/Thu ngân/Kho/Quản trị/Đăng nhập chưa được đổi sang Sahara ở lượt pilot này.

### Kiểm tra thực sự đã chạy

- **253/253 test frontend đạt**: 249 test cũ và bốn test mới cho menu trực tiếp không
  mở rộng quyền, trang ngoài pilot giữ hành vi cũ, URL ảnh/escape/hook chọn món/giá và
  liên kết HTML có phiên bản nhưng không tự thêm tableId.
  `node --check` bốn JS đã sửa và `git diff --check` đạt.
- Cả ba HTML giữ nguyên ID (Bàn 26, POS 73, Tiện ích 27), không ID trùng.
  Mỗi trang có 11/11 tài nguyên CSS/JS trả HTTP 200.
- Fixture HTTP riêng cổng 5508 phục vụ source thật và dữ liệu giả có banner vàng;
  không proxy API, chặn toàn bộ request ghi. Ba màn x bốn kích thước 320/390/768/1440px:
  **12 cấu hình không tràn ngang**, menu Phục vụ chỉ ba mục đúng quyền.
- Fixture: bàn -> POS -> Chọn bàn khác -> mục Đặt món trở về bước chọn bàn, không dính
  bàn cũ. Chọn cỡ/topping/2 phần, tăng/giảm, ghi chú, mở/đóng/xóa giỏ; không gửi bếp.
  Giỏ 320/390px có nút gửi cao 44px trong vùng nhìn thấy, không bị thanh dưới che.
  Ở 320px elementFromPoint tại tâm nút xác định đúng cartSendButton.
- Fixture: dòng Preparing không có nút hủy, chỉ Gọi tăng và giải thích khóa. Bàn bảo trì
  không hiện thực đơn; lỗi tải có Thử lại. Chi tiết bàn mở/đóng được ở 320/768px.
  Tiện ích chỉ có bàn trống THỬ 04 làm đích; mở xác nhận chuyển rồi Hủy, không xác nhận ghi.
- Website thật localhost:5500: tài khoản Admin hiện có, GET dữ liệu bàn/thực đơn thật,
  **8 cấu hình** (hai màn x 320/390/768/1440px) không tràn ngang. Ảnh trước/sau được chụp
  cùng vai trò và kích thước; không dùng kết quả fixture để khẳng định dữ liệu thật đã ghi.

### Giới hạn và bàn giao

- **Hải / backend:** không có thay đổi backend cần áp dụng trong pilot này. Không thay
  database, tài khoản, trạng thái danh mục/món/bàn, tổng tiền hoặc cấu hình máy.
- Chưa nghiệm thu gửi bếp/giảm-hủy món/chuyển bàn thật hay luồng Bếp/Thu ngân trên database.
  Các kiểm tra giả không phải bằng chứng các lệnh ghi backend đã chạy hoàn chỉnh.
- Dữ liệu chưa có ảnh dùng chữ viết tắt; không tự tải ảnh stock hoặc chỉnh database.
- Chưa commit/push. Backup source frontend trước sửa, ảnh/log/matrix nằm ngoài repo tại
  `_codex_runtime/service-sahara-20261111/` tính từ workspace. Báo cáo so sánh:
  `hkdn/Restaurant-UXUI-Audit/reports/service-sahara-pilot-2026-11-11.md`.

### Bổ sung chống quay lại giao diện HTML cũ

- Đã quan sát trình duyệt tải lại `tables.html` từ cache, không có stylesheet/body class
  mới dù source đã đổi. Không coi việc CSS tải thành công là đủ để kết luận chuyển trang đúng.
- `navigation.js` giữ tham số `ui=20261111-sahara3` cho liên kết ba trang pilot;
  `tables.js`, `order.js`, `order-tools.js` giữ phiên bản khi chọn bàn, chọn bàn khác,
  quay lại yêu cầu pending, mở tiện ích và quay lại POS. Các HTML tăng version JS tương ứng.
  Không đổi tableId/orderId, URL API hoặc tự khôi phục bàn cũ.
- Đã thử trực tiếp trên website thật: Chọn bàn -> POS tableId=1 -> Chọn bàn khác ->
  Đặt món không tableId. Cả ba URL giữ ui=sahara3, body vẫn service-workspace, màu chính
  #a94f1c; bước cuối trở về Chọn bàn để bắt đầu. Bằng chứng `cache-roundtrip.json`.
- Đây là xử lý phía frontend. Tab cũ đã lưu HTML từ trước vẫn nên mở link phiên bản mới
  hoặc tải lại không dùng cache một lần; không sửa cấu hình static server/file BAT.

## Mở rộng Sahara toàn bộ giao diện — 12/11/2026

- **Đông / frontend:** đã mở rộng khung Sahara từ ba trang pilot sang đủ 17 HTML:
  Bếp, Thu ngân, Món ăn/Kho/Nhập hàng/Công thức, Dashboard/Báo cáo, Quản trị,
  Nhân viên/Vai trò/Tài khoản và Đăng nhập/Setup. Không đổi ma trận quyền hoặc API.
- Đã sửa lỗi frontend Bếp/Thu ngân báo danh sách rỗng khi GET thất bại. Loading/lỗi
  dùng số liệu `—`; dữ liệu cũ chỉ xem và khóa lệnh cho tới khi tải lại đầy đủ.
- Toàn bộ navbar dùng phiên bản `20261112-service-nav5`; trang nghiệp vụ và CSS mới
  dùng `20261112-sahara-all2`. Đây là phiên bản thay cho khóa cache pilot ghi ở trên.
- **266/266 test frontend đạt**, sáu JS đạt kiểm tra cú pháp, diff check đạt. Giữ 577 ID
  cũ của 17 trang, thêm ba anchor Thanh toán; 199 tham chiếu CSS/JS tồn tại.
- **Hải / backend:** không cần sửa backend để nhận bộ giao diện này. Chưa nghiệm thu
  lệnh ghi trên database thật, provider MoMo hoặc thu hồi JWT; không xem thay đổi UI là
  bằng chứng đã giải quyết các ghi chú backend cũ.
- Chi tiết từng file, hành vi, quyền hoàn tiền đúng `CashierDesk`, các lượt kiểm tra thật,
  fixture và giới hạn: đọc [SAHARA_UI_HANDOFF.md](SAHARA_UI_HANDOFF.md).
- Chưa commit/push. Không đưa prototype `docs/design/`, `figma-plugin/` hoặc file local
  `BACKEND_CHANGES_FOR_FRONTEND.md` vào phạm vi này.

## Bố cục Sahara theo Stitch — cập nhật cấu trúc 12/11/2026

Mốc này thay thế mô tả bố cục/font/cache của lượt “Mở rộng Sahara” phía trên; các ghi chú
lịch sử vẫn giữ để đối chiếu. Source đang ở local nhánh `dong`, chưa commit/push.

- **Đông / frontend:** đã thay cách tổ chức công việc, không chỉ thay màu. Shell dùng
  Manrope + EB Garamond đóng gói local tại `assets/fonts/` cùng hai giấy phép OFL; menu có
  icon và nhãn theo quyền. Không thêm package hoặc phụ thuộc CDN.
- POS có toolbar/catalog riêng và một ticket: món đã gửi trước, món mới chưa gửi sau.
  Thu ngân có hàng đợi trái/hóa đơn dạng giấy phải, tab Thanh toán/Phiếu thu. Quyền hoàn
  tiền giữ đúng API `CashierDesk` cho **Admin và Cashier**, không đổi hợp đồng nghiệp vụ.
- Bếp dùng toolbar gọn và hàng đợi/tab trạng thái. Kho có register trái/chi tiết nguyên
  liệu phải, tab Tồn kho/Lịch sử; deep link chứng từ mở đúng lịch sử. Nhập hàng tách tab
  Phiếu nhập/Nhà cung cấp. Món ăn dùng register căn cột; Công thức có list/rail định lượng
  và phiên bản. Mobile dùng sheet chi tiết đóng được; thống kê thu gọn thành dòng đếm.
- Dashboard có hero/tổng quan, Báo cáo có công tắc kỳ; Nhân viên mở form trong hộp thoại;
  Quản trị có rail chọn module; Tài khoản tách danh tính/hồ sơ/mật khẩu. Các giới hạn API,
  MoMo Mock, đơn vị/phiên bản, trạng thái, tổng tiền server và quyền vẫn giữ nguyên.
- Sửa GET chi tiết phiếu/công thức lỗi trên màn hẹp: đóng sheet, đưa focus về thông báo
  để người dùng thấy lỗi; không để sheet trống che nội dung. Các trạng thái lỗi/rỗng/tải
  Bếp/Thu ngân đã sửa ở lượt trước tiếp tục giữ.
- Cache hiện tại: `navigation.js?v=20261112-service-nav6`, UI/CSS/JS
  `20261112-stitch-layout3`; `recipes.js` và `purchases.js` thêm hậu tố `-fix1`.
- **Đã chạy:** 281/281 test frontend đạt; 17 HTML giữ đủ 577 ID cũ, không ID trùng;
  222 tham chiếu local tồn tại; 21 JS/CJS đạt cú pháp, 6 CSS và hai font được kiểm tra;
  `git diff --check` toàn repository đạt.
- **Trình duyệt thật:** 12 trang (tables/payment/inventory/purchases/recipes/foods/reports/
  employees/roles/administration/account/index) × 320/390/768/1440px = 48 cấu hình;
  không quan sát thấy tràn ngang toàn trang hoặc lỗi trong các lượt đó. Đây là viewport,
  chưa thử thiết bị thật. Đã mở form điều chỉnh tồn rồi Hủy và mở/đóng dialog Nhân viên
  ở 390px, không lưu dữ liệu.
- **Fixture 5508:** thêm một món vào giỏ tạm, mở/xóa giỏ; nút Gửi bếp 50px nhìn thấy ở
  390px, không bấm gửi. Mở xác nhận thanh toán rồi Quay lại; đổi tab Phiếu thu và dùng
  ArrowLeft quay lại. Fixture chặn ghi, không nối database; không xem đây là nghiệm thu
  đặt đơn/thanh toán/nhập kho thật.
- **Hải / backend:** không có file backend, endpoint, DTO, migration hoặc database được
  thay trong lượt này. Các ghi chú backend/JWT cũ vẫn cần xử lý riêng. Chưa nghiệm thu
  luồng end-to-end có ghi dữ liệu thật, provider MoMo, máy in hoặc tải giờ cao điểm.
- Đọc [SAHARA_UI_HANDOFF.md](SAHARA_UI_HANDOFF.md) để biết chính xác file Đông sửa,
  quy tắc nghiệp vụ giữ nguyên và giới hạn. Ảnh/log/JSON ngoài repo tại
  `_codex_runtime/stitch-layout-20261112/` tính từ workspace; báo cáo trước/sau ở
  `hkdn/Restaurant-UXUI-Audit/reports/stitch-structural-redesign-2026-11-12.md`.
  Không đưa prototype `docs/design/`, `figma-plugin/` hoặc file local
  `BACKEND_CHANGES_FOR_FRONTEND.md` vào phạm vi frontend này.
