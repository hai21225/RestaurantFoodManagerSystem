using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using RestaurantFoodManagerSystem.Migrations;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

// SQL Server thật, database tạm riêng; không chạy trên database nghiệp vụ hoặc SQLite.
public sealed class LegacyQuantityFactAttribute : FactAttribute
{
    public LegacyQuantityFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("C23_TEST_SQLSERVER")))
            Skip = "Set C23_TEST_SQLSERVER to a SQL Server master connection to run isolated migration tests.";
    }
}

public sealed class LegacyQuantityTheoryAttribute : TheoryAttribute
{
    public LegacyQuantityTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("C23_TEST_SQLSERVER")))
            Skip = "Set C23_TEST_SQLSERVER to a SQL Server master connection to run isolated migration tests.";
    }
}

public sealed class LegacyQuantitySqlServerFixture : IDisposable
{
    private readonly string? _masterConnection;
    private readonly string _database = $"RestaurantFoodManager_C23_Test_{Guid.NewGuid():N}";
    public string ConnectionString { get; } = string.Empty;

    public LegacyQuantitySqlServerFixture()
    {
        var configured = Environment.GetEnvironmentVariable("C23_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(configured)) return;

        var builder = new SqlConnectionStringBuilder(configured);
        if (!string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("C23_TEST_SQLSERVER must explicitly select master; tests create their own database.");
        _masterConnection = builder.ConnectionString;
        using (var connection = new SqlConnection(_masterConnection))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{_database}]";
            command.ExecuteNonQuery();
        }
        builder.InitialCatalog = _database;
        ConnectionString = builder.ConnectionString;
    }

    public SqlConnection Open()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        if (_masterConnection is null) return;
        SqlConnection.ClearAllPools();
        using var connection = new SqlConnection(_masterConnection);
        connection.Open();
        using var command = connection.CreateCommand();
        // Chỉ xóa database mang GUID do chính fixture này tạo, không nhận tên từ bên ngoài.
        command.CommandText = $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];";
        command.ExecuteNonQuery();
    }
}

public sealed class LegacyOrderQuantityMigrationTests : IClassFixture<LegacyQuantitySqlServerFixture>
{
    private const string MigrationId = "20261101103000_NormalizeLegacyOrderQuantities";
    private readonly LegacyQuantitySqlServerFixture _fixture;

    public LegacyOrderQuantityMigrationTests(LegacyQuantitySqlServerFixture fixture) => _fixture = fixture;

    private static Migration Migration() => new NormalizeLegacyOrderQuantities
    {
        ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer"
    };

    private static string UpSql() => Assert.IsType<SqlOperation>(Assert.Single(Migration().UpOperations)).Sql;

    private void Reset(string orderType = "decimal(18,3)", string kitchenType = "decimal(18,3)")
    {
        using var connection = _fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            DROP TABLE IF EXISTS [dbo].[kitchen_order_items];
            DROP TABLE IF EXISTS [dbo].[order_items];
            CREATE TABLE [dbo].[order_items] (Id int NOT NULL PRIMARY KEY, Quanlity {orderType} NOT NULL);
            CREATE TABLE [dbo].[kitchen_order_items] (Id int NOT NULL PRIMARY KEY, Quanlity {kitchenType} NOT NULL);
            INSERT INTO [dbo].[order_items] VALUES (1, 1), (2, 2147483647), (3, -2147483648);
            INSERT INTO [dbo].[kitchen_order_items] VALUES (1, 1), (2, 2147483647), (3, -2147483648);
            """;
        command.ExecuteNonQuery();
    }

    private void Apply()
    {
        using var connection = _fixture.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = UpSql();
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private string ColumnTypes()
    {
        using var connection = _fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT STRING_AGG(CONVERT(nvarchar(max), TYPE_NAME(user_type_id)), ',')
                WITHIN GROUP (ORDER BY CASE WHEN OBJECT_NAME(object_id)='order_items' THEN 0 ELSE 1 END)
            FROM sys.columns WHERE name='Quanlity'
              AND object_id IN (OBJECT_ID('dbo.order_items'), OBJECT_ID('dbo.kitchen_order_items'));
            """;
        return (string)command.ExecuteScalar()!;
    }

