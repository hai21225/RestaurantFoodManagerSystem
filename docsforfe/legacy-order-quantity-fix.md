# FE-C23 — sửa kiểu số lượng trên database cũ

Ngày 2026-11-01, nhánh `dong`. **Đông/Codex đã thực hiện** sửa migration và database cho FE-C23
theo yêu cầu trực tiếp của chủ dự án. Các công việc backend khác trong bàn giao vẫn thuộc Hải.

## Kết quả trên máy Đông

- Database `RestaurantFoodManager`, SQL Server `localhost\SQLEXPRESS`.
- `dbo.order_items.Quanlity`: `decimal(18,3)` → `int NOT NULL`.
- `dbo.kitchen_order_items.Quanlity`: `decimal(18,3)` → `int NOT NULL`.
- Lịch sử migration từ 15 lên 16 bản ghi, có `20261101103000_NormalizeLegacyOrderQuantities`.
  Giữ migration legacy `20260929190848_AddFlexibleFoodSaleUnits` và toàn bộ lịch sử cũ.
- Hai đơn đang có: đơn 1/bàn 3 và đơn 2/bàn 9, đều `SentToKitchen`, mỗi đơn một dòng số lượng 1.
  Hai chi tiết phiếu bếp tương ứng đọc được. Một phiếu thanh toán có sẵn được giữ nguyên.
- Không tạo/gửi lại/nhận/hủy/hoàn thành đơn hoặc thu/hoàn tiền để kiểm thử trên database gốc.
- Không đổi số lượng tồn, định lượng công thức, giá tiền, tài khoản hoặc quyền.
  Định lượng nguyên liệu/công thức cần phần lẻ vẫn dùng decimal.
- Backend cổng 5225 và frontend cổng 5500 tiếp tục chạy. Không restart backend nên không làm mất
  receipt/idempotency đang lưu trong bộ nhớ tiến trình hiện tại.

## File đã thêm và cách sửa

| File | Nội dung | Người thực hiện |
|---|---|---|
| `RestaurantFoodManagerSystem/Migrations/20261101103000_NormalizeLegacyOrderQuantities.cs` | Migration tương thích database cũ, kiểm tra trước khi đổi cả hai cột | Đông/Codex |
| `RestaurantFoodManagerSystem/Migrations/20261101103000_NormalizeLegacyOrderQuantities.Designer.cs` | Metadata ID và target model giữ nguyên hợp đồng int hiện tại | Đông/Codex |
| `RestaurantFoodManagerSystem.Tests/LegacyOrderQuantityMigrationTests.cs` | 16 ca kiểm thử SQL Server riêng, tự tạo/xóa database test mang GUID | Đông/Codex |

Không sửa `OrderItem`, `KitchenOrderItem`, DTO hay `AppDbContextModelSnapshot`: chúng đã dùng int.
Không sửa service để ép kiểu khi đọc, không đổi câu báo lỗi trong frontend để che HTTP 500.
Không sửa migration đã áp dụng, appsettings hoặc connection string được Git theo dõi.

Migration mới thực hiện trong transaction của EF:

1. Kiểm tra hai cột tồn tại, không nullable/computed, chỉ nhận int/decimal/numeric.
2. Giữ khóa cả hai bảng đến cuối transaction để không có số lẻ mới chen giữa kiểm tra và ALTER.
3. Kiểm tra **cả hai bảng trước khi đổi**: không có phần lẻ, không vượt `-2147483648..2147483647`.
   Nếu không an toàn, dừng với SQL error 51023 và tên bảng; không tự làm tròn hoặc xóa dữ liệu.
4. Chỉ ALTER cột chưa là int. Database hiện tại đã đúng int sẽ giữ nguyên schema.
5. Nếu ALTER sau gặp dependency không hỗ trợ, transaction rollback ALTER trước.
   Không tự xóa index/check/default lạ của máy khác để ép migration chạy.

`Down()` chủ ý không đổi về decimal: model trước migration này cũng đã là int.
Rollback migration chỉ bỏ marker, không tái tạo schema legacy bị lỗi.
Khôi phục chính xác database trước sửa phải dùng backup đã xác minh, có cân nhắc dữ liệu phát sinh sau đó.

## Sao lưu và đối chiếu dữ liệu

- Tạo backup **mới sau khi đã có đơn**, `COPY_ONLY + CHECKSUM`, `RESTORE VERIFYONLY` đạt.
- Sao chép backup ra thư mục riêng ngoài repository; SHA256 bản SQL Server và bản sao local khớp.
  Metadata đường dẫn/hash nằm tại `_codex_runtime/c23-backup.json` ngoài Git; không đưa backup hoặc mật khẩu lên Git.
