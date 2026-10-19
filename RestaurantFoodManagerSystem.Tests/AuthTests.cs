using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>Pins current AuthService / UserService / UserRoleService behaviour.</summary>
public sealed class AuthTests : TestBase
{
    private Users SeedUserWithPassword(string password, bool isActive = true)
    {
        var username = $"auth-{Guid.NewGuid():N}";
        var user = new Users
        {
            UserName = username,
            UserPassword = BCrypt.Net.BCrypt.HashPassword(password),
            UserFullName = "Auth User",
            UserEmail = $"{username}@example.test",
            UserPhone = "0000000000",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task Login_ReturnsUser_WhenCredentialsAreValid()
    {
        var user = SeedUserWithPassword("secret123");

        var result = await Auth.Login(user.UserName, "secret123");

        Assert.NotNull(result);
        Assert.Equal(user.Id, result!.UserId);
        Assert.Equal(user.UserName, result.UserName);
    }

    [Fact]
    public async Task Login_ReturnsNull_WhenPasswordIsWrong()
    {
        var user = SeedUserWithPassword("secret123");

        Assert.Null(await Auth.Login(user.UserName, "wrong"));
    }

    [Fact]
    public async Task Login_ReturnsNull_WhenUserIsInactive()
    {
        var user = SeedUserWithPassword("secret123", isActive: false);

        Assert.Null(await Auth.Login(user.UserName, "secret123"));
    }

    [Fact]
    public async Task Login_ReturnsNull_WhenUserDoesNotExist()
    {
        Assert.Null(await Auth.Login("no-such-user", "secret123"));
    }

    [Fact]
    public async Task Login_ReturnsNull_WhenUsernameIsBlank()
    {
        Assert.Null(await Auth.Login("   ", "secret123"));
    }

    [Fact]
    public async Task Register_StoresPasswordAsBcryptHash()
    {
        var username = $"reg-{Guid.NewGuid():N}";

        var result = await Auth.Register(new UserDto
        {
            UserName = username,
            Email = $"{username}@example.test",
            Password = "secret123",
            ConfirmPassword = "secret123",
            FullName = "New User",
            Phone = "0123"
        });

        Assert.True(result);

        var stored = Db.Users.Single(u => u.UserName == username);
        Assert.NotEqual("secret123", stored.UserPassword);
        Assert.True(BCrypt.Net.BCrypt.Verify("secret123", stored.UserPassword));
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Register_Throws_WhenUsernameAlreadyExists()
    {
        var user = SeedUserWithPassword("secret123");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Auth.Register(new UserDto
        {
            UserName = user.UserName,
            Email = "other@example.test",
            Password = "secret123",
            ConfirmPassword = "secret123"
        }));

        Assert.Equal("Username already exists", ex.Message);
    }

    [Fact]
    public async Task Register_ReturnsFalse_WhenPasswordConfirmationDiffers()
    {
        var username = $"reg-{Guid.NewGuid():N}";

        var result = await Auth.Register(new UserDto
        {
            UserName = username,
            Email = $"{username}@example.test",
            Password = "secret123",
            ConfirmPassword = "different"
        });

        Assert.False(result);
        Assert.False(Db.Users.Any(u => u.UserName == username));
    }

    [Fact]
    public async Task Register_Throws_WhenEmailIsMissing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Auth.Register(new UserDto
        {
            UserName = $"reg-{Guid.NewGuid():N}",
            Email = "",
            Password = "secret123",
            ConfirmPassword = "secret123"
        }));
    }

    [Fact]
    public async Task AssignRoleToUser_CreatesUserRoleRow()
    {
        var user = SeedUserWithPassword("secret123");
        var role = SeedRole("Chef");

        var result = await Auth.AssignRoleToUser(user.Id, "Chef");

        Assert.True(result);
        Assert.True(Db.UserRoles.Any(ur => ur.UserId == user.Id && ur.RoleId == role.RoleId));
    }

    [Fact]
    public async Task AssignRoleToUser_Throws_WhenRoleDoesNotExist()
    {
        var user = SeedUserWithPassword("secret123");

        var ex = await Assert.ThrowsAsync<Exception>(() => Auth.AssignRoleToUser(user.Id, "NotARole"));

        Assert.Equal("Role not found", ex.Message);
    }
}
