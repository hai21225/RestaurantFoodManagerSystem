using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Auth;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// P4: kiểm tra HTTP-level cho hồ sơ tự phục vụ, đổi mật khẩu, danh sách role và thu hồi role
/// trên host thật có <c>Auth:Enforce=true</c>.
/// </summary>
public sealed class UserAccountHttpTests : IClassFixture<EnforcedRestaurantApiFactory>
{
    private const string InitialPassword = "init-pass";

    private readonly EnforcedRestaurantApiFactory _factory;

    public UserAccountHttpTests(EnforcedRestaurantApiFactory factory)
    {
        _factory = factory;
    }

    // ==========================================================
    // GET /api/users/me
    // ==========================================================

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_ReturnsOwnProfile_ForNonAdminRole()
    {
        var (userId, userName, _) = SeedUser("Waiter");
        using var client = CreateClient(userId, userName, AuthPolicies.Roles.Waiter);

        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId, json.GetProperty("userId").GetInt32());
        Assert.Equal(userName, json.GetProperty("userName").GetString());
        Assert.Equal([AuthPolicies.Roles.Waiter], json.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        Assert.False(json.TryGetProperty("password", out _));
        Assert.False(json.TryGetProperty("userPassword", out _));
    }

    // ==========================================================
    // PUT /api/users/me
    // ==========================================================

