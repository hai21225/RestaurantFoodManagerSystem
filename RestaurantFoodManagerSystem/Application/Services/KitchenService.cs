using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Domain.AppDbContext;

public sealed class KitchenService
{
    private readonly AppDbContext _db;
    public KitchenService(AppDbContext db) => _db = db;

    public Task<List<KitchenOrder>> GetOrdersAsync(CancellationToken ct) =>
        _db.KitchenOrders.AsNoTracking().OrderBy(x => x.Priority).ThenBy(x => x.SentAt).ToListAsync(ct);

    public Task<List<KitchenOrderItem>> GetItemsAsync(int kitchenOrderId, CancellationToken ct) =>
        _db.KitchenOrderItems.AsNoTracking().Where(x => x.KitchenOrderId == kitchenOrderId).ToListAsync(ct);

    public async Task<bool> UpdateOrderStatusAsync(int id, KitchenStatusRequest request, CancellationToken ct)
    {
        var kitchenOrder = await _db.KitchenOrders.FirstOrDefaultAsync(x => x.KitchenOrderId == id, ct);
        if (kitchenOrder is null) return false;

        var status = request.Status.Trim();
        var allowed = new[] { "Pending", "Accepted", "Preparing", "Ready", "Completed", "Cancelled" };
        if (!allowed.Contains(status, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid kitchen order status");

        kitchenOrder.Status = status;
        if (status.Equals("Accepted", StringComparison.OrdinalIgnoreCase) || status.Equals("Preparing", StringComparison.OrdinalIgnoreCase))
            kitchenOrder.StartedAt ??= DateTime.UtcNow;
        if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase) || status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            kitchenOrder.CompletedAt ??= DateTime.UtcNow;

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);
        if (order is not null)
        {
            order.Status = status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? "Ready" :
                status.Equals("Ready", StringComparison.OrdinalIgnoreCase) ? "Ready" :
                status.Equals("Preparing", StringComparison.OrdinalIgnoreCase) ? "Preparing" : "SentToKitchen";
            order.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateItemStatusAsync(int id, KitchenItemStatusRequest request, CancellationToken ct)
    {
        var item = await _db.KitchenOrderItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return false;
        var status = request.Status.Trim();
        var allowed = new[] { "Pending", "Preparing", "Ready", "Completed", "Cancelled" };
        if (!allowed.Contains(status, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid kitchen item status");

        item.Status = status;
        var orderItem = await _db.OrderItems.FirstOrDefaultAsync(x => x.Id == item.OrderItemId, ct);
        if (orderItem is not null) orderItem.Status = status;

        var kitchenOrder = await _db.KitchenOrders.FirstAsync(x => x.KitchenOrderId == item.KitchenOrderId, ct);
        var allItems = await _db.KitchenOrderItems.Where(x => x.KitchenOrderId == item.KitchenOrderId).ToListAsync(ct);
        if (allItems.All(x => x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase)))
            kitchenOrder.Status = "Ready";
        else if (allItems.Any(x => x.Status.Equals("Preparing", StringComparison.OrdinalIgnoreCase)))
            kitchenOrder.Status = "Preparing";

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);
        if (order is not null)
        {
            order.Status = kitchenOrder.Status == "Ready" ? "Ready" :
                kitchenOrder.Status == "Preparing" ? "Preparing" : "SentToKitchen";
            order.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
