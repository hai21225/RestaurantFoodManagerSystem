using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddMomoPaymentProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "PaidAt",
                table: "payments",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            // CreatedAt cho các bản ghi cũ: mặc định lấy theo thời điểm thu tiền (PaidAt),
            // không phải thời điểm chạy migration.
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "payments",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.Sql(
                "UPDATE [payments] SET [CreatedAt] = COALESCE([PaidAt], GETUTCDATE());");

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "payments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "payments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Direct");

            migrationBuilder.AddColumn<string>(
                name: "ProviderResponse",
                table: "payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderTransactionId",
                table: "payments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "payments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_RequestId",
                table: "payments",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_RequestId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ProviderResponse",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ProviderTransactionId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "payments");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PaidAt",
                table: "payments",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}
