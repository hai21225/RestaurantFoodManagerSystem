using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemHistoryAndStatusGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_item_status_histories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderItemId = table.Column<int>(type: "int", nullable: true),
                    KitchenOrderItemId = table.Column<int>(type: "int", nullable: true),
                    OldStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NewStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_status_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_item_status_histories_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_item_status_histories_users_ChangedBy",
                        column: x => x.ChangedBy,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // C-11: Accepted chỉ còn ở cấp phiếu bếp. Chuyển dữ liệu item cũ trước khi
            // thêm CHECK constraint để migration không fail trên DB đang chạy.
            migrationBuilder.Sql(
                "UPDATE order_items SET Status = 'Preparing' WHERE Status = 'Accepted';");
            migrationBuilder.Sql(
                "UPDATE kitchen_order_items SET Status = 'Preparing' WHERE Status = 'Accepted';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_Status",
                table: "orders",
                sql: "[Status] IN ('Pending','SentToKitchen','Preparing','Ready','Completed','Paid','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_items_Status",
                table: "order_items",
                sql: "[Status] IN ('Pending','Preparing','Ready','Completed','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_kitchen_orders_Status",
                table: "kitchen_orders",
                sql: "[Status] IN ('Pending','Accepted','Preparing','Ready','Completed','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_kitchen_order_items_Status",
                table: "kitchen_order_items",
                sql: "[Status] IN ('Pending','Preparing','Ready','Completed','Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_status_histories_ChangedBy",
                table: "order_item_status_histories",
                column: "ChangedBy");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_status_histories_CreatedAt",
                table: "order_item_status_histories",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_status_histories_KitchenOrderItemId",
                table: "order_item_status_histories",
                column: "KitchenOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_status_histories_OrderId",
                table: "order_item_status_histories",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_status_histories_OrderItemId",
                table: "order_item_status_histories",
                column: "OrderItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_item_status_histories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_Status",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_items_Status",
                table: "order_items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_kitchen_orders_Status",
                table: "kitchen_orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_kitchen_order_items_Status",
                table: "kitchen_order_items");
        }
    }
}
