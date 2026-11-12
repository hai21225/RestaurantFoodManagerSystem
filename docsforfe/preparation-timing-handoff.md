# FE-C29 — Thời gian chế biến: bàn giao Đông / Hải

Ngày 12/11/2026. Mục tiêu là hiển thị **thời gian đã trôi qua từ lúc bắt đầu**, không phải
thời gian dự kiến hoàn thành, đếm ngược hoặc cam kết món sẽ xong trong bao lâu.

**Trạng thái:** Đông đã làm phần frontend với nguồn dữ liệu hiện có. Phần backend
`preparationStartedAt` nêu dưới đây **là đề xuất giao Hải, chưa được thực hiện trong lượt này**.
Không sửa backend, database, migration hoặc quyền server để hoàn thành phần frontend.
Chưa commit/push. Không tạo đơn, nhận món, báo hoàn thành hoặc mở lại món trên database
thật chỉ để kiểm thử đồng hồ.

## 1. Hiện tại nguồn dữ liệu có gì

| Dữ liệu / endpoint | Có thể dùng làm gì | Giới hạn cần giữ đúng |
|---|---|---|
| `GET /api/orders/{id}/item-history` | Audit có `orderItemId`, `kitchenOrderItemId`, `oldStatus`, `newStatus`, `createdAt`; POS xác định lần món vào Preparing | Policy `PosOrCashier`: Admin/Waiter/Cashier. **Chef không có quyền đọc** |
| `GET /api/management/kitchen/orders` | `KitchenOrderResponse.startedAt` là mốc bắt đầu/nhận **phiếu** | Policy `KitchenDesk`: Admin/Chef. Mốc này dùng chung cho phiếu, không phải giờ nhận riêng từng món |
| `GET /api/management/kitchen/orders/{id}/items` | Dòng bếp có `id`, `orderItemId`, trạng thái, số lượng và tên món | `KitchenItemResponse` hiện **không có** thời điểm bắt đầu từng món |
| `GET /api/orders/{id}` | Chi tiết đơn được Admin/Waiter/Cashier/Chef đọc theo `PosKitchenRead` | `OrderItemDetailResponse` hiện **không có** thời điểm bắt đầu từng món |

`sentAt` là giờ gửi phiếu, không phải giờ Bếp nhận món. `order.updatedAt` còn thay đổi do
tiền, ghi chú, tách số lượng và các thao tác khác. Không dùng hai field này, giờ tải trang,
hoặc giờ frontend lần đầu thấy Preparing để giả thành thời điểm bắt đầu nấu.

`KitchenOrder.startedAt` được gán `??= now` khi phiếu Accepted/Preparing; reopen không
reset mốc đầu tiên này. Vì vậy một phiếu có hai món được nhận cách nhau nhiều phút vẫn
chỉ có một mốc phiếu; không được gắn mốc đó lên từng món rồi ghi “Đang làm”.

## 2. Đông / frontend đã xử lý

### POS Phục vụ

- Chỉ hiển thị đồng hồ cho dòng món hiện ở **Preparing** trong đơn còn mở.
- Đọc tối đa một lần item-history bổ sung cho đơn đang hiển thị sau mỗi lượt refresh;
  đây là dữ liệu thời gian tùy chọn. Lỗi đọc lịch sử không biến thành thành công giả,
  không khóa hoặc sửa lệnh nghiệp vụ; giao diện báo chưa rõ mốc giờ.
- Ghép audit theo đúng **`orderItemId`**, không ghép bằng tên món, size hoặc group.
- Lấy `createdAt` mới nhất của sự kiện **`newStatus = Preparing` và
  `oldStatus != Preparing`**. Không phụ thuộc thứ tự phần tử trong response.
- Bỏ qua `Preparing → Preparing`: đó có thể là audit tách số phần/đổi số lượng, không
  phải một lần bắt đầu chế biến mới.
- Khi món đã xong được mở lại, audit `Ready/Completed → Preparing` có mốc mới; đồng hồ
  dùng lần này, không tiếp tục từ lần nhận món đầu tiên.

### Bếp Chef — giới hạn hiện tại cần đọc rõ

- Hiện có đồng hồ **cấp phiếu**, nhãn **“Phiếu đã bắt đầu”**, khi phiếu còn có món Preparing.
  Tooltip giải thích món trong cùng phiếu có thể được nhận ở thời điểm khác nhau.