    [Fact]
    public async Task UpdateMe_UpdatesProfile_ButCannotChangeRolesOrActiveState()
    {
        var (userId, userName, _) = SeedUser("Cashier");
        using var client = CreateClient(userId, userName, AuthPolicies.Roles.Cashier);

        var response = await client.PutAsJsonAsync("/api/users/me", new
        {
            fullName = "  Trần Thị B  ",
            email = $"updated-{Guid.NewGuid():N}@example.test",
            phone = "0912345678",
            // Cố tình gửi field ngoài phạm vi cho phép (mass assignment).
            isActive = false,
            roles = new[] { AuthPolicies.Roles.Admin }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Trần Thị B", json.GetProperty("fullName").GetString());
        Assert.Equal("0912345678", json.GetProperty("phone").GetString());
        Assert.True(json.GetProperty("isActive").GetBoolean());
        Assert.Equal([AuthPolicies.Roles.Cashier], json.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task UpdateMe_DuplicateEmail_Returns400()
    {
        var (firstId, firstUserName, _) = SeedUser();
        var second = SeedUser();

        using var client = CreateClient(firstId, firstUserName, AuthPolicies.Roles.Waiter);
        var response = await client.PutAsJsonAsync("/api/users/me", new
        {
            fullName = "Test User",
            email = second.Email,
            phone = "0000000000"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Email already exists", json.GetProperty("message").GetString());
    }

    // ==========================================================
    // Batch A — Admin quản lý nhân viên
    // ==========================================================

    [Fact]
    public async Task Admin_CanReadUpdateLockAndResetEmployee()
    {
        var (adminId, adminName, _) = SeedUser(AuthPolicies.Roles.Admin);
        var (targetId, targetName, _) = SeedUser(AuthPolicies.Roles.Waiter);
        using var admin = CreateClient(adminId, adminName, AuthPolicies.Roles.Admin);

        var detail = await admin.GetAsync($"/api/users/{targetId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailJson = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(targetId, detailJson.GetProperty("userId").GetInt32());
        Assert.Equal([AuthPolicies.Roles.Waiter], detailJson.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));

        var newEmail = $"employee-{Guid.NewGuid():N}@example.test";
        var updated = await admin.PutAsJsonAsync($"/api/users/{targetId}", new
        {
            fullName = "  Nhân Viên Đã Sửa  ",
            email = newEmail,
            phone = "0900111222"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedJson = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Nhân Viên Đã Sửa", updatedJson.GetProperty("fullName").GetString());
        Assert.Equal(newEmail, updatedJson.GetProperty("email").GetString());

        var locked = await admin.PatchAsync($"/api/users/{targetId}/active?active=false", null);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.False((await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean());

        using var anonymous = _factory.CreateClient();
        var loginWhileLocked = await anonymous.PostAsJsonAsync("/api/auth/login", new
        {
            userName = targetName,
            password = InitialPassword
        });
        Assert.Equal(HttpStatusCode.Unauthorized, loginWhileLocked.StatusCode);

        var unlocked = await admin.PatchAsync($"/api/users/{targetId}/active?active=true", null);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        Assert.True((await unlocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean());

        var reset = await admin.PostAsJsonAsync($"/api/users/{targetId}/reset-password", new
        {
            newPassword = "reset-pass",
            confirmPassword = "reset-pass"
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var oldLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new
        {
            userName = targetName,
            password = InitialPassword
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new
        {
            userName = targetName,
            password = "reset-pass"
        });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotLockOwnAccount()
    {
        var (adminId, adminName, _) = SeedUser(AuthPolicies.Roles.Admin);
        using var admin = CreateClient(adminId, adminName, AuthPolicies.Roles.Admin);

        var response = await admin.PatchAsync($"/api/users/{adminId}/active?active=false", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("You cannot lock your own account", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task EmployeeManagement_ForNonAdmin_Returns403()
    {
        var (waiterId, waiterName, _) = SeedUser(AuthPolicies.Roles.Waiter);
        var (targetId, _, _) = SeedUser();
        using var waiter = CreateClient(waiterId, waiterName, AuthPolicies.Roles.Waiter);

        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync($"/api/users/{targetId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PutAsJsonAsync($"/api/users/{targetId}", new
        {
            fullName = "Hacked",
            email = "hacked@example.test"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PatchAsync($"/api/users/{targetId}/active?active=false", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsJsonAsync($"/api/users/{targetId}/reset-password", new
        {
            newPassword = "reset-pass",
            confirmPassword = "reset-pass"
        })).StatusCode);
    }

    // ==========================================================
    // POST /api/auth/change-password
    // ==========================================================

    [Fact]
    public async Task ChangePassword_WrongCurrent_Returns400_ThenNewPasswordWorks()
    {
        var (userId, userName, _) = SeedUser("Waiter");
        using var client = CreateClient(userId, userName, AuthPolicies.Roles.Waiter);

        var wrong = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "wrong-pass",
            newPassword = "new-pass-1",
            confirmNewPassword = "new-pass-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var wrongJson = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Current password is incorrect", wrongJson.GetProperty("message").GetString());

        var changed = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = InitialPassword,
            newPassword = "new-pass-1",
            confirmNewPassword = "new-pass-1"
        });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        using var anonymous = _factory.CreateClient();
        var oldLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            password = InitialPassword
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            password = "new-pass-1"
        });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "a",
            newPassword = "b",
            confirmNewPassword = "b"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ==========================================================
    // GET /api/roles
    // ==========================================================

    [Fact]
    public async Task GetRoles_ForAdmin_ReturnsTheFiveSeededRoles()
    {
        using var client = CreateClient(1, "p4-role-admin", AuthPolicies.Roles.Admin);

        var response = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var names = json.EnumerateArray().Select(x => x.GetProperty("roleName").GetString()).ToList();

        Assert.Equal(5, names.Count);
        Assert.Contains(AuthPolicies.Roles.Admin, names);
        Assert.Contains(AuthPolicies.Roles.WarehouseManager, names);
        Assert.Contains(AuthPolicies.Roles.Chef, names);
        Assert.Contains(AuthPolicies.Roles.Cashier, names);
        Assert.Contains(AuthPolicies.Roles.Waiter, names);
        // Không trả ma trận quyền — quyết định P4: ma trận là policy trong code.
        Assert.DoesNotContain(json.EnumerateArray(), row => row.TryGetProperty("permissions", out _));
    }

    // ==========================================================
    // DELETE /api/users/{id}/roles/{roleName}
    // ==========================================================

    [Fact]
    public async Task DeleteRole_AdminRemovesAssignment()
    {
        var (adminId, adminName, _) = SeedUser(AuthPolicies.Roles.Admin);
        var (targetId, _, _) = SeedUser(AuthPolicies.Roles.Waiter);
        using var admin = CreateClient(adminId, adminName, AuthPolicies.Roles.Admin);

        var response = await admin.DeleteAsync($"/api/users/{targetId}/roles/Waiter");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        var target = list.EnumerateArray().Single(x => x.GetProperty("userId").GetInt32() == targetId);
        Assert.DoesNotContain(AuthPolicies.Roles.Waiter, target.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task DeleteRole_WhenNotAssigned_Returns404()
    {
        var (adminId, adminName, _) = SeedUser(AuthPolicies.Roles.Admin);
        var (targetId, _, _) = SeedUser();
        using var admin = CreateClient(adminId, adminName, AuthPolicies.Roles.Admin);

        var response = await admin.DeleteAsync($"/api/users/{targetId}/roles/Waiter");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRole_ForNonAdmin_Returns403()
    {
        var (userId, userName, _) = SeedUser(AuthPolicies.Roles.Waiter);
        var (adminId, _, _) = SeedUser(AuthPolicies.Roles.Admin);
        using var waiter = CreateClient(userId, userName, AuthPolicies.Roles.Waiter);

        var response = await waiter.DeleteAsync($"/api/users/{adminId}/roles/Admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRole_LastAdmin_Returns400()
    {
        // Dùng factory riêng để DB chỉ có đúng một Admin, không phụ thuộc thứ tự test khác.
        using var factory = new EnforcedRestaurantApiFactory();
        var (adminId, adminName, _) = SeedUser(factory, AuthPolicies.Roles.Admin);
        using var client = CreateClient(factory, adminId, adminName, AuthPolicies.Roles.Admin);

        var response = await client.DeleteAsync($"/api/users/{adminId}/roles/Admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cannot remove the last Admin role", json.GetProperty("message").GetString());
    }

    // ==========================================================
    // Helpers
    // ==========================================================

    private (int UserId, string UserName, string Email) SeedUser(string? roleName = null)
        => SeedUser(_factory, roleName);

    private static (int UserId, string UserName, string Email) SeedUser(
        EnforcedRestaurantApiFactory factory,
        string? roleName = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userName = $"p4-{Guid.NewGuid():N}";
        var email = $"{userName}@example.test";
        var user = new Users
        {
            UserName = userName,
            UserPassword = BCrypt.Net.BCrypt.HashPassword(InitialPassword),
            UserFullName = "P4 Test User",
            UserEmail = email,
            UserPhone = "0000000000",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.SaveChanges();

        if (!string.IsNullOrWhiteSpace(roleName))
        {
            var role = db.Roles.Single(r => r.RoleName == roleName);
            db.UserRoles.Add(new UserRoles { UserId = user.Id, RoleId = role.RoleId });
            db.SaveChanges();
        }

        return (user.Id, userName, email);
    }

    private HttpClient CreateClient(int userId, string userName, params string[] roles)
        => CreateClient(_factory, userId, userName, roles);

    private static HttpClient CreateClient(
        EnforcedRestaurantApiFactory factory,
        int userId,
        string userName,
        params string[] roles)
    {
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        var token = tokenService.CreateToken(
            new UserDto { UserId = userId, UserName = userName, FullName = "P4 Test User" },
            roles);

        Assert.False(string.IsNullOrWhiteSpace(token));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
