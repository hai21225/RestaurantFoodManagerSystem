using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemCancellationAndItemLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "order_items",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "order_items",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledBy",
                table: "order_items",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderItemId",
                table: "inventory_transactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_items_CancelledBy",
                table: "order_items",
                column: "CancelledBy");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_OrderItemId",
                table: "inventory_transactions",
                column: "OrderItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_transactions_order_items_OrderItemId",
                table: "inventory_transactions",
                column: "OrderItemId",
                principalTable: "order_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_order_items_users_CancelledBy",
                table: "order_items",
                column: "CancelledBy",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_transactions_order_items_OrderItemId",
                table: "inventory_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_order_items_users_CancelledBy",
                table: "order_items");

            migrationBuilder.DropIndex(
                name: "IX_order_items_CancelledBy",
                table: "order_items");

            migrationBuilder.DropIndex(
                name: "IX_inventory_transactions_OrderItemId",
                table: "inventory_transactions");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "OrderItemId",
                table: "inventory_transactions");
        }
    }
}
