using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace RestaurantFoodManagerSystem.Infrastructure.Auth;

/// <summary>
/// Đăng ký ma trận policy phân quyền (GĐ6). Dùng chung cho <c>Program.cs</c> và test
/// để tránh lệch cấu hình giữa app thật và test.
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddRestaurantAuthorization(
        this IServiceCollection services,
        AuthOptions authOptions)
    {
        services.AddSingleton(authOptions);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.Authenticated, p => p.RequireAuthenticatedUser());

            options.AddPolicy(AuthPolicies.AdminOnly,
                p => p.RequireRole(AuthPolicies.Roles.Admin));

            options.AddPolicy(AuthPolicies.PosOperator,
                p => p.RequireRole(AuthPolicies.Roles.Admin, AuthPolicies.Roles.Waiter));

            options.AddPolicy(AuthPolicies.CashierDesk,
                p => p.RequireRole(AuthPolicies.Roles.Admin, AuthPolicies.Roles.Cashier));

            options.AddPolicy(AuthPolicies.KitchenDesk,
                p => p.RequireRole(AuthPolicies.Roles.Admin, AuthPolicies.Roles.Chef));

            options.AddPolicy(AuthPolicies.WarehouseDesk,
                p => p.RequireRole(AuthPolicies.Roles.Admin, AuthPolicies.Roles.WarehouseManager));

            options.AddPolicy(AuthPolicies.PosOrCashier,
                p => p.RequireRole(
                    AuthPolicies.Roles.Admin,
                    AuthPolicies.Roles.Waiter,
                    AuthPolicies.Roles.Cashier));
        });

        // Đăng ký sau AddAuthorization để ghi đè DefaultAuthorizationPolicyProvider.
        services.AddSingleton<IAuthorizationPolicyProvider, EnforceableAuthorizationPolicyProvider>();

        return services;
    }
}
