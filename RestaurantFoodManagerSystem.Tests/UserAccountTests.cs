using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// P4: hồ sơ tự phục vụ + đổi mật khẩu + thu hồi role ở tầng service.
/// </summary>
public sealed class UserAccountTests : TestBase
{
    private UserService Users => new(Db);

    private void LinkRole(int userId, int roleId)
    {
        Db.UserRoles.Add(new UserRoles { UserId = userId, RoleId = roleId });
        Db.SaveChanges();
    }

    // ==========================================================
    // GET /api/users/me
    // ==========================================================

    [Fact]
    public async Task GetProfile_ReturnsOwnFieldsAndRoles()
    {
        var user = SeedUser();
        var role = SeedRole("Waiter");
        LinkRole(user.Id, role.RoleId);

        var profile = await Users.GetProfileAsync(user.Id);

        Assert.NotNull(profile);
        Assert.Equal(user.Id, profile!.UserId);
        Assert.Equal(user.UserName, profile.UserName);
        Assert.Equal(user.UserFullName, profile.FullName);
        Assert.Equal(user.UserEmail, profile.Email);
        Assert.Equal(user.UserPhone, profile.Phone);
        Assert.True(profile.IsActive);
        Assert.Equal(["Waiter"], profile.Roles);
    }

    [Fact]
    public async Task GetProfile_ReturnsNull_ForUnknownUser()
    {
        Assert.Null(await Users.GetProfileAsync(int.MaxValue));
    }

    // ==========================================================
    // PUT /api/users/me
    // ==========================================================

    [Fact]
    public async Task UpdateProfile_TrimsAndUpdatesOnlySelfServiceFields()
    {
        var user = SeedUser();
        var role = SeedRole("Cashier");
        LinkRole(user.Id, role.RoleId);

        user.UpdatedAt = DateTime.UtcNow.AddDays(-1);
        Db.SaveChanges();
        var before = user.UpdatedAt;

        var profile = await Users.UpdateProfileAsync(user.Id, new UpdateProfileRequest
        {
            FullName = "  Nguyễn Văn A  ",
            Email = "  a@example.test  ",
            Phone = "  0901234567  "
        });

        Assert.Equal("Nguyễn Văn A", profile.FullName);
        Assert.Equal("a@example.test", profile.Email);
        Assert.Equal("0901234567", profile.Phone);
        Assert.True(profile.IsActive);
        Assert.Equal(["Cashier"], profile.Roles);
        Assert.True(profile.UpdatedAt > before);

        var stored = Db.Users.Single(u => u.Id == user.Id);
        Assert.Equal("Nguyễn Văn A", stored.UserFullName);
        Assert.Equal("a@example.test", stored.UserEmail);
        Assert.Equal("0901234567", stored.UserPhone);
    }

    [Fact]
    public async Task UpdateProfile_EmptyPhone_ClearsIt()
    {
        var user = SeedUser();

        var profile = await Users.UpdateProfileAsync(user.Id, new UpdateProfileRequest
        {
            FullName = "Test User",
            Email = user.UserEmail,
            Phone = "   "
        });

        Assert.Equal(string.Empty, profile.Phone);
    }

    [Fact]
    public async Task UpdateProfile_RejectsEmailBelongingToAnotherUser_IgnoringCase()
    {
        var target = SeedUser();
        var other = SeedUser();
        other.UserEmail = "Other@Example.test";
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Users.UpdateProfileAsync(target.Id, new UpdateProfileRequest
            {
                FullName = "Test User",
                Email = "other@example.test"
            }));

