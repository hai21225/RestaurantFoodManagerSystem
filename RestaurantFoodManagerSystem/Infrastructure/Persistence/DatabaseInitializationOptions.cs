namespace RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// GĐ8: tách việc migrate + seed khỏi <c>Program.cs</c> để host có thể dựng trong test
/// (<c>WebApplicationFactory</c>) mà không chạm DB thật.
///
/// <para>
/// Mặc định cả hai cờ là <c>true</c> ⇒ hành vi production không đổi. Test HTTP-level đặt
/// <c>Database:MigrateOnStartup=false</c> (vì migration viết cho SQL Server) và tự tạo schema
/// SQLite bằng <c>EnsureCreated()</c>.
/// </para>
/// </summary>
public class DatabaseInitializationOptions
{
    public const string SectionName = "Database";

    /// <summary>Chạy <c>Database.MigrateAsync()</c> lúc khởi động (mặc định bật).</summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>Seed dữ liệu hệ thống (hiện là 5 role) lúc khởi động (mặc định bật, idempotent).</summary>
    public bool SeedOnStartup { get; set; } = true;
}
