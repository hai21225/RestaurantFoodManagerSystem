using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class V2Upgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "users",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "QrToken",
                table: "restaurant_tables",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                table: "payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerUserId",
                table: "orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReservationId",
                table: "orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cashier_shifts",
                columns: table => new
                {
                    ShiftId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CashierUserId = table.Column<int>(type: "int", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpeningCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Variance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CashTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CardTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MomoTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OtherTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cashier_shifts", x => x.ShiftId);
                    table.ForeignKey(
                        name: "FK_cashier_shifts_users_CashierUserId",
                        column: x => x.CashierUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scope = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "table_reservations",
                columns: table => new
                {
                    ReservationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TableId = table.Column<int>(type: "int", nullable: false),
                    CustomerUserId = table.Column<int>(type: "int", nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PartySize = table.Column<int>(type: "int", nullable: false),
                    ReservedFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReservedTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DepositAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DepositStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DepositTransactionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConfirmedBy = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SeatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_reservations", x => x.ReservationId);
                    table.ForeignKey(
                        name: "FK_table_reservations_restaurant_tables_TableId",
                        column: x => x.TableId,
                        principalTable: "restaurant_tables",
                        principalColumn: "TableId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_table_reservations_users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_tables_QrToken",
                table: "restaurant_tables",
                column: "QrToken",
                unique: true,
                filter: "[QrToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payments_ShiftId",
                table: "payments",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_CustomerUserId",
                table: "orders",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_ReservationId",
                table: "orders",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_CashierUserId",
                table: "cashier_shifts",
                column: "CashierUserId",
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_Status",
                table: "cashier_shifts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_ExpiresAt",
                table: "idempotency_keys",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_Scope_Key",
                table: "idempotency_keys",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_reservations_CustomerUserId",
                table: "table_reservations",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_table_reservations_OrderId",
                table: "table_reservations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_table_reservations_Status",
                table: "table_reservations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_table_reservations_TableId_ReservedFrom_ReservedTo",
                table: "table_reservations",
                columns: new[] { "TableId", "ReservedFrom", "ReservedTo" });

            migrationBuilder.AddForeignKey(
                name: "FK_orders_table_reservations_ReservationId",
                table: "orders",
                column: "ReservationId",
                principalTable: "table_reservations",
                principalColumn: "ReservationId");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_users_CustomerUserId",
                table: "orders",
                column: "CustomerUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_cashier_shifts_ShiftId",
                table: "payments",
                column: "ShiftId",
                principalTable: "cashier_shifts",
                principalColumn: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_table_reservations_ReservationId",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "FK_orders_users_CustomerUserId",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "FK_payments_cashier_shifts_ShiftId",
                table: "payments");

            migrationBuilder.DropTable(
                name: "cashier_shifts");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "table_reservations");

            migrationBuilder.DropIndex(
                name: "IX_restaurant_tables_QrToken",
                table: "restaurant_tables");

            migrationBuilder.DropIndex(
                name: "IX_payments_ShiftId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_orders_CustomerUserId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_ReservationId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "users");

            migrationBuilder.DropColumn(
                name: "QrToken",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CustomerUserId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "orders");
        }
    }
}