        Assert.Equal("Email already exists", ex.Message);
    }

    [Theory]
    [InlineData("", "a@example.test")]
    [InlineData("   ", "a@example.test")]
    [InlineData("Test User", "")]
    [InlineData("Test User", "not-an-email")]
    [InlineData("Test User", "a b@example.test")]
    public async Task UpdateProfile_RejectsInvalidInput(string fullName, string email)
    {
        var user = SeedUser();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Users.UpdateProfileAsync(user.Id, new UpdateProfileRequest
            {
                FullName = fullName,
                Email = email
            }));
    }

    [Fact]
    public async Task UpdateProfile_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Users.UpdateProfileAsync(int.MaxValue, new UpdateProfileRequest
            {
                FullName = "Test User",
                Email = "a@example.test"
            }));
    }

    // ==========================================================
    // Batch A — Admin quản lý nhân viên
    // ==========================================================

    [Fact]
    public async Task AdminUpdateUser_UpdatesContactFields_WithoutChangingRolesOrActiveState()
    {
        var user = SeedUser();
        var role = SeedRole("Waiter");
        LinkRole(user.Id, role.RoleId);

        var profile = await Users.UpdateUserAsync(user.Id, new UpdateUserRequest
        {
            FullName = "  Nhân Viên A  ",
            Email = $"admin-updated-{Guid.NewGuid():N}@example.test",
            Phone = "  0900111222  "
        });

        Assert.Equal("Nhân Viên A", profile.FullName);
        Assert.Equal("0900111222", profile.Phone);
        Assert.True(profile.IsActive);
        Assert.Equal(["Waiter"], profile.Roles);

        var stored = Db.Users.Single(u => u.Id == user.Id);
        Assert.Equal("Nhân Viên A", stored.UserFullName);
    }

    [Fact]
    public async Task AdminUpdateUser_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Users.UpdateUserAsync(int.MaxValue, new UpdateUserRequest
            {
                FullName = "Test",
                Email = "a@example.test"
            }));
    }

    [Fact]
    public async Task SetActive_LocksAndUnlocksEmployee()
    {
        var user = SeedUser();

        var locked = await Users.SetActiveAsync(user.Id, false, actingUserId: null);
        Assert.False(locked.IsActive);

        var unlocked = await Users.SetActiveAsync(user.Id, true, actingUserId: null);
        Assert.True(unlocked.IsActive);
    }

    [Fact]
    public async Task SetActive_RejectsLockingYourOwnAccount()
    {
        var user = SeedUser();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Users.SetActiveAsync(user.Id, false, actingUserId: user.Id));

        Assert.Equal("You cannot lock your own account", ex.Message);
        Assert.True(Db.Users.Single(u => u.Id == user.Id).IsActive);
    }

    [Fact]
    public async Task SetActive_RejectsLockingTheLastActiveAdmin()
    {
        var adminRole = SeedRole("Admin");
        var admin = SeedUser();
        LinkRole(admin.Id, adminRole.RoleId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Users.SetActiveAsync(admin.Id, false, actingUserId: null));

        Assert.Equal("Cannot lock the last active Admin account", ex.Message);
        Assert.True(Db.Users.Single(u => u.Id == admin.Id).IsActive);
    }

    [Fact]
    public async Task SetActive_AllowsLockingOneOfTwoActiveAdmins()
    {
        var adminRole = SeedRole("Admin");
        var first = SeedUser();
        var second = SeedUser();
        LinkRole(first.Id, adminRole.RoleId);
        LinkRole(second.Id, adminRole.RoleId);

        var locked = await Users.SetActiveAsync(first.Id, false, actingUserId: null);

        Assert.False(locked.IsActive);
        Assert.True(Db.Users.Single(u => u.Id == second.Id).IsActive);
    }

    // ==========================================================
    // POST /api/auth/change-password
    // ==========================================================

    [Fact]
    public async Task ChangePassword_ReplacesHash_SoOnlyNewPasswordLogsIn()
    {
        var user = SeedUser();
        user.UserPassword = BCrypt.Net.BCrypt.HashPassword("old-pass");
        Db.SaveChanges();

        await Auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "old-pass",
            NewPassword = "new-pass",
            ConfirmNewPassword = "new-pass"
        });

        Assert.Null(await Auth.Login(user.UserName, "old-pass"));
        Assert.NotNull(await Auth.Login(user.UserName, "new-pass"));

        var stored = Db.Users.Single(u => u.Id == user.Id);
        Assert.NotEqual("new-pass", stored.UserPassword);
        Assert.True(BCrypt.Net.BCrypt.Verify("new-pass", stored.UserPassword));
    }

    [Fact]
    public async Task ChangePassword_RejectsWrongCurrentPassword()
    {
        var user = SeedUser();
        user.UserPassword = BCrypt.Net.BCrypt.HashPassword("old-pass");
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest
            {
                CurrentPassword = "wrong-pass",
                NewPassword = "new-pass",
                ConfirmNewPassword = "new-pass"
            }));

        Assert.Equal("Current password is incorrect", ex.Message);
    }

    [Theory]
    [InlineData("old-pass", "new-pass", "different-pass")]
    [InlineData("old-pass", "short", "short")]
    [InlineData("old-pass", "old-pass", "old-pass")]
    [InlineData("", "new-pass", "new-pass")]
    public async Task ChangePassword_RejectsInvalidInput(
        string current,
        string next,
        string confirm)
    {
        var user = SeedUser();
        user.UserPassword = BCrypt.Net.BCrypt.HashPassword("old-pass");
        Db.SaveChanges();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest
            {
                CurrentPassword = current,
                NewPassword = next,
                ConfirmNewPassword = confirm
            }));
    }

    [Fact]
    public async Task ChangePassword_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Auth.ChangePasswordAsync(int.MaxValue, new ChangePasswordRequest
            {
                CurrentPassword = "old-pass",
                NewPassword = "new-pass",
                ConfirmNewPassword = "new-pass"
            }));
    }

    // ==========================================================
    // Batch A — Admin đặt lại mật khẩu
    // ==========================================================

    [Fact]
    public async Task ResetPassword_ChangesHash_SoOnlyNewPasswordLogsIn()
    {
        var user = SeedUser();
        user.UserPassword = BCrypt.Net.BCrypt.HashPassword("old-pass");
        Db.SaveChanges();

        await Auth.ResetPasswordAsync(user.Id, new ResetUserPasswordRequest
        {
            NewPassword = "reset-pass",
            ConfirmPassword = "reset-pass"
        });

        Assert.Null(await Auth.Login(user.UserName, "old-pass"));
        Assert.NotNull(await Auth.Login(user.UserName, "reset-pass"));

        var stored = Db.Users.Single(u => u.Id == user.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("reset-pass", stored.UserPassword));
    }

    [Theory]
    [InlineData("short", "short")]
    [InlineData("reset-pass", "different-pass")]
    [InlineData("", "")]
    public async Task ResetPassword_RejectsInvalidInput(string newPassword, string confirmPassword)
    {
        var user = SeedUser();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Auth.ResetPasswordAsync(user.Id, new ResetUserPasswordRequest
            {
                NewPassword = newPassword,
                ConfirmPassword = confirmPassword
            }));
    }

    [Fact]
    public async Task ResetPassword_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Auth.ResetPasswordAsync(int.MaxValue, new ResetUserPasswordRequest
            {
                NewPassword = "reset-pass",
                ConfirmPassword = "reset-pass"
            }));
    }

    // ==========================================================
    // DELETE /api/users/{id}/roles/{roleName}
    // ==========================================================

    [Fact]
    public async Task RemoveRole_RemovesAssignment()
    {
        var user = SeedUser();
        var role = SeedRole("Waiter");
        LinkRole(user.Id, role.RoleId);

        await Auth.RemoveRoleFromUserAsync(user.Id, "Waiter");

        Assert.False(Db.UserRoles.Any(x => x.UserId == user.Id && x.RoleId == role.RoleId));
    }

    [Fact]
    public async Task RemoveRole_WhenNotAssigned_Throws()
    {
        var user = SeedUser();
        SeedRole("Waiter");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Auth.RemoveRoleFromUserAsync(user.Id, "Waiter"));
    }

    [Fact]
    public async Task RemoveRole_UnknownUserOrRole_Throws()
    {
        var user = SeedUser();
        SeedRole("Waiter");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Auth.RemoveRoleFromUserAsync(int.MaxValue, "Waiter"));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Auth.RemoveRoleFromUserAsync(user.Id, "NoSuchRole"));
    }

    [Fact]
    public async Task RemoveRole_RejectsRemovingTheLastAdmin()
    {
        var adminRole = SeedRole("Admin");
        var admin = SeedUser();
        LinkRole(admin.Id, adminRole.RoleId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Auth.RemoveRoleFromUserAsync(admin.Id, "Admin"));

        Assert.Equal("Cannot remove the last Admin role", ex.Message);
        Assert.True(Db.UserRoles.Any(x => x.UserId == admin.Id && x.RoleId == adminRole.RoleId));
    }

    [Fact]
    public async Task RemoveRole_AllowsRemovingOneOfTwoAdmins()
    {
        var adminRole = SeedRole("Admin");
        var first = SeedUser();
        var second = SeedUser();
        LinkRole(first.Id, adminRole.RoleId);
        LinkRole(second.Id, adminRole.RoleId);

        await Auth.RemoveRoleFromUserAsync(first.Id, "Admin");

        Assert.False(Db.UserRoles.Any(x => x.UserId == first.Id && x.RoleId == adminRole.RoleId));
        Assert.True(Db.UserRoles.Any(x => x.UserId == second.Id && x.RoleId == adminRole.RoleId));
    }

    // ==========================================================
    // GET /api/roles
    // ==========================================================

    [Fact]
    public async Task GetAllRoles_ReturnsSeededRolesOrderedById()
    {
        var first = SeedRole("Admin");
        var second = SeedRole("Waiter");

        var roles = await new RoleService(Db).GetAllAsync();

        Assert.Equal([first.RoleId, second.RoleId], roles.Select(r => r.RoleId));
        Assert.Equal(["Admin", "Waiter"], roles.Select(r => r.RoleName));
        Assert.All(roles, r => Assert.False(string.IsNullOrWhiteSpace(r.RoleDescription)));
    }
}
