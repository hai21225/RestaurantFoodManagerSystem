using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTransactionCodeUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // C-19: trước đây TransactionCode lấy thẳng từ client, thiếu thì để ""
            // ⇒ nhiều dòng trùng nhau. Trước khi tạo unique index phải dọn dữ liệu cũ.

            // 1) Dòng chưa có mã ⇒ sinh mã giao dịch nội bộ (duy nhất theo PaymentId).
            migrationBuilder.Sql(
                """
                UPDATE payments
                SET TransactionCode = CONCAT(
                        'LEGACY-',
                        RIGHT('000000000000' + CAST(PaymentId AS varchar(20)), 12),
                        '-',
                        REPLACE(CONVERT(varchar(36), NEWID()), '-', ''))
                WHERE TransactionCode IS NULL OR LTRIM(RTRIM(TransactionCode)) = '';
                """);

            // 2) Mã đang trùng ⇒ giữ bản đầu tiên, các bản sau ghi hậu tố riêng.
            migrationBuilder.Sql(
                """
                WITH duplicated AS (
                    SELECT PaymentId,
                           ROW_NUMBER() OVER (PARTITION BY TransactionCode ORDER BY PaymentId) AS rn
                    FROM payments
                    WHERE TransactionCode <> ''
                )
                UPDATE p
                SET TransactionCode = LEFT(
                        CONCAT(
                            p.TransactionCode,
                            '-D',
                            REPLACE(CONVERT(varchar(36), NEWID()), '-', '')),
                        100)
                FROM payments AS p
                INNER JOIN duplicated AS d ON p.PaymentId = d.PaymentId
                WHERE d.rn > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_payments_TransactionCode",
                table: "payments");

            migrationBuilder.CreateIndex(
                name: "IX_payments_TransactionCode",
                table: "payments",
                column: "TransactionCode",
                unique: true,
                filter: "[TransactionCode] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_TransactionCode",
                table: "payments");

            migrationBuilder.CreateIndex(
                name: "IX_payments_TransactionCode",
                table: "payments",
                column: "TransactionCode");
        }
    }
}
