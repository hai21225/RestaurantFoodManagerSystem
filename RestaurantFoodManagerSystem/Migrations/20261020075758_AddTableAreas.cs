using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantFoodManagerSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTableAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AreaId",
                table: "restaurant_tables",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "table_areas",
                columns: table => new
                {
                    AreaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AreaName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_areas", x => x.AreaId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_tables_AreaId",
                table: "restaurant_tables",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_table_areas_AreaName",
                table: "table_areas",
                column: "AreaName",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_restaurant_tables_table_areas_AreaId",
                table: "restaurant_tables",
                column: "AreaId",
                principalTable: "table_areas",
                principalColumn: "AreaId",
                onDelete: ReferentialAction.SetNull);

            // Dữ liệu mặc định để bàn cũ vẫn có khu vực ngay sau khi migrate,
            // kể cả khi Database:SeedOnStartup bị tắt.
            migrationBuilder.Sql("""
                INSERT INTO [table_areas] ([AreaName], [Description], [DisplayOrder], [IsActive])
                VALUES
                    (N'Sảnh chính', N'Khu vực phục vụ chung', 1, 1),
                    (N'Lầu 2', N'Khu vực bàn trên lầu', 2, 1),
                    (N'Ngoài trời', N'Khu vực sân ngoài', 3, 1);

                UPDATE [restaurant_tables]
                SET [AreaId] = (
                    SELECT TOP 1 [AreaId]
                    FROM [table_areas]
                    ORDER BY [DisplayOrder], [AreaId])
                WHERE [AreaId] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_restaurant_tables_table_areas_AreaId",
                table: "restaurant_tables");

            migrationBuilder.DropTable(
                name: "table_areas");

            migrationBuilder.DropIndex(
                name: "IX_restaurant_tables_AreaId",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "AreaId",
                table: "restaurant_tables");
        }
    }
}
