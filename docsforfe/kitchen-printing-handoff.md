# FE-C30 — In phiếu Bếp: bàn giao Đông / Hải

Ngày 12/11/2026. Phạm vi lượt này là **frontend in thủ công qua hộp thoại in của trình
duyệt**, trên màn Bếp của Chef/Admin. Kết quả test cuối và bằng chứng thiết bị được cập
nhật sau khi kiểm tra; tài liệu này không tự xác nhận máy in đã nhả giấy thành công.

**Bản chốt theo yêu cầu mới:** mỗi dòng món một tờ riêng; thời gian trên giấy là
**Gửi phiếu lúc** lấy từ `ticket.sentAt`. Không in nhãn Chờ nhận/Đang chế biến và
không còn mục Giờ đặt món. Mục 9–10 thay thế các mô tả bản giấy trước đó bên dưới.

**Yêu cầu cập nhật: mỗi dòng món là một phiếu giấy riêng**, có số phần, tên món, cỡ,
giờ đặt món, bàn, mã phiếu Bếp, mã đơn và mã dòng món. Chi tiết nguồn giờ và cách tách
giấy ở mục 3; kết quả 329 test/mục 7 là mốc trước yêu cầu tách này, không thay cho lần
kiểm tra bổ sung đang thực hiện.

## 1. Phân công và khả năng hiện tại

| Phần | Người phụ trách | Trạng thái / phạm vi |
|---|---|---|
| Nút In phiếu, xem trước, khổ nội dung và nhãn In lại | **Đông / frontend** | Triển khai trên màn Bếp, dùng dữ liệu GET thật; không đổi trạng thái món |
| In thủ công từ hộp thoại trình duyệt tới máy đã cài driver | **Nhân viên tại máy** | Chọn máy, khổ giấy, số bản, in hoặc hủy trong hộp thoại hệ điều hành/trình duyệt |
| Tự in ngay khi POS gửi món; tự chọn máy Bếp; theo dõi job/receipt/retry | **Hải / backend và dịch vụ máy in** | **Chưa có trong lượt này**, cần hợp đồng server và thành phần kết nối máy in |
| Ghi nhận đã in thành công trên mọi thiết bị | **Hải / backend + thiết bị** | Chưa có dữ liệu xác nhận; frontend không tự đặt cờ thành công |

`window.print()` yêu cầu trình duyệt mở quy trình in của người dùng. Nó không tự chọn
máy in Bếp và không cung cấp receipt khẳng định giấy đã ra. Người dùng có thể chọn Lưu
PDF, hủy hoặc gặp lỗi máy in sau khi hộp thoại đóng. Event `afterprint` cũng không phân
biệt các kết quả đó. Vì vậy không được đổi nhãn thành “Đã in thành công” hoặc đánh dấu
món đã nhận/chế biến chỉ vì hàm in đã được gọi.

## 2. Luồng Đông làm trong frontend

1. Chef/Admin bấm **In phiếu** ở thẻ Bếp. Một phiếu có thể xuất hiện ở nhiều làn do món
   có trạng thái khác nhau; mọi nút trên các làn vẫn chỉ cùng một `kitchenOrderId`.
2. Đọc lại dữ liệu phiếu, dòng Bếp và chi tiết đơn bằng các GET được phép, rồi dựng
   snapshot **toàn bộ phiếu nguồn**, sau đó tách mỗi dòng được chọn thành một phiếu món.
   Không sao chép riêng `row.items` của làn đang bấm hoặc HTML đang lọc,
   vì làm vậy sẽ mất các món thuộc làn khác trong cùng phiếu.
3. Mở native dialog `kitchenPrintDialog` với snapshot vừa tải. Chọn phạm vi **món còn
   xử lý** (Pending + Preparing) hoặc toàn bộ phiếu để đối chiếu, chọn khổ 80/58mm và đánh
   dấu **In lại** khi chủ động in lại. Bản xem trước xếp các phiếu món riêng liên tiếp;
   mỗi tờ lặp bàn, mã phiếu/đơn, mã dòng món, giờ đặt và giờ gửi phiếu, trạng thái,
   số phần, kích cỡ, topping, ghi chú cần chế biến từ API.