- **Chưa có đồng hồ chính xác từng món cho Chef** vì DTO hiện thiếu mốc từng món và
  Chef không được đọc item-history. Frontend không gọi route lịch sử bằng Chef để né quyền.
- Nếu backend sau này trả `item.preparationStartedAt` hợp lệ, helper đã sẵn sàng đọc
  field đó để hiển thị thời gian riêng từng món. Hiện không coi field này là đã tồn tại.

### Cách cập nhật thời gian

- Mỗi **1 giây** chỉ cập nhật text của đồng hồ trong DOM; không tải lại trang, không
  render lại toàn bộ danh sách hoặc gửi API mỗi giây.
- Dữ liệu trạng thái tiếp tục poll **5 giây** theo điều kiện hiện có của từng màn;
  không poll khi tab bị ẩn hoặc khi luồng thao tác hiện tại cần giữ ổn định.
- Helper `frontend/js/common/preparation-timing.js` ưu tiên field mới
  `preparationStartedAt` nếu hợp lệ, sau đó mới dùng audit phù hợp khi được cung cấp.
- Timestamp ISO không có offset của backend được hiểu là UTC; timestamp có offset
  giữ đúng instant. Thiếu/sai ngày/giờ tương lai không được tạo nhãn `NaN` hoặc số âm.
  Đồng hồ là `MM:SS`, từ một giờ là `H:MM:SS`.
- Không thay tiền, số lượng, trạng thái, snapshot/pending hoặc cách gửi lệnh nghiệp vụ.

File frontend Đông phụ trách: `frontend/js/common/preparation-timing.js`,
`frontend/js/pages/order.js`, `frontend/js/pages/kitchen.js`, HTML/CSS của POS/Bếp và
các test frontend tương ứng. Các kết quả kiểm thử tổng hợp được cập nhật tại bàn giao
frontend; tài liệu này không tự khẳng định kiểm thử ghi database đã đạt.

## 3. Hải / backend cần bổ sung chính xác gì

### Hợp đồng field đề xuất

Thêm field **không phá response cũ** trên cả hai DTO:

```csharp
public DateTime? PreparationStartedAt { get; set; }
```

JSON: **`preparationStartedAt`**, giá trị UTC ISO 8601 có `Z`/offset UTC rõ ràng, hoặc
`null` nếu không xác định được. Đây là mốc của **lần Preparing hiện tại của dòng món**.
Không gửi chuỗi giờ địa phương thiếu quy ước, không gửi số giây elapsed luôn thay đổi
hoặc ETA. Frontend chỉ chạy đồng hồ khi trạng thái dòng hiện tại là Preparing.

Giữ nguyên các policy GET hiện có: field đi cùng response KitchenDesk/PosKitchenRead.
**Không cần mở quyền đọc toàn bộ lịch sử món cho Chef** và không cần cấp quyền sửa đơn.

### Quy tắc lưu hoặc dựng mốc

Hải chọn lưu mốc bền vững cùng lệnh đổi trạng thái, hoặc dựng mốc từ audit trong query.
Nếu dựng từ audit, cần xử lý theo lô khi đọc nhiều món, tránh một truy vấn riêng cho
mỗi đồng hồ. Hai DTO phải có cùng ý nghĩa và cùng giá trị cho cùng `orderItemId`.

| Trường hợp | Mốc đúng |
|---|---|
| Nhận món Pending → Preparing | Mốc UTC server chấp nhận chuyển trạng thái đó |
| Hai món cùng phiếu nhận ở hai thời điểm khác nhau | Mỗi dòng giữ mốc riêng; không lấy `KitchenOrder.startedAt` dùng chung |
| Audit Preparing → Preparing khi báo xong một phần | Dòng gốc còn Preparing giữ **mốc cũ**, không lấy giờ cập nhật số lượng |
| Tách dòng Completed cho phần đã xong | Không tạo đồng hồ đang chạy cho dòng Completed; không suy giờ bắt đầu từ giờ tạo dòng tách |
| Ready/Completed → Preparing qua reopen hợp lệ | Bắt đầu vòng mới bằng mốc UTC của lệnh reopen |
| Hủy hoặc kết thúc chế biến | Không còn đồng hồ đang chạy; không tự khởi động lại khi render |
| Dữ liệu cũ thiếu audit/mốc lưu | Trả `null`; không lấy sentAt/createdAt/updatedAt hoặc mốc phiếu để lấp chỗ trống |

