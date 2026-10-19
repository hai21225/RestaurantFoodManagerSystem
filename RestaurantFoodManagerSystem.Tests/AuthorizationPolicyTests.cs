using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Auth;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Pins the phase 6b authorization matrix (AuthPolicies) and the Auth:Enforce toggle.
/// Uses the shared AddRestaurantAuthorization registration so app and test cannot drift.
/// </summary>
public sealed class AuthorizationPolicyTests
{
    private static IAuthorizationService BuildAuthorizationService(bool enforce)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRestaurantAuthorization(new AuthOptions { Enforce = enforce });

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal Principal(params string[] roles)
    {
        var identity = new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)),
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    // ==========================================================
    // Enforce = true : ma trận policy × role phải đúng thiết kế
    // ==========================================================

    [Theory]
    // Authenticated: mọi role hợp lệ
    [InlineData(AuthPolicies.Authenticated, AuthPolicies.Roles.Waiter, true)]
    [InlineData(AuthPolicies.Authenticated, AuthPolicies.Roles.Chef, true)]
    // AdminOnly
    [InlineData(AuthPolicies.AdminOnly, AuthPolicies.Roles.Admin, true)]
    [InlineData(AuthPolicies.AdminOnly, AuthPolicies.Roles.WarehouseManager, false)]
    [InlineData(AuthPolicies.AdminOnly, AuthPolicies.Roles.Waiter, false)]
    // PosOperator = Admin + Waiter
    [InlineData(AuthPolicies.PosOperator, AuthPolicies.Roles.Admin, true)]
    [InlineData(AuthPolicies.PosOperator, AuthPolicies.Roles.Waiter, true)]
    [InlineData(AuthPolicies.PosOperator, AuthPolicies.Roles.Cashier, false)]
    [InlineData(AuthPolicies.PosOperator, AuthPolicies.Roles.Chef, false)]
    // CashierDesk = Admin + Cashier
    [InlineData(AuthPolicies.CashierDesk, AuthPolicies.Roles.Admin, true)]
    [InlineData(AuthPolicies.CashierDesk, AuthPolicies.Roles.Cashier, true)]
    [InlineData(AuthPolicies.CashierDesk, AuthPolicies.Roles.Waiter, false)]
    // KitchenDesk = Admin + Chef
    [InlineData(AuthPolicies.KitchenDesk, AuthPolicies.Roles.Admin, true)]
    [InlineData(AuthPolicies.KitchenDesk, AuthPolicies.Roles.Chef, true)]
    [InlineData(AuthPolicies.KitchenDesk, AuthPolicies.Roles.Cashier, false)]
    // WarehouseDesk = Admin + WarehouseManager
    [InlineData(AuthPolicies.WarehouseDesk, AuthPolicies.Roles.Admin, true)]
    [InlineData(AuthPolicies.WarehouseDesk, AuthPolicies.Roles.WarehouseManager, true)]
    [InlineData(AuthPolicies.WarehouseDesk, AuthPolicies.Roles.Chef, false)]
    // PosOrCashier = Admin + Waiter + Cashier
    [InlineData(AuthPolicies.PosOrCashier, AuthPolicies.Roles.Admin, true)]
    [InlineData(AuthPolicies.PosOrCashier, AuthPolicies.Roles.Waiter, true)]
    [InlineData(AuthPolicies.PosOrCashier, AuthPolicies.Roles.Cashier, true)]
    [InlineData(AuthPolicies.PosOrCashier, AuthPolicies.Roles.Chef, false)]
    [InlineData(AuthPolicies.PosOrCashier, AuthPolicies.Roles.WarehouseManager, false)]
    public async Task Enforced_PolicyMatrix(string policy, string role, bool expected)
    {
        var authorization = BuildAuthorizationService(enforce: true);

        var result = await authorization.AuthorizeAsync(Principal(role), resource: null, policy);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task Enforced_Authenticated_DeniesAnonymous()
    {
        var authorization = BuildAuthorizationService(enforce: true);

        var result = await authorization.AuthorizeAsync(Anonymous(), null, AuthPolicies.Authenticated);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Enforced_DefaultPolicy_RequiresAuthentication()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRestaurantAuthorization(new AuthOptions { Enforce = true });
        var provider = services.BuildServiceProvider();

        var defaultPolicy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetDefaultPolicyAsync();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(Anonymous(), null, defaultPolicy);

        Assert.False(result.Succeeded);
    }

    // ==========================================================
    // Enforce = false : [Authorize] được gắn nhưng không chặn (dev)
    // ==========================================================

    [Theory]
    [InlineData(AuthPolicies.AdminOnly)]
    [InlineData(AuthPolicies.PosOperator)]
    [InlineData(AuthPolicies.CashierDesk)]
    [InlineData(AuthPolicies.KitchenDesk)]
    [InlineData(AuthPolicies.WarehouseDesk)]
    [InlineData(AuthPolicies.PosOrCashier)]
    [InlineData(AuthPolicies.Authenticated)]
    public async Task Disabled_AllowsAnonymous_ForAnyNamedPolicy(string policy)
    {
        var authorization = BuildAuthorizationService(enforce: false);

        var result = await authorization.AuthorizeAsync(Anonymous(), null, policy);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Disabled_DefaultPolicy_AllowsAnonymous()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRestaurantAuthorization(new AuthOptions { Enforce = false });
        var provider = services.BuildServiceProvider();

        var defaultPolicy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetDefaultPolicyAsync();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(Anonymous(), null, defaultPolicy);

        Assert.True(result.Succeeded);
    }
}