4. Nội dung xem trước được giữ cố định trong lúc người dùng kiểm tra. Poll Bếp không
   được âm thầm thay món/số lượng trong dialog đang mở. Đây là bản đối chiếu tại một mốc,
   không phải cam kết dữ liệu server sẽ không đổi trong lúc nhân viên xem giấy.
5. Khi người dùng bấm In, đọc lại nguồn một lần nữa. Nếu dữ liệu khác snapshot, cập nhật
   bản xem trước và yêu cầu người dùng kiểm tra, bấm In lần nữa; không tự in nội dung mới
   khi người dùng vừa xác nhận một nội dung cũ. GET lỗi hoặc yêu cầu nghiệp vụ chưa đối
   chiếu xong phải được báo rõ và chặn in bản được coi là đã xác minh.
6. Chỉ thao tác bấm In của người dùng mới gọi `window.print()`. Không tự gọi sau poll,
   sau khi mở preview, nhận phiếu mới, đổi khổ, đóng dialog hoặc đăng nhập.
7. Khi đóng preview, đổi phiên/quyền hoặc rời trang, xóa snapshot và vùng in cũ. Ctrl+P
   sau khi đã đóng dialog không được vô tình in lại tờ snapshot vừa xem. Không giữ một
   bản in cũ nằm ẩn nhưng vẫn xuất hiện trong `@media print`.

Fingerprint dùng để đối chiếu phải dựa vào dữ liệu nguồn ổn định: phiếu, đơn, dòng món,
số lượng, trạng thái, ghi chú/topping và phạm vi cần in. Không đưa `Date.now()`, “In lúc”
hoặc số giây đồng hồ đang chạy vào fingerprint, vì chúng đổi ở mỗi lần kiểm tra.

Các GET này không phải giao dịch khóa bản in. Vẫn có thể phát sinh thay đổi ngay sau
GET cuối và trước khi giấy ra; bảo đảm nhất quán bền vững cần print-job snapshot/version
ở server như mục 5. Frontend không tự thay trạng thái để cố ngăn thay đổi đó.

## 3. Phạm vi bản giấy và nguy cơ làm trùng món

### Mỗi dòng món là một phiếu độc lập

- Mỗi phần tử của `ticket.items` lọt qua bộ lọc phạm vi tạo một `article.kitchen-slip`.
  Trên giấy, mỗi article bắt đầu một tờ riêng, ngắt trang sau mỗi tờ trừ tờ cuối;
  phần đếm tổng của lần xem trước chỉ hiện trên màn hình, không thành tờ tổng hợp.
- Ví dụ một dòng `1 phần Lẩu Thái hải sản`, cỡ `Lớn`, và một dòng Tôm tạo hai phiếu.
  Dòng `2 phần Lẩu Thái hải sản` vẫn tạo **một phiếu ghi 2 phần**, không tự nhân thành
  hai phiếu. Không gộp hai dòng trùng tên/cỡ vì ghi chú, trạng thái hoặc lượt gọi có thể khác.
- Mỗi tờ tự đủ thông tin để mang tới vị trí nấu: bàn, `kitchenOrderId`, `orderId`,
  `orderItemId ?? id`, số phần, tên món, cỡ, trạng thái, topping và ghi chú món/đơn.
  Chỉ lặp thông tin đơn cần thiết; không đưa tên hoặc topping của món khác vào cùng tờ.
- Mã dòng là định danh từ API. Chỉ số tờ X/Y là vị trí trong lần xem/in này, **không**
  phải mã print-job, số lần đã in hoặc số thứ tự mới được lưu vào database.
- Một lần mở hộp thoại in có thể chứa nhiều tờ riêng. CSS ngắt trang không bảo đảm
  máy in cuộn sẽ tự cắt từng tờ: chức năng cắt, khổ và chiều dài giấy do driver/thiết bị
  quyết định. Chưa xác nhận thao tác cắt giấy hoặc giấy thực tế trong lượt frontend.

