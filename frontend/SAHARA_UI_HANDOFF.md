# Bàn giao frontend Sahara — bố cục Stitch, 12/11/2026

Tài liệu này mô tả **phiên bản cấu trúc mới nhất trên nhánh `dong`**.
Các ghi chú pilot và lượt đổi màu trước đó trong `ROLE_ACCESS.md` là lịch sử; dùng tài liệu
này để xác định bố cục, font, phiên bản cache và kết quả kiểm tra hiện tại.

Source được chốt theo hai phần: `ed8fd5c` (workspace nhân viên/quản trị và POS),
`0e2c5f8` (hàng đợi Bếp và in từng món). Các câu “chưa commit/push” trong nhật ký
bên dưới mô tả thời điểm kiểm tra trước khi chốt source, không phải trạng thái Git hiện tại.

Kiểm tra trước khi đưa lên Git: toàn bộ **332/332 test frontend đạt**, **27 JS/CJS**
đạt cú pháp; **17 HTML** giữ 577 ID gốc và 230 liên kết tài nguyên local tồn tại.
Sau chỉnh tooltip POS về giờ Việt Nam, chạy lại **20/20 test POS đạt**. Không nghiệm thu
thêm luồng ghi dữ liệu thật hoặc máy in vật lý trong bước đồng bộ Git.

## Phân công và phạm vi

- **Đông / frontend:** HTML, CSS, JavaScript hiển thị/tương tác, điều hướng đúng quyền,
  responsive, trạng thái tải/rỗng/lỗi và các test giao diện được liệt kê dưới đây.
- **Hải / backend:** tiếp tục phụ trách API, quyền ở server, JWT, tính tiền, trạng thái đơn,
  nhận/hủy món, ghi kho, thanh toán/hoàn tiền và các vấn đề backend đã ghi tại `docsforfe`.
  Lượt giao diện này không yêu cầu Hải đổi endpoint, DTO hoặc migration để sử dụng bố cục mới.
- Giữ HTML/CSS/JavaScript của dự án và API hiện tại. Không nhập mã nghiệp vụ từ Stitch,
  không đổi session nghiệp vụ, không cài package, không sửa backend/database hoặc tài khoản.
  Không có lệnh tạo đơn, gửi bếp, điều chỉnh kho, nhận hàng, thu hay hoàn tiền thật được
  thực hiện để kiểm thử lượt này. Chưa commit/push hoặc triển khai.

## Bố cục hiện tại khác gì lượt chỉ đổi màu

### Khung chung và chữ

- Dùng **Manrope** cho nội dung/điều khiển và **EB Garamond** cho tiêu đề chính.
  Hai font được đóng gói local tại `assets/fonts/Manrope-Variable.ttf` và
  `assets/fonts/EBGaramond-Variable.ttf`; giấy phép đi kèm là `Manrope-OFL.txt` và
  `EBGaramond-OFL.txt`. Không tải font từ CDN khi mở website.
- Nền kem, chữ nâu đậm, nút cam đất `#a94f1c`; màu nút giữ mức tương phản trắng tính được
  5.49:1. Sidebar có icon, nhãn và trạng thái đang chọn; desktop có thanh đầu gọn.
- Điện thoại đặt tiêu đề ở hàng trên, vai trò/tên/đăng xuất ở hàng dưới. Vai trò có ít
  màn hình được dùng các tab có icon ở thanh dưới; vai trò nhiều mục vẫn dùng nhóm menu.
- Ma trận quyền không thay đổi. CSS không mở các mục bị `restaurantAuth` ẩn. Giới hạn
  hiển thị frontend không thay thế việc kiểm tra quyền tại API của Hải.
- Nút chính, ô nhập và nút đóng có vùng thao tác tối thiểu 44px; ô nhập trên điện thoại
  dùng 16px. Không đưa ảnh, giá, số tồn hoặc số doanh thu giả vào website thật.

### Phục vụ: Chọn bàn → Đặt món → Tiện ích

