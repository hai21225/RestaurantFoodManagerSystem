using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Các thao tác dùng chung cho Phase 2 (Cách A — tách dòng):
/// tách dòng món, tính lại tiền, hoàn kho phần chưa nấu và ghi wastage.
///
/// <para>
/// Không tự mở transaction/SaveChanges. Caller đang giữ transaction chịu trách nhiệm lưu.
/// </para>
/// </summary>
internal static class OrderItemMutations
{
    public const string CustomerChange = "CustomerChange";
    public const string OutOfStock = "OutOfStock";
    public const string PhysicalShortage = "PhysicalShortage";
    public const string KitchenError = "KitchenError";

    private static readonly string[] ReasonCodes =
        [CustomerChange, OutOfStock, PhysicalShortage, KitchenError];

    public static bool ReturnsStock(string reasonCode) =>
        string.Equals(reasonCode, CustomerChange, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeReasonCode(string? code, string fallback = CustomerChange)
    {
        var normalized = string.IsNullOrWhiteSpace(code) ? fallback : code.Trim();
        var match = ReasonCodes.FirstOrDefault(x =>
            string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new ArgumentException(
                "ReasonCode must be one of CustomerChange, OutOfStock, PhysicalShortage, KitchenError");
        }

        return match;
    }

    /// <summary>
    /// C-10: ghi audit trạng thái/số lượng của một dòng món. Không gọi <c>SaveChanges</c>.
    /// Với thay đổi số lượng nhưng không đổi trạng thái, caller truyền old/new status giống
    /// nhau và mô tả số lượng trong <paramref name="reason"/>.
    /// </summary>
    public static void RecordItemHistory(
        AppDbContext db,
        OrderItem item,
        int? kitchenOrderItemId,
        string oldStatus,
        string newStatus,
        int changedBy,
        DateTime now,
        string? reason = null,
        string? reasonCode = null)
    {
        // ChangedBy là FK tới users. Các luồng không xác thực vẫn phải resolve được actor
        // từ body/order trước khi gọi; nếu không có actor thì bỏ qua thay vì làm hỏng giao dịch.
        if (changedBy <= 0)
        {
            return;
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason is { Length: > 500 })
        {
            normalizedReason = normalizedReason[..500];
        }

        var normalizedReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? null
            : reasonCode.Trim();
        if (normalizedReasonCode is { Length: > 50 })
        {
            normalizedReasonCode = normalizedReasonCode[..50];
        }

        db.OrderItemStatusHistories.Add(new OrderItemStatusHistory
        {
            OrderId = item.OrderId,
            OrderItemId = item.Id > 0 ? item.Id : null,
            KitchenOrderItemId = kitchenOrderItemId is > 0 ? kitchenOrderItemId : null,
            OldStatus = string.IsNullOrWhiteSpace(oldStatus) ? item.Status : oldStatus.Trim(),
            NewStatus = newStatus.Trim(),
            ChangedBy = changedBy,
            CreatedAt = now,
            Reason = normalizedReason,
            ReasonCode = normalizedReasonCode
        });
    }

    /// <summary>Gán group id cho dòng gốc nếu dữ liệu cũ chưa có.</summary>
    public static Guid EnsureGroupId(OrderItem item) =>
        item.OrderItemGroupId ??= Guid.NewGuid();

    /// <summary>
    /// Các dòng còn hoạt động của cùng một món gọi ban đầu. Dữ liệu cũ chưa có group
    /// được coi là một nhóm một dòng.
    /// </summary>
    public static async Task<List<OrderItem>> LoadGroupAsync(
        AppDbContext db,
        OrderItem item,
        CancellationToken ct)
    {
        if (item.OrderItemGroupId is null)
        {
            return [item];
        }

        var items = await db.OrderItems
            .Where(x => x.OrderId == item.OrderId
                     && x.OrderItemGroupId == item.OrderItemGroupId
                     && x.Status != KitchenStatuses.Cancelled)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        // Dòng legacy vừa được gán group id trong bộ nhớ nhưng chưa SaveChanges vẫn
        // phải nằm trong nhóm (query theo cột DB cũ sẽ không thấy nó).
        if (items.All(x => x.Id != item.Id))
        {
            items.Add(item);
        }

        return items;
    }

    public static void RecalculateTotals(Order order, IEnumerable<OrderItem> items, DateTime now)
    {
        var active = items
            .Where(x => !KitchenStatuses.IsCancelled(x.Status))
            .ToList();

        order.Subtotal = active.Sum(x => x.UnitPrice * x.Quanlity);
        // Mọi thay đổi dòng món làm thay đổi cơ sở khuyến mãi; bỏ discount cũ.
        order.DiscountAmount = 0m;
        order.TotalAmount = Math.Round(
            order.Subtotal + order.TaxAmount, 2, MidpointRounding.AwayFromZero);
        order.UpdatedAt = now;
    }

    public static Task<KitchenOrderItem?> FindKitchenItemAsync(
        AppDbContext db,
        int orderItemId,
        CancellationToken ct) =>
        db.KitchenOrderItems
            .Where(x => x.OrderItemId == orderItemId)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

    public static async Task<List<KitchenOrderItem>> LoadKitchenItemsAsync(
        AppDbContext db,
        IEnumerable<int> orderItemIds,
        CancellationToken ct)
    {
        var ids = orderItemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.KitchenOrderItems
            .Where(x => ids.Contains(x.OrderItemId))
            .ToListAsync(ct);
    }

    public static OrderItem CloneLine(
        OrderItem source,
        int quantity,
        string status,
        DateTime now) =>
        new()
        {
            OrderId = source.OrderId,
            SizeId = source.SizeId,
            Quanlity = quantity,
            UnitPrice = source.UnitPrice,
            Note = source.Note,
            Status = status,
            CreatedAt = now,
            OrderItemGroupId = source.OrderItemGroupId
        };

    /// <summary>Copy topping từ dòng gốc sang dòng tách. Topping được tính theo 1 phần.</summary>
    public static async Task CopyToppingsAsync(
        AppDbContext db,
        int sourceOrderItemId,
        int targetOrderItemId,
        CancellationToken ct)
    {
        var toppings = await db.OrderItemToppings
            .AsNoTracking()
            .Where(x => x.OrderItemId == sourceOrderItemId)
            .ToListAsync(ct);

        foreach (var topping in toppings)
        {
            db.OrderItemToppings.Add(new OrderItemTopping
            {
                OrderItemId = targetOrderItemId,
                ToppingId = topping.ToppingId,
                Quanlity = topping.Quanlity,
                UnitPrice = topping.UnitPrice
            });
        }
    }

    /// <summary>Nhu cầu nguyên liệu cho một size với số lượng bất kỳ.</summary>
    public static async Task<Dictionary<int, decimal>> CalculateRequirementsForQuantityAsync(
        AppDbContext db,
        int sizeId,
        int quantity,
        CancellationToken ct)
    {
        var recipeIngredients = await (
            from recipe in db.Recipes.AsNoTracking()
            join recipeIngredient in db.RecipeIngredients.AsNoTracking()
                on recipe.RecipeId equals recipeIngredient.RecipeId
            where recipe.SizeId == sizeId && recipe.IsActive
            select recipeIngredient).ToListAsync(ct);

        if (recipeIngredients.Count == 0)
        {
            throw new InvalidOperationException(
                $"Food size {sizeId} does not have an active recipe");
        }

        var requirements = new Dictionary<int, decimal>();
        foreach (var ingredient in recipeIngredients)
        {
            requirements[ingredient.IngredientId] =
                requirements.GetValueOrDefault(ingredient.IngredientId)
                + ingredient.Quanlity * quantity;
        }

        return requirements;
    }

    /// <summary>
    /// Chuyển một phần ledger tiêu hao của dòng gốc sang dòng tách. Chỉ giảm các dòng
    /// <c>Sale/AdjustmentOut</c> và thêm dòng tương ứng cho dòng mới, nên tổng tiêu hao
    /// của cả đơn không đổi (không tạo giao dịch kho ảo).
    /// </summary>
    public static async Task SplitItemLedgerAsync(
        AppDbContext db,
        OrderItem source,
        OrderItem target,
        int originalQuantity,
        int movedQuantity,
        int changedBy,
        DateTime now,
        CancellationToken ct)
    {
        if (originalQuantity <= 0 || movedQuantity <= 0)
        {
            return;
        }

        var transactions = await db.InventoryTransactions
            .Where(x => x.OrderItemId == source.Id
                     && x.ReferenceType == "Order"
                     && x.ReferenceId == source.OrderId)
            .OrderByDescending(x => x.Id)
            .ToListAsync(ct);

        if (transactions.Count == 0)
        {
            return;
        }

        var netByIngredient = ComputeNetConsumption(transactions);
        var additions = new List<InventoryTransaction>();

        foreach (var (ingredientId, net) in netByIngredient)
        {
            if (net <= 0)
            {
                continue;
            }

            var moved = Math.Round(
                net * movedQuantity / originalQuantity,
                3,
                MidpointRounding.AwayFromZero);

            if (moved <= 0)
            {
                continue;
            }

            var remainingToMove = moved;
            foreach (var transaction in transactions
                         .Where(x => x.IngredientId == ingredientId
                                  && (x.Type == "Sale" || x.Type == "AdjustmentOut")
                                  && x.Quanlity > 0))
            {
                if (remainingToMove <= 0)
                {
                    break;
                }

                var take = Math.Min(transaction.Quanlity, remainingToMove);
                transaction.Quanlity -= take;
                remainingToMove -= take;

                additions.Add(new InventoryTransaction
                {
                    IngredientId = ingredientId,
                    Type = transaction.Type,
                    Quanlity = take,
                    QuanlityBefore = transaction.QuanlityBefore,
                    QuanlityAfter = transaction.QuanlityAfter,
                    ReferenceType = transaction.ReferenceType,
                    ReferenceId = transaction.ReferenceId,
                    OrderItemId = target.Id,
                    CreatedAt = now,
                    CreatedBy = changedBy,
                    Note = "Ledger split when an order line was split"
                });
            }
        }

        if (additions.Count > 0)
        {
            db.InventoryTransactions.AddRange(additions);
        }
    }

    /// <summary>
    /// Hoàn kho một phần số lượng của dòng món (chỉ dùng cho phần chưa nấu).
    /// Không gọi SaveChanges.
    /// </summary>
    public static async Task ReturnStockForPartialItemAsync(
        AppDbContext db,
        int orderId,
        OrderItem item,
        int removedQuantity,
        IDictionary<int, decimal> remainingConsumption,
        int changedBy,
        DateTime now,
        CancellationToken ct)
    {
        if (removedQuantity <= 0 || item.Quanlity <= 0)
        {
            return;
        }

        if (removedQuantity >= item.Quanlity)
        {
            await OrderCancellation.ReturnStockForItemAsync(
                db, orderId, item, remainingConsumption, changedBy, now, ct);
            return;
        }

        var consumption = await OrderCancellation.ResolveItemConsumptionAsync(
            db, orderId, item, ct);
        if (consumption.Count == 0)
        {
            return;
        }

        var factor = (decimal)removedQuantity / item.Quanlity;
        var returns = new List<(int IngredientId, decimal Quantity)>();

        foreach (var (ingredientId, quantity) in consumption)
        {
            var requested = Math.Round(
                quantity * factor, 3, MidpointRounding.AwayFromZero);
            if (requested <= 0)
            {
                continue;
            }

            remainingConsumption.TryGetValue(ingredientId, out var remaining);
            var returnQuantity = Math.Min(requested, Math.Max(0m, remaining));
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
                OrderItemId = item.Id,
                CreatedAt = now,
                CreatedBy = changedBy,
                Note = "Returned stock when item quantity was reduced"
            });
        }
    }

    /// <summary>
    /// Ghi nhận phần nguyên liệu đã tiêu hao nhưng không bán được (wastage).
    /// Không thay đổi tồn kho: hàng đã trừ khi tạo đơn, đây chỉ là bút toán audit
    /// để không sinh tồn kho ảo.
    /// </summary>
    public static async Task RecordWastageAsync(
        AppDbContext db,
        int orderId,
        OrderItem item,
        int quantity,
        int changedBy,
        DateTime now,
        string reasonCode,
        string? note,
        CancellationToken ct)
    {
        if (quantity <= 0 || item.Quanlity <= 0)
        {
            return;
        }

        var consumption = await OrderCancellation.ResolveItemConsumptionAsync(
            db, orderId, item, ct);

        if (consumption.Count == 0)
        {
            // Chỉ fallback công thức khi dòng chưa từng có ledger. Nếu ledger đã về 0
            // (đã hoàn/wastage hết) thì không được sinh tiêu hao ảo.
            var hasItemLedger = await db.InventoryTransactions
                .AnyAsync(x => x.OrderItemId == item.Id, ct);
            if (!hasItemLedger)
            {
                consumption = await CalculateRequirementsForQuantityAsync(
                    db, item.SizeId, item.Quanlity, ct);
            }
        }

        var factor = (decimal)quantity / item.Quanlity;
        var amounts = consumption
            .Select(x => new
            {
                x.Key,
                Quantity = Math.Round(
                    x.Value * factor, 3, MidpointRounding.AwayFromZero)
            })
            .Where(x => x.Quantity > 0)
            .ToList();

        if (amounts.Count == 0)
        {
            return;
        }

        var ingredientIds = amounts.Select(x => x.Key).ToList();
        var stocks = await db.Inventories
            .Where(x => ingredientIds.Contains(x.IngredientId))
            .ToDictionaryAsync(x => x.IngredientId, ct);

        foreach (var amount in amounts)
        {
            if (!stocks.TryGetValue(amount.Key, out var stock))
            {
                throw new InvalidOperationException(
                    $"No inventory record exists for ingredient {amount.Key}");
            }

            db.InventoryTransactions.Add(new InventoryTransaction
            {
                IngredientId = amount.Key,
                Type = "Wastage",
                Quanlity = amount.Quantity,
                QuanlityBefore = stock.Quanlity,
                QuanlityAfter = stock.Quanlity,
                ReferenceType = "Order",
                ReferenceId = orderId,
                OrderItemId = item.Id,
                CreatedAt = now,
                CreatedBy = changedBy,
                Note = string.IsNullOrWhiteSpace(note)
                    ? $"Wastage ({reasonCode})"
                    : $"Wastage ({reasonCode}): {note}"
            });
        }
    }

    private static Dictionary<int, decimal> ComputeNetConsumption(
        IEnumerable<InventoryTransaction> transactions)
    {
        var consumption = new Dictionary<int, decimal>();

        foreach (var transaction in transactions)
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

        return consumption;
    }
}