### Giờ đặt món khác giờ gửi phiếu

| Trường trên từng tờ | Nguồn frontend đọc | Khi thiếu / giới hạn |
|---|---|---|
| Giờ đặt món | `restaurantPreparationTiming.orderedForItem(item, [])`: ưu tiên `item.orderedAt`, rồi `item.placedAt` hợp lệ | Hiện **Chưa có dữ liệu**; không thay bằng `ticket.sentAt`, `order.createdAt`, giờ in hoặc `createdAt` của dòng clone |
| Giờ gửi phiếu | `ticket.sentAt` | Nhãn riêng **Giờ gửi phiếu**; không gọi đó là giờ đặt riêng của từng món |
| Lập bản in lúc | Giờ trên thiết bị tại lần dựng snapshot | Chỉ phục vụ đối chiếu, không phải thời điểm khách gọi món hoặc receipt đã ra giấy |

Tất cả mốc hợp lệ được trình bày theo giờ Việt Nam. Không gọi thêm route lịch sử POS
bằng Chef để lấp chỗ trống: Chef không có policy `PosOrCashier` cho item-history.
Hải cần bổ sung `orderedAt` đúng nghĩa trong `KitchenItemResponse` và
`OrderItemDetailResponse` theo **FE-C29**. File cụ thể là
`RestaurantFoodManagerSystem/Application/DTO/Responses/Management/KitchenItemResponse.cs`
và `RestaurantFoodManagerSystem/Application/DTO/Responses/Order/OrderDetailResponse.cs`;
nguồn projection/mutation và điều kiện giữ/đổi mốc xem [bàn giao giờ món](preparation-timing-handoff.md).
Nếu một dòng đã gộp phần từ nhiều lượt gọi, một timestamp không diễn tả được giờ đặt
của từng phần: Hải cần metadata batch/delta nếu muốn phiếu chỉ cho lượt mới.

### Phạm vi còn xử lý và in lại

**Món còn xử lý** là snapshot tất cả dòng Pending/Preparing đang có trong phiếu. Nó
không đồng nghĩa “chỉ các món vừa gọi thêm”. Bản giấy cần ghi rõ phạm vi và trạng thái
từng dòng để người Bếp đối chiếu với màn hình, tránh hiểu rằng mọi phần trên giấy đều
là lệnh làm thêm mới.

Ví dụ, một dòng Pending đang có 3 phần được cộng thêm 2 phần thành 5. API hiện tại có
thể chỉ trả tổng 5 ở cùng dòng; frontend không được đoán 2 phần vừa thêm bằng cách so
với localStorage của một máy. Tương tự, phần gọi tăng có thể tạo Pending mới trong
phiếu cũ; `kitchenOrderId` một mình không xác định được duy nhất lượt gọi mới.

- Mặc định món còn xử lý phải bỏ phần Completed/Ready/Cancelled khỏi nội dung cần làm,
  nhưng khi chọn toàn bộ để đối chiếu thì các trạng thái cũ phải được ghi rõ.
- Nhãn **In lại** do người dùng chọn là thông tin trên tờ giấy; chưa có server print-log
  để tự biết phiếu đó đã in ở máy khác hay chưa. Nó chưa phải cơ chế chống in trùng.
- Không tự gộp dòng khác trạng thái, khác ghi chú/topping hoặc khác lượt gọi chỉ vì cùng
  tên món. Không dùng tên/size làm định danh job hoặc khóa chống trùng.
- Không tự cộng, trừ tồn hoặc đánh dấu đã nhận món sau khi in. Nút Nhận món/Báo đã làm
  vẫn là thao tác nghiệp vụ riêng được backend kiểm tra.

## 4. Khổ giấy, quyền và file frontend

CSS bố trí vùng nội dung **72mm cho giấy 80mm**, **50mm cho giấy 58mm**, lề trang 4mm.
Đây là kích thước trình bày; người vận hành vẫn phải chọn đúng paper/driver/scale ở hộp
thoại in. CSS không âm thầm chuyển máy in hoặc thay cấu hình thiết bị. Cần kiểm tra tên
món/ghi chú tiếng Việt dài, xuống dòng, số phần và ngắt trang trên đúng thiết bị thực.

