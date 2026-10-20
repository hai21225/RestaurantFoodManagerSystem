using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// GĐ6/A1 + A2 — endpoints khởi tạo hệ thống và quản lý nhân viên
/// (<c>GET /api/setup/status</c>, <c>POST /api/setup/first-admin</c>,
/// <c>GET /api/users</c>, <c>POST /api/users</c>).
/// Test ở tầng service (SQLite in-memory) theo cách của TestBase.
/// </summary>
public sealed class SetupFlowTests : TestBase
{
    private static RegisterRequest FirstAdminRequest(string password = "secret123")
        => new()
        {
            UserName = $"admin-{Guid.NewGuid():N}",
            Email = $"admin-{Guid.NewGuid():N}@example.test",
            Password = password,
            ConfirmPassword = password,
            FullName = "Quản trị viên",
            Phone = "0900000000"
        };

    // ---------- GET /api/setup/status ----------

    [Fact]
    public async Task SetupRequired_IsTrue_WhenNoUserExists()
    {
        Assert.True(await SetupSvc.IsSetupRequiredAsync());
    }

    [Fact]
    public async Task SetupRequired_IsFalse_WhenAtLeastOneUserExists()
    {
        SeedUser();

        Assert.False(await SetupSvc.IsSetupRequiredAsync());
    }

    // ---------- POST /api/setup/first-admin ----------

    [Fact]
    public async Task CreateFirstAdmin_CreatesActiveUser_WithAdminRole()
    {
        SeedRole("Admin");
        var request = FirstAdminRequest();

        var created = await SetupSvc.CreateFirstAdminAsync(request);

        Assert.True(created);
        var user = await Db.Users.AsNoTracking()
            .FirstAsync(u => u.UserName == request.UserName);
        Assert.True(user.IsActive);

        var roleNames = await Db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == user.Id)
            .Join(Db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.RoleId, (ur, r) => r.RoleName)
            .ToListAsync();
        Assert.Equal(["Admin"], roleNames);
    }

    [Fact]
    public async Task CreateFirstAdmin_StoresHashedPassword_NotPlaintext()
    {
        SeedRole("Admin");
        var request = FirstAdminRequest("my-secret-123");

        await SetupSvc.CreateFirstAdminAsync(request);

        var stored = await Db.Users.AsNoTracking()
            .Where(u => u.UserName == request.UserName)
            .Select(u => u.UserPassword)
            .FirstAsync();

        Assert.NotEqual("my-secret-123", stored);
        Assert.True(BCrypt.Net.BCrypt.Verify("my-secret-123", stored));
    }

    [Fact]
    public async Task CreateFirstAdmin_ReturnsFalse_WhenUsersAlreadyExist()
    {
        SeedRole("Admin");
        SeedUser(); // hệ thống đã có user ⇒ không được phép tạo Admin qua setup

        var created = await SetupSvc.CreateFirstAdminAsync(FirstAdminRequest());

        Assert.False(created);
    }

    [Fact]
    public async Task CreateFirstAdmin_Throws_WhenPasswordsDoNotMatch()
    {
        SeedRole("Admin");
        var request = FirstAdminRequest();
        request.ConfirmPassword = "khac-mat-khau";

        await Assert.ThrowsAsync<ArgumentException>(
            () => SetupSvc.CreateFirstAdminAsync(request));
    }

    // ---------- GET /api/users ----------

    [Fact]
    public async Task GetEmployees_ReturnsRolesPerUser()
    {
        var admin = SeedUser();
        var waiter = SeedUser();
        var adminRole = SeedRole("Admin");
        var waiterRole = SeedRole("Waiter");
        Db.UserRoles.Add(new UserRoles { UserId = admin.Id, RoleId = adminRole.RoleId });
        Db.UserRoles.Add(new UserRoles { UserId = waiter.Id, RoleId = waiterRole.RoleId });
        Db.SaveChanges();

        var employees = await new UserService(Db).GetAllWithRolesAsync();

        Assert.Equal(2, employees.Count);
        Assert.Equal(["Admin"], employees.Single(e => e.UserId == admin.Id).Roles);
        Assert.Equal(["Waiter"], employees.Single(e => e.UserId == waiter.Id).Roles);
    }

    [Fact]
    public async Task GetEmployees_ReturnsEmptyRoles_WhenUserHasNoRole()
    {
        var user = SeedUser();

        var employees = await new UserService(Db).GetAllWithRolesAsync();

        Assert.Empty(employees.Single(e => e.UserId == user.Id).Roles);
    }

    // ---------- POST /api/users ----------

    [Fact]
    public async Task CreateEmployee_AssignsRole_AndHashesPassword()
    {
        SeedRole("Chef");
        var dto = new UserDto
        {
            UserName = $"chef-{Guid.NewGuid():N}",
            Email = $"chef-{Guid.NewGuid():N}@example.test",
            Password = "chef-secret",
            ConfirmPassword = "chef-secret",
            FullName = "Đầu bếp",
            Phone = "0911111111"
        };

        var created = await Auth.CreateUserWithRoleAsync(dto, "Chef");

        Assert.True(created);
        var user = await Db.Users.AsNoTracking().FirstAsync(u => u.UserName == dto.UserName);
        Assert.NotEqual("chef-secret", user.UserPassword);

        var roleNames = await Auth.GetRoleNamesByUserIdAsync(user.Id);
        Assert.Equal(["Chef"], roleNames);
    }

    [Fact]
    public async Task CreateEmployee_WithoutRoleName_LeavesUserWithoutRole()
    {
        var dto = new UserDto
        {
            UserName = $"norole-{Guid.NewGuid():N}",
            Email = $"norole-{Guid.NewGuid():N}@example.test",
            Password = "secret123",
            ConfirmPassword = "secret123"
        };

        var created = await Auth.CreateUserWithRoleAsync(dto, null);

        Assert.True(created);
        var user = await Db.Users.AsNoTracking().FirstAsync(u => u.UserName == dto.UserName);
        Assert.Empty(await Auth.GetRoleNamesByUserIdAsync(user.Id));
    }

    [Fact]
    public async Task CreateEmployee_RollsBackUser_WhenRoleDoesNotExist()
    {
        var dto = new UserDto
        {
            UserName = $"badrole-{Guid.NewGuid():N}",
            Email = $"badrole-{Guid.NewGuid():N}@example.test",
            Password = "secret123",
            ConfirmPassword = "secret123"
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => Auth.CreateUserWithRoleAsync(dto, "KhongTonTai"));

        // transaction phải rollback ⇒ không còn user nào được tạo
        Assert.False(await Db.Users.AnyAsync(u => u.UserName == dto.UserName));
    }

    [Fact]
    public async Task CreateEmployee_Throws_WhenUsernameAlreadyExists()
    {
        var existing = SeedUser();
        var dto = new UserDto
        {
            UserName = existing.UserName,
            Email = $"dup-{Guid.NewGuid():N}@example.test",
            Password = "secret123",
            ConfirmPassword = "secret123"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Auth.CreateUserWithRoleAsync(dto, null));
    }
}