- **Chọn bàn:** board bàn là không gian chính; bộ lọc/tổng quan gọn và rail chi tiết riêng.
  Giữ khu vực, sức chứa, trạng thái từ API. Tablet/mobile mở chi tiết qua panel có nút đóng.
- **POS:** thanh ngữ cảnh bàn tách khỏi catalog; tìm kiếm/danh mục nằm trong không gian
  thực đơn. Desktop có **một ticket đơn hàng bên phải**: phần món **đã gửi trước**, phần
  **món mới chưa gửi sau**, thay cho hai khối lặp rời rạc. Tổng đơn đã gửi là giá trị server;
  thuế/phí được đặt trong phần chi tiết tiền hàng có thể mở ra xem.
- Giữ bước Chọn bàn để bắt đầu khi chưa chọn bàn; Chọn bàn khác không tự giữ tableId cũ.
  Mobile mở giỏ qua thanh đơn tạm, có nút đóng, xóa và gửi bếp trong giỏ.
- Tìm không dấu/viết tắt/từ khóa rời vẫn hoạt động trong thực đơn đã tải. Ảnh món dùng
  URL HTTP/HTTPS hợp lệ của API; thiếu/lỗi ảnh dùng chữ viết tắt.
- Tiện ích đơn giữ điều kiện chuyển toàn bộ đơn sang bàn trống/hoạt động. Không thêm
  chia/gộp hóa đơn, đặt chỗ theo khách/giờ hoặc giả dữ liệu khách đang ngồi.

### Bếp

- Toolbar gọn, trạng thái hàng đợi và tìm kiếm nằm gần danh sách phiếu. Desktop đủ rộng
  có các cột xử lý; màn hẹp chuyển tab để không ép tên món/ghi chú/topping vào cột nhỏ.
- Nhận **từng món**, nhập **số phần vừa làm xong**, báo thiếu/hỏng kèm lý do, hủy và mở lại
  theo điều kiện sẵn có. Không tự thay quy tắc trừ kho/hao hụt/hoàn kho.
- Giữ sửa lỗi báo rỗng khi GET thất bại: số chưa xác định hiển thị `—`, có thông báo lỗi;
  chỉ GET thành công nhưng rỗng mới hiển thị 0. Dữ liệu cũ khi tải lại lỗi chỉ để xem,
  các lệnh xử lý bị khóa. Poll phục hồi không tự xóa lỗi nghiệp vụ khác.

### Thu ngân

- Tách tab **Thanh toán / Phiếu thu**, hỗ trợ thao tác phím trên tab. Thanh toán có hàng
  đợi đơn ở trái, **hóa đơn dạng tờ giấy ở phải**, rồi đến phương thức và nút xác nhận thu.
  Ledger có bộ lọc/phân trang riêng, không nằm kéo dài dưới toàn bộ quầy thanh toán.
- Tổng tiền, thuế/phí/giảm giá và trạng thái dùng phản hồi server. Giữ điều kiện đơn
  Ready/Completed, xác nhận đã nhận tiền, xem/in phiếu, khóa yêu cầu chưa rõ kết quả.
- Quyền hoàn tiền theo đúng chính sách API `CashierDesk`: **Admin và Cashier**; lý do và
  điều kiện hoàn toàn bộ giữ nguyên. Không đổi thành quyền chỉ Admin vì mẫu thiết kế.
- MoMo giữ cơ chế/nhãn Mock hiện có; chưa có bằng chứng đã tích hợp provider thật.
  Không thêm tiền khách đưa/tiền thối, ca/két hoặc công tắc VAT không có hợp đồng API.
- Tải danh sách đơn và ledger được theo dõi riêng: lỗi GET không bị mô tả là danh sách
  rỗng; số liệu loading/lỗi là `—`; hàng cũ chỉ xem nếu chưa tải lại đầy đủ. Không sửa
  `PaymentSession`, idempotency, endpoint hoặc công thức tiền.

### Kho, Nhập hàng, Món ăn, Nguyên liệu/Công thức