Hiện chỉ **Chef/Admin** ở màn Bếp được mở luồng in này. Các dữ liệu dùng `KitchenDesk`
và GET chi tiết đơn `PosKitchenRead`. Waiter có quyền gửi món ở POS nhưng không có quyền
đọc toàn bộ dữ liệu KitchenDesk; không gọi endpoint Bếp bằng Waiter rồi tìm cách né 403.
Không đổi JWT, cấp role mới hoặc mở policy chỉ để nút in có dữ liệu.

| File Đông phụ trách | Nội dung |
|---|---|
| `frontend/js/common/kitchen-printing.js` | Dựng dữ liệu/nội dung bản giấy từ snapshot, phạm vi món, nhãn in lại và escape chuỗi |
| `frontend/js/common/kitchen-print-controller.js` | Điều khiển DOM preview, GET mới, fingerprint/snapshot đổi, xác nhận in và vòng đời dialog/phiên |
| `frontend/js/pages/kitchen.js` | Nối nút thẻ phiếu với controller; giữ guard quyền/pending và dừng poll khi preview đang mở |
| `frontend/pages/kitchen.html` | Nút/điều khiển in, native `kitchenPrintDialog`, vùng in độc lập `kitchenPrintPaper` ở body |
| `frontend/css/pages/kitchen-printing.css` | Trình bày từng phiếu món trong preview, 58/80mm và ngắt trang giữa các phiếu; `@media print` chỉ in nội dung giấy được xác minh |
| `frontend/tests/kitchen-printing.test.cjs` | Kiểm tra helper, dữ liệu/trạng thái/phạm vi, nội dung dài và escape |
| `frontend/tests/kitchen-print-controller.test.cjs` | Kiểm tra GET, dialog, snapshot đổi/lỗi, role, hành động in do người dùng và vòng đời preview |
| `frontend/tests/kitchen-view.test.cjs` | Kiểm tra cầu nối nút in/guard poll ở màn Bếp và giữ nguyên các hành động nghiệp vụ |

Luồng Thu ngân hiện có tại `frontend/js/pages/payment.js` dùng preview rồi
`window.print()`; `docsforfe/f3-cashier-progress.md` cũng chỉ ghi in/lưu PDF qua trình
duyệt. Không xem phần đó là bằng chứng hệ thống đã có dịch vụ in Bếp tự động.

## 5. Hải cần gì nếu muốn tự in tới máy Bếp — đề xuất chưa thực hiện

Muốn vừa gửi món ở POS là máy Bếp tự nhả đúng lượt mới, Hải cần chốt **print-job bền
vững và thành phần kết nối máy in**. Không chỉ thêm một lệnh `window.print()` trong
callback POS, vì máy/trình duyệt của Phục vụ có thể không nối với máy in Bếp và người
dùng vẫn có thể phải chọn máy trong hộp thoại.

### Payload và định danh lượt in

Server cần trả/lưu snapshot chỉ của **lượt gửi mới hoặc phần tăng mới**, gồm:

- `printJobId`, `kitchenOrderId`, `orderId`, bàn/khu vực đích và máy/trạm Bếp cần nhận;
- `batchId` hoặc revision của lượt gọi, các `orderItemId`/dòng Bếp, số phần **của lượt đó**,
  size, topping, ghi chú, thời điểm server nhận lượt và người gửi;
- mỗi dòng có snapshot phiếu riêng với `orderedAt` của lượt, bàn, phiếu/đơn và định
  danh dòng; job chứa nhiều tờ phải phân biệt receipt/retry của từng tờ để không in
  lại cả batch khi chỉ một món lỗi. Không tự gộp món trùng tên hoặc nhân số tờ theo số phần;
- loại `original`/`reprint`, job gốc và lý do/người yêu cầu in lại khi chính sách yêu cầu;
- trạng thái job, phiên bản snapshot, khóa chống trùng và receipt/lỗi mà dịch vụ in
  thực sự xác nhận được. Nội dung đã tạo job cần bất biến để retry không in một snapshot
  khác cùng mã job.