Đặc biệt, `OrderItem.CreatedAt` của dòng clone là **giờ tách**, không phải giờ phần món
bắt đầu nấu. `OrderItemGroupId` chỉ mô tả quan hệ dòng; không bảo đảm mọi dòng trong nhóm
đều có cùng lần bắt đầu, nhất là khi một phần đã được reopen.

## 4. File backend cụ thể Hải cần đọc/sửa

Các đường dẫn tính từ repository. Đây là hướng dẫn bàn giao, **chưa sửa các file này**.

| File | Vai trò / điểm cần xử lý |
|---|---|
| `RestaurantFoodManagerSystem/Application/DTO/Responses/Management/KitchenItemResponse.cs` | Thêm property `PreparationStartedAt`; DTO items Bếp hiện chưa có timestamp riêng |
| `RestaurantFoodManagerSystem/Application/DTO/Responses/Order/OrderDetailResponse.cs` | Thêm property vào **`OrderItemDetailResponse`**, không đặt mốc chung trên Order rồi dùng cho mọi món |
| `RestaurantFoodManagerSystem/Application/Services/Kitchen/KitchenService.Queries.cs` | `GetItemsAsync` map timestamp cho từng `orderItemId` của phiếu |
| `RestaurantFoodManagerSystem/Application/Services/Order/OrderService.Queries.cs` | Map dòng trong `GetAsync`; `GetItemHistoryAsync` hiện trả audit theo `CreatedAt`, `Id` |
| `RestaurantFoodManagerSystem/Application/Services/Kitchen/KitchenService.cs` | `UpdateItemStatusAsync` ghi audit nhận món; `ReopenItemAsync` ghi mốc vòng mới; `UpdatePreparedQuantityAsync` giữ dòng gốc và tách dòng Completed; `SplitKitchenOrderItemLineAsync` ghi audit cho dòng tách |
| `RestaurantFoodManagerSystem/Application/Common/OrderItemMutations.cs` | `RecordItemHistory` ghi OldStatus/NewStatus/CreatedAt; `CloneLine` tạo CreatedAt mới khi tách, không được nhầm thành giờ bắt đầu nấu |
| `RestaurantFoodManagerSystem/Domain/Entity/Order/OrderItemStatusHistory.cs` | Dữ liệu audit có OrderItemId/KitchenOrderItemId nullable, trạng thái và CreatedAt; nguồn đọc khi chọn dựng từ lịch sử |
| `RestaurantFoodManagerSystem/Application/DTO/Responses/Order/OrderItemHistoryResponse.cs` | Shape audit POS hiện đọc để dựng mốc trong thời gian chờ field mới |
| `RestaurantFoodManagerSystem/Domain/Entity/Kitchen/KitchenOrderItem.cs` và `Domain/Entity/Order/OrderItem.cs` trong cùng project | Nếu chọn lưu mốc riêng thay vì dựng audit, Hải chốt entity/quan hệ lưu và migration cần thiết; không yêu cầu frontend tự sửa schema |
| `RestaurantFoodManagerSystem/Application/DTO/Responses/Management/KitchenOrderResponse.cs` | `StartedAt` hiện là **cấp phiếu**; giữ ý nghĩa cũ, không đổi ngầm thành giờ từng món |
| `RestaurantFoodManagerSystem/Application/Services/Order/OrderLifecycleCoordinator.cs` | `RecalculateTicketStatus` gán `ticket.StartedAt ??= now`; đây là lý do không dùng mốc phiếu cho mọi món hoặc reset reopen |
| `RestaurantFoodManagerSystem/Api/Controllers/Order/OrdersController.cs` | `GET {id}/item-history` dùng PosOrCashier; GET chi tiết đơn dùng PosKitchenRead |
| `RestaurantFoodManagerSystem/Api/Controllers/Management/ManagementController.cs` | GET kitchen list/items dùng KitchenDesk; field mới đi trong DTO hiện có |
| `RestaurantFoodManagerSystem/Infrastructure/Auth/AuthorizationServiceCollectionExtensions.cs` | Xác nhận các role hiện hành: PosOrCashier không có Chef; không mở rộng policy chỉ để lấy timestamp |

## 5. Checklist nghiệm thu Hải và Đông cần chạy sau khi có contract