    private AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(_fixture.ConnectionString).Options);

    [LegacyQuantityTheory]
    [InlineData("decimal(18,3)", "decimal(18,3)")]
    [InlineData("int", "int")]
    [InlineData("int", "decimal(18,3)")]
    [InlineData("numeric(18,3)", "int")]
    public async Task Up_PreservesValues_AndCanBeReadByCurrentModels(string orderType, string kitchenType)
    {
        Reset(orderType, kitchenType);
        Apply();
        Apply(); // Script chạy lặp không làm mất dữ liệu hoặc đưa int về decimal.
        Assert.Equal("int,int", ColumnTypes());
        using var db = Context();
        var expected = new[] { 1, int.MaxValue, int.MinValue };
        Assert.Equal(expected, await db.OrderItems.OrderBy(x => x.Id).Select(x => x.Quanlity).ToArrayAsync());
        Assert.Equal(expected, await db.KitchenOrderItems.OrderBy(x => x.Id).Select(x => x.Quanlity).ToArrayAsync());
    }

    [LegacyQuantityTheory]
    [InlineData("order_items", "1.125")]
    [InlineData("kitchen_order_items", "1.125")]
    [InlineData("order_items", "2147483648")]
    [InlineData("kitchen_order_items", "2147483648")]
    [InlineData("order_items", "-2147483649")]
    [InlineData("kitchen_order_items", "-2147483649")]
    public void Up_RejectsUnsafeValues_WithoutChangingEitherColumn(string table, string value)
    {
        Reset();
        using var connection = _fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE [dbo].[{table}] SET Quanlity=@value WHERE Id=1";
        command.Parameters.AddWithValue("@value", decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        var error = Assert.Throws<SqlException>(Apply);
        Assert.Equal(51023, error.Number);
        Assert.Contains(table + ".Quanlity", error.Message);
        Assert.Equal("decimal,decimal", ColumnTypes());
        command.CommandText = $"SELECT Quanlity FROM [dbo].[{table}] WHERE Id=1";
        Assert.Equal(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), command.ExecuteScalar());
    }

    [LegacyQuantityFact]
    public void Up_RejectsUnexpectedSchema_BeforeChangingOtherTable()
    {
        Reset(kitchenType: "bigint");
        var error = Assert.Throws<SqlException>(Apply);
        Assert.Equal(51023, error.Number);
        Assert.Contains("unexpected kitchen_order_items", error.Message);
        Assert.Equal("decimal,bigint", ColumnTypes());
    }

    [LegacyQuantityTheory]
    [InlineData("order_items")]
    [InlineData("kitchen_order_items")]
    public void Up_RejectsNullableLegacyColumns_WithoutChangingSchema(string table)
    {
        Reset();
        using var connection = _fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE [dbo].[{table}] ALTER COLUMN Quanlity decimal(18,3) NULL";
        command.ExecuteNonQuery();
        var error = Assert.Throws<SqlException>(Apply);
        Assert.Equal(51023, error.Number);
        Assert.Contains("unexpected " + table, error.Message);
        Assert.Equal("decimal,decimal", ColumnTypes());
    }

    [LegacyQuantityFact]
    public void Up_RollsBackFirstAlter_WhenSecondColumnHasAnUnsupportedDependency()
    {
        Reset();
        using var connection = _fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE INDEX IX_C23_QuantityDependency ON [dbo].[kitchen_order_items] (Quanlity)";
        command.ExecuteNonQuery();
        var error = Assert.Throws<SqlException>(Apply);
        Assert.Contains("IX_C23_QuantityDependency", error.Message);
        Assert.Equal("decimal,decimal", ColumnTypes());
        command.CommandText = "SELECT COUNT(*) FROM [dbo].[order_items] WHERE Quanlity IN (1,2147483647,-2147483648)";
        Assert.Equal(3, (int)command.ExecuteScalar()!);
    }

    [LegacyQuantityFact]
    public void Up_RequiresTransaction_AndDownKeepsCorrectIntContract()
    {
        Reset();
        using var connection = _fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = UpSql();
        Assert.Equal(51023, Assert.Throws<SqlException>(() => command.ExecuteNonQuery()).Number);
        Assert.Equal("decimal,decimal", ColumnTypes());
        Assert.Empty(Migration().DownOperations);
    }

    [LegacyQuantityFact]
    public void Ef_DiscoversMigration_AndGeneratesTransactionalUpgrade()
    {
        using var db = Context();
        Assert.Contains(MigrationId, db.Database.GetMigrations());
        var script = db.GetService<IMigrator>().GenerateScript("20261021070215_AddOrderBillNote", MigrationId);
        Assert.Contains("BEGIN TRANSACTION", script);
        Assert.Contains("THROW 51023", script);
        Assert.Contains(MigrationId, script);
        Assert.True(script.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal) < script.IndexOf("THROW 51023", StringComparison.Ordinal));
    }
}