`kitchenOrderId` không đủ làm khóa chống trùng vì một phiếu có thể nhận thêm Pending
mới hoặc tăng số lượng. Khóa cần gắn lượt/batch/revision và loại in. Nếu lệnh gửi món
đã có Idempotency-Key, retry cùng lệnh phải trả lại cùng kết quả/lượt in, không tạo job
mới. Lượt in lại có định danh riêng, tham chiếu original và được ghi audit rõ.

Nên tạo job/outbox trong cùng transaction với lượt gọi đã được server chấp nhận để
tránh món lưu thành công nhưng job mất, hoặc lệnh bị rollback mà giấy vẫn ra. In lỗi
không được tự gửi lại đơn, trừ kho lần nữa hoặc đánh dấu món chưa tạo thành đã tạo.

### Quyền và luồng POS

- Nếu POS Waiter được yêu cầu in lượt vừa gửi, server phải xác minh actor, đơn, lượt gọi
  vừa được chấp nhận và phạm vi được in. Chỉ cung cấp payload/job tương ứng với quyền đó;
  không mở toàn bộ KitchenDesk cho Waiter.
- Chef/Admin tra cứu hoặc yêu cầu in lại theo chính sách được chốt riêng. Không suy rằng
  quyền đọc phiếu đồng nghĩa quyền phát tùy ý bao nhiêu job in lại cũng được.
- Đường dẫn endpoint, DTO và policy cho create/read/reprint/ack cần Hải chốt trong
  contract. Hiện chưa có endpoint in tự động nào được frontend giả định hoặc tự gọi.
- Thông tin máy/trạm, credential hoặc kết nối dịch vụ in phải thuộc cấu hình triển khai
  phù hợp; không nhúng bí mật dịch vụ vào JavaScript frontend hoặc commit cấu hình máy.

### Trạng thái, retry và receipt

Hải cần phân biệt ít nhất job đã nhận, đang xử lý, đã được spooler/thiết bị chấp nhận,
lỗi và kết quả chưa rõ. “Gửi tới spooler thành công” có thể vẫn chưa chứng minh giấy đã
ra; tên trạng thái và mức xác nhận phải đúng khả năng driver/thiết bị đang dùng.

Khi mất kết nối sau gửi, job có thể đã in. Không tự retry mù rồi khẳng định chỉ có một
bản giấy. Dịch vụ cần khóa/receipt, chiến lược đối chiếu và lựa chọn reprint có audit;
frontend hiển thị đúng “chưa rõ kết quả” khi không có bằng chứng. Không hứa exactly-once
trên giấy nếu giao thức máy in không có xác nhận bền vững tương ứng.

File Hải cần đọc/sửa khi nhận phần tự động, **chưa sửa trong lượt frontend này**:

| Nhóm file | Điểm cần chốt |
|---|---|
| `RestaurantFoodManagerSystem/Application/Services/Order/OrderService.cs` | Tạo đơn/gọi thêm/tăng số lượng: xác định batch và số phần delta, transaction/outbox in cùng lượt được chấp nhận |
| `RestaurantFoodManagerSystem/Application/Common/OrderItemMutations.cs` | Phân biệt clone do partial/hủy với dòng của lượt gọi tăng; giữ định danh lượt như bàn giao FE-C29 |
| `RestaurantFoodManagerSystem/Application/DTO/Responses/Management/` và `.../Responses/Order/` | DTO snapshot/job rõ original/reprint/batch, không buộc Waiter đọc KitchenDesk để lấy dữ liệu |
| `RestaurantFoodManagerSystem/Api/Controllers/Order/OrdersController.cs` và `.../Management/ManagementController.cs` | Chốt route/policy có kiểm tra phạm vi job, không phát quyền rộng cho frontend |
| `RestaurantFoodManagerSystem/Infrastructure/Auth/AuthorizationServiceCollectionExtensions.cs` | Chốt quyền gửi/đọc/in lại/ack job theo đúng vai trò và trạm |
| Entity + `Infrastructure/Persistence/` + migration mới nếu cần | Job, batch, outbox, receipt/idempotency và audit bền vững; Hải phụ trách schema/dữ liệu cũ |
| Dịch vụ/agent kết nối máy in tại trạm Bếp | Chọn thiết bị, queue, driver/protocol, timeout, offline/reconnect và trả mức xác nhận thực có |

