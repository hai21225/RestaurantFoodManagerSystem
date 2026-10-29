using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsStatusCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_Status",
                table: "payments",
                sql: "[Status] IN ('Pending','Paid','Failed','Refunded','Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_Status",
                table: "payments");
        }
    }
}
