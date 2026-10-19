using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Auth;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// GĐ6e: test phân quyền ở **mức HTTP** trên host thật (TestServer), không chỉ
/// <c>IAuthorizationService</c>.
///
/// <para>
/// <see cref="EnforcedRestaurantApiFactory"/> bật <c>Auth:Enforce=true</c> + khóa ký JWT test,
/// nên thiếu token → 401, sai role → 403, đúng role → 200. Token sinh bằng chính
/// <see cref="TokenService"/> của app để kiểm tra luôn pipeline đọc claim <c>sub</c>/<c>role</c>.
/// </para>
/// </summary>
public sealed class AuthHttpTests : IClassFixture<EnforcedRestaurantApiFactory>
{
    private const string Foods = "/api/foods";
    private const string RestaurantTables = "/api/restaurant-tables";
    private const string OrderById = "/api/orders/1";
    private const string WarehouseIngredients = "/api/management/ingredients";
    private const string KitchenOrders = "/api/management/kitchen/orders";
    private const string AdminPromotions = "/api/management/promotions";
    private const string AdminUsers = "/api/users";

    private readonly EnforcedRestaurantApiFactory _factory;

    public AuthHttpTests(EnforcedRestaurantApiFactory factory)
    {
        _factory = factory;
    }

    // ==========================================================
    // 401 — thiếu token / token không hợp lệ
    // ==========================================================

    [Theory]
    [InlineData(Foods)]                    // Authenticated
    [InlineData(RestaurantTables)]         // PosOrCashier
    [InlineData(OrderById)]                // PosOrCashier
    [InlineData(WarehouseIngredients)]     // WarehouseDesk
    [InlineData(KitchenOrders)]            // KitchenDesk
    [InlineData(AdminPromotions)]          // AdminOnly
    [InlineData(AdminUsers)]               // AdminOnly (cấp controller)
    public async Task ProtectedEndpoints_WithoutToken_Return401(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMalformedToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-valid-jwt");

        var response = await client.GetAsync(Foods);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ==========================================================
    // 403 — có token nhưng sai role
    // ==========================================================

    [Theory]
    [InlineData(AuthPolicies.Roles.Waiter, WarehouseIngredients)]              // kho: Admin + WarehouseManager
    [InlineData(AuthPolicies.Roles.Chef, RestaurantTables)]                     // POS/thu ngân: Admin + Waiter + Cashier
    [InlineData(AuthPolicies.Roles.Cashier, KitchenOrders)]                     // bếp: Admin + Chef
    [InlineData(AuthPolicies.Roles.WarehouseManager, AdminPromotions)]          // AdminOnly
    [InlineData(AuthPolicies.Roles.Waiter, AdminUsers)]                         // AdminOnly (cấp controller)
    public async Task ProtectedEndpoint_WithWrongRole_Returns403(string role, string path)
    {
        using var client = CreateAuthenticatedClient(role);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==========================================================
    // 200 — đúng role (bao gồm Admin cho mọi policy)
    // ==========================================================

    [Theory]
    [InlineData(AuthPolicies.Roles.Waiter, Foods)]                    // Authenticated
    [InlineData(AuthPolicies.Roles.Chef, Foods)]                      // Authenticated
    [InlineData(AuthPolicies.Roles.Waiter, RestaurantTables)]         // PosOrCashier
    [InlineData(AuthPolicies.Roles.Cashier, RestaurantTables)]        // PosOrCashier
    [InlineData(AuthPolicies.Roles.Chef, KitchenOrders)]              // KitchenDesk
    [InlineData(AuthPolicies.Roles.WarehouseManager, WarehouseIngredients)] // WarehouseDesk
    [InlineData(AuthPolicies.Roles.Admin, AdminPromotions)]           // AdminOnly
    [InlineData(AuthPolicies.Roles.Admin, AdminUsers)]                // AdminOnly
    public async Task ProtectedEndpoint_WithAllowedRole_Returns200(string role, string path)
    {
        using var client = CreateAuthenticatedClient(role);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ==========================================================
    // AllowAnonymous vẫn đi qua khi enforce bật
    // ==========================================================

    [Fact]
    public async Task AllowAnonymousEndpoint_StillReturnsOk_WhenEnforced()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/setup/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ==========================================================
    // Helpers
    // ==========================================================

    private HttpClient CreateAuthenticatedClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(role));
        return client;
    }

    private string CreateToken(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        var token = tokenService.CreateToken(
            new UserDto
            {
                UserId = 4242,
                UserName = $"http-test-{role.ToLowerInvariant()}",
                FullName = "HTTP Auth Test",
            },
            [role]);

        Assert.False(
            string.IsNullOrWhiteSpace(token),
            "TokenService phải sinh được token khi Auth:Enforce=true và đã cấu hình khóa test.");

        return token!;
    }
}

/// <summary>
/// GĐ6e: khoá baseline dev — <c>Auth:Enforce=false</c> thì <c>[Authorize]</c> vẫn được gắn
/// nhưng KHÔNG chặn (quirk #1 của <c>endpoint-baseline.md</c>). Nếu ai bật enforce mặc định,
/// các test này sẽ đỏ và buộc phải cập nhật tài liệu + phối hợp frontend.
/// </summary>
public sealed class AuthDisabledHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public AuthDisabledHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/foods")]
    [InlineData("/api/management/ingredients")]
    [InlineData("/api/users")]
    public async Task ProtectedEndpoints_WithoutToken_StillReturn200_WhenEnforceIsFalse(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidToken_IsParsedWithoutError_WhenEnforceIsFalse()
    {
        using var client = _factory.CreateClient();

        using (var scope = _factory.Services.CreateScope())
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
            var token = tokenService.CreateToken(
                new UserDto { UserId = 4243, UserName = "http-test-admin", FullName = "HTTP Auth Test" },
                [AuthPolicies.Roles.Admin]);

            Assert.False(string.IsNullOrWhiteSpace(token));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