## 6. Kiểm thử và giới hạn cần ghi rõ

Kết quả frontend đã chạy được ghi ở mục 7. Các mục dưới đây là checklist toàn luồng,
không phải danh sách đã thành công trên máy in vật lý:

- [ ] In từ bất kỳ làn nào của phiếu hỗn hợp đều đọc đúng toàn phiếu, không mất hoặc lặp món.
- [ ] Hai dòng món tạo hai phiếu độc lập; từng tờ có đủ bàn/mã phiếu/mã đơn/mã dòng,
  đúng món/cỡ/số phần/giờ của dòng đó và không chứa nội dung món khác.
- [ ] Hai phần trong cùng một dòng giữ một phiếu ghi hai phần; hai dòng trùng tên vẫn
  giữ hai phiếu. Bản in ngắt trang giữa phiếu, không có trang tổng hợp hoặc trang trắng cuối.
- [ ] Giờ đặt ưu tiên orderedAt/placedAt hợp lệ; thiếu thì báo Chưa có dữ liệu, không
  lấy giờ gửi chung/giờ in thay thế. Mỗi tờ vẫn có Giờ gửi phiếu với nhãn riêng.
- [ ] Món còn xử lý và toàn bộ có phạm vi/nhãn khác nhau; dòng hủy/xong không bị hiểu là lệnh làm mới.
- [ ] Tên/topping/ghi chú chứa ký tự HTML được escape, tiếng Việt dài xuống dòng ở cả hai khổ.
- [ ] Dữ liệu đổi trước lần bấm In được tải vào preview và phải kiểm tra/bấm lại; GET lỗi không in bản cũ.
- [ ] Đóng/đổi phiên/rời trang xóa snapshot; Ctrl+P không in lại snapshot cũ ngoài preview hợp lệ.
- [ ] Chỉ click In mới gọi `window.print`; mở/đóng preview hoặc poll không tự mở máy in.
- [ ] Hủy hộp thoại in không tạo cờ thành công, không nhận món, đổi số phần hoặc trừ kho.
- [ ] Chef/Admin được dùng; Waiter không gọi KitchenDesk trái policy. POS tự in cần contract mới.
- [ ] Chọn đúng driver, giấy vật lý 58/80mm, scale và lề; kiểm tra ngắt trang, độ rõ, tiếng Việt trên máy thật.
- [ ] Offline/hết giấy/hỏng driver/mất mạng giữa job có kết quả rõ hoặc trạng thái chưa rõ, không retry mù.

**Chưa nghiệm thu máy in vật lý, driver, thiết bị giấy, tự in sau POS hoặc print-job
server trong lượt frontend này.** Không sửa backend/database, không cài driver/package,
không phát lệnh in giấy thật chỉ để chứng minh giao diện và không commit/push.

## 7. Đông / frontend đã kiểm tra ngày 12/11/2026 — trước yêu cầu mỗi món một tờ

- **329/329 test frontend đạt**: phần in bổ sung 7 test renderer, 8 test controller và
  một test tích hợp màn Bếp. Có kiểm tra scope của toàn phiếu hỗn hợp, role, snapshot
  đổi trước khi in, GET lỗi/đến trễ, đóng/đổi phiên/rời trang, nguồn in cũ và nhãn in lại.
  Các API/lệnh nhận món trong test là giả trong bộ nhớ; không ghi database thật.
- **27 JS/CJS** sửa hoặc mới đạt cú pháp; **17 HTML** giữ đủ 577 ID từ HEAD, không trùng;
  **230 tham chiếu local** tồn tại. `git diff --check` đạt. Backend/config/database không sửa.