- **Kho:** tab **Tồn kho / Lịch sử biến động** tách hai công việc. Desktop là register
  nguyên liệu căn cột tên/trạng thái/tồn/đơn vị ở trái, chi tiết nguyên liệu ở phải.
  Rail có tồn ghi sổ, ngưỡng tối thiểu, thời điểm cập nhật, Điều chỉnh và Xem lịch sử.
  Điện thoại/tablet hẹp mở chi tiết bằng sheet; đóng được và trả focus về dòng đã chọn.
  Lệnh điều chỉnh vẫn mở form/xác nhận với snapshot và lý do như trước.
- Link có `ingredientId`, `referenceType` hoặc `referenceId` tự mở tab lịch sử và giữ
  bộ lọc chứng từ. Ledger có vùng cuộn ngang riêng, focus được bằng bàn phím.
- **Nhập hàng:** tab **Phiếu nhập / Nhà cung cấp** thay cho hai danh sách dài xếp nối nhau.
  Phiếu nhập có register trái/chi tiết phải; mobile dùng sheet. Chỉ phiếu Nháp được nhận
  hoặc hủy theo session/API cũ. Không thêm sửa/xóa phiếu hoặc tự cộng tồn từ UI.
- **Món ăn:** bộ lọc nằm trong register; desktop căn cột tên, danh mục, giá, trạng thái
  và có rail chi tiết trắng. Màn hẹp dùng danh sách gọn và sheet chi tiết có nút đóng.
- **Công thức:** danh sách món/kích cỡ và phiên bản bên trái, định lượng/lịch sử bên phải;
  mobile mở sheet. Thống kê các màn này là **dòng đếm gọn**, không còn hàng KPI lớn chiếm
  phần đầu. Giữ nguyên đơn vị kho, định lượng, lịch sử phiên bản và trạng thái nguyên liệu.
- GET chi tiết phiếu/công thức lỗi trên màn hẹp: sheet được đóng, đưa focus về thông báo
  lỗi để người dùng nhìn thấy và thử lại; không để một sheet rỗng che thông báo phía sau.

### Quản lý và quản trị

- **Dashboard:** doanh thu là vùng chính, ba chỉ số phụ gọn ở cạnh; sáu lối tắt đi tới
  công việc hiện có. Bảng đơn, sơ đồ bàn và tổng hợp bếp vẫn dùng dữ liệu API.
- **Báo cáo:** thanh Ngày/Tuần/Tháng cạnh ngày và nút CSV; doanh thu chính nằm trước
  biểu đồ và bảng chi tiết.
  Không tạo doanh thu ca, giá vốn/lợi nhuận hoặc số liệu không có API; xuất vẫn là CSV.
- **Nhân viên:** danh bạ căn hàng nhận diện/liên hệ/vai trò/trạng thái; hồ sơ nằm cạnh
  danh sách trên desktop. Từ 1100px trở xuống, chọn Quản lý mở native dialog
  `employeeDetailSheet` chứa chính hồ sơ đó, có nút đóng và trở về danh sách. Thêm/sửa
  vẫn dùng hộp thoại và các kiểm tra tài khoản, vai trò, trạng thái sẵn có.
- **Quản trị danh mục:** rail chọn module ở trái, nội dung module ở phải; mobile chuyển
  bố cục để không ép bảng/form. **Vai trò** giữ đúng khả năng API hiện có, không dựng chức
  năng sửa ma trận quyền mới.
- **Tài khoản:** rail danh tính bên cạnh hai form hồ sơ/đổi mật khẩu xếp dọc. Đăng nhập/Setup
  dùng cùng bộ chữ và hệ điều khiển. Không thêm đăng nhập nhanh hoặc lưu mật khẩu mẫu.

## Đông cần sửa file nào khi tiếp tục

