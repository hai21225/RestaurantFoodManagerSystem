using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Quy tắc huỷ đơn/món dùng chung cho <see cref="OrderService"/> và <see cref="KitchenService"/>.
///
/// <para>
/// P1a/P1b: huỷ phải đồng bộ ba nơi — trạng thái đơn, phiếu bếp và tồn kho — và phải làm ở
/// cấp <b>từng món</b>. Trước đây huỷ cả đơn hoàn toàn bộ nguyên liệu kể cả món đã nấu, còn
/// huỷ một món thì không hoàn gì.
/// </para>
///
/// <para>
/// Quy tắc hoàn kho đã chốt: chỉ hoàn phần nguyên liệu của món còn <c>Pending</c> (bếp chưa
/// nhận). Món <c>Preparing/Ready/Completed</c> coi như đã tiêu hao, không hoàn. Số lượng hoàn
/// lấy từ ledger theo món (<see cref="InventoryTransaction.OrderItemId"/>); nếu đơn cũ chưa có
/// ledger theo món thì ước tính từ công thức, nhưng luôn bị chặn bởi phần tiêu hao còn lại của
/// cả đơn để không hoàn vượt. Đơn chưa từng trừ kho thì không được cộng tồn kho ảo.
/// </para>
/// </summary>
internal static class OrderCancellation
{
    /// <summary>
    /// Huỷ toàn bộ đơn: đánh dấu mọi món/phiếu bếp còn hoạt động là <c>Cancelled</c>, hoàn kho
    /// cho các món còn <c>Pending</c>, rồi đặt đơn thành <c>Cancelled</c>.
    /// Không gọi <c>SaveChanges</c>; caller chịu trách nhiệm lưu trong transaction của mình.
    /// </summary>
    public static async Task ApplyAsync(
        AppDbContext db,
        Order order,
        int changedBy,
        CancellationToken ct,
        string? reason = null,
        string? reasonCode = null)
    {
        var now = DateTime.UtcNow;
        var effectiveReasonCode = OrderItemMutations.NormalizeReasonCode(reasonCode);
        var returnsStock = OrderItemMutations.ReturnsStock(effectiveReasonCode);

        var orderItems = await db.OrderItems
            .Where(x => x.OrderId == order.OrderId)
            .ToListAsync(ct);

        var kitchenOrders = await db.KitchenOrders
            .Where(x => x.OrderId == order.OrderId)
            .ToListAsync(ct);
        var kitchenOrderIds = kitchenOrders.Select(x => x.KitchenOrderId).ToList();

        List<KitchenOrderItem> kitchenItems = kitchenOrderIds.Count == 0
            ? []
            : await db.KitchenOrderItems
                .Where(x => kitchenOrderIds.Contains(x.KitchenOrderId))
                .ToListAsync(ct);

        var kitchenItemsByOrderItem = kitchenItems
            .GroupBy(x => x.OrderItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var remainingConsumption = await LoadOrderRemainingAsync(db, order.OrderId, ct);

        foreach (var orderItem in orderItems.Where(x => !KitchenStatuses.IsCancelled(x.Status)))
        {
            kitchenItemsByOrderItem.TryGetValue(orderItem.Id, out var linkedKitchenItems);
            var kitchenStatus = linkedKitchenItems is { Count: > 0 }
                ? linkedKitchenItems[0].Status
                : orderItem.Status;

            // Chỉ món bếp chưa nhận mới được xử lý nguyên liệu. Nếu khách đổi món thì
            // hoàn kho; nếu hết hàng/hỏng/lỗi bếp thì ghi wastage, không tạo tồn kho ảo.
            if (KitchenStatuses.IsPending(kitchenStatus) &&
                KitchenStatuses.IsPending(orderItem.Status))
            {
                if (returnsStock)
                {
                    await ReturnStockForItemAsync(
                        db, order.OrderId, orderItem, remainingConsumption, changedBy, now, ct);
                }
                else
                {
                    await OrderItemMutations.RecordWastageAsync(
                        db,
                        order.OrderId,
                        orderItem,
                        orderItem.Quanlity,
                        changedBy,
                        now,
                        effectiveReasonCode,
                        reason,
                        ct);
                }
            }

            OrderItemMutations.RecordItemHistory(
                db,
                orderItem,
                linkedKitchenItems?.FirstOrDefault()?.Id,
                orderItem.Status,
                KitchenStatuses.Cancelled,
                changedBy,
                now,
                reason,
                effectiveReasonCode);

            MarkItemCancelled(orderItem, now, changedBy, reason);
        }

        foreach (var kitchenItem in kitchenItems)
        {
            kitchenItem.Status = KitchenStatuses.Cancelled;
        }

        foreach (var kitchenOrder in kitchenOrders)
        {
            kitchenOrder.Status = KitchenStatuses.Cancelled;
            kitchenOrder.CompletedAt ??= now;
        }

        order.Status = OrderStatuses.Cancelled;
        order.UpdatedAt = now;
    }

    /// <summary>
    /// Hoàn nguyên liệu cho đúng một dòng món. Không gọi <c>SaveChanges</c>.
    /// <paramref name="remainingConsumption"/> bị trừ dần để nhiều món huỷ liên tiếp không
    /// hoàn vượt quá lượng đã trừ của cả đơn.
    /// </summary>
    public static async Task ReturnStockForItemAsync(
        AppDbContext db,
        int orderId,
        OrderItem orderItem,
        IDictionary<int, decimal> remainingConsumption,
        int changedBy,
        DateTime now,
        CancellationToken ct)
    {
        var consumption = await ResolveItemConsumptionAsync(db, orderId, orderItem, ct);
        if (consumption.Count == 0)
        {
            return;
        }

        var returns = new List<(int IngredientId, decimal Quantity)>();
        foreach (var (ingredientId, quantity) in consumption)
        {
            remainingConsumption.TryGetValue(ingredientId, out var remaining);
            var returnQuantity = Math.Min(quantity, Math.Max(0m, remaining));
            if (returnQuantity <= 0)
            {
                continue;
            }

            remainingConsumption[ingredientId] = remaining - returnQuantity;
            returns.Add((ingredientId, returnQuantity));
        }

        if (returns.Count == 0)
        {
            return;
        }

        var ingredientIds = returns.Select(x => x.IngredientId).ToList();
        var stocks = await db.Inventories
            .Where(x => ingredientIds.Contains(x.IngredientId))
            .ToDictionaryAsync(x => x.IngredientId, ct);

        foreach (var (ingredientId, quantity) in returns)
        {
            if (!stocks.TryGetValue(ingredientId, out var stock))
            {
                throw new InvalidOperationException(
                    $"No inventory record exists for ingredient {ingredientId}");
            }

            var before = stock.Quanlity;
            stock.Quanlity += quantity;
            stock.UpdatedAt = now;

            db.InventoryTransactions.Add(new InventoryTransaction
            {
                IngredientId = ingredientId,
                Type = "AdjustmentIn",
                Quanlity = quantity,
                QuanlityBefore = before,
                QuanlityAfter = stock.Quanlity,
                ReferenceType = "Order",
                ReferenceId = orderId,
                OrderItemId = orderItem.Id,
                CreatedAt = now,
                CreatedBy = changedBy,
                Note = "Returned stock when order was cancelled"
            });
        }
    }

    /// <summary>Đánh dấu một dòng món đã huỷ kèm lý do/người/thời điểm.</summary>
    public static void MarkItemCancelled(
        OrderItem orderItem,
        DateTime now,
        int changedBy,
        string? reason)
    {
        orderItem.Status = KitchenStatuses.Cancelled;
        orderItem.CancelReason = reason;
        orderItem.CancelledAt = now;
        orderItem.CancelledBy = changedBy;
    }

    /// <summary>
    /// Lượng nguyên liệu còn được coi là đã tiêu hao của cả đơn: <c>Sale/AdjustmentOut</c>
    /// trừ <c>AdjustmentIn/Wastage</c>. Rỗng nếu đơn chưa từng trừ kho (đơn legacy).
    /// </summary>
    public static async Task<Dictionary<int, decimal>> LoadOrderRemainingAsync(
        AppDbContext db,
        int orderId,
        CancellationToken ct)
    {
        var transactions = await db.InventoryTransactions
            .AsNoTracking()
            .Where(x => x.ReferenceType == "Order" && x.ReferenceId == orderId)
            .Select(x => new { x.IngredientId, x.Type, x.Quanlity })
            .ToListAsync(ct);

        var consumption = new Dictionary<int, decimal>();
        foreach (var transaction in transactions)
        {
            var delta = transaction.Type switch
            {
                "Sale" => transaction.Quanlity,
                "AdjustmentOut" => transaction.Quanlity,
                "AdjustmentIn" => -transaction.Quanlity,
                // Wastage không đổi tồn kho nhưng làm giảm phần tiêu hao còn có thể
                // hoàn/đối chiếu. Nhờ vậy giảm số lượng nhiều lần không hoàn vượt.
                "Wastage" => -transaction.Quanlity,
                _ => 0m
            };

            consumption[transaction.IngredientId] =
                consumption.GetValueOrDefault(transaction.IngredientId) + delta;
        }

        return consumption
            .Where(x => x.Value > 0)
            .ToDictionary(x => x.Key, x => x.Value);
    }

    /// <summary>
    /// Tiêu hao còn có thể hoàn của một dòng món: ưu tiên ledger theo <c>OrderItemId</c>
    /// (đã trừ <c>AdjustmentIn/Wastage</c>); nếu đơn đã có ledger nhưng dòng này chưa được gắn
    /// (đơn sửa qua <c>PUT /items</c>) thì ước tính từ công thức. Đơn chưa từng trừ kho trả về
    /// rỗng để không sinh tồn kho ảo.
    /// </summary>
    internal static async Task<Dictionary<int, decimal>> ResolveItemConsumptionAsync(
        AppDbContext db,
        int orderId,
        OrderItem orderItem,
        CancellationToken ct)
    {
        var orderHasLedger = await db.InventoryTransactions
            .AsNoTracking()
            .AnyAsync(x => x.ReferenceType == "Order" && x.ReferenceId == orderId, ct);

        if (!orderHasLedger)
        {
            return [];
        }

        var itemTransactions = await db.InventoryTransactions
            .AsNoTracking()
            .Where(x => x.OrderItemId == orderItem.Id)
            .Select(x => new { x.IngredientId, x.Type, x.Quanlity })
            .ToListAsync(ct);

        var consumption = new Dictionary<int, decimal>();
        foreach (var transaction in itemTransactions)
        {
            var delta = transaction.Type switch
            {
                "Sale" => transaction.Quanlity,
                "AdjustmentOut" => transaction.Quanlity,
                "AdjustmentIn" => -transaction.Quanlity,
                "Wastage" => -transaction.Quanlity,
                _ => 0m
            };

            consumption[transaction.IngredientId] =
                consumption.GetValueOrDefault(transaction.IngredientId) + delta;
        }

        // Có ledger riêng của dòng thì luôn tin ledger, kể cả khi net <= 0
        // (đã hoàn/wastage hết). Chỉ fallback công thức cho dữ liệu cũ chưa có
        // ledger theo dòng, tránh sinh tiêu hao ảo sau khi đã wastage.
        if (itemTransactions.Count > 0)
        {
            return consumption
                .Where(x => x.Value > 0)
                .ToDictionary(x => x.Key, x => x.Value);
        }

        var recipeIngredients = await (
            from recipe in db.Recipes.AsNoTracking()
            join recipeIngredient in db.RecipeIngredients.AsNoTracking()
                on recipe.RecipeId equals recipeIngredient.RecipeId
            where recipe.SizeId == orderItem.SizeId && recipe.IsActive
            select recipeIngredient).ToListAsync(ct);

        foreach (var ingredient in recipeIngredients)
        {
            consumption[ingredient.IngredientId] =
                consumption.GetValueOrDefault(ingredient.IngredientId)
                + ingredient.Quanlity * orderItem.Quanlity;
        }

        return consumption;
    }
}
