using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <summary>
    /// FE-C23: database cũ từng dùng decimal cho số phần; hợp đồng hiện tại dùng int.
    /// Không sửa lịch sử migration cũ hoặc làm tròn số lượng đã lưu.
    /// </summary>
    public partial class NormalizeLegacyOrderQuantities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF @@TRANCOUNT = 0
                    THROW 51023, 'FE-C23: run this migration inside a transaction.', 1;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[order_items]')
                      AND name = N'Quanlity'
                      AND TYPE_NAME(user_type_id) IN (N'int', N'decimal', N'numeric')
                      AND is_nullable = 0 AND is_computed = 0)
                    THROW 51023, 'FE-C23: unexpected order_items.Quanlity schema; no conversion performed.', 1;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[kitchen_order_items]')
                      AND name = N'Quanlity'
                      AND TYPE_NAME(user_type_id) IN (N'int', N'decimal', N'numeric')
                      AND is_nullable = 0 AND is_computed = 0)
                    THROW 51023, 'FE-C23: unexpected kitchen_order_items.Quanlity schema; no conversion performed.', 1;

                -- Giữ khóa cả hai bảng tới cuối transaction để không lọt số lẻ sau kiểm tra.
                DECLARE @OrderRows bigint, @KitchenRows bigint;
                SELECT @OrderRows = COUNT_BIG(*) FROM [dbo].[order_items] WITH (TABLOCKX, HOLDLOCK);
                SELECT @KitchenRows = COUNT_BIG(*) FROM [dbo].[kitchen_order_items] WITH (TABLOCKX, HOLDLOCK);

                -- Kiểm tra cả hai phía trước ALTER; tuyệt đối không cắt/làm tròn số lẻ.
                IF EXISTS (
                    SELECT 1 FROM [dbo].[order_items]
                    WHERE [Quanlity] <> ROUND([Quanlity], 0, 1)
                       OR [Quanlity] < -2147483648 OR [Quanlity] > 2147483647)
                    THROW 51023, 'FE-C23: order_items.Quanlity has fractional or out-of-int-range values; review data before retrying.', 1;

                IF EXISTS (
                    SELECT 1 FROM [dbo].[kitchen_order_items]
                    WHERE [Quanlity] <> ROUND([Quanlity], 0, 1)
                       OR [Quanlity] < -2147483648 OR [Quanlity] > 2147483647)
                    THROW 51023, 'FE-C23: kitchen_order_items.Quanlity has fractional or out-of-int-range values; review data before retrying.', 1;

                -- Database mới đã đúng int: giữ nguyên schema, chỉ ghi nhận migration.
                IF EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[order_items]')
                      AND name = N'Quanlity' AND TYPE_NAME(user_type_id) <> N'int')
                    ALTER TABLE [dbo].[order_items] ALTER COLUMN [Quanlity] int NOT NULL;

                IF EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[kitchen_order_items]')
                      AND name = N'Quanlity' AND TYPE_NAME(user_type_id) <> N'int')
                    ALTER TABLE [dbo].[kitchen_order_items] ALTER COLUMN [Quanlity] int NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Model trước migration này cũng là int. Không đưa DB trở lại kiểu decimal bị lỗi.
            // Muốn khôi phục chính xác schema legacy phải dùng backup trước chuyển đổi.
        }
    }
}