| Phần | CSS trình bày | HTML / JavaScript hiển thị |
|---|---|---|
| Font, shell, icon/menu, POS/Chọn bàn/Tiện ích | `css/pages/service-workspace.css` | `pages/tables.html`, `order.html`, `order-tools.html`; `js/common/navigation.js`, `js/pages/tables.js`, `order.js`, `order-tools.js` |
| Bếp và hàng chờ | `css/pages/kitchen-sahara.css`, `kitchen-queue.css` | `pages/kitchen.html`, `js/pages/kitchen.js` |
| Phiếu in Bếp | `css/pages/kitchen-printing.css` | `js/common/kitchen-printing.js`, `kitchen-print-controller.js`; HTML/JS màn Bếp |
| Giờ gọi / chờ nhận / đang làm | `css/pages/preparation-timing.css` | `js/common/preparation-timing.js`; JS POS/Bếp và các test tương ứng |
| Thu ngân, hóa đơn, tab/ledger | `css/pages/payment-sahara.css` | `pages/payment.html`, `js/pages/payment.js` |
| Register/rail/sheet của kho và catalog | `css/pages/warehouse-sahara.css` | `pages/foods.html`, `inventory.html`, `purchases.html`, `recipes.html`; bốn JS cùng tên tại `js/pages/` |
| Dashboard/Báo cáo/Nhân viên/Quản trị/Vai trò/Tài khoản | `css/pages/management-sahara.css` | `index.html`; các HTML tương ứng và `js/pages/dashboard.js`, `reports.js`, `employees.js`, `administration.js`, `roles.js`, `account.js` |
| Đăng nhập/Setup | `css/pages/auth-sahara.css` | `pages/login.html`, `pages/setup.html` |
| Font local và giấy phép | `assets/fonts/` | Hai file TTF và hai file OFL đã nêu |

Thứ tự CSS: base/layout/components → CSS cũ của module → `service-workspace.css`
→ stylesheet Sahara của module. Giữ class body cũ và ID/hook khi chỉnh HTML.
Session nghiệp vụ/DTO/route không phải nơi sửa bố cục này.

## Phiên bản chống cache hiện tại

- Navbar: `navigation.js?v=20261112-service-nav10`.
- HTML nghiệp vụ dùng `ui=20261112-print1`; bộ CSS bố cục vẫn có version
  `20261112-stitch-layout3`. Helper/CSS đồng hồ dùng `20261112-queue1`, JS Phục vụ/Bếp dùng `20261112-print1`;
  CSS/renderer in dùng `20261112-print4`, controller in dùng `20261112-print2`.
  riêng `kitchen-queue.css` dùng `20261112-queue2` để sửa ancestor overflow cho thanh nhắc sticky.
  Riêng `recipes.js` và `purchases.js` dùng
  `20261112-stitch-layout3-fix1` cho sửa xử lý lỗi GET chi tiết.
- Giữ query có sẵn như `tab`, tableId/orderId và bộ lọc chứng từ; không tự thêm bàn/đơn.
  Tab đã mở với HTML cũ có thể cần Ctrl+Shift+R một lần. Không sửa BAT/server để chống cache.

## Kiểm tra đã thực sự chạy

### Tự động và kiểm tra source

- Đã chạy `node --test frontend/tests/*.test.cjs`: **332/332 đạt** cho source hiện tại,
  gồm 16 test đồng hồ, 16 test giờ gọi/hàng chờ và 19 test in phiếu sau lượt cấu trúc 281 test.
  Bao gồm session nghiệp vụ hiện có và test UI cho phân quyền/menu, lỗi GET, tab,
  chọn dòng chỉ đọc, sheet, lỗi chi tiết và khôi phục focus. Dữ liệu/API giả trong test
  không phải bằng chứng đã ghi thành công lên database thật.
- Kiểm tra **17 HTML**: giữ toàn bộ **577 ID cũ** từ HEAD, không trùng ID;
  **230 tham chiếu local href/src** tồn tại.
- **27 JS/CJS sửa hoặc mới** đạt `node --check`; có **9 CSS sửa hoặc mới**,
  hai font local đều tồn tại. `git diff --check` toàn repository đạt.

### Trình duyệt website thật

