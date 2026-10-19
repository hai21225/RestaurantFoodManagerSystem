using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>Pins the JWT generation contract added in phase 6a (TokenService).</summary>
public sealed class TokenServiceTests
{
    private static TokenService CreateService(JwtOptions? options = null)
        => new(options ?? new JwtOptions
        {
            Key = new string('k', 48),
            Issuer = "RestaurantFoodManagerSystem",
            Audience = "RestaurantFoodManagerSystem",
            ExpiryHours = 8
        }, NullLogger<TokenService>.Instance);

    [Fact]
    public void CreateToken_EmitsExpectedClaims()
    {
        var service = CreateService();
        var user = new UserDto { UserId = 42, UserName = "alice", FullName = "Alice Nguyen" };

        var token = service.CreateToken(user, new[] { "Admin", "Chef", "Admin" });

        Assert.False(string.IsNullOrWhiteSpace(token));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("42", jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("alice", jwt.Claims.Single(c => c.Type == "unique_name").Value);
        Assert.Equal("Alice Nguyen", jwt.Claims.Single(c => c.Type == "name").Value);
        Assert.False(string.IsNullOrWhiteSpace(jwt.Claims.Single(c => c.Type == "jti").Value));
        Assert.Contains(jwt.Claims, c => c.Type == "iat");
        Assert.Equal("RestaurantFoodManagerSystem", jwt.Issuer);
        Assert.Contains("RestaurantFoodManagerSystem", jwt.Audiences);

        // Duplicate roles are collapsed; a claim per distinct role.
        var roles = jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToList();
        Assert.Equal(2, roles.Count);
        Assert.Contains("Admin", roles);
        Assert.Contains("Chef", roles);
    }

    [Fact]
    public void CreateToken_UsesConfiguredEightHourLifetime()
    {
        var service = CreateService();

        var token = service.CreateToken(new UserDto { UserId = 1, UserName = "bob" }, Array.Empty<string>());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalHours, 7.99, 8.01);
    }

    [Fact]
    public void CreateToken_FallsBackToEightHours_WhenExpiryIsNotPositive()
    {
        var service = CreateService(new JwtOptions { Key = new string('k', 48), ExpiryHours = 0 });

        var token = service.CreateToken(new UserDto { UserId = 1, UserName = "bob" }, null);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalHours, 7.99, 8.01);
    }

    [Fact]
    public void CreateToken_OmitsRoleClaims_WhenNoRoles()
    {
        var service = CreateService();

        var token = service.CreateToken(new UserDto { UserId = 1, UserName = "bob" }, Array.Empty<string>());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "role");
    }

    [Fact]
    public void CreateToken_ReturnsNull_WhenSigningKeyIsMissing()
    {
        var service = CreateService(new JwtOptions { Issuer = "iss", Audience = "aud" });

        var token = service.CreateToken(new UserDto { UserId = 1, UserName = "bob" }, new[] { "Admin" });

        Assert.Null(token);
    }
}
