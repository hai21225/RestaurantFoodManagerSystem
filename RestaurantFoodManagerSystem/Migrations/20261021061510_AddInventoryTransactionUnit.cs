using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransactionUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "inventory_transactions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // C-17: backfill đơn vị cho các dòng ledger đã có. Đơn vị của nguyên liệu
            // tại thời điểm này là giá trị đúng cho lịch sử cũ, vì trước đây hệ thống
            // không cho sửa đơn vị nguyên liệu (C-17 mới chặn).
            migrationBuilder.Sql(
                """
                UPDATE t
                SET Unit = i.Unit
                FROM inventory_transactions AS t
                INNER JOIN ingredients AS i ON i.IngredientId = t.IngredientId
                WHERE t.Unit IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Unit",
                table: "inventory_transactions");
        }
    }
}
