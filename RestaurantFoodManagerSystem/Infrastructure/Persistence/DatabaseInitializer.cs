using Microsoft.EntityFrameworkCore;

namespace RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// GĐ8: một chỗ duy nhất chịu trách nhiệm migrate schema và seed dữ liệu hệ thống lúc khởi động.
/// Trước GĐ8 khối này nằm trực tiếp trong <c>Program.cs</c>, khiến host test
/// (<c>WebApplicationFactory</c>) luôn cố migrate DB SQL Server thật.
///
/// <para>
/// <see cref="InitializeAsync"/> nhận <see cref="IServiceProvider"/> của app (không phải scope),
/// tự mở scope riêng, nên gọi được cả từ <c>Program.cs</c> lẫn test.
/// </para>
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>Role mặc định của hệ thống, dùng chung cho seed và test.</summary>
    /// <summary>Khu vực bàn mặc định cho lần khởi động đầu tiên.</summary>
    public static readonly IReadOnlyList<(string Name, string Description, int DisplayOrder)> DefaultTableAreas =
    [
        ("Sảnh chính", "Khu vực phục vụ chung", 1),
        ("Lầu 2", "Khu vực bàn trên lầu", 2),
        ("Ngoài trời", "Khu vực sân ngoài", 3)
    ];

    public static readonly IReadOnlyList<(string Name, string Description)> DefaultRoles =
    [
        ("Admin", "Người quản lí cấp cao"),
        ("WarehouseManager", "Nhân viên quản lí kho"),
        ("Chef", "Nhân viên bếp"),
        ("Cashier", "Nhân viên thu ngân"),
        ("Waiter", "Nhân viên phục vụ"),
    ];

    /// <summary>
    /// Migrate (nếu bật) rồi seed (nếu bật). Không làm gì khi cả hai cờ đều tắt.
    /// Idempotent: seed chỉ chạy khi bảng <c>Roles</c> đang rỗng.
    /// </summary>
    public static async Task InitializeAsync(
        IServiceProvider services,
        DatabaseInitializationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        options ??= new DatabaseInitializationOptions();
        if (!options.MigrateOnStartup && !options.SeedOnStartup)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (options.MigrateOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (options.SeedOnStartup)
        {
            await SeedRolesAsync(db, cancellationToken);
            await SeedTableAreasAsync(db, cancellationToken);
        }
    }

    /// <summary>
    /// Thêm 5 role mặc định nếu bảng <c>Roles</c> rỗng.
    /// </summary>
    /// <returns><c>true</c> nếu lần gọi này đã thêm role; <c>false</c> nếu bảng đã có dữ liệu.</returns>
    public static async Task<bool> SeedRolesAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.Roles.AnyAsync(cancellationToken))
        {
            return false;
        }

        db.Roles.AddRange(DefaultRoles.Select(role => new Roles
        {
            RoleName = role.Name,
            RoleDescription = role.Description,
        }));

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Thêm 3 khu vực bàn mặc định nếu bảng <c>table_areas</c> rỗng, sau đó gán các bàn
    /// chưa có khu vực vào khu vực mặc định đầu tiên. Idempotent: chạy lại không nhân đôi dữ liệu.
    /// </summary>
    public static async Task<bool> SeedTableAreasAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var created = false;
        if (!await db.TableAreas.AnyAsync(cancellationToken))
        {
            db.TableAreas.AddRange(DefaultTableAreas.Select(area => new TableArea
            {
                AreaName = area.Name,
                Description = area.Description,
                DisplayOrder = area.DisplayOrder,
                IsActive = true
            }));

            await db.SaveChangesAsync(cancellationToken);
            created = true;
        }

        var defaultAreaId = await db.TableAreas
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.AreaId)
            .Select(x => (int?)x.AreaId)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultAreaId.HasValue)
        {
            var unassigned = await db.RestaurantTables
                .Where(x => x.AreaId == null)
                .ToListAsync(cancellationToken);

            if (unassigned.Count > 0)
            {
                foreach (var table in unassigned)
                {
                    table.AreaId = defaultAreaId.Value;
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return created;
    }
}
