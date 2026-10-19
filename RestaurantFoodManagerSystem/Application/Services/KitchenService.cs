using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class KitchenService
{
    private readonly AppDbContext _db;
    public KitchenService(AppDbContext db) => _db = db;

    public Task<List<KitchenOrder>> GetOrdersAsync(CancellationToken ct) =>
        _db.KitchenOrders.AsNoTracking().OrderBy(x => x.Priority).ThenBy(x => x.SentAt).ToListAsync(ct);

    /// <summary>
    /// Danh sách món của phiếu bếp (Nhóm C/GĐ7). Bổ sung <c>foodName</c>/<c>sizeName</c>/<c>quantity</c>
    /// (join từ order item) và giữ nguyên <c>quanlity</c> cũ.
    /// </summary>
    public Task<List<KitchenItemResponse>> GetItemsAsync(int kitchenOrderId, CancellationToken ct) =>
        (from ki in _db.KitchenOrderItems.AsNoTracking()
         join oi in _db.OrderItems on ki.OrderItemId equals oi.Id
         join fs in _db.FoodSizes on oi.SizeId equals fs.SizeId
         join f in _db.Foods on fs.FoodId equals f.FoodId
         where ki.KitchenOrderId == kitchenOrderId
         select new KitchenItemResponse
         {
             Id = ki.Id,
             KitchenOrderId = ki.KitchenOrderId,
             OrderItemId = ki.OrderItemId,
             Quanlity = ki.Quanlity,
             Status = ki.Status,
             Note = ki.Note,
             FoodName = f.FoodName,
             SizeName = fs.SizeName
         }).ToListAsync(ct);

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
            order.Status = MapOrderStatus(status);
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
            order.Status = MapOrderStatus(kitchenOrder.Status);
            order.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Ánh xạ trạng thái phiếu bếp → trạng thái đơn hàng (Nhóm B1).
    /// Trước đây mọi trạng thái ngoài Ready/Preparing — kể cả <c>Cancelled</c> — đều bị đưa về
    /// <c>SentToKitchen</c>, tức là huỷ phiếu bếp lại vô tình "mở lại" đơn.
    /// </summary>
    private static string MapOrderStatus(string kitchenStatus) => kitchenStatus.Trim().ToLowerInvariant() switch
    {
        "preparing" => OrderStatuses.Preparing,
        "ready" => OrderStatuses.Ready,
        "completed" => OrderStatuses.Ready,
        "cancelled" => OrderStatuses.Cancelled,
        _ => OrderStatuses.SentToKitchen // Pending, Accepted
    };
}
