using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

[Authorize(Policy = AuthPolicies.Authenticated)]
public class RestaurantHub : Hub
{
    private readonly AppDbContext _db;

    public RestaurantHub(AppDbContext db)
    {
        _db = db;
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is not null)
        {
            var isAdmin = HasRole(user, AuthPolicies.Roles.Admin);

            if (isAdmin || HasRole(user, AuthPolicies.Roles.Chef))
                await Groups.AddToGroupAsync(Context.ConnectionId, "role:Kitchen");

            if (isAdmin || HasRole(user, AuthPolicies.Roles.Cashier))
                await Groups.AddToGroupAsync(Context.ConnectionId, "role:Cashier");

            if (isAdmin || HasRole(user, AuthPolicies.Roles.Waiter))
                await Groups.AddToGroupAsync(Context.ConnectionId, "role:Waiter");

            var userId = GetUserId(user);
            if (userId.HasValue)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"customer:{userId.Value}");
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinOrder(int orderId)
    {
        var user = Context.User;
        if (user is not null &&
            HasRole(user, AuthPolicies.Roles.Customer) &&
            !HasRole(user, AuthPolicies.Roles.Admin) &&
            !HasRole(user, AuthPolicies.Roles.Waiter) &&
            !HasRole(user, AuthPolicies.Roles.Cashier) &&
            !HasRole(user, AuthPolicies.Roles.Chef))
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
                throw new HubException("Forbidden");

            var order = await _db.Orders
                .AsNoTracking()
                .Where(x => x.OrderId == orderId)
                .Select(x => new { x.CustomerUserId, x.CreatedBy })
                .FirstOrDefaultAsync();

            if (order is null || (order.CustomerUserId != userId.Value && order.CreatedBy != userId.Value))
                throw new HubException("Forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"order:{orderId}");
    }

    public Task LeaveOrder(int orderId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"order:{orderId}");

    private static bool HasRole(ClaimsPrincipal user, string role) =>
        user.IsInRole(role) ||
        user.FindAll("role").Any(c => string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase));

    private static int? GetUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }
}