- Restore thành database tạm riêng rồi áp dụng migration bằng EF trước khi chạy trên bản gốc.
- So sánh số dòng và SHA256 nội dung JSON theo primary key của **30 bảng nghiệp vụ** trước/sau,
  trên cả bản sao và bản gốc: **khớp toàn bộ**. Khi tính hash, chỉ normalize hai cột số lượng về decimal(18,3)
  để so giá trị trước/sau đổi kiểu; không normalize giá tiền, trạng thái, rowversion hay các dữ liệu khác.
- Chỉ `__EFMigrationsHistory` được thêm một bản ghi theo đúng mục đích nâng cấp.

## Kiểm tra đã thực sự chạy

| Kiểm tra | Kết quả |
|---|---|
| Build backend riêng | Đạt, 0 warning / 0 error |
| SQL Server migration tests | **16/16 đạt, 0 bỏ qua**: legacy decimal/numeric, int/mixed, chạy lặp, biên int, số lẻ, vượt biên ở cả hai bảng, schema nullable/bigint, yêu cầu transaction, rollback khi ALTER thứ hai lỗi, EF discovery/script |
| Bản sao đầy đủ và database gốc | Áp dụng migration đạt; 30/30 bảng giữ số dòng/hash giá trị; model đọc quantity=1 cho cả hai đơn/phiếu |
| `GET /api/orders/1`, `/api/orders/2` | Trước: 500; sau: **200**, mỗi đơn một dòng quantity=1 |
| `GET /api/management/kitchen/orders/1/items`, `/2/items` | Trước: 500; sau: **200**, mỗi phiếu một dòng |
| `GET /api/management/kitchen/orders` | 200, có 2 phiếu |
| `GET /api/management/recipes?includeInactive=true` | 200, 6 công thức |
| `GET /api/management/ingredients`, `/api/management/inventory` | 200, mỗi danh sách 17 dòng |
| Nhóm CoreOrderKitchenTests, KitchenStockRuleTests, KitchenFlowTests | **11 đạt / 21 lỗi / 32 ca**; bản baseline chưa thêm migration có cùng kết quả và cùng thông báo lỗi cho từng ca: `Không có actor trong token; cần xác thực.` Không coi bộ backend là đã đạt hết |
| Giao diện trình duyệt sau chuyển | Chưa kiểm chứng trực tiếp trong lượt này vì công cụ browser báo `browser.user is unavailable`; kết quả sửa được xác minh bằng SQL và HTTP thật |

Các file kết quả nằm ngoài Git tại `_codex_reports/20261101/`:
`c23-migration-tests-final.trx`, `c23-flow-tests.trx`, `c23-flow-baseline.trx`,
`c23-api-before.json`, `c23-api-after.json`, `c23-original-before.json`, `c23-original-after.json`,
`c23-clone-before.json`, `c23-clone-after.json`, `c23-upgrade.sql`.

Chạy lại đúng nhóm test migration (SQL Server local, tài khoản Windows cần quyền tạo database test):

```powershell
$env:C23_TEST_SQLSERVER = 'Server=localhost\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True'
dotnet test RestaurantFoodManagerSystem.Tests --filter FullyQualifiedName~LegacyOrderQuantityMigrationTests
Remove-Item Env:\C23_TEST_SQLSERVER
```

Không đặt biến này thì các ca SQL Server được đánh dấu skip, không được báo là đã kiểm chứng migration.
Fixture chỉ thao tác database test mới có tiền tố `RestaurantFoodManager_C23_Test_` và GUID của chính nó.

## Việc còn lại theo người phụ trách

- **Đông:** tải lại Bếp/Đặt món. Nếu POS còn pending, chọn đúng bàn và dùng “Thử xác nhận lại yêu cầu”.
  Với lệnh đã ACK, frontend chỉ GET đơn cũ rồi xóa pending sau khi đọc thành công; không tạo thêm đơn để thử.
  Lượt sửa này không tự xóa localStorage hay thực hiện lệnh POS thay người dùng.
- **Hải:** review migration mới và đưa cùng thay đổi backend khi đồng bộ máy khác; không viết thêm migration trùng
  hoặc đổi entity về decimal. Khi có dữ liệu bán lẻ thật, cần chốt lại toàn bộ hợp đồng DTO/nghiệp vụ riêng.
  FE-C21 và FE-C24–28 cùng các ticket cũ khác vẫn theo [bàn giao F6/F7](f6-f7-handoff.md).
- **Máy khác:** sau khi source mới được commit/push và lấy về, backup database của máy đó trước khi khởi động
  backend đã build. `DatabaseInitializer` hiện chạy migration khi startup nếu `Database:MigrateOnStartup=true`.
  Nếu gặp 51023 phải xem dữ liệu/schema được nêu trong lỗi, không xóa lịch sử migration hoặc ép tròn số lượng.
- Đây mới xác nhận sửa lỗi đọc decimal→int; chưa chứng minh toàn bộ luồng nhận món, hoàn thành, hủy, thanh toán
  trên database gốc. Không dùng các đơn có sẵn để tạo giao dịch kiểm thử ngoài yêu cầu của chủ dự án.