- [ ] Hai món trong cùng phiếu nhận cách nhau vài phút có mốc khác nhau trên Bếp và POS.
- [ ] Reload trang, đổi phiên đăng nhập hoặc mở thiết bị/trình duyệt khác vẫn lấy cùng
  mốc server; đồng hồ không bắt đầu từ 0 chỉ vì mới mở màn hình.
- [ ] Waiter đọc mốc ở order detail; Chef đọc mốc ở kitchen item theo đúng policy.
  Chef vẫn bị chặn item-history nếu chưa được cấp role khác; không đổi quyền ghi.
- [ ] Báo xong 1/3 phần: hai phần còn lại giữ start cũ; phần Completed không có đồng hồ chạy.
- [ ] Reopen phần đã xong: chỉ phần đó bắt đầu vòng mới; phần đang làm khác không reset.
- [ ] Quantity-only audit Preparing → Preparing không làm lệch start; response không phụ
  thuộc thứ tự history. Hủy/Ready/Completed dừng hiển thị đồng hồ đang làm.
- [ ] Legacy thiếu lịch sử trả null; history tham chiếu dòng đã xóa/null không gắn nhầm
  sang món khác. Không suy mốc bằng tên, nhóm, giờ gửi phiếu hoặc giờ cập nhật đơn.
- [ ] UTC, offset, chuỗi không hợp lệ và mốc tương lai được xử lý rõ; không hiện NaN,
  số âm hoặc dùng giờ máy khách làm giờ server chấp nhận lệnh.
- [ ] GET timestamp lỗi không gửi lại lệnh nhận/báo xong/reopen và không đổi trạng thái món.

Đây là **checklist cần nghiệm thu**, không phải danh sách đã chạy trên backend thật.
Lượt frontend hiện tại chỉ đọc và dùng dữ liệu/API giả để kiểm thử các nhánh nguy hiểm;
không phát lệnh ghi database thật, không tự thay schema và không commit/push.

## 6. Kết quả frontend đã chạy ngày 12/11/2026

- `node --test frontend/tests/*.test.cjs`: **297/297 đạt**, gồm 8 test helper thời gian,
  4 test tích hợp POS và 4 test tích hợp Bếp mới. Kiểm tra cả UTC/offset ở hai timezone,
  partial/reopen, GET lỗi/đến trễ, thiếu mốc, tick không GET/rerender và quyền Chef.
- `node --check`: 23 JS/CJS sửa hoặc mới đạt; `git diff --check` đạt.
- 17 HTML giữ đủ 577 ID từ HEAD, không trùng ID; 226 tham chiếu local tồn tại.
- Trình duyệt fixture độc lập cổng 5508: POS Waiter và Bếp Chef ở 320/390/768/1440px,
  không tràn ngang; POS đọc mốc món, Bếp ghi rõ mốc phiếu. Reload POS vẫn giữ cùng
  mốc server giả. Fixture chặn toàn bộ request ghi và không proxy database.
- Phiên bản menu/HTML mới `ui=20261112-preparation2`, navbar `20261112-service-nav7`;
  helper, CSS đồng hồ và JS hai màn có query `v=20261112-preparation2`.
- Website thật, phiên Admin đang đăng nhập: chỉ xem các phiếu #20/#21 đã có món
  Preparing và đơn #14 tại bàn K20. Bếp hiện mốc cấp phiếu; POS hiện thời gian món
  Nước suối 500 ml từ lịch sử nhận món. Không tạo, nhận hoặc đổi trạng thái các món đó
  để thử. Ảnh `kitchen-live.png`, `pos-live.png` và `live-observation.json` ghi nhận lượt đọc.
- Ảnh, `responsive.json`, `static.json`, `tests.log` ở ngoài repository:
  `D:/Hoc_Tap/quanliduan1111/_codex_runtime/preparation-timing-20261112/`.

## 7. Bổ sung giờ gọi món, thời gian chờ nhận và FIFO — 12/11/2026

Mốc bổ sung này giải quyết tình huống món chưa được Bếp nhận hoặc nhiều lượt gọi đến
cùng lúc. Cần đọc cùng giới hạn timestamp từng món ở các mục trên. Các số kiểm thử tại
mục 6 là của lượt đồng hồ trước; kết quả lượt FIFO được cập nhật riêng sau khi kiểm tra.

### Đông / frontend: tổ chức hàng đợi và cảnh báo bỏ sót

