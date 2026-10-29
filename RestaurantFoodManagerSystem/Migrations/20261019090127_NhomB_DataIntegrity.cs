using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class NhomB_DataIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_toppings_ToppingName",
                table: "toppings");

            migrationBuilder.DropIndex(
                name: "IX_restaurant_tables_TableNumber",
                table: "restaurant_tables");

            migrationBuilder.DropIndex(
                name: "IX_recipes_SizeId",
                table: "recipes");

            migrationBuilder.DropIndex(
                name: "IX_payments_OrderId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_ingredients_Name",
                table: "ingredients");

            migrationBuilder.DropIndex(
                name: "IX_food_sizes_FoodId_SizeName",
                table: "food_sizes");

            migrationBuilder.DropIndex(
                name: "IX_categories_CategoryName",
                table: "categories");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "restaurant_tables",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "orders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "inventory",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_toppings_ToppingName",
                table: "toppings",
                column: "ToppingName",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_tables_TableNumber",
                table: "restaurant_tables",
                column: "TableNumber",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_recipes_SizeId",
                table: "recipes",
                column: "SizeId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrderId",
                table: "payments",
                column: "OrderId",
                unique: true,
                filter: "[Status] = 'Paid'");

            migrationBuilder.CreateIndex(
                name: "IX_ingredients_Name",
                table: "ingredients",
                column: "Name",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_food_sizes_FoodId_SizeName",
                table: "food_sizes",
                columns: new[] { "FoodId", "SizeName" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_categories_CategoryName",
                table: "categories",
                column: "CategoryName",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_toppings_ToppingName",
                table: "toppings");

            migrationBuilder.DropIndex(
                name: "IX_restaurant_tables_TableNumber",
                table: "restaurant_tables");

            migrationBuilder.DropIndex(
                name: "IX_recipes_SizeId",
                table: "recipes");

            migrationBuilder.DropIndex(
                name: "IX_payments_OrderId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_ingredients_Name",
                table: "ingredients");

            migrationBuilder.DropIndex(
                name: "IX_food_sizes_FoodId_SizeName",
                table: "food_sizes");

            migrationBuilder.DropIndex(
                name: "IX_categories_CategoryName",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "inventory");

            migrationBuilder.CreateIndex(
                name: "IX_toppings_ToppingName",
                table: "toppings",
                column: "ToppingName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_tables_TableNumber",
                table: "restaurant_tables",
                column: "TableNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipes_SizeId",
                table: "recipes",
                column: "SizeId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrderId",
                table: "payments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ingredients_Name",
                table: "ingredients",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_food_sizes_FoodId_SizeName",
                table: "food_sizes",
                columns: new[] { "FoodId", "SizeName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_categories_CategoryName",
                table: "categories",
                column: "CategoryName",
                unique: true);
        }
    }
}
