using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Domain.AppDbContext;

public sealed class OrderService
{
    private readonly AppDbContext _db;
    private readonly RestaurantTableService _tables;
    public OrderService(AppDbContext db, RestaurantTableService tables) { _db = db; _tables = tables; }

    public async Task<Order?> GetAsync(int id, CancellationToken ct) => await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == id, ct);

    public async Task<Order> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        if (!await _db.RestaurantTables.AnyAsync(x => x.TableId == request.TableId && x.IsActive, ct)) throw new KeyNotFoundException("Table not found");

        // Calculate all ingredient requirements before creating the order.
        // One order can contain the same size more than once, so quantities are aggregated.
        var requiredIngredients = new Dictionary<int, decimal>();
        foreach (var itemRequest in request.Items)
        {
            var recipeIngredients = await (
                from recipe in _db.Recipes
                join recipeIngredient in _db.RecipeIngredients on recipe.RecipeId equals recipeIngredient.RecipeId
                where recipe.SizeId == itemRequest.SizeId && recipe.IsActive
                select recipeIngredient).ToListAsync(ct);

            if (recipeIngredients.Count == 0)
                throw new InvalidOperationException($"Food size {itemRequest.SizeId} does not have an active recipe");

            foreach (var ingredient in recipeIngredients)
            {
                var required = ingredient.Quanlity * itemRequest.Quantity;
                requiredIngredients[ingredient.IngredientId] =
                    requiredIngredients.GetValueOrDefault(ingredient.IngredientId) + required;
            }
        }

        var stocks = await _db.Inventories
            .Where(x => requiredIngredients.Keys.Contains(x.IngredientId))
            .ToDictionaryAsync(x => x.IngredientId, ct);
        var ingredients = await _db.Ingredients
            .Where(x => requiredIngredients.Keys.Contains(x.IngredientId))
            .ToDictionaryAsync(x => x.IngredientId, ct);

        foreach (var requirement in requiredIngredients)
        {
            if (!stocks.TryGetValue(requirement.Key, out var stock))
                throw new InvalidOperationException($"No inventory record exists for ingredient {requirement.Key}");

            var ingredientName = ingredients.TryGetValue(requirement.Key, out var ingredient)
                ? ingredient.Name
                : requirement.Key.ToString();

            if (stock.Quanlity < requirement.Value)
                throw new InvalidOperationException(
                    $"Insufficient ingredient: {ingredientName}. Required {requirement.Value}, available {stock.Quanlity}");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var order = new Order { TableId = request.TableId, CreatedBy = request.CreatedBy, Status = "Pending", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Orders.Add(order); await _db.SaveChangesAsync(ct);
        foreach (var itemRequest in request.Items)
        {
            var size = await _db.FoodSizes.FirstOrDefaultAsync(x => x.SizeId == itemRequest.SizeId && x.IsActive, ct);
            if (size is null) throw new KeyNotFoundException($"Food size {itemRequest.SizeId} not found");
            var item = new OrderItem { OrderId = order.OrderId, SizeId = size.SizeId, Quanlity = itemRequest.Quantity, UnitPrice = size.SizePrice, Note = itemRequest.Note, Status = "Pending", CreatedAt = DateTime.UtcNow };
            order.Subtotal += item.UnitPrice * item.Quanlity;
            _db.OrderItems.Add(item);
            await _db.SaveChangesAsync(ct);
            order.TotalAmount = order.Subtotal;
            foreach (var toppingRequest in itemRequest.Toppings)
            {
                var topping = await _db.Toppings.FirstOrDefaultAsync(x => x.ToppingId == toppingRequest.ToppingId && x.IsActive && x.IsAvailable, ct);
                if (topping is null) throw new KeyNotFoundException($"Topping {toppingRequest.ToppingId} not found");
                item.UnitPrice += topping.ToppingPrice * toppingRequest.Quantity;
                order.Subtotal += topping.ToppingPrice * toppingRequest.Quantity * item.Quanlity;
                _db.OrderItemToppings.Add(new OrderItemTopping { OrderItemId = item.Id, ToppingId = topping.ToppingId, Quanlity = toppingRequest.Quantity, UnitPrice = topping.ToppingPrice });
            }
        }
        order.TotalAmount = order.Subtotal;
        order.Status = "SentToKitchen";
        await _db.SaveChangesAsync(ct);

        // Create the kitchen ticket only after every order item has been persisted.
        var kitchenOrder = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = "Pending",
            SentAt = DateTime.UtcNow,
            Priority = 0
        };
        _db.KitchenOrders.Add(kitchenOrder);
        await _db.SaveChangesAsync(ct);

        var orderItems = await _db.OrderItems
            .Where(x => x.OrderId == order.OrderId)
            .ToListAsync(ct);
        foreach (var orderItem in orderItems)
        {
            _db.KitchenOrderItems.Add(new KitchenOrderItem
            {
                KitchenOrderId = kitchenOrder.KitchenOrderId,
                OrderItemId = orderItem.Id,
                Quanlity = orderItem.Quanlity,
                Status = "Pending",
                Note = orderItem.Note
            });
        }

        // Deduct stock only after all order and kitchen items are valid.
        foreach (var requirement in requiredIngredients)
        {
            var stock = stocks[requirement.Key];
            var before = stock.Quanlity;
            stock.Quanlity -= requirement.Value;
            stock.UpdatedAt = DateTime.UtcNow;
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                IngredientId = requirement.Key,
                Type = "Sale",
                Quanlity = requirement.Value,
                QuanlityBefore = before,
                QuanlityAfter = stock.Quanlity,
                ReferenceType = "Order",
                ReferenceId = order.OrderId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = request.CreatedBy,
                Note = "Automatically deducted when order was sent to kitchen"
            });
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _tables.RefreshStatusForTableAsync(order.TableId);
        return order;
    }

    public async Task<bool> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == id, ct); if (order is null) return false;
        var old = order.Status; order.Status = request.Status.Trim(); order.UpdatedAt = DateTime.UtcNow;
        _db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = id, OldStatus = old, NewStatus = order.Status, ChangedBy = request.ChangedBy, Note = request.Note, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct); return true;
    }

    public async Task<bool> ApplyPromotionAsync(int orderId, int promotionId, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        var promotion = await _db.Promotions.FirstOrDefaultAsync(x => x.Id == promotionId && x.IsActive && x.StartAt <= DateTime.UtcNow && x.EndAt >= DateTime.UtcNow, ct);
        if (order is null || promotion is null || order.Subtotal < promotion.MinimumOrderAmount) return false;
        var discount = promotion.DiscountType.Equals("Fixed", StringComparison.OrdinalIgnoreCase) ? promotion.DiscountValue : order.Subtotal * promotion.DiscountValue / 100m;
        if (promotion.MaxDiscountAmount > 0) discount = Math.Min(discount, promotion.MaxDiscountAmount);
        order.DiscountAmount = Math.Min(discount, order.Subtotal); order.TotalAmount = order.Subtotal - order.DiscountAmount + order.TaxAmount; order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct); return true;
    }
}
