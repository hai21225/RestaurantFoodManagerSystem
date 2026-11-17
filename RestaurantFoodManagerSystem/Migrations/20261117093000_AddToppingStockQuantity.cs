using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261117093000_AddToppingStockQuantity")]
    /// <inheritdoc />
    public partial class AddToppingStockQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "toppings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "toppings");
        }
    }
}
