using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TableNumer",
                table: "restaurant_tables",
                newName: "TableNumber");

            migrationBuilder.RenameIndex(
                name: "IX_restaurant_tables_TableNumer",
                table: "restaurant_tables",
                newName: "IX_restaurant_tables_TableNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TableNumber",
                table: "restaurant_tables",
                newName: "TableNumer");

            migrationBuilder.RenameIndex(
                name: "IX_restaurant_tables_TableNumber",
                table: "restaurant_tables",
                newName: "IX_restaurant_tables_TableNumer");
        }
    }
}