- Bảng Bếp phân làn theo **trạng thái từng dòng món**, thay vì chỉ nhìn trạng thái phiếu.
  Phiếu đang Preparing nhưng còn một dòng Pending vẫn có phần chờ nhận ở làn **Chờ nhận**.
  Một phiếu có thể có thẻ ở nhiều làn; mỗi dòng chỉ nằm trong làn trạng thái tương ứng,
  không lặp toàn bộ món trong mọi thẻ.
- Tổng phiếu là số phiếu **không trùng**. Các làn đếm **dòng món** và **phần**; không
  cộng số thẻ ở ba làn để kết luận tổng phiếu. Một dòng số lượng 3 là một dòng món, ba phần.
- Thông báo món chưa nhận được tổng hợp từ mọi đơn chưa Paid/Cancelled, kể cả khi đang
  lọc tìm kiếm, lọc ưu tiên hoặc xem làn khác. Hiện số dòng Pending, số phần và mốc phiếu
  cũ nhất khi có dữ liệu hợp lệ. Nút đi tới hàng chờ xóa các bộ lọc và mở làn Chờ nhận.
  Sau khi nhận một món trên mobile, giữ làn Chờ nhận để các món còn lại không bị khuất.
- Mặc định sắp **phiếu gửi trước lên trước**: `ticket.sentAt` tăng dần; timestamp thiếu
  hoặc không hợp lệ xuống cuối; dùng mã phiếu làm điều kiện phụ để thứ tự ổn định.
- Có lựa chọn sắp **Ưu tiên trước**: nhóm `priority > 0` đứng trước nhóm thường, rồi FIFO
  trong mỗi nhóm. Không suy rằng giá trị ưu tiên 3 bắt buộc cao hơn 1 khi contract chưa
  chốt thứ bậc số. Chế độ này được hiển thị rõ để nhân viên biết đã đổi khỏi FIFO thuần.
- Trên thẻ Bếp, giờ nhận/gửi hiện có được ghi đúng phạm vi **“Gửi phiếu lúc”** và
  **“Phiếu gửi cách đây”**. Thời gian từ khi gửi vẫn tiếp tục thấy khi món chưa được nhận;
  đồng hồ chế biến tiếp tục là mốc riêng của việc bắt đầu nấu. Không lấy thời gian nấu
  thay cho thời gian khách đã chờ, không tự gán ETA hoặc thời gian chuẩn hoàn thành món.
- POS Phục vụ ưu tiên field `orderedAt` hợp lệ khi backend bổ sung. Trong thời gian chờ,
  chỉ dựng giờ gọi dòng từ sự kiện audit đầu tiên **`oldStatus` rỗng → `Pending`** đúng
  `orderItemId`. Nếu không có bằng chứng đó, hiện thiếu mốc; không đoán bằng tên món,
  group, `order.createdAt`, `updatedAt`, giờ mở trang hoặc nội dung tự do trong `reason`.

Các file frontend Đông phụ trách: `frontend/js/pages/kitchen.js` (làn, sắp xếp, thông báo,
đếm và điều hướng tới món chờ), `frontend/pages/kitchen.html` (điều khiển hàng đợi),
`frontend/js/pages/order.js` (giờ gọi/chờ ở POS),
`frontend/js/common/preparation-timing.js` (mốc UTC và thứ tự FIFO), CSS đồng hồ/Bếp và
test tương ứng. Lượt này không thay lệnh nhận món, trạng thái, số lượng hoặc dữ liệu kho.

### Giới hạn hiện tại: FIFO theo phiếu chưa phải FIFO chính xác từng món

`POST /api/orders/{id}/items` tạo phiếu mới với `sentAt` mới, nên các lượt gọi thêm này
được phân biệt theo phiếu. Tuy nhiên `OrderService` còn có luồng tăng số lượng:

1. Dòng đang Pending có thể được cộng thêm số phần ngay vào cùng dòng.
2. Nếu món đã vào chế biến, phần tăng có thể thành **dòng Pending mới trong phiếu cũ**.
   Phiếu giữ `sentAt` cũ, dù phần gọi thêm vừa được khách yêu cầu.
3. Dòng Pending mới này được ghi audit `Pending → Pending` với mô tả tạo dòng. Đây khác
   với sự kiện tạo món ban đầu `"" → Pending`. Frontend không phân tích câu `reason` để
   suy thành hợp đồng timestamp bền vững, vì vậy có thể chưa biết giờ gọi riêng dòng đó.
4. Tách phần đã làm xong/đã hủy tạo dòng clone có `CreatedAt = now`; giờ tạo clone không
   tự động trở thành giờ khách gọi món.

