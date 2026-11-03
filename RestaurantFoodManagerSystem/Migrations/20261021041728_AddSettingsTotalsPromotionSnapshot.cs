using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsTotalsPromotionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromotionDiscountType",
                table: "orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PromotionDiscountValue",
                table: "orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromotionId",
                table: "orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargeAmount",
                table: "orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "order_status_histories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DisplayOrder",
                table: "categories",
                type: "int",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.CreateTable(
                name: "restaurant_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RestaurantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxIncluded = table.Column<bool>(type: "bit", nullable: false),
                    ServiceChargeRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_PromotionId",
                table: "orders",
                column: "PromotionId");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_promotions_PromotionId",
                table: "orders",
                column: "PromotionId",
                principalTable: "promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ------------------------------------------------------------------
            // Đợt 2 / A1 — seed bản ghi cấu hình singleton (Id = 1).
            // Mặc định: không thu VAT, không thu phí dịch vụ, giá coi như đã gồm VAT
            // => hành vi tiền y hệt hệ thống cũ cho tới khi Admin cấu hình.
            // ------------------------------------------------------------------
            migrationBuilder.Sql(@"
INSERT INTO [restaurant_settings]
    ([Id], [RestaurantName], [Address], [Phone], [TaxCode], [Currency],
     [TaxRate], [TaxIncluded], [ServiceChargeRate], [UpdatedAt])
VALUES
    (1, N'', N'', N'', N'', N'VND', 0.0, 1, 0.0, SYSUTCDATETIME());
");

            // ------------------------------------------------------------------
            // P0-05 — đồng bộ dữ liệu khuyến mãi cũ sang múi giờ UTC.
            //
            // Trước Đợt 2, API nhận giờ VN nhưng lưu thẳng (không quy đổi) => các dòng
            // promotions đang có trong DB mang giờ lịch VN. Quy ước mới là "lưu UTC"
            // nên phải trừ 7 giờ để cửa sổ khuyến mãi (theo giờ VN) giữ nguyên.
            //
            // UPDATE vô điều kiện là an toàn — nếu bảng rỗng thì không đổi gì.
            // ------------------------------------------------------------------
            migrationBuilder.Sql(@"
UPDATE [promotions]
SET [StartAt] = DATEADD(hour, -7, [StartAt]),
    [EndAt]   = DATEADD(hour, -7, [EndAt]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_promotions_PromotionId",
                table: "orders");

            migrationBuilder.DropTable(
                name: "restaurant_settings");

            migrationBuilder.DropIndex(
                name: "IX_orders_PromotionId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PromotionDiscountType",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PromotionDiscountValue",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PromotionId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ServiceChargeAmount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "order_status_histories");

            migrationBuilder.AlterColumn<bool>(
                name: "DisplayOrder",
                table: "categories",
                type: "bit",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
