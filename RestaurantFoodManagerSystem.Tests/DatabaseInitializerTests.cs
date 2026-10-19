using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// GĐ8: khoá hành vi của <see cref="DatabaseInitializer"/> sau khi tách migrate/seed ra khỏi
/// <c>Program.cs</c>. Dùng SQLite in-memory của <see cref="TestBase"/> nên không chạm DB thật và
/// không đụng schema/migration.
/// </summary>
public class DatabaseInitializerTests : TestBase
{
    [Fact]
    public void Options_DefaultToEnabled_SoProductionStartupIsUnchanged()
    {
        var options = new DatabaseInitializationOptions();

        Assert.True(options.MigrateOnStartup);
        Assert.True(options.SeedOnStartup);
    }

    [Fact]
    public async Task SeedRolesAsync_AddsFiveDefaultRoles_WhenRolesTableIsEmpty()
    {
        var inserted = await DatabaseInitializer.SeedRolesAsync(Db);

        Assert.True(inserted);

        var actual = await Db.Roles.AsNoTracking().ToListAsync();
        Assert.Equal(DatabaseInitializer.DefaultRoles.Count, actual.Count);

        foreach (var (name, description) in DatabaseInitializer.DefaultRoles)
        {
            var role = Assert.Single(actual, r => r.RoleName == name);
            Assert.Equal(description, role.RoleDescription);
        }
    }

    [Fact]
    public async Task SeedRolesAsync_IsIdempotent_AndDoesNotDuplicateRoles()
    {
        Assert.True(await DatabaseInitializer.SeedRolesAsync(Db));

        var insertedAgain = await DatabaseInitializer.SeedRolesAsync(Db);

        Assert.False(insertedAgain);
        Assert.Equal(DatabaseInitializer.DefaultRoles.Count, await Db.Roles.CountAsync());
    }

    [Fact]
    public async Task InitializeAsync_SeedsRoles_WhenMigrationIsDisabled()
    {
        // MigrateOnStartup=false vì migration viết cho SQL Server; schema SQLite đã có từ TestBase.
        using var services = new ServiceCollection()
            .AddSingleton(Db)
            .BuildServiceProvider();

        await DatabaseInitializer.InitializeAsync(
            services,
            new DatabaseInitializationOptions { MigrateOnStartup = false, SeedOnStartup = true });

        Assert.Equal(DatabaseInitializer.DefaultRoles.Count, await Db.Roles.CountAsync());
    }

    [Fact]
    public async Task InitializeAsync_DoesNotTouchDatabase_WhenBothStepsAreDisabled()
    {
        // Provider rỗng: nếu initializer cố resolve AppDbContext thì test sẽ nổ.
        using var services = new ServiceCollection().BuildServiceProvider();

        await DatabaseInitializer.InitializeAsync(
            services,
            new DatabaseInitializationOptions { MigrateOnStartup = false, SeedOnStartup = false });

        Assert.Equal(0, await Db.Roles.CountAsync());
    }
}