- Đã kiểm tra **48 cấu hình**: 12 trang **tables, payment, inventory, purchases, recipes,
  foods, reports, employees, roles, administration, account, index** × bốn viewport
  **320 / 390 / 768 / 1440px**. Không quan sát thấy tràn ngang toàn trang hoặc lỗi ở các
  lượt đo này. Đây là viewport trình duyệt; chưa phải thử trên thiết bị điện thoại thật.
- Đã chọn nguyên liệu, mở form điều chỉnh tồn rồi Hủy; mở/đóng hộp thoại Nhân viên ở
  390px; xem định lượng công thức rồi Escape để đóng sheet. Phiếu nhập thật hiện rỗng;
  nhánh GET chi tiết phiếu được kiểm bằng test độc lập. Không gửi lệnh lưu, điều chỉnh
  hoặc thay đổi tài khoản/quyền.

### Fixture độc lập

- Fixture cổng **5508** có banner dữ liệu giả, chặn request ghi và không proxy tới database.
  Lượt cấu trúc mới kiểm tra **12 cấu hình** POS/Bếp/Thu ngân × 320/390/768/1440px,
  không phát hiện tràn ngang; kết quả ở `fixture-matrix.json`.
  Đã chọn/thêm một món vào giỏ tạm, mở rồi xóa giỏ; ở 390px nút Gửi bếp cao **50px**
  trong vùng nhìn thấy. Chưa bấm gửi bếp.
- Thu ngân: mở xác nhận thanh toán rồi Quay lại; chuyển tab Phiếu thu và dùng ArrowLeft
  quay lại Thanh toán. Không xác nhận thu hoặc hoàn tiền.
- Các lượt trước đã mở/đóng thao tác Bếp (làm một phần, thiếu/hỏng, hủy, mở lại), đổi MoMo
  Mock, xem trước phiếu và các trạng thái tải/rỗng/lỗi trên fixture. Đó là kiểm tra form/
  phản hồi giao diện, không phải nghiệm thu các lệnh ghi của backend trong thiết kế mới.

## Giới hạn chưa được giải quyết

- Chưa nghiệm thu end-to-end có ghi dữ liệu thật: đặt/gửi/nhận món, hoàn thành một phần,
  hủy/mở lại, chuyển bàn, thanh toán/hoàn tiền, điều chỉnh hoặc nhập kho.
- Chưa kiểm chứng provider MoMo thật, máy in vật lý, thiết bị điện thoại thật, hiệu năng
  giờ cao điểm hoặc toàn bộ nhánh nghiệp vụ. Không khẳng định website đã sẵn sàng vận hành
  chỉ từ các lượt kiểm tra bố cục/tự động này.
- Các hạn chế backend đã ghi trong `docsforfe`, gồm việc khóa/thu quyền/đổi mật khẩu chưa
  vô hiệu hóa ngay JWT cũ trên thiết bị khác, không được coi là đã sửa bởi lượt frontend này.
  Nếu API/SQL lỗi, Hải cần đối chiếu log và hợp đồng dữ liệu; UI không thay schema database.
- Các danh mục/món đã ngừng dùng vẫn giữ trạng thái trong database; không bật lại để làm
  giao diện đầy dữ liệu. Giá, số lượng và trạng thái tiếp tục lấy từ API.
- Chưa commit/push. Prototype `docs/design/pilot-f1-01.html`, `figma-plugin/` và file local
  `BACKEND_CHANGES_FOR_FRONTEND.md` không nằm trong phạm vi bàn giao frontend này.

## Bằng chứng và báo cáo