- Trình duyệt fixture chỉ đọc cổng 5508: hai khổ 80/58 mm × 320/390/768/1440px,
  không tràn ngang toàn trang/phiếu; nút in cao 44px và nằm trong viewport. Đã đổi
  scope, đánh dấu BẢN IN LẠI, xem trạng thái ĐÃ XONG / ĐÃ HỦY — KHÔNG LÀM và đóng xem trước.
- Đã thử gọi `window.print` trên fixture. Trang bị chặn ở bước in; dùng Escape trở về
  màn Bếp và xác minh dialog đóng, preview/paper rỗng, class in bị xóa. **Không xác nhận
  được giao diện hộp thoại in hệ thống hoặc giấy đã ra máy** từ lượt này; không ghi là in thành công.
- Website thật: đăng nhập lại Admin đã được người dùng cung cấp, đọc phiếu #20/bàn K10.
  Xem trước đúng cả Tôm đang chế biến và Lẩu chờ nhận, mỗi món một phần; không nhận,
  làm xong, hủy, gửi món hoặc bấm in giấy thật. Giữ database gốc `RestaurantFoodManager`;
  server khởi động với migrate/seed tắt như cấu hình local sẵn có.
- Phiên bản mở mới: `ui=20261112-print1`, navbar `20261112-service-nav10`;
  helper/controller/CSS in và JS màn Bếp dùng `v=20261112-print1`.
- Bằng chứng ngoài repository:
  `D:/Hoc_Tap/quanliduan1111/_codex_runtime/kitchen-printing-20261112/` có `tests.log`,
  `static.json`, `responsive.json`, `cancel-check.json`, ảnh hai khổ và `preview-live-final.png`.

Đông đã hoàn thành phần tạo/xem trước/gọi in thủ công từ frontend. Hải chưa thực hiện
dịch vụ tự in, print-job/outbox/receipt, đồng bộ đếm bản in hoặc kết nối máy in trạm Bếp.
Phần giấy/driver thật cần nghiệm thu trên thiết bị được chỉ định; không đổi thành công
của test frontend thành xác nhận máy in vật lý đã hoạt động.

## 8. Theo dõi thay đổi mỗi món một phiếu

Yêu cầu bổ sung này chỉ sửa renderer/CSS/nhãn xem trước, kiểm tra frontend và tài liệu.
**Đông phụ trách** tách tờ, lặp thông tin nhận diện, hiển thị mốc giờ đúng nguồn, giữ
scope/trạng thái/ghi chú và kiểm tra lại trên trình duyệt. **Hải phụ trách** dữ liệu
`orderedAt` chuẩn từng lượt (FE-C29) và dịch vụ tự in/job/receipt (FE-C30). Không đổi
schema, cấp quyền, tạo job hoặc sửa backend trong phần frontend này.

- **332/332 test frontend đạt** trên source sau tách tờ: renderer có 10 test, bổ sung
  kiểm tra mỗi tờ chỉ một dòng, thời gian/cỡ/ghi chú không lẫn dòng, hai dòng trùng tên
  không bị gộp và một dòng 3 phần vẫn một tờ. Controller/cầu nối xác nhận nguồn in
  có đúng hai article riêng, vẫn đối chiếu GET và không tự gửi lệnh nghiệp vụ.
- 5 JS/CJS sửa trong lượt này đạt `node --check`; `git diff --check` đạt.
- Website thật chỉ xem trước phiếu #20/bàn K10/đơn #15: Tôm dòng #24 và Lẩu dòng #25
  nằm trên hai tờ, mỗi tờ lặp đủ thông tin nhận diện. API thật chưa có giờ đặt riêng,
  nên mỗi tờ ghi Chưa có dữ liệu; giờ gửi 12:37:12 được ghi ở mục riêng.
- Fixture độc lập, không kết nối database: Lẩu 1 phần/cỡ Lớn/12:02:02 và Tôm 2 phần/
  cỡ Vừa/12:07:02 thành hai tờ, không ba tờ; kiểm tra khổ 58/80mm ở 320/390/768/1440px.
  Cả tám trường hợp không tràn ngang trang/phiếu, nút in cao 44px còn trong viewport.
