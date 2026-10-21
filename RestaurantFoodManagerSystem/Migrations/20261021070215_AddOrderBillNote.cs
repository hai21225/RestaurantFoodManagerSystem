using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderBillNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "orders");
        }
    }
}
