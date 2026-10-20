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
    private readonly OrderLifecycleCoordinator _lifecycle;

    public OrderService(
        AppDbContext db,
        RestaurantTableService tables,
        ICurrentUser currentUser,
        ILogger<OrderService> logger,
        IOrderIdempotencyStore idempotency,
        OrderLifecycleCoordinator lifecycle)
    {
        _db = db;
        _tables = tables;
        _currentUser = currentUser;
        _logger = logger;
        _idempotency = idempotency;
        _lifecycle = lifecycle;
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
                               Status = oi.Status,
                               OrderItemGroupId = oi.OrderItemGroupId,
                               CancelReason = oi.CancelReason,
                               CancelledAt = oi.CancelledAt,
                               CancelledBy = oi.CancelledBy
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
        // Nhóm B8: JSON `null` cho Items lọt qua [MinLength] ⇒ chặn sớm thay vì NRE.
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("Order must contain at least one item");

        // Defect #3 + Batch A2 (15.1): mọi thao tác kiểm tra/trừ kho phải nằm trong cùng
        // transaction Serializable. Kiểm tra "bàn đã có đơn mở" cũng phải ở trong transaction,
        // nếu không hai request đồng thời cùng thấy "chưa có đơn" rồi cùng tạo đơn.
        // Trên SQL Server, Serializable giữ key-range lock trên TableId tới khi commit.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Defect #20: chỉ kiểm IsActive là chưa đủ — bàn đang bảo trì hoặc đang có đơn mở
        // không được nhận thêm đơn. Trạng thái bàn có thể cũ, nên kiểm trực tiếp từ Orders.
        var table = await _db.RestaurantTables
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TableId == request.TableId && x.IsActive, ct);
        if (table is null) throw new KeyNotFoundException("Table not found");
        if (string.Equals(table.Status, RestaurantTableStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Table is under maintenance");

        // Completed chỉ là bếp/khách đã xong món; bàn chỉ được trả về trống sau khi Paid
        // (hoặc Cancelled). Vì vậy Completed vẫn phải chặn tạo đơn mới trên cùng bàn.
        var tableHasOpenOrder = await _db.Orders.AnyAsync(
            o => o.TableId == request.TableId
              && o.Status != OrderStatuses.Paid
              && o.Status != OrderStatuses.Cancelled,
            ct);
        if (tableHasOpenOrder)
            throw new InvalidOperationException("Table already has an open order");

        var preparedItems = await PrepareItemsAsync(request.Items, ct);
        var itemRequirements = await CalculateItemRequirementsAsync(preparedItems, ct);
        var requirements = AggregateRequirements(itemRequirements);
        var stockDeltas = await PrepareStockDeltasAsync(
            new Dictionary<int, decimal>(),
            requirements,
            ct);

        var createdBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.CreatedBy, _logger, nameof(request.CreatedBy), ct);
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

        // P1a: ghi ledger kho theo từng dòng món để huỷ món/đơn hoàn đúng phần đã trừ.
        ApplyItemStock(itemRequirements, orderItems, stockDeltas, order.OrderId, createdBy);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        return order;
    }

    /// <summary>
    /// LEGACY (C-09) — KHÔNG còn expose qua HTTP.
    ///
    /// <para>
    /// Route <c>PUT /api/orders/{id}/items</c> đã bị chặn (trả <c>410 Gone</c>) vì
    /// replace-all xoá hẳn dòng món cũ (mất liên kết ledger + dấu vết huỷ), ghi ledger ở
    /// cấp đơn nên huỷ món sau đó phải ước tính lại từ công thức, và không ghi history món.
    /// Thay bằng: <c>AddItemsAsync</c> (gọi thêm), <c>AdjustItemQuantityAsync</c> (đổi số
    /// lượng) và <c>CancelItemAsync</c> (hủy 1 món).
    /// </para>
    ///
    /// <para>
    /// Method này chỉ còn lại cho test mô tả hành vi cũ; không được gọi từ code mới.
    /// Xoá hẳn khi không còn client nào dùng.
    /// </para>
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

        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);

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
            await _lifecycle.ApplyOrderStatusAsync(
                order,
                OrderStatuses.SentToKitchen,
                changedBy,
                "Order items updated",
                cancelReason: null,
                ct);
        }

        ApplyStockDeltas(stockDeltas, orderId, changedBy, isEdit: true);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        return true;
    }

    /// <summary>
    /// Gọi thêm món vào đơn đang mở. Luôn tạo một round/ticket bếp mới; không replace-all
    /// và không đụng các dòng/ticket cũ. Order <c>Completed</c> được mở lại thành
    /// <c>SentToKitchen</c> nhưng bàn vẫn <c>Occupied</c>.
    /// </summary>
    public async Task<bool> AddItemsAsync(
        int orderId,
        AddOrderItemsRequest request,
        CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("Order must contain at least one item");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        var table = await _db.RestaurantTables
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TableId == order.TableId, ct);

        if (table is null || !table.IsActive ||
            !string.Equals(table.Status, RestaurantTableStatuses.Occupied, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Table is no longer occupied; open a new order instead");
        }

        var preparedItems = await PrepareItemsAsync(request.Items, ct);
        var itemRequirements = await CalculateItemRequirementsAsync(preparedItems, ct);
        var requirements = AggregateRequirements(itemRequirements);
        var stockDeltas = await PrepareStockDeltasAsync(
            new Dictionary<int, decimal>(),
            requirements,
            ct);

        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);
        var now = DateTime.UtcNow;

        var kitchenOrder = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = KitchenStatuses.Pending,
            SentAt = now,
            Priority = 0
        };
        _db.KitchenOrders.Add(kitchenOrder);
        await _db.SaveChangesAsync(ct);

        var orderItems = await AddOrderItemsAsync(order.OrderId, preparedItems, ct);
        AddKitchenItems(kitchenOrder, orderItems);
        ApplyItemStock(itemRequirements, orderItems, stockDeltas, order.OrderId, changedBy);

        var allItems = await _db.OrderItems
            .Where(x => x.OrderId == order.OrderId)
            .ToListAsync(ct);
        OrderItemMutations.RecalculateTotals(order, allItems, now);

        var statusChanged = await _lifecycle.RefreshOrderStatusFromAllItemsAsync(
            order,
            changedBy,
            "Added items (new kitchen round)",
            allowReopen: true,
            cancelReason: null,
            ct);

        if (!statusChanged)
        {
            _lifecycle.RecordOrderNote(order, changedBy, "Added items (new kitchen round)");
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        return true;
    }

    /// <summary>
    /// Giảm/tăng số lượng của một món, kể cả sau khi bếp đã nhận.
    ///
    /// <para>
    /// Cách A: các dòng đã tách (prepared/pending) được cộng dồn theo <c>OrderItemGroupId</c>.
    /// Giảm chỉ tác động phần chưa nấu; giảm xuống dưới số đã nấu bị chặn (409) trừ khi
    /// Admin bật <c>AllowWastage</c>. Tăng sau ticket <c>Completed</c> tạo round/ticket mới.
    /// </para>
    /// </summary>
    public async Task<bool> AdjustItemQuantityAsync(
        int orderId,
        int itemId,
        AdjustOrderItemQuantityRequest request,
        CancellationToken ct)
    {
        if ((request.Quantity is null) == (request.Delta is null))
            throw new ArgumentException("Provide exactly one of quantity or delta");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        var source = await _db.OrderItems
            .FirstOrDefaultAsync(x => x.Id == itemId && x.OrderId == orderId, ct);
        if (source is null) return false;

        if (KitchenStatuses.IsCancelled(source.Status))
            throw new InvalidOperationException("Order item is already cancelled");

        OrderItemMutations.EnsureGroupId(source);

        var groupItems = await OrderItemMutations.LoadGroupAsync(_db, source, ct);
        var groupKitchenItems = await OrderItemMutations.LoadKitchenItemsAsync(
            _db, groupItems.Select(x => x.Id), ct);
        var kitchenByOrderItem = groupKitchenItems
            .GroupBy(x => x.OrderItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

        string EffectiveStatus(OrderItem item) =>
            kitchenByOrderItem.TryGetValue(item.Id, out var kitchenItem)
                ? kitchenItem.Status
                : item.Status;

        var activeItems = groupItems
            .Where(x => !KitchenStatuses.IsCancelled(x.Status)
                     && !KitchenStatuses.IsCancelled(EffectiveStatus(x)))
            .ToList();

        if (activeItems.Count == 0)
            throw new InvalidOperationException("Order item is already cancelled");

        var currentQuantity = activeItems.Sum(x => x.Quanlity);
        var preparedItems = activeItems
            .Where(x => KitchenStatuses.IsDone(EffectiveStatus(x)))
            .ToList();
        var preparedQuantity = preparedItems.Sum(x => x.Quanlity);
        var pendingItems = activeItems
            .Where(x => !KitchenStatuses.IsDone(EffectiveStatus(x)))
            .ToList();

        int targetQuantity;
        if (request.Quantity is not null)
        {
            targetQuantity = request.Quantity.Value;
        }
        else
        {
            var target = (long)currentQuantity + request.Delta!.Value;
            if (target < 0 || target > int.MaxValue)
                throw new ArgumentException("Quantity must be between 0 and the maximum integer");
            targetQuantity = (int)target;
        }

        if (targetQuantity < 0)
            throw new ArgumentException("Quantity cannot be negative");

        if (targetQuantity == currentQuantity)
            return true;

        var reasonCode = OrderItemMutations.NormalizeReasonCode(
            request.ReasonCode, OrderItemMutations.CustomerChange);
        var detail = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason.Trim();

        if (targetQuantity == 0 && detail is null)
            throw new ArgumentException("Reason is required when quantity is reduced to 0");

        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);
        var now = DateTime.UtcNow;
        var affectedTicketIds = new HashSet<int>();
        var createdNewRound = false;

        if (targetQuantity > currentQuantity)
        {
            var added = targetQuantity - currentQuantity;

            var pendingCandidate = pendingItems
                .Where(x =>
                {
                    if (!kitchenByOrderItem.TryGetValue(x.Id, out var kitchenItem))
                        return false;

                    var ticket = _db.KitchenOrders.Local
                        .FirstOrDefault(t => t.KitchenOrderId == kitchenItem.KitchenOrderId);
                    ticket ??= _db.KitchenOrders
                        .FirstOrDefault(t => t.KitchenOrderId == kitchenItem.KitchenOrderId);

                    return ticket is not null
                        && !KitchenStatuses.IsTerminal(ticket.Status)
                        && !string.Equals(ticket.Status, KitchenStatuses.Ready, StringComparison.OrdinalIgnoreCase)
                        && !KitchenStatuses.IsDone(kitchenItem.Status);
                })
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

            if (pendingCandidate is not null)
            {
                var kitchenItem = kitchenByOrderItem[pendingCandidate.Id];
                var oldQuantity = pendingCandidate.Quanlity;
                pendingCandidate.Quanlity += added;
                kitchenItem.Quanlity += added;
                affectedTicketIds.Add(kitchenItem.KitchenOrderId);

                OrderItemMutations.RecordItemHistory(
                    _db,
                    pendingCandidate,
                    kitchenItem.Id,
                    pendingCandidate.Status,
                    pendingCandidate.Status,
                    changedBy,
                    now,
                    $"Quantity {oldQuantity} -> {pendingCandidate.Quanlity}",
                    reasonCode);

                var extraRequirements = await OrderItemMutations.CalculateRequirementsForQuantityAsync(
                    _db, pendingCandidate.SizeId, added, ct);
                var extraDeltas = await PrepareStockDeltasAsync(
                    new Dictionary<int, decimal>(), extraRequirements, ct);
                ApplyItemStock(
                    [extraRequirements], [pendingCandidate], extraDeltas, order.OrderId, changedBy);
            }
            else
            {
                // Tăng sau khi mọi phần đã nấu xong: tạo round/ticket mới, không mở lại dòng cũ.
                var newItem = OrderItemMutations.CloneLine(
                    source, added, KitchenStatuses.Pending, now);
                _db.OrderItems.Add(newItem);
                await _db.SaveChangesAsync(ct);
                await OrderItemMutations.CopyToppingsAsync(_db, source.Id, newItem.Id, ct);

                OrderItemMutations.RecordItemHistory(
                    _db,
                    newItem,
                    null,
                    KitchenStatuses.Pending,
                    KitchenStatuses.Pending,
                    changedBy,
                    now,
                    $"Created with quantity {newItem.Quanlity} (new kitchen round)",
                    reasonCode);

                var kitchenOrder = new KitchenOrder
                {
                    OrderId = order.OrderId,
                    Status = KitchenStatuses.Pending,
                    SentAt = now,
                    Priority = 0
                };
                _db.KitchenOrders.Add(kitchenOrder);
                await _db.SaveChangesAsync(ct);

                _db.KitchenOrderItems.Add(new KitchenOrderItem
                {
                    KitchenOrderId = kitchenOrder.KitchenOrderId,
                    OrderItemId = newItem.Id,
                    Quanlity = newItem.Quanlity,
                    Status = KitchenStatuses.Pending,
                    Note = newItem.Note
                });

                var extraRequirements = await OrderItemMutations.CalculateRequirementsForQuantityAsync(
                    _db, newItem.SizeId, added, ct);
                var extraDeltas = await PrepareStockDeltasAsync(
                    new Dictionary<int, decimal>(), extraRequirements, ct);
                ApplyItemStock(
                    [extraRequirements], [newItem], extraDeltas, order.OrderId, changedBy);

                affectedTicketIds.Add(kitchenOrder.KitchenOrderId);
                createdNewRound = true;
            }
        }
        else
        {
            if (targetQuantity < preparedQuantity)
            {
                var isAdmin = _currentUser.Roles.Count == 0
                    || OrderCancellationPolicy.HasRole(_currentUser, AuthPolicies.Roles.Admin);

                if (!isAdmin)
                {
                    throw new UnauthorizedAccessException(
                        "Only an administrator can reduce below prepared quantity");
                }

                if (!request.AllowWastage)
                {
                    throw new InvalidOperationException(
                        "Cannot reduce below prepared quantity without AllowWastage");
                }
            }

            var toRemove = currentQuantity - targetQuantity;
            var remainingConsumption = await OrderCancellation.LoadOrderRemainingAsync(
                _db, order.OrderId, ct);
            var returnsStock = OrderItemMutations.ReturnsStock(reasonCode);

            // 1) Giảm/huỷ toàn bộ phần chưa nấu trước.
            foreach (var line in pendingItems.OrderByDescending(x => x.Id))
            {
                if (toRemove <= 0)
                {
                    break;
                }

                var take = Math.Min(line.Quanlity, toRemove);
                kitchenByOrderItem.TryGetValue(line.Id, out var kitchenItem);

                if (take >= line.Quanlity)
                {
                    if (returnsStock)
                    {
                        await OrderCancellation.ReturnStockForItemAsync(
                            _db, order.OrderId, line, remainingConsumption, changedBy, now, ct);
                    }
                    else
                    {
                        await OrderItemMutations.RecordWastageAsync(
                            _db, order.OrderId, line, line.Quanlity, changedBy, now,
                            reasonCode, detail, ct);
                    }

                    OrderItemMutations.RecordItemHistory(
                        _db,
                        line,
                        kitchenItem?.Id,
                        line.Status,
                        KitchenStatuses.Cancelled,
                        changedBy,
                        now,
                        detail ?? $"Cancelled whole line (quantity {line.Quanlity})",
                        reasonCode);

                    OrderCancellation.MarkItemCancelled(
                        line, now, changedBy, detail ?? reasonCode);

                    if (kitchenItem is not null)
                    {
                        kitchenItem.Status = KitchenStatuses.Cancelled;
                        affectedTicketIds.Add(kitchenItem.KitchenOrderId);
                    }
                }
                else
                {
                    if (returnsStock)
                    {
                        await OrderItemMutations.ReturnStockForPartialItemAsync(
                            _db, order.OrderId, line, take, remainingConsumption, changedBy, now, ct);
                    }
                    else
                    {
                        await OrderItemMutations.RecordWastageAsync(
                            _db, order.OrderId, line, take, changedBy, now,
                            reasonCode, detail, ct);
                    }

                    var oldQuantity = line.Quanlity;
                    line.Quanlity -= take;
                    if (kitchenItem is not null)
                    {
                        kitchenItem.Quanlity = Math.Max(0, kitchenItem.Quanlity - take);
                        affectedTicketIds.Add(kitchenItem.KitchenOrderId);
                    }

                    OrderItemMutations.RecordItemHistory(
                        _db,
                        line,
                        kitchenItem?.Id,
                        line.Status,
                        line.Status,
                        changedBy,
                        now,
                        detail is null
                            ? $"Quantity {oldQuantity} -> {line.Quanlity}"
                            : $"Quantity {oldQuantity} -> {line.Quanlity}: {detail}",
                        reasonCode);
                }

                toRemove -= take;
            }

            // 2) Nếu vẫn còn phải giảm (target < prepared) thì đây là Admin override:
            // phần đã nấu bị giảm, ghi wastage, tuyệt đối không hoàn kho.
            foreach (var line in preparedItems.OrderByDescending(x => x.Id))
            {
                if (toRemove <= 0)
                {
                    break;
                }

                var take = Math.Min(line.Quanlity, toRemove);
                kitchenByOrderItem.TryGetValue(line.Id, out var kitchenItem);

                await OrderItemMutations.RecordWastageAsync(
                    _db, order.OrderId, line, take, changedBy, now,
                    OrderItemMutations.KitchenError,
                    detail ?? "Reduced below prepared quantity",
                    ct);

                var oldQuantity = line.Quanlity;
                if (take >= line.Quanlity)
                {
                    line.Quanlity = 0;

                    OrderItemMutations.RecordItemHistory(
                        _db,
                        line,
                        kitchenItem?.Id,
                        line.Status,
                        KitchenStatuses.Cancelled,
                        changedBy,
                        now,
                        detail ?? $"Reduced below prepared quantity (quantity {oldQuantity} -> 0)",
                        OrderItemMutations.KitchenError);

                    OrderCancellation.MarkItemCancelled(
                        line, now, changedBy, detail ?? "Wastage override");
                    if (kitchenItem is not null)
                    {
                        kitchenItem.Quanlity = 0;
                        kitchenItem.Status = KitchenStatuses.Cancelled;
                        affectedTicketIds.Add(kitchenItem.KitchenOrderId);
                    }
                }
                else
                {
                    line.Quanlity -= take;
                    if (kitchenItem is not null)
                    {
                        kitchenItem.Quanlity = Math.Max(0, kitchenItem.Quanlity - take);
                        affectedTicketIds.Add(kitchenItem.KitchenOrderId);
                    }

                    OrderItemMutations.RecordItemHistory(
                        _db,
                        line,
                        kitchenItem?.Id,
                        line.Status,
                        line.Status,
                        changedBy,
                        now,
                        detail is null
                            ? $"Reduced below prepared quantity: {oldQuantity} -> {line.Quanlity}"
                            : $"Quantity {oldQuantity} -> {line.Quanlity}: {detail}",
                        OrderItemMutations.KitchenError);
                }

                toRemove -= take;
            }
        }

        foreach (var ticketId in affectedTicketIds)
        {
            var ticket = await _db.KitchenOrders
                .FirstOrDefaultAsync(x => x.KitchenOrderId == ticketId, ct);
            if (ticket is null)
            {
                continue;
            }

            var ticketItems = await _db.KitchenOrderItems
                .Where(x => x.KitchenOrderId == ticketId)
                .ToListAsync(ct);
            OrderLifecycleCoordinator.RecalculateTicketStatus(
                ticket,
                ticketItems.Select(x => x.Status).ToList(),
                now);
        }

        var allItems = await _db.OrderItems
            .Where(x => x.OrderId == order.OrderId)
            .ToListAsync(ct);

        // Huỷ dòng cuối cùng = huỷ cả đơn: giữ Subtotal/TotalAmount lịch sử, giống
        // CancelItemAsync/CancelOrderAsync. Chỉ tính lại khi còn món hoạt động.
        if (allItems.Any(x => !KitchenStatuses.IsCancelled(x.Status)))
        {
            OrderItemMutations.RecalculateTotals(order, allItems, now);
        }

        var statusChanged = await _lifecycle.RefreshOrderStatusFromAllItemsAsync(
            order,
            changedBy,
            createdNewRound
                ? "Quantity increased (new kitchen round)"
                : "Order item quantity adjusted",
            allowReopen: targetQuantity > currentQuantity,
            cancelReason: targetQuantity == 0 ? (detail ?? reasonCode) : null,
            ct);

        if (!statusChanged && createdNewRound)
        {
            _lifecycle.RecordOrderNote(order, changedBy, "Quantity increased (new kitchen round)");
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
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

            // P1: chỉ kiểm FoodSize.IsActive là chưa đủ — món đã ngừng bán/soft-delete
            // vẫn có size active, nên phải join Foods và kiểm cả IsActive/IsAvailable.
            var size = await (
                from fs in _db.FoodSizes.AsNoTracking()
                join f in _db.Foods.AsNoTracking() on fs.FoodId equals f.FoodId
                where fs.SizeId == itemRequest.SizeId
                select new
                {
                    fs.SizeId,
                    fs.FoodId,
                    fs.SizePrice,
                    SizeIsActive = fs.IsActive,
                    FoodIsActive = f.IsActive,
                    FoodIsAvailable = f.IsAvailable
                }).FirstOrDefaultAsync(ct);

            if (size is null || !size.SizeIsActive)
                throw new KeyNotFoundException($"Food size {itemRequest.SizeId} not found");

            if (!size.FoodIsActive || !size.FoodIsAvailable)
                throw new KeyNotFoundException(
                    $"Food size {itemRequest.SizeId} belongs to a food that is no longer available");

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

    /// <summary>
    /// Nhu cầu nguyên liệu của từng dòng món, giữ đúng thứ tự với <paramref name="items"/>.
    /// Dùng để ghi ledger kho theo món khi tạo đơn.
    /// </summary>
    private async Task<List<Dictionary<int, decimal>>> CalculateItemRequirementsAsync(
        IReadOnlyList<PreparedOrderItem> items,
        CancellationToken ct)
    {
        var result = new List<Dictionary<int, decimal>>(items.Count);

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

            var requirements = new Dictionary<int, decimal>();
            foreach (var ingredient in recipeIngredients)
            {
                var required = ingredient.Quanlity * item.Quanlity;
                requirements[ingredient.IngredientId] =
                    requirements.GetValueOrDefault(ingredient.IngredientId) + required;
            }

            result.Add(requirements);
        }

        return result;
    }

    private async Task<Dictionary<int, decimal>> CalculateRequirementsAsync(
        IReadOnlyList<PreparedOrderItem> items,
        CancellationToken ct) =>
        AggregateRequirements(await CalculateItemRequirementsAsync(items, ct));

    private static Dictionary<int, decimal> AggregateRequirements(
        IReadOnlyList<Dictionary<int, decimal>> itemRequirements)
    {
        var requirements = new Dictionary<int, decimal>();
        foreach (var item in itemRequirements)
        {
            foreach (var (ingredientId, quantity) in item)
            {
                requirements[ingredientId] = requirements.GetValueOrDefault(ingredientId) + quantity;
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
                CreatedAt = now,
                OrderItemGroupId = Guid.NewGuid()
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
                // Wastage zero-stock nhưng không còn là phần tiêu hao có thể hoàn.
                "Wastage" => -transaction.Quanlity,
                _ => 0m
            };

            consumption[transaction.IngredientId] =
                consumption.GetValueOrDefault(transaction.IngredientId) + delta;
        }

        if (transactions.Count > 0)
        {
            return consumption
                .Where(x => x.Value > 0)
                .ToDictionary(x => x.Key, x => x.Value);
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

    /// <summary>
    /// Trừ kho và ghi một giao dịch <c>Sale</c> cho mỗi cặp (dòng món, nguyên liệu) khi tạo đơn.
    /// Nhờ <c>OrderItemId</c>, huỷ đúng món sẽ hoàn đúng phần nguyên liệu của món đó.
    /// </summary>
    private void ApplyItemStock(
        IReadOnlyList<Dictionary<int, decimal>> itemRequirements,
        IReadOnlyList<OrderItem> orderItems,
        IReadOnlyList<StockDelta> stockDeltas,
        int orderId,
        int createdBy)
    {
        var now = DateTime.UtcNow;
        var stocks = stockDeltas.ToDictionary(x => x.IngredientId, x => x.Stock);

        for (var i = 0; i < orderItems.Count; i++)
        {
            foreach (var (ingredientId, quantity) in itemRequirements[i])
            {
                if (quantity <= 0)
                {
                    continue;
                }

                if (!stocks.TryGetValue(ingredientId, out var stock))
                {
                    throw new InvalidOperationException(
                        $"No inventory record exists for ingredient {ingredientId}");
                }

                var before = stock.Quanlity;
                stock.Quanlity -= quantity;
                stock.UpdatedAt = now;

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    IngredientId = ingredientId,
                    Type = "Sale",
                    Quanlity = quantity,
                    QuanlityBefore = before,
                    QuanlityAfter = stock.Quanlity,
                    ReferenceType = "Order",
                    ReferenceId = orderId,
                    OrderItemId = orderItems[i].Id,
                    CreatedAt = now,
                    CreatedBy = createdBy,
                    Note = "Automatically deducted when order was sent to kitchen"
                });
            }
        }
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
    /// C-10: lịch sử thay đổi trạng thái/số lượng từng dòng món của đơn.
    /// Trả <c>null</c> khi đơn không tồn tại; đơn không có lịch sử trả <c>[]</c>.
    /// </summary>
    public async Task<List<OrderItemHistoryResponse>?> GetItemHistoryAsync(
        int orderId,
        CancellationToken ct = default)
    {
        if (!await _db.Orders.AsNoTracking().AnyAsync(x => x.OrderId == orderId, ct))
            return null;

        return await (
            from h in _db.OrderItemStatusHistories.AsNoTracking()
            join u in _db.Users.AsNoTracking() on h.ChangedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            where h.OrderId == orderId
            orderby h.CreatedAt, h.Id
            select new OrderItemHistoryResponse
            {
                Id = h.Id,
                OrderId = h.OrderId,
                OrderItemId = h.OrderItemId,
                KitchenOrderItemId = h.KitchenOrderItemId,
                OldStatus = h.OldStatus,
                NewStatus = h.NewStatus,
                ChangedBy = h.ChangedBy,
                ChangedByName = u != null ? u.UserFullName : null,
                Reason = h.Reason,
                ReasonCode = h.ReasonCode,
                CreatedAt = h.CreatedAt
            }).ToListAsync(ct);
    }

    /// <summary>
    /// Huỷ một dòng món tại POS/bếp (P1a/P1b).
    ///
    /// <para>
    /// Chỉ hoàn nguyên liệu khi món còn <c>Pending</c>; món đang nấu/đã ra đĩa không hoàn.
    /// Dòng bị huỷ không còn được tính vào <c>Subtotal/TotalAmount</c>, và nếu đây là dòng cuối
    /// cùng thì đơn tự chuyển <c>Cancelled</c> và trả bàn.
    /// </para>
    /// </summary>
    public async Task<bool> CancelItemAsync(
        int orderId,
        int itemId,
        CancelOrderItemRequest request,
        CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancel reason is required");
        if (reason.Length > 500)
            throw new ArgumentException("Cancel reason must be at most 500 characters");

        var reasonCode = OrderItemMutations.NormalizeReasonCode(request.ReasonCode);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        var item = await _db.OrderItems
            .FirstOrDefaultAsync(x => x.Id == itemId && x.OrderId == orderId, ct);
        if (item is null) return false;

        if (KitchenStatuses.IsCancelled(item.Status))
            throw new InvalidOperationException("Order item is already cancelled");

        var kitchenItem = await _db.KitchenOrderItems
            .FirstOrDefaultAsync(x => x.OrderItemId == item.Id, ct);
        var kitchenOrder = kitchenItem is null
            ? null
            : await _db.KitchenOrders
                .FirstOrDefaultAsync(x => x.KitchenOrderId == kitchenItem.KitchenOrderId, ct);

        var currentStatus = kitchenItem?.Status ?? item.Status;
        if (!OrderCancellationPolicy.CanCancelItem(_currentUser, currentStatus))
            throw new UnauthorizedAccessException("You are not allowed to cancel this order item");

        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);
        var now = DateTime.UtcNow;

        // Chỉ món bếp chưa nhận mới xử lý nguyên liệu. Khách đổi món thì hoàn kho;
        // hết hàng/hỏng/lỗi bếp thì ghi wastage, không hoàn tồn kho ảo.
        if (KitchenStatuses.IsPending(currentStatus) && KitchenStatuses.IsPending(item.Status))
        {
            if (OrderItemMutations.ReturnsStock(reasonCode))
            {
                var remaining = await OrderCancellation.LoadOrderRemainingAsync(_db, order.OrderId, ct);
                await OrderCancellation.ReturnStockForItemAsync(
                    _db, order.OrderId, item, remaining, changedBy, now, ct);
            }
            else
            {
                await OrderItemMutations.RecordWastageAsync(
                    _db, order.OrderId, item, item.Quanlity,
                    changedBy, now, reasonCode, reason, ct);
            }
        }

        OrderItemMutations.RecordItemHistory(
            _db,
            item,
            kitchenItem?.Id,
            item.Status,
            KitchenStatuses.Cancelled,
            changedBy,
            now,
            reason,
            reasonCode);

        OrderCancellation.MarkItemCancelled(item, now, changedBy, reason);
        if (kitchenItem is not null)
        {
            kitchenItem.Status = KitchenStatuses.Cancelled;
        }

        var allItems = await _db.OrderItems
            .Where(x => x.OrderId == orderId)
            .ToListAsync(ct);
        var activeItems = allItems
            .Where(x => !KitchenStatuses.IsCancelled(x.Status))
            .ToList();

        if (activeItems.Count == 0)
        {
            // Dòng cuối cùng bị huỷ ⇒ huỷ đơn và trả bàn. ApplyAsync bỏ qua các món đã huỷ
            // nên không hoàn kho lần hai.
            await _lifecycle.CancelOrderAsync(order, changedBy, reason, reason, ct, reasonCode);
        }
        else
        {
            // Dòng huỷ không còn được tính tiền; bỏ discount cũ vì cơ sở khuyến mãi đã đổi.
            order.Subtotal = activeItems.Sum(x => x.UnitPrice * x.Quanlity);
            order.DiscountAmount = 0m;
            order.TotalAmount = Math.Round(
                order.Subtotal + order.TaxAmount, 2, MidpointRounding.AwayFromZero);
            order.UpdatedAt = now;

            if (kitchenOrder is not null && kitchenItem is not null)
            {
                var kitchenItems = await _db.KitchenOrderItems
                    .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
                    .ToListAsync(ct);

                // Dùng effective statuses (gồm món vừa huỷ trong bộ nhớ), không đọc lại DB
                // để tránh C-01: ticket/order bị kẹt ở Preparing.
                await _lifecycle.SyncTicketAndOrderAfterItemChangeAsync(
                    order,
                    kitchenOrder,
                    kitchenItems,
                    kitchenItem.Id,
                    KitchenStatuses.Cancelled,
                    changedBy,
                    reason,
                    reason,
                    now,
                    ct);
            }
            else
            {
                await _lifecycle.SyncOrderFromItemsAsync(
                    order,
                    allItems,
                    changedBy,
                    reason,
                    ct);
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            await _tables.RefreshStatusForTableAsync(order.TableId, ct);

        return true;
    }

    /// <summary>
    /// Cập nhật trạng thái đơn (Nhóm B2): chỉ nhận allow-list, từ chối chuỗi rỗng/không hợp lệ,
    /// và không cho đổi khỏi trạng thái cuối (<c>Paid</c>/<c>Cancelled</c>).
    ///
    /// <para>
    /// Batch A2: huỷ đơn là thao tác nhiều bước (trạng thái + phiếu bếp + tồn kho) nên chạy
    /// trong transaction <c>Serializable</c>; hai request huỷ đồng thời không thể hoàn kho hai lần.
    /// </para>
    /// </summary>
    public async Task<bool> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var status = request.Status?.Trim() ?? string.Empty;
        if (status.Length == 0)
            throw new ArgumentException("Order status is required");
        if (!OrderStatuses.IsUpdatable(status))
            throw new ArgumentException("Invalid order status");

        var isCancelling = string.Equals(status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
        var cancelReason = request.Note?.Trim();
        if (isCancelling && string.IsNullOrWhiteSpace(cancelReason))
            throw new ArgumentException("Cancel reason is required");
        if (cancelReason is { Length: > 500 })
            throw new ArgumentException("Cancel reason must be at most 500 characters");

        var reasonCode = isCancelling
            ? OrderItemMutations.NormalizeReasonCode(request.ReasonCode)
            : null;

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == id, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException($"Order is {order.Status} and can no longer change status");

        // P1d: không cho quay lui (Ready → Pending) hoặc nhảy cóc (Pending → Completed).
        if (!OrderStatuses.CanTransition(order.Status, status))
            throw new InvalidOperationException(
                $"Cannot change order status from {order.Status} to {status}");

        // C-03: POS không được đánh dấu Ready/Completed khi bếp chưa xong.
        await _lifecycle.EnsureOrderCanCloseAsync(id, status, ct);

        if (isCancelling)
        {
            var anyDone = await _db.OrderItems
                .Where(x => x.OrderId == id && x.Status != KitchenStatuses.Cancelled)
                .Select(x => x.Status)
                .ToListAsync(ct);
            if (!OrderCancellationPolicy.CanCancelOrder(
                    _currentUser,
                    anyDone.Any(KitchenStatuses.IsDone)))
            {
                throw new UnauthorizedAccessException(
                    "Only an administrator can cancel an order after a dish is ready");
            }
        }

        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);

        await _lifecycle.ApplyOrderStatusAsync(
            order,
            status,
            changedBy,
            cancelReason ?? request.Note,
            cancelReason,
            ct,
            reasonCode);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            await _tables.RefreshStatusForTableAsync(order.TableId, ct);

        return true;
    }

    /// <summary>
    /// Áp khuyến mãi cho đơn đang mở (Batch A2).
    ///
    /// <list type="bullet">
    /// <item>Không áp cho đơn <c>Paid</c>/<c>Cancelled</c>.</item>
    /// <item><c>DiscountType</c> chỉ nhận <c>Percent</c> hoặc <c>Fixed</c>; giá trị khác trả <c>false</c>
    /// (dữ liệu cũ có thể sai vì trước đây không validate).</item>
    /// <item>Nếu promotion có <c>PromotionFood</c>/<c>PromotionCategory</c>, giảm giá chỉ tính trên
    /// phần tiền của các dòng món thuộc phạm vi; không có targeting thì áp toàn đơn như cũ.</item>
    /// </list>
    ///
    /// <para>
    /// Giới hạn đã biết: schema <c>orders</c> không có cột lưu promotion id (Batch A2 không đổi schema),
    /// nên không thể lưu snapshot promotion trên đơn ở bước này.
    /// </para>
    /// </summary>
    public async Task<bool> ApplyPromotionAsync(int orderId, int promotionId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null || OrderStatuses.IsTerminal(order.Status)) return false;

        var now = DateTime.UtcNow;
        var promotion = await _db.Promotions.FirstOrDefaultAsync(
            x => x.Id == promotionId && x.IsActive && x.StartAt <= now && x.EndAt >= now,
            ct);
        if (promotion is null || order.Subtotal < promotion.MinimumOrderAmount) return false;

        var discountType = promotion.DiscountType?.Trim() ?? string.Empty;
        var isPercent = discountType.Equals("Percent", StringComparison.OrdinalIgnoreCase);
        var isFixed = discountType.Equals("Fixed", StringComparison.OrdinalIgnoreCase);
        if (!isPercent && !isFixed) return false;

        var discountBase = await ResolveDiscountBaseAsync(order, promotionId, ct);
        if (discountBase <= 0) return false;

        var discount = isFixed
            ? Math.Min(promotion.DiscountValue, discountBase)
            : discountBase * promotion.DiscountValue / 100m;

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
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Phần tiền được hưởng khuyến mãi: toàn bộ <c>Subtotal</c> khi promotion không targeting,
    /// ngược lại chỉ các dòng món thuộc <c>PromotionFood.SizeId</c> hoặc
    /// <c>PromotionCategory.CategoryId</c> của món.
    /// </summary>
    private async Task<decimal> ResolveDiscountBaseAsync(
        Order order,
        int promotionId,
        CancellationToken ct)
    {
        var targetSizeIds = await _db.PromotionFoods
            .AsNoTracking()
            .Where(x => x.PromotionId == promotionId)
            .Select(x => x.SizeId)
            .ToListAsync(ct);
        var targetCategoryIds = await _db.PromotionCategories
            .AsNoTracking()
            .Where(x => x.PromotionId == promotionId)
            .Select(x => x.CategoryId)
            .ToListAsync(ct);

        if (targetSizeIds.Count == 0 && targetCategoryIds.Count == 0)
        {
            return order.Subtotal;
        }

        var sizeIds = targetSizeIds.ToHashSet();
        var categoryIds = targetCategoryIds.ToHashSet();

        var lines = await (
            from orderItem in _db.OrderItems.AsNoTracking()
            join size in _db.FoodSizes.AsNoTracking() on orderItem.SizeId equals size.SizeId
            join food in _db.Foods.AsNoTracking() on size.FoodId equals food.FoodId
            where orderItem.OrderId == order.OrderId
               && orderItem.Status != KitchenStatuses.Cancelled
            select new
            {
                orderItem.SizeId,
                food.CategoryId,
                LineTotal = orderItem.UnitPrice * orderItem.Quanlity
            }).ToListAsync(ct);

        return lines
            .Where(x => sizeIds.Contains(x.SizeId) || categoryIds.Contains(x.CategoryId))
            .Sum(x => x.LineTotal);
    }
}