Vì vậy **Bếp hiện chỉ bảo đảm thứ tự phiếu theo `sentAt`**, chưa thể khẳng định mọi dòng
món ở Chef đều có thứ tự gọi riêng chính xác. Nhãn cấp phiếu được giữ để tránh hiểu nhầm.
Cảnh báo món chưa nhận giúp không bỏ sót dòng Pending trong một phiếu đã bắt đầu, nhưng
không thay thế việc bổ sung mốc gọi từng dòng tại backend.

### Hải / backend: mở rộng FE-C29 với giờ gọi riêng của dòng món — chưa thực hiện

Ngoài `preparationStartedAt`, bổ sung field UTC nullable **`orderedAt`** trên cả
`KitchenItemResponse` và `OrderItemDetailResponse`. Nếu chọn tên `placedAt` thay thế,
Hải và Đông cần chốt một tên JSON thống nhất trước khi nối; không âm thầm dùng cả hai
với ý nghĩa khác nhau. Field mô tả giờ server chấp nhận **lượt gọi của dòng món đó**, không
phải giờ bếp nhận hoặc giờ cập nhật trạng thái.

| Tình huống | `orderedAt` đúng | `preparationStartedAt` đúng |
|---|---|---|
| Khách gọi món lần đầu hoặc POST gọi thêm tạo lượt mới | Mốc server nhận lượt gọi mới | Null tới khi bắt đầu nấu |
| Tăng món đang làm, tạo Pending mới trong phiếu cũ | Mốc của phần gọi tăng mới, không dùng `ticket.sentAt` cũ | Null tới khi nhận phần mới |
| Tách dòng vì làm xong/hủy một phần | Giữ mốc gọi của phần gốc; không dùng giờ tạo clone | Giữ quy tắc lần nấu tương ứng; dòng đã xong không chạy đồng hồ |
| Mở lại món đã xong để làm lại | Giữ giờ khách gọi ban đầu nếu không phải một lượt đặt mới | Mốc reopen của vòng chế biến hiện tại |
| Tăng số lượng và gộp vào cùng dòng Pending | Phải chốt ý nghĩa theo lô; không thể khẳng định từng phần có cùng giờ gọi chỉ từ một timestamp | Null nếu vẫn chưa nấu |
| Dữ liệu cũ không xác định được lượt gọi | Null, không lấp bằng giờ đơn/phiếu/cập nhật | Giữ quy tắc thiếu mốc tại mục 3 |

Nếu cần FIFO **tới từng phần gọi tăng** trong dòng Pending đang gộp, một field `orderedAt`
cho cả dòng là chưa đủ. Hải cần bảo toàn các lô gọi riêng hoặc trả metadata các lượt tăng
(mốc/số lượng/định danh lô) được chốt cùng nghiệp vụ; frontend hiện không tự tách dữ liệu
đã gộp thành các lượt tưởng tượng. Khi chưa có metadata đó, chỉ được mô tả thời gian của
lượt sớm nhất đã biết, không nói mọi phần trong dòng đều đã chờ cùng khoảng thời gian.

File Hải cần bổ sung ngoài các điểm `preparationStartedAt` đã liệt kê ở mục 4:

- `RestaurantFoodManagerSystem/Application/DTO/Responses/Management/KitchenItemResponse.cs`
  và `Application/DTO/Responses/Order/OrderDetailResponse.cs`: field JSON `orderedAt` cho
  đúng từng `orderItemId` ở Bếp và POS.
- `RestaurantFoodManagerSystem/Application/Services/Order/OrderService.cs`:
  `AddOrderItemsAsync`, tạo lượt gọi thêm và nhánh tăng số lượng/gộp Pending/tạo Pending
  mới trong phiếu cũ phải ghi và bảo toàn ý nghĩa lượt gọi trong cùng transaction.
- `RestaurantFoodManagerSystem/Application/Common/OrderItemMutations.cs`: `CloneLine`
  cần phân biệt clone do tách phần đã gọi với dòng của lượt tăng mới; không dùng chung
  `CreatedAt` làm giờ gọi cho mọi trường hợp.
- `RestaurantFoodManagerSystem/Application/Services/Kitchen/KitchenService.Queries.cs`
  và `Application/Services/Order/OrderService.Queries.cs`: map cùng mốc gọi cho cùng dòng;
  nếu dựng từ lịch sử thì xử lý theo lô, không dựa vào tìm chuỗi trong `reason`.
