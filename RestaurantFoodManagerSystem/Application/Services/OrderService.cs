using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class OrderService
{
    private readonly AppDbContext _db;
    private readonly RestaurantTableService _tables;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OrderService> _logger;
    private readonly IOrderIdempotencyStore _idempotency;

    public OrderService(
        AppDbContext db,
        RestaurantTableService tables,
        ICurrentUser currentUser,
        ILogger<OrderService> logger,
        IOrderIdempotencyStore idempotency)
    {
        _db = db;
        _tables = tables;
        _currentUser = currentUser;
        _logger = logger;
        _idempotency = idempotency;
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

        // Batch A: nạp topping của tất cả dòng món trong một truy vấn (tránh N+1).
        if (items.Count > 0)
        {
            var itemIds = items.Select(x => x.Id).ToList();
            var toppingRows = await (
                from ot in _db.OrderItemToppings.AsNoTracking()
                join t in _db.Toppings.AsNoTracking() on ot.ToppingId equals t.ToppingId
                where itemIds.Contains(ot.OrderItemId)
                orderby ot.Id
                select new
                {
                    ot.OrderItemId,
                    Dto = new OrderItemToppingDetailResponse
                    {
                        Id = ot.Id,
                        ToppingId = ot.ToppingId,
                        ToppingName = t.ToppingName,
                        Quanlity = ot.Quanlity,
                        UnitPrice = ot.UnitPrice
                    }
                }).ToListAsync(ct);

            var toppingsByItem = toppingRows
                .GroupBy(x => x.OrderItemId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());

            foreach (var item in items)
            {
                if (toppingsByItem.TryGetValue(item.Id, out var toppings))
                {
                    item.Toppings = toppings;
                }
            }
        }

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

    /// <summary>
    /// Danh sách đơn có lọc + phân trang cho <c>GET /api/orders</c> (P1).
    /// Lấy kèm <c>tableNumber</c> (join bàn) và <c>itemCount</c> (đếm dòng món) trong cùng truy vấn.
    /// </summary>
    public async Task<PagedResponse<OrderListItemResponse>> GetListAsync(OrderListQuery query, CancellationToken ct = default)
    {
        var skip = Paging.ResolveSkip(query.Page, query.PageSize);
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToLowerInvariant();
        var tableId = query.TableId;
        var fromDate = query.From;
        var toDate = query.To;

        var filtered =
            from o in _db.Orders.AsNoTracking()
            join t in _db.RestaurantTables.AsNoTracking() on o.TableId equals t.TableId into tables
            from t in tables.DefaultIfEmpty()
            where (status == null || o.Status.ToLower() == status)
               && (tableId == null || o.TableId == tableId)
               && (fromDate == null || o.CreatedAt >= fromDate)
               && (toDate == null || o.CreatedAt <= toDate)
            select new OrderListItemResponse
            {
                OrderId = o.OrderId,
                TableId = o.TableId,
                TableNumber = t != null ? t.TableNumber : string.Empty,
                Status = o.Status,
                Subtotal = o.Subtotal,
                DiscountAmount = o.DiscountAmount,
                TaxAmount = o.TaxAmount,
                TotalAmount = o.TotalAmount,
                ItemCount = _db.OrderItems.Count(x => x.OrderId == o.OrderId),
                CreatedBy = o.CreatedBy,
                CreatedAt = o.CreatedAt,
                UpdatedAt = o.UpdatedAt
            };

        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.OrderId)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResponse<OrderListItemResponse>
        {
            Page = query.Page,
            PageSize = query.PageSize,
            Total = total,
            Items = items
        };
    }

    public Task<Order> CreateAsync(CreateOrderRequest request, CancellationToken ct) =>
        CreateCoreAsync(request, ct);

    /// <summary>
    /// Tạo đơn có hỗ trợ header <c>Idempotency-Key</c>. Nếu key rỗng, giữ nguyên hành vi cũ.
    /// Cùng key + cùng payload trả lại đúng response cũ; cùng key + payload khác trả 409.
    /// </summary>
    public async Task<OrderCreatedResponse> CreateIdempotentAsync(
        CreateOrderRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return OrderCreatedResponse.From(await CreateCoreAsync(request, ct));
        }

        var key = idempotencyKey.Trim();
        if (key.Length > 128)
        {
            throw new ArgumentException("Idempotency-Key must be at most 128 characters");
        }

        var scope = (_currentUser.UserId ?? request.CreatedBy).ToString();
        var fingerprint = BuildIdempotencyFingerprint(request);

        return await _idempotency.ExecuteAsync(
            $"{scope}:{key}",
            fingerprint,
            async token => OrderCreatedResponse.From(await CreateCoreAsync(request, token)),
            ct);
    }

    private async Task<Order> CreateCoreAsync(CreateOrderRequest request, CancellationToken ct)
    {
        // Defect #20: chỉ kiểm IsActive là chưa đủ — bàn đang bảo trì hoặc đang có đơn mở
        // không được nhận thêm đơn. Trạng thái bàn có thể cũ, nên kiểm trực tiếp từ Orders.
        var table = await _db.RestaurantTables
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TableId == request.TableId && x.IsActive, ct);
        if (table is null) throw new KeyNotFoundException("Table not found");
        if (string.Equals(table.Status, RestaurantTableStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Table is under maintenance");

        var tableHasOpenOrder = await _db.Orders.AnyAsync(
            o => o.TableId == request.TableId
              && o.Status != OrderStatuses.Paid
              && o.Status != OrderStatuses.Completed
              && o.Status != OrderStatuses.Cancelled,
            ct);
        if (tableHasOpenOrder)
            throw new InvalidOperationException("Table already has an open order");

        // Nhóm B8: JSON `null` cho Items lọt qua [MinLength] ⇒ chặn sớm thay vì NRE.
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("Order must contain at least one item");

        // Defect #3: mọi thao tác kiểm tra/trừ kho phải nằm trong cùng transaction
        // Serializable và đọc lại stock ngay trước khi ghi, tránh hai đơn cùng vượt check.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var preparedItems = await PrepareItemsAsync(request.Items, ct);
        var requirements = await CalculateRequirementsAsync(preparedItems, ct);
        var stockDeltas = await PrepareStockDeltasAsync(
            new Dictionary<int, decimal>(),
            requirements,
            ct);

        var createdBy = _currentUser.ResolveActorId(request.CreatedBy, _logger, nameof(request.CreatedBy));
        var order = new Order
        {
            TableId = request.TableId,
            CreatedBy = createdBy,
            Status = OrderStatuses.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var orderItems = await AddOrderItemsAsync(order.OrderId, preparedItems, ct);
        order.Subtotal = orderItems.Sum(x => x.UnitPrice * x.Quanlity);
        order.TotalAmount = order.Subtotal;
        order.Status = OrderStatuses.SentToKitchen;
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

        AddKitchenItems(kitchenOrder, orderItems);
        ApplyStockDeltas(stockDeltas, order.OrderId, createdBy, isEdit: false);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        return order;
    }

    /// <summary>
    /// Thay toàn bộ dòng món của một đơn đang mở. Chỉ cho phép khi bếp chưa bắt đầu
    /// (kitchen order và mọi kitchen item còn <c>Pending</c>); tồn kho được điều chỉnh
    /// theo phần chênh lệch giữa đơn cũ và đơn mới.
    /// </summary>
    public async Task<bool> UpdateItemsAsync(
        int orderId,
        UpdateOrderItemsRequest request,
        CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("Order must contain at least one item");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return false;

        if (!string.Equals(order.Status, OrderStatuses.Pending, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(order.Status, OrderStatuses.SentToKitchen, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Order is {order.Status} and can no longer be edited");
        }

        var kitchenOrder = await _db.KitchenOrders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (kitchenOrder is not null)
        {
            if (!string.Equals(kitchenOrder.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Kitchen has already started this order");

            var kitchenItems = await _db.KitchenOrderItems
                .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
                .ToListAsync(ct);

            if (kitchenItems.Any(x => !string.Equals(x.Status, "Pending", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Kitchen has already started this order");

            _db.KitchenOrderItems.RemoveRange(kitchenItems);
            await _db.SaveChangesAsync(ct);
        }

        var existingItems = await _db.OrderItems
            .Where(x => x.OrderId == orderId)
            .ToListAsync(ct);
        var existingItemIds = existingItems.Select(x => x.Id).ToList();
        var existingToppings = existingItemIds.Count == 0
            ? []
            : await _db.OrderItemToppings
                .Where(x => existingItemIds.Contains(x.OrderItemId))
                .ToListAsync(ct);

        var oldRequirements = await LoadOrderConsumptionAsync(orderId, existingItems, ct);
        var preparedItems = await PrepareItemsAsync(request.Items, ct);
        var newRequirements = await CalculateRequirementsAsync(preparedItems, ct);
        var stockDeltas = await PrepareStockDeltasAsync(oldRequirements, newRequirements, ct);

        _db.OrderItemToppings.RemoveRange(existingToppings);
        _db.OrderItems.RemoveRange(existingItems);
        await _db.SaveChangesAsync(ct);

        var orderItems = await AddOrderItemsAsync(orderId, preparedItems, ct);
        order.Subtotal = orderItems.Sum(x => x.UnitPrice * x.Quanlity);
        // Sửa món làm thay đổi cơ sở tính khuyến mãi; bỏ discount cũ để tránh áp sai.
        order.DiscountAmount = 0m;
        order.TotalAmount = Math.Round(order.Subtotal + order.TaxAmount, 2, MidpointRounding.AwayFromZero);
        order.UpdatedAt = DateTime.UtcNow;

        var changedBy = _currentUser.ResolveActorId(request.ChangedBy, _logger, nameof(request.ChangedBy));

        if (kitchenOrder is null)
        {
            kitchenOrder = new KitchenOrder
            {
                OrderId = orderId,
                Status = "Pending",
                SentAt = DateTime.UtcNow,
                Priority = 0
            };
            _db.KitchenOrders.Add(kitchenOrder);
            await _db.SaveChangesAsync(ct);
        }

        AddKitchenItems(kitchenOrder, orderItems);

        if (string.Equals(order.Status, OrderStatuses.Pending, StringComparison.OrdinalIgnoreCase))
        {
            var oldStatus = order.Status;
            order.Status = OrderStatuses.SentToKitchen;
            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                OldStatus = oldStatus,
                NewStatus = order.Status,
                ChangedBy = changedBy,
                Note = "Order items updated",
                CreatedAt = DateTime.UtcNow
            });
        }

        ApplyStockDeltas(stockDeltas, orderId, changedBy, isEdit: true);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        return true;
    }

    private async Task<List<PreparedOrderItem>> PrepareItemsAsync(
        IReadOnlyList<CreateOrderItemRequest> itemRequests,
        CancellationToken ct)
    {
        var prepared = new List<PreparedOrderItem>(itemRequests.Count);

        foreach (var itemRequest in itemRequests)
        {
            if (itemRequest.Toppings is null)
                throw new ArgumentException("Order item toppings cannot be null");

            var size = await _db.FoodSizes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SizeId == itemRequest.SizeId && x.IsActive, ct);
            if (size is null)
                throw new KeyNotFoundException($"Food size {itemRequest.SizeId} not found");

            var unitPrice = size.SizePrice;
            var toppings = new List<PreparedTopping>(itemRequest.Toppings.Count);

            foreach (var toppingRequest in itemRequest.Toppings)
            {
                // Defect #19: topping phải được gán cho đúng món qua FoodToppings.
                var topping = await (
                    from t in _db.Toppings.AsNoTracking()
                    join ft in _db.FoodToppings.AsNoTracking() on t.ToppingId equals ft.ToppingId
                    where t.ToppingId == toppingRequest.ToppingId
                       && t.IsActive
                       && t.IsAvailable
                       && ft.FoodId == size.FoodId
                    select new { t.ToppingId, t.ToppingPrice }).FirstOrDefaultAsync(ct);

                if (topping is null)
                    throw new KeyNotFoundException($"Topping {toppingRequest.ToppingId} is not available for this food");

                unitPrice += topping.ToppingPrice * toppingRequest.Quanlity;
                toppings.Add(new PreparedTopping(
                    topping.ToppingId,
                    toppingRequest.Quanlity,
                    topping.ToppingPrice));
            }

            prepared.Add(new PreparedOrderItem(
                size.SizeId,
                itemRequest.Quanlity,
                unitPrice,
                itemRequest.Note,
                toppings));
        }

        return prepared;
    }

    private async Task<Dictionary<int, decimal>> CalculateRequirementsAsync(
        IReadOnlyList<PreparedOrderItem> items,
        CancellationToken ct)
    {
        var requirements = new Dictionary<int, decimal>();

        foreach (var item in items)
        {
            var recipeIngredients = await (
                from recipe in _db.Recipes.AsNoTracking()
                join recipeIngredient in _db.RecipeIngredients.AsNoTracking()
                    on recipe.RecipeId equals recipeIngredient.RecipeId
                where recipe.SizeId == item.SizeId && recipe.IsActive
                select recipeIngredient).ToListAsync(ct);

            if (recipeIngredients.Count == 0)
                throw new InvalidOperationException($"Food size {item.SizeId} does not have an active recipe");

            foreach (var ingredient in recipeIngredients)
            {
                var required = ingredient.Quanlity * item.Quanlity;
                requirements[ingredient.IngredientId] =
                    requirements.GetValueOrDefault(ingredient.IngredientId) + required;
            }
        }

        return requirements;
    }

    private async Task<List<StockDelta>> PrepareStockDeltasAsync(
        IReadOnlyDictionary<int, decimal> oldRequirements,
        IReadOnlyDictionary<int, decimal> newRequirements,
        CancellationToken ct)
    {
        var ingredientIds = oldRequirements.Keys
            .Union(newRequirements.Keys)
            .ToList();

        if (ingredientIds.Count == 0)
        {
            return [];
        }

        var stocks = await _db.Inventories
            .Where(x => ingredientIds.Contains(x.IngredientId))
            .ToDictionaryAsync(x => x.IngredientId, ct);
        var ingredients = await _db.Ingredients
            .AsNoTracking()
            .Where(x => ingredientIds.Contains(x.IngredientId))
            .ToDictionaryAsync(x => x.IngredientId, ct);

        var deltas = new List<StockDelta>();

        foreach (var ingredientId in ingredientIds)
        {
            var delta = newRequirements.GetValueOrDefault(ingredientId)
                        - oldRequirements.GetValueOrDefault(ingredientId);
            if (delta == 0)
            {
                continue;
            }

            if (!stocks.TryGetValue(ingredientId, out var stock))
            {
                throw new InvalidOperationException(
                    $"No inventory record exists for ingredient {ingredientId}");
            }

            var ingredientName = ingredients.TryGetValue(ingredientId, out var ingredient)
                ? ingredient.Name
                : ingredientId.ToString();

            if (delta > 0 && stock.Quanlity < delta)
            {
                throw new InvalidOperationException(
                    $"Insufficient ingredient: {ingredientName}. Required {delta}, available {stock.Quanlity}");
            }

            deltas.Add(new StockDelta(stock, ingredientId, delta));
        }

        return deltas;
    }

    private async Task<List<OrderItem>> AddOrderItemsAsync(
        int orderId,
        IReadOnlyList<PreparedOrderItem> preparedItems,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var orderItems = preparedItems
            .Select(x => new OrderItem
            {
                OrderId = orderId,
                SizeId = x.SizeId,
                Quanlity = x.Quanlity,
                UnitPrice = x.UnitPrice,
                Note = x.Note,
                Status = "Pending",
                CreatedAt = now
            })
            .ToList();

        _db.OrderItems.AddRange(orderItems);
        await _db.SaveChangesAsync(ct);

        for (var i = 0; i < orderItems.Count; i++)
        {
            var orderItem = orderItems[i];
            foreach (var topping in preparedItems[i].Toppings)
            {
                _db.OrderItemToppings.Add(new OrderItemTopping
                {
                    OrderItemId = orderItem.Id,
                    ToppingId = topping.ToppingId,
                    Quanlity = topping.Quanlity,
                    UnitPrice = topping.UnitPrice
                });
            }
        }

        return orderItems;
    }

    private void AddKitchenItems(
        KitchenOrder kitchenOrder,
        IReadOnlyList<OrderItem> orderItems)
    {
        _db.KitchenOrderItems.AddRange(orderItems.Select(x => new KitchenOrderItem
        {
            KitchenOrderId = kitchenOrder.KitchenOrderId,
            OrderItemId = x.Id,
            Quanlity = x.Quanlity,
            Status = "Pending",
            Note = x.Note
        }));
    }

    private async Task<Dictionary<int, decimal>> LoadOrderConsumptionAsync(
        int orderId,
        IReadOnlyList<OrderItem> existingItems,
        CancellationToken ct)
    {
        var transactions = await _db.InventoryTransactions
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
                "AdjustmentIn" => -transaction.Quanlity,
                "AdjustmentOut" => transaction.Quanlity,
                _ => 0m
            };

            consumption[transaction.IngredientId] =
                consumption.GetValueOrDefault(transaction.IngredientId) + delta;
        }

        if (consumption.Count > 0)
        {
            return consumption;
        }

        // Legacy order chưa có InventoryTransactions: ước tính lại từ món hiện tại.
        foreach (var item in existingItems)
        {
            var recipeIngredients = await (
                from recipe in _db.Recipes.AsNoTracking()
                join recipeIngredient in _db.RecipeIngredients.AsNoTracking()
                    on recipe.RecipeId equals recipeIngredient.RecipeId
                where recipe.SizeId == item.SizeId && recipe.IsActive
                select recipeIngredient).ToListAsync(ct);

            foreach (var ingredient in recipeIngredients)
            {
                consumption[ingredient.IngredientId] =
                    consumption.GetValueOrDefault(ingredient.IngredientId)
                    + ingredient.Quanlity * item.Quanlity;
            }
        }

        return consumption;
    }

    private void ApplyStockDeltas(
        IReadOnlyList<StockDelta> deltas,
        int orderId,
        int createdBy,
        bool isEdit)
    {
        var now = DateTime.UtcNow;

        foreach (var delta in deltas)
        {
            var before = delta.Stock.Quanlity;
            delta.Stock.Quanlity -= delta.Delta;
            delta.Stock.UpdatedAt = now;

            var isConsumption = delta.Delta > 0;
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                IngredientId = delta.IngredientId,
                Type = isConsumption ? "Sale" : "AdjustmentIn",
                Quanlity = Math.Abs(delta.Delta),
                QuanlityBefore = before,
                QuanlityAfter = delta.Stock.Quanlity,
                ReferenceType = "Order",
                ReferenceId = orderId,
                CreatedAt = now,
                CreatedBy = createdBy,
                Note = isEdit
                    ? (isConsumption
                        ? "Additional consumption when order items were updated"
                        : "Returned stock when order items were updated")
                    : "Automatically deducted when order was sent to kitchen"
            });
        }
    }

    private static string BuildIdempotencyFingerprint(CreateOrderRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private sealed record PreparedOrderItem(
        int SizeId,
        int Quanlity,
        decimal UnitPrice,
        string? Note,
        List<PreparedTopping> Toppings);

    private sealed record PreparedTopping(
        int ToppingId,
        int Quanlity,
        decimal UnitPrice);

    private sealed record StockDelta(
        Inventory Stock,
        int IngredientId,
        decimal Delta);

    /// <summary>
    /// Lịch sử trạng thái đơn cho <c>GET /api/orders/{id}/history</c> (P1), sắp xếp theo thời gian tăng dần.
    /// Trả <c>null</c> khi đơn không tồn tại để controller map thành 404 (đơn rỗng cũng là 200 `[]`).
    /// </summary>
    public async Task<List<OrderStatusHistoryResponse>?> GetHistoryAsync(int orderId, CancellationToken ct = default)
    {
        if (!await _db.Orders.AsNoTracking().AnyAsync(x => x.OrderId == orderId, ct))
            return null;

        return await (
            from h in _db.OrderStatusHistories.AsNoTracking()
            join u in _db.Users.AsNoTracking() on h.ChangedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            where h.OrderId == orderId
            orderby h.CreatedAt, h.Id
            select new OrderStatusHistoryResponse
            {
                Id = h.Id,
                OrderId = h.OrderId,
                OldStatus = h.OldStatus,
                NewStatus = h.NewStatus,
                ChangedBy = h.ChangedBy,
                ChangedByName = u != null ? u.UserFullName : null,
                Note = h.Note,
                CreatedAt = h.CreatedAt
            }).ToListAsync(ct);
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

        // Defect #18: DB lưu decimal(18,2); làm tròn ngay tại đây để giá trị trả về/tính tiếp
        // khớp với giá trị thực sự được lưu (SQL Server làm tròn half away from zero).
        // Clamp 0..Subtotal để promotion cấu hình sai (giá trị âm) không làm tăng tổng tiền.
        var roundedDiscount = Math.Round(
            Math.Clamp(discount, 0m, order.Subtotal),
            2,
            MidpointRounding.AwayFromZero);
        order.DiscountAmount = roundedDiscount;
        order.TotalAmount = Math.Round(order.Subtotal - roundedDiscount + order.TaxAmount, 2, MidpointRounding.AwayFromZero);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct); return true;
    }
}