- Ảnh/log/JSON của lượt cấu trúc mới đặt **ngoài repository**:
  `D:\Hoc_Tap\quanliduan1111\_codex_runtime\stitch-layout-20261112\`.
- Báo cáo so sánh trước/sau:
  `D:\Hoc_Tap\quanliduan1111\hkdn\Restaurant-UXUI-Audit\reports\stitch-structural-redesign-2026-11-12.md`.
- Bằng chứng pilot và lượt mở rộng trước vẫn ở `_codex_runtime/service-sahara-20261111/`
  và `_codex_runtime/sahara-all-20261111/`; không dùng số liệu/ảnh cũ để thay cho kết quả
  của lượt cấu trúc mới ghi ở trên.

## Bổ sung thời gian chế biến — 12/11/2026

- **Đông / frontend:** POS Phục vụ hiện `Đang làm · MM:SS` từ audit nhận đúng dòng món;
  đổi số lượng/báo xong một phần không reset, reopen dùng lần bắt đầu mới. Tick một giây
  chỉ sửa text DOM, trạng thái vẫn poll năm giây. Lỗi lịch sử có thông báo riêng và
  không khóa đặt thêm; dữ liệu thiếu không bị giả 00:00. Reload không tạo mốc mới.
- **Bếp Chef:** hiện `Phiếu đã bắt đầu · MM:SS` theo `ticket.startedAt`, chỉ khi còn món
  Preparing. Đây là cấp phiếu, **chưa phải thời gian riêng từng món cho Chef**. Bếp không
  gọi item-history vì policy không cho phép. Helper đã sẵn sàng đọc field từng món
  `preparationStartedAt` khi Hải bổ sung vào DTO.
- **Hải / backend, FE-C29 chưa thực hiện:** cần field UTC nullable ở `KitchenItemResponse`
  và `OrderItemDetailResponse`, giữ đúng mốc partial/reopen. Đọc file, hợp đồng và checklist
  cụ thể tại [preparation-timing-handoff.md](../docsforfe/preparation-timing-handoff.md).
- Kiểm tra mới: 297/297 test tổng hợp; 23 JS/CJS đạt cú pháp, diff check đạt; 17 HTML
  giữ đủ 577 ID từ HEAD, không trùng, 226 tham chiếu local tồn tại. Fixture hai vai trò
  × 320/390/768/1440px có nhãn đồng hồ đúng phạm vi, không tràn ngang; reload POS giữ mốc.
  Các nhánh partial/reopen được kiểm bằng API giả; không bấm nhận/làm xong/reopen thật.
- Bằng chứng: `_codex_runtime/preparation-timing-20261112/` ngoài repository. Giữ nguyên
  các giới hạn kiểm thử ghi, provider và thiết bị thật đã nêu; chưa commit/push.
- Đã đối chiếu thêm website thật bằng phiên Admin hiện có: phiếu #20/#21 và đơn #14
  có món Preparing do dữ liệu đang vận hành sẵn. Đồng hồ phiếu và món hiển thị được;
  ảnh `kitchen-live.png` / `pos-live.png`. Chỉ đọc, không tạo/nhận/làm xong món để thử.

## Bổ sung hàng đợi, giờ gọi và chống bỏ sót — 12/11/2026

- **Đông / frontend — Bếp:** phân làn theo trạng thái từng dòng. Một phiếu đang làm vẫn
  có món Pending ở Chờ nhận; mỗi dòng xuất hiện đúng làn, không sao chép toàn bộ món
  vào mọi thẻ. Tổng phiếu không trùng; các làn phân biệt số dòng món với số phần.
- Thông báo Pending toàn cục xét mọi đơn chưa kết thúc, kể cả khi đang lọc hoặc ở làn
  khác. Có số món/phần chưa nhận, mốc phiếu cũ nhất hợp lệ và nút xóa bộ lọc/mở Chờ nhận.
  Nhận một món trên mobile vẫn giữ hàng Chờ nhận để nhân viên nhìn thấy phần còn lại.
- Mặc định **phiếu gửi trước lên trước** theo `sentAt`; timestamp thiếu xuống cuối,
  mã phiếu làm tie-break. Có chế độ **Ưu tiên trước**: `priority > 0` thành nhóm trước,
  FIFO trong mỗi nhóm; không tự đặt thứ bậc cho các số ưu tiên chưa được contract chốt.
- Thẻ Bếp ghi **Gửi phiếu lúc / Phiếu gửi cách đây**, để thấy cả khoảng nằm chờ nhận.
  Đây là mốc cấp phiếu; thời gian bắt đầu nấu có ý nghĩa riêng, không phải ETA món xong.
- **Đông / frontend — POS:** ưu tiên field `orderedAt` khi có; hiện dùng audit tạo dòng
  `oldStatus` rỗng → Pending đầu tiên đúng `orderItemId`. Không suy giờ gọi từ tên, group,
  giờ đơn, giờ cập nhật hoặc câu `reason`. Không đủ mốc thì báo thiếu thay vì tự tạo thời gian.
- **Giới hạn:** tăng số lượng món đã nấu có thể tạo Pending mới trong **phiếu cũ**, nên
  `sentAt` không phải giờ gọi riêng dòng mới. Dòng Pending gộp tăng cũng có thể chứa các
  phần được gọi ở nhiều thời điểm. FIFO hiện tại ở Bếp là **theo phiếu**, chưa bảo đảm
  thứ tự gọi riêng mọi dòng/phần khi backend chưa cung cấp dữ liệu đó.
- **Hải / backend, mở rộng FE-C29 chưa thực hiện:** bổ sung `orderedAt` UTC nullable ở
  hai DTO Bếp/POS, cùng `preparationStartedAt` đã đề xuất. Phân biệt lượt gọi mới với clone
  do partial/cancel; Pending mới gọi tăng trong phiếu cũ có giờ mới; reopen giữ giờ gọi,
  đổi mốc bắt đầu nấu. Nếu cần FIFO từng phần đang gộp phải có metadata/lô riêng.
- File/luồng cụ thể, bảng semantics và checklist cho Hải/Đông ở
  [preparation-timing-handoff.md, mục 7](../docsforfe/preparation-timing-handoff.md#7-bổ-sung-giờ-gọi-món-thời-gian-chờ-nhận-và-fifo--12112026).
  Phần Bếp/HTML/CSS/JS do Đông sửa; DTO/query/entity/migration/quyền server do Hải phụ trách.
- Mốc **297 test** phía trên thuộc lượt đồng hồ trước; sau giờ gọi/FIFO hiện **313/313 đạt**.
  23 JS/CJS đạt cú pháp, 17 HTML giữ đủ 577 ID từ HEAD và 227 tài nguyên local tồn tại.
  Fixture POS/Bếp × 320/390/768/1440px không tràn ngang; thanh nhắc vẫn ở top 8px khi
  cuộn xuống trên 390px. Lọc không kết quả vẫn nhắc món chưa nhận; giữ focus tìm kiếm
  không ngăn cập nhật phiếu mới. Chỉ hộp thoại/yêu cầu đang xử lý được bảo vệ khỏi poll.
- Website thật chỉ đọc đã xác nhận Lẩu Pending của phiếu #20 hiện riêng ở Chờ nhận;
  Tôm Preparing cùng phiếu ở Đang chế biến. POS đơn #14 có giờ gọi 09:03:42 và 12:45:28
  riêng theo từng lượt. Ảnh/log ở `_codex_runtime/kitchen-queue-20261112/` ngoài repo.
  Không sửa backend/database hoặc tự commit/push.

## Bổ sung in phiếu Bếp thủ công — 12/11/2026

- **Đông / frontend đã làm:** Bếp → In phiếu bếp → xem trước toàn phiếu, chọn món còn
  xử lý hoặc toàn bộ, khổ 58/80 mm và nhãn in lại → Mở hộp thoại in. Không nhận món
  hoặc thay đổi trạng thái nghiệp vụ khi xem/in; dữ liệu được đọc lại trước khi gọi in.
- Đã kiểm tra website thật bằng preview phiếu #20/bàn K10; không phát lệnh in giấy thật.
  Bộ kiểm tra hiện tại **329/329 đạt**, 27 JS/CJS đạt cú pháp; kiểm tra hai khổ trên
  320/390/768/1440px không tràn ngang. Các số 313/227 ở mục FIFO là kết quả lượt trước.
- **Hải / backend và trạm in chưa làm:** tự in sau POS gửi món, job/batch/outbox,
  receipt và xử lý offline/retry. Chưa nghiệm thu máy in/driver hoặc xác nhận giấy đã ra.
- Bàn giao chi tiết: [kitchen-printing-handoff.md](../docsforfe/kitchen-printing-handoff.md).
  Ảnh/log ngoài repo: `D:/Hoc_Tap/quanliduan1111/_codex_runtime/kitchen-printing-20261112/`.

## Điều chỉnh phiếu Bếp: mỗi món một tờ — 12/11/2026

- Theo yêu cầu mới, mỗi dòng món trong phiếu nguồn thành một phiếu giấy riêng, có
  số phần + tên món, cỡ, bàn, mã phiếu Bếp, mã đơn, mã dòng món và giờ đặt riêng.
  Hai phần của một dòng vẫn là một tờ ghi hai phần; hai dòng trùng tên không bị gộp.
- Giờ đặt dùng `preparation-timing.orderedForItem(item, [])` từ `orderedAt`/`placedAt`
  hợp lệ. Thiếu mốc thì ghi **Chưa có dữ liệu**; giờ gửi phiếu được ghi riêng, không
  thay cho giờ khách gọi. Chef không gọi thêm lịch sử POS trái quyền.
- Preview xếp các phiếu riêng cùng số tờ của lần xem; phần đếm tổng chỉ hiện màn hình.
  CSS ngắt trang giữa các phiếu khi in. Driver/máy in quyết định cắt giấy, không có
  cam kết mỗi tờ đã ra hoặc đã được máy cắt. Topping, ghi chú, trạng thái và nhãn in lại
  vẫn nằm đúng từng món; xem/in không đổi trạng thái hoặc tồn kho.
- **Đông:** renderer/CSS/nhãn và kiểm tra frontend. **Hải:** `orderedAt` đúng lượt gọi
  ở DTO (FE-C29), snapshot từng tờ/job/receipt/retry và máy trạm (FE-C30).
  Chi tiết file và tiêu chí ở [bàn giao in Bếp, mục 3 và 8](../docsforfe/kitchen-printing-handoff.md).
- Đã chạy lại toàn bộ **332/332 test đạt**; 5 JS/CJS của lượt tách tờ đạt cú pháp,
  `git diff --check` đạt. Website thật phiếu #20 có hai tờ riêng Tôm/Lẩu, mỗi tờ một
  dòng món và đủ mã #20 / đơn #15 / dòng #24 hoặc #25. Giờ đặt còn thiếu ở API thật.
- Fixture chỉ đọc kiểm tra hai món có giờ đặt 12:02:02 và 12:07:02 riêng; hai khổ
  × 320/390/768/1440px không tràn ngang, nút in 44px còn trong viewport. Không bấm
  in máy thật hoặc ghi dữ liệu nghiệp vụ. Ảnh/log mới ngoài repo:
  `D:/Hoc_Tap/quanliduan1111/_codex_runtime/kitchen-dish-slips-20261112/`.
  CSS/renderer/controller in dùng `20261112-print2`; ảnh 329 test trước đó là lịch sử.

- Theo yêu cầu rút gọn tiếp: renderer `20261112-print3` bỏ CHỜ NHẬN / ĐANG CHẾ BIẾN
  và khung MÓN CẦN LÀM trên tờ mặc định; không thay trạng thái trên bảng Bếp. Cảnh báo
  món xong/hủy khi đối chiếu vẫn giữ. 25/25 kiểm tra renderer/màn Bếp hiện có đạt,
  đã xác minh preview thật. Chi tiết và ảnh mới ở bàn giao in Bếp mục 9.

- Theo yêu cầu dùng giờ gửi: bản giấy `20261112-print4` hiển thị **Gửi phiếu lúc**
  ngay dưới cỡ món, lấy `ticket.sentAt` như thẻ Bếp. Bỏ Giờ đặt món và dòng giờ gửi
  lặp ở phần mã phiếu; mỗi tờ cùng phiếu nguồn có chung mốc gửi. Không sửa backend;
  FE-C29 vẫn cần Hải cho giờ gọi/FIFO chi tiết, không chặn dòng giờ gửi trên giấy.