- Nếu lưu field/lô riêng: Hải phụ trách entity, migration và dữ liệu cũ nullable.
  Giữ policy GET hiện có; Chef nhận field qua DTO Bếp, không cần quyền đọc toàn bộ audit.

Checklist bổ sung cần nghiệm thu sau khi có field:

- [ ] Hai lần gọi cùng món ở thời điểm khác nhau có giờ riêng và thứ tự đúng trên POS/Bếp.
- [ ] Tăng món đang nấu, Pending mới trong phiếu cũ không được mang giờ phiếu cũ.
- [ ] Phiếu có cả Pending/Preparing/Completed vẫn hiện đủ dòng ở đúng làn, không lặp món;
  tổng phiếu không trùng, số dòng và số phần khớp.
- [ ] Đổi bộ lọc/làn không làm mất thông báo Pending toàn cục; nút xem hàng chờ đưa về
  đầy đủ món cần nhận, kể cả món ở phiếu Preparing.
- [ ] Partial/cancel/reopen không tự đổi giờ gọi; clone do tách không thành lượt gọi mới.
- [ ] Gộp tăng số lượng Pending được mô tả đúng theo lô, hoặc trả metadata đủ để phân biệt
  các phần gọi sau; không khẳng định FIFO tới từng phần khi chưa có dữ liệu.
- [ ] Chế độ FIFO và Ưu tiên trước cho kết quả rõ ràng, timestamp thiếu không biến thành 0;
  reload/thiết bị khác giữ mốc server và không gửi lại lệnh nghiệp vụ.

Đây là phần bàn giao backend **chưa thực hiện/nghiệm thu**, không phải xác nhận mọi
giờ gọi riêng từng món trên Chef đã chính xác. Không sửa backend/database hoặc push Git.

## 8. Kết quả chốt lượt giờ gọi / hàng chờ

- Tổng kiểm tra frontend: **313/313 đạt**. Phần bổ sung sau mốc 297 gồm 6 test helper
  giờ gọi/FIFO, 4 test POS và 6 test Bếp. Kiểm tra cả thứ tự phiếu, làn món hỗn hợp,
  đếm phiếu không trùng, Pending toàn cục, giờ thiếu/clone và giữ focus tìm kiếm.
- Bộ lọc tìm kiếm/sắp xếp không làm poll dừng khi còn focus. Hộp thoại xử lý món và
  yêu cầu pending vẫn được bảo vệ; test dùng API giả xác nhận không gửi lệnh ghi.
- 23 JS/CJS sửa hoặc mới đạt cú pháp; 17 HTML giữ đủ 577 ID từ HEAD, không trùng;
  227 tham chiếu local tồn tại. `git diff --check` đạt.
- Trình duyệt fixture chỉ đọc: POS Waiter và Bếp Chef × 320/390/768/1440px không tràn ngang.
  FIFO cho thứ tự phiếu #10/#11/#13; chọn ưu tiên thành #11/#10/#13. Lọc không kết quả
  vẫn báo 3 món/9 phần chưa nhận; nút Xem món chưa nhận bỏ lọc và trở về hàng chờ.
- Sticky được kiểm tra ở 390px khi cuộn xuống phiếu sau: mép trên thanh nhắc vẫn ở 8px.
  Sửa `workspace` từ overflow hidden tạo scroll container sang clip/visible ở riêng màn Bếp.
- Website thật, phiên Admin hiện có, chỉ đọc: phiếu #20 có Lẩu Pending và Tôm Preparing;
  hai món hiện ở đúng hai cột, thanh nhắc báo 1 món/1 phần chưa nhận. POS đơn #14 phân biệt
  hai món Lẩu gọi 09:03:42 và Nước suối gọi 12:45:28. Không nhận, nấu, hủy hay thêm món để thử.
- Bản mở mới: `ui=20261112-queue2`, navbar `20261112-service-nav9`, JS POS/Bếp/luồng
  Phục vụ `v=20261112-queue2`; CSS hàng chờ `v=20261112-queue2`.
- Bằng chứng ngoài repository: `_codex_runtime/kitchen-queue-20261112/` gồm `tests.log`,
  `static.json`, `responsive.json`, `kitchen-live.png`, `pos-live.png`, `pos-live-times.json`
  và ảnh kiểm tra điện thoại. Chưa commit/push; backend/DB không thay đổi.