- Không gọi in giấy hoặc kiểm tra máy cắt vật lý trong lượt tách tờ. Preview viewport
  đã reset; server fixture đã dừng, frontend/API thật giữ chạy. Không sửa backend/DB,
  không commit/push. CSS/renderer/controller in dùng `v=20261112-print2`.
- Ảnh/log mới ngoài repository:
  `D:/Hoc_Tap/quanliduan1111/_codex_runtime/kitchen-dish-slips-20261112/` gồm `tests.log`,
  `responsive.json`, ảnh hai khổ và `preview-live-first.png`, `preview-live-second.png`.
  Ảnh mục 7 thuộc phiếu tổng trước đó; không dùng làm bằng chứng bố cục từng tờ.

## 9. Rút gọn nội dung phiếu theo yêu cầu chủ dự án

- **Đông / frontend:** bỏ chữ CHỜ NHẬN và ĐANG CHẾ BIẾN trên từng tờ; bỏ khung
  MÓN CẦN LÀM ở đầu tờ mặc định. Chế độ toàn bộ chỉ ghi BẢN ĐỐI CHIẾU, vẫn giữ
  cảnh báo ĐÃ XONG / ĐÃ HỦY — KHÔNG LÀM, đơn kết thúc và nhãn in lại nếu có.
- Giữ số phần, tên món, cỡ, giờ đặt, bàn, mã phiếu/đơn/dòng món và ghi chú. Bảng
  Bếp vẫn hiển thị trạng thái, phạm vi lọc và các thao tác nghiệp vụ như trước.
- Đã chạy lại 25/25 kiểm tra renderer/màn Bếp hiện có; cú pháp đạt, không thêm test
  mới. Đã xem trước website thật phiếu #20/bàn K10, xác nhận hai tờ không còn nhãn
  trạng thái đang xử lý. Không in giấy, không ghi DB hoặc sửa backend, không push.
- Renderer dùng `v=20261112-print3`. Ảnh ngoài repo:
  `D:/Hoc_Tap/quanliduan1111/_codex_runtime/kitchen-dish-slips-20261112/preview-without-status.png`.

## 10. Dùng mốc gửi phiếu trên giấy theo yêu cầu mới

- **Đông / frontend:** dòng thời gian dưới cỡ món đổi thành **Gửi phiếu lúc**, lấy
  `ticket.sentAt`, cùng nguồn với thẻ phiếu trên bảng Bếp, quy đổi UTC sang giờ Việt Nam.
  Bỏ mục Giờ đặt món và giờ gửi trùng lặp ở phần mã phiếu. Mỗi tờ món vẫn độc lập;
  những món thuộc cùng phiếu nguồn có cùng mốc gửi phiếu đó.
- Không đổi tên `sentAt` thành giờ khách đặt riêng, không lấy giờ in/giờ máy hoặc mốc
  của món khác. Nếu `sentAt` thiếu/sai thì ghi Chưa có dữ liệu. Bổ sung `orderedAt`
  (FE-C29) vẫn do **Hải** phụ trách để hỗ trợ thời gian/FIFO từng lượt gọi trên hệ thống;
  không còn là điều kiện cần để có dòng thời gian trên bản giấy hiện tại.
- CSS/renderer in dùng `v=20261112-print4`; backend, database, giờ trên bảng Bếp và
  quy trình in thủ công không thay đổi. Các mô tả Giờ đặt món ở mục 3/8/9 là lịch sử.
- Đã chạy 10/10 kiểm tra renderer hiện có và kiểm tra cú pháp đạt; xác nhận giờ gửi
  xuất hiện đúng một lần trên mỗi tờ. Preview thật phiếu #20/bàn K10 có Gửi phiếu lúc
  12:37:12 12/11/2026 trên cả hai tờ Tôm/Lẩu, không còn Giờ đặt món. Không in giấy.
  Ảnh: `D:/Hoc_Tap/quanliduan1111/_codex_runtime/kitchen-dish-slips-20261112/preview-sent-at.png`.
