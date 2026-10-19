using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class OrderService
{
    private readonly AppDbContext _db;
    private readonly RestaurantTableService _tables;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OrderService> _logger;

    public OrderService(AppDbContext db, RestaurantTableService tables, ICurrentUser currentUser, ILogger<OrderService> logger)
    {
        _db = db;
        _tables = tables;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Chi tiết đơn hàng cho <c>GET /api/orders/{id}</c> (Nhóm C/GĐ7).
    /// Bổ sung <c>items[]</c> (kèm tên món/cỡ) so với entity <c>Order</c> thô; mọi field cũ được giữ nguyên.
    /// </summary>
    public async Task<OrderDetailResponse?> GetAsync(int id, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == id, ct);
        if (order is null) return null;

        var items = await (from oi in _db.OrderItems.AsNoTracking()
                           join fs in _db.FoodSizes on oi.SizeId equals fs.SizeId
                           join f in _db.Foods on fs.FoodId equals f.FoodId
                           where oi.OrderId == id
                           select new OrderItemDetailResponse
                           {
                               Id = oi.Id,
                               SizeId = oi.SizeId,
                               FoodName = f.FoodName,
                               SizeName = fs.SizeName,
                               Quanlity = oi.Quanlity,
                               UnitPrice = oi.UnitPrice,
                               Note = oi.Note,
                               Status = oi.Status
                           }).ToListAsync(ct);

        return new OrderDetailResponse
        {
            OrderId = order.OrderId,
            TableId = order.TableId,
            Status = order.Status,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            TotalAmount = order.TotalAmount,
            CreatedBy = order.CreatedBy,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Items = items
        };
    }

    public async Task<Order> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        if (!await _db.RestaurantTables.AnyAsync(x => x.TableId == request.TableId && x.IsActive, ct)) throw new KeyNotFoundException("Table not found");

        // Nhóm B8: JSON `null` cho Items lọt qua [MinLength] ⇒ chặn sớm thay vì NRE.
        if (request.Items is null || request.Items.Count == 0) throw new ArgumentException("Order must contain at least one item");

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
                var required = ingredient.Quanlity * itemRequest.Quanlity;
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
        var createdBy = _currentUser.ResolveActorId(request.CreatedBy, _logger, nameof(request.CreatedBy));
        var order = new Order { TableId = request.TableId, CreatedBy = createdBy, Status = "Pending", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Orders.Add(order); await _db.SaveChangesAsync(ct);
        foreach (var itemRequest in request.Items)
        {
            var size = await _db.FoodSizes.FirstOrDefaultAsync(x => x.SizeId == itemRequest.SizeId && x.IsActive, ct);
            if (size is null) throw new KeyNotFoundException($"Food size {itemRequest.SizeId} not found");
            var item = new OrderItem { OrderId = order.OrderId, SizeId = size.SizeId, Quanlity = itemRequest.Quanlity, UnitPrice = size.SizePrice, Note = itemRequest.Note, Status = "Pending", CreatedAt = DateTime.UtcNow };
            order.Subtotal += item.UnitPrice * item.Quanlity;
            _db.OrderItems.Add(item);
            await _db.SaveChangesAsync(ct);
            order.TotalAmount = order.Subtotal;
            foreach (var toppingRequest in itemRequest.Toppings)
            {
                var topping = await _db.Toppings.FirstOrDefaultAsync(x => x.ToppingId == toppingRequest.ToppingId && x.IsActive && x.IsAvailable, ct);
                if (topping is null) throw new KeyNotFoundException($"Topping {toppingRequest.ToppingId} not found");
                item.UnitPrice += topping.ToppingPrice * toppingRequest.Quanlity;
                order.Subtotal += topping.ToppingPrice * toppingRequest.Quanlity * item.Quanlity;
                _db.OrderItemToppings.Add(new OrderItemTopping { OrderItemId = item.Id, ToppingId = topping.ToppingId, Quanlity = toppingRequest.Quanlity, UnitPrice = topping.ToppingPrice });
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
                CreatedBy = createdBy,
                Note = "Automatically deducted when order was sent to kitchen"
            });
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _tables.RefreshStatusForTableAsync(order.TableId);
        return order;
    }

    /// <summary>
    /// Cập nhật trạng thái đơn (Nhóm B2): chỉ nhận allow-list, từ chối chuỗi rỗng/không hợp lệ,
    /// và không cho đổi khỏi trạng thái cuối (<c>Paid</c>/<c>Cancelled</c>).
    /// </summary>
    public async Task<bool> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == id, ct);
        if (order is null) return false;

        var status = request.Status?.Trim() ?? string.Empty;
        if (status.Length == 0)
            throw new ArgumentException("Order status is required");
        if (!OrderStatuses.IsUpdatable(status))
            throw new ArgumentException("Invalid order status");
        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException($"Order is {order.Status} and can no longer change status");

        var old = order.Status;
        order.Status = status;
        order.UpdatedAt = DateTime.UtcNow;
        var changedBy = _currentUser.ResolveActorId(request.ChangedBy, _logger, nameof(request.ChangedBy));
        _db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = id, OldStatus = old, NewStatus = order.Status, ChangedBy = changedBy, Note = request.Note, CreatedAt = DateTime.UtcNow });
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
