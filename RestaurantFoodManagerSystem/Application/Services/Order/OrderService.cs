using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed partial class OrderService
{
    private readonly AppDbContext _db;
    private readonly RestaurantTableService _tables;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OrderService> _logger;
    private readonly IOrderIdempotencyStore _idempotency;
    private readonly OrderLifecycleCoordinator _lifecycle;
    private readonly RestaurantSettingsService _settings;

    public OrderService(
        AppDbContext db,
        RestaurantTableService tables,
        ICurrentUser currentUser,
        ILogger<OrderService> logger,
        IOrderIdempotencyStore idempotency,
        OrderLifecycleCoordinator lifecycle,
        RestaurantSettingsService settings)
    {
        _db = db;
        _tables = tables;
        _currentUser = currentUser;
        _logger = logger;
        _idempotency = idempotency;
        _lifecycle = lifecycle;
        _settings = settings;
    }

    /// <summary>
    /// Ghi chú cho cả hoá đơn — <c>PATCH /api/orders/{id}/note</c> (Đợt 3 / B3).
    ///
    /// <para>
    /// Khác với <c>OrderItem.Note</c> (ghi chú cho từng món): đây là ghi chú cho **hoá đơn**
    /// — "xuất hoá đơn công ty ABC", "chia đôi với bàn 6", "khách đợi bạn"…
    /// </para>
    ///
    /// <para>Gửi chuỗi rỗng để **xoá** ghi chú. Mọi thay đổi đều được ghi 1 dòng lịch sử đơn.</para>
    /// </summary>
    public async Task<bool> UpdateNoteAsync(
        int orderId,
        UpdateOrderNoteRequest request,
        CancellationToken ct = default)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null)
        {
            return false;
        }

        var trimmed = request.Note?.Trim();
        var newNote = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;

        if (string.Equals(order.Note, newNote, StringComparison.Ordinal))
        {
            return true;
        }

        var actor = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct,
            fallbackUserId: order.CreatedBy);

        order.Note = newNote;
        order.UpdatedAt = DateTime.UtcNow;

        // Ghi dòng audit: ghi chú hoá đơn là thay đổi cần truy vết (thu ngân ghi để in hoá đơn).
        _lifecycle.RecordOrderNote(
            order,
            actor,
            newNote is null ? "Cleared bill note" : $"Updated bill note: {newNote}");

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<Order> CreateAsync(CreateOrderRequest request, CancellationToken ct) =>
        CreateCoreAsync(request, ct);

    /// <summary>
    /// Tạo đơn có hỗ trợ header <c>Idempotency-Key</c>. Nếu key rỗng, giữ nguyên hành vi cũ.
    /// Cùng key + cùng payload trả lại đúng response cũ; cùng key + payload khác trả 409.
    /// </summary>
    public Task<OrderCreatedResponse> CreateIdempotentAsync(
        CreateOrderRequest request,
        string? idempotencyKey,
        CancellationToken ct) =>
        _idempotency.RunIfKeyedAsync(
            idempotencyKey,
            _currentUser.UserId ?? request.CreatedBy,
            "orders",
            string.Empty,
            request,
            async token => OrderCreatedResponse.From(await CreateCoreAsync(request, token)),
            ct);

    private async Task<Order> CreateCoreAsync(CreateOrderRequest request, CancellationToken ct)
    {
        // Đợt 2 / A1: cấu hình quán (VAT + phí dịch vụ) đọc TRƯỚC khi mở transaction để không
        // giữ khoá đọc trên bảng settings suốt thời gian xử lý đơn.
        var settings = await _settings.GetAsync(ct);
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
        var stockDeltas = await PrepareStockDeltasAsync(requirements, ct);

        var createdBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.CreatedBy, _logger, nameof(request.CreatedBy), ct);
        var order = new Order
        {
            TableId = request.TableId,
            CreatedBy = createdBy,
            Status = OrderStatuses.Pending,
            // Đợt 3 / B3: ghi chú cho cả hoá đơn (tuỳ chọn khi tạo đơn).
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var orderItems = await AddOrderItemsAsync(order.OrderId, preparedItems, createdBy, ct);
        order.Subtotal = orderItems.Sum(x => x.UnitPrice * x.Quanlity);
        order.DiscountAmount = 0m;
        // Đợt 2 / A1: trước đây chỗ này gán `Total = Subtotal` (thiếu VAT lẫn phí dịch vụ)
        // ⇒ đơn sai ngay từ lúc tạo cho tới lần sửa món đầu tiên.
        OrderTotalsCalculator.Apply(order, settings);
        order.Status = OrderStatuses.SentToKitchen;
        await _db.SaveChangesAsync(ct);

        // P0-03: lịch sử đơn phải có điểm bắt đầu (Pending → SentToKitchen), không bắt đầu
        // giữa chừng.
        _lifecycle.RecordOrderCreated(order, createdBy, "Order created");

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
    /// Gọi thêm món vào đơn đang mở. Luôn tạo một round/ticket bếp mới; không replace-all
    /// và không đụng các dòng/ticket cũ. Order <c>Completed</c> được mở lại thành
    /// <c>SentToKitchen</c> nhưng bàn vẫn <c>Occupied</c>.
    /// </summary>
    public async Task<bool> AddItemsAsync(
        int orderId,
        AddOrderItemsRequest request,
        CancellationToken ct)
    {
        // Đợt 2 / A1: cấu hình quán đọc trước transaction.
        var settings = await _settings.GetAsync(ct);
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
        var stockDeltas = await PrepareStockDeltasAsync(requirements, ct);

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

        var orderItems = await AddOrderItemsAsync(order.OrderId, preparedItems, changedBy, ct);
        AddKitchenItems(kitchenOrder, orderItems);
        ApplyItemStock(itemRequirements, orderItems, stockDeltas, order.OrderId, changedBy);

        var allItems = await _db.OrderItems
            .Where(x => x.OrderId == order.OrderId)
            .ToListAsync(ct);
        await OrderItemMutations.RecalculateTotalsAsync(_db, order, allItems, now, settings, ct);

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
    /// Gọi thêm món, hỗ trợ header <c>Idempotency-Key</c> (Đợt 2 / A5).
    ///
    /// <para>
    /// Bấm "gọi thêm" 2 lần (double-click, retry mạng) chỉ tạo **một** round bếp và trừ kho
    /// **một** lần. Không có header thì hành vi cũ (không chống trùng).
    /// </para>
    /// </summary>
    public Task<OrderDetailResponse?> AddItemsIdempotentAsync(
        int orderId,
        AddOrderItemsRequest request,
        string? idempotencyKey,
        CancellationToken ct) =>
        _idempotency.RunIfKeyedAsync(
            idempotencyKey,
            _currentUser.UserId ?? request.ChangedBy,
            $"orders/{orderId}/items",
            $"orders:{orderId}:items:",
            request,
            async token => await AddItemsAsync(orderId, request, token) ? await GetAsync(orderId, token) : null,
            ct);

    /// <summary>
    /// Đổi số lượng món, hỗ trợ header <c>Idempotency-Key</c> (Đợt 2 / A5).
    /// </summary>
    public Task<OrderDetailResponse?> AdjustItemQuantityIdempotentAsync(
        int orderId,
        int itemId,
        AdjustOrderItemQuantityRequest request,
        string? idempotencyKey,
        CancellationToken ct) =>
        _idempotency.RunIfKeyedAsync(
            idempotencyKey,
            _currentUser.UserId ?? request.ChangedBy,
            $"orders/{orderId}/items/{itemId}/quantity",
            $"orders:{orderId}:items:{itemId}:quantity:",
            request,
            async token => await AdjustItemQuantityAsync(orderId, itemId, request, token) ? await GetAsync(orderId, token) : null,
            ct);

    public async Task<bool> AdjustItemQuantityAsync(
        int orderId,
        int itemId,
        AdjustOrderItemQuantityRequest request,
        CancellationToken ct)
    {
        // Đợt 2 / A1: cấu hình quán đọc trước transaction.
        var settings = await _settings.GetAsync(ct);
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

        // Gán group id để gom các dòng tách từ cùng 1 món. Nhớ lại trạng thái cũ để
        // hoàn tác nếu cuối cùng hoá đơn không có gì thay đổi (xem nhánh return sớm bên dưới).
        var hadGroupId = source.OrderItemGroupId is not null;
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

        // C-13: mốc "Preparing" là mốc KHÓA với POS — bếp đã bắt đầu làm thì nguyên liệu đã
        // tiêu thật, món đó không còn hoàn kho được, chỉ ghi hao hụt (wastage).
        // Tách 3 nhóm để luồng giảm xử lý đúng theo mốc trạng thái:
        //   notStarted (Pending)  → còn hoàn kho được
        //   inProgress (Preparing) → đã tiêu thật ⇒ wastage
        //   prepared (Ready/Completed) → đã ra đĩa ⇒ wastage (chỉ Admin được phép)
        var preparedItems = activeItems
            .Where(x => KitchenStatuses.IsDone(EffectiveStatus(x)))
            .ToList();
        var preparedQuantity = preparedItems.Sum(x => x.Quanlity);
        var notStartedItems = activeItems
            .Where(x => KitchenStatuses.IsPending(EffectiveStatus(x)))
            .ToList();
        // Mọi thứ chưa "Pending" và chưa "xong" (kể cả trạng thái lạ) ⇒ coi như đã bắt đầu
        // ⇒ không hoàn kho. An toàn hơn là nhả tồn kho cho món đang nấu.
        var inProgressItems = activeItems
            .Where(x => !KitchenStatuses.IsDone(EffectiveStatus(x))
                     && !KitchenStatuses.IsPending(EffectiveStatus(x)))
            .ToList();
        var pendingItems = notStartedItems.Concat(inProgressItems).ToList();

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
        {
            // Không có gì thay đổi ⇒ hoàn tác group id vừa gán (nếu trước đó chưa có),
            // để không lọt ra ngoài khi transaction bị rollback hoặc khi context dùng lại.
            if (!hadGroupId)
            {
                source.OrderItemGroupId = null;
            }

            return true;
        }

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
        string? quantityNote = null;

        if (targetQuantity > currentQuantity)
        {
            var added = targetQuantity - currentQuantity;

            // C-20: nạp sẵn phiếu bếp của nhóm dòng (tránh truy vấn lặp trong vòng lặp).
            var ticketIds = groupKitchenItems.Select(x => x.KitchenOrderId).Distinct().ToList();
            var ticketsById = await _db.KitchenOrders
                .Where(t => ticketIds.Contains(t.KitchenOrderId))
                .ToDictionaryAsync(t => t.KitchenOrderId, ct);

            // Phiếu vừa tạo trong chính lần này (chưa có trong DB) cũng phải dùng được.
            foreach (var local in _db.KitchenOrders.Local)
            {
                ticketsById[local.KitchenOrderId] = local;
            }

            // C-20: chỉ GỘP vào dòng còn `Pending` (bếp chưa bắt đầu nấu).
            // Món đã vào nấu / đã ra đĩa ⇒ TÁCH thành dòng `Pending` mới nằm dưới (nhánh else).
            var pendingCandidate = pendingItems
                .Where(x =>
                {
                    if (!kitchenByOrderItem.TryGetValue(x.Id, out var kitchenItem))
                        return false;

                    if (!KitchenStatuses.IsPending(kitchenItem.Status))
                        return false;

                    var ticket = ticketsById.GetValueOrDefault(kitchenItem.KitchenOrderId);
                    return ticket is not null
                        && !KitchenStatuses.IsTerminal(ticket.Status)
                        && !string.Equals(ticket.Status, KitchenStatuses.Ready, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

            if (pendingCandidate is not null)
            {
                // Món vẫn chưa bếp bắt đầu ⇒ gộp chung vào dòng Pending, không tạo dòng mới.
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
                var extraDeltas = await PrepareStockDeltasAsync(extraRequirements, ct);
                ApplyItemStock(
                    [extraRequirements], [pendingCandidate], extraDeltas, order.OrderId, changedBy);
            }
            else
            {
                // C-20: món đã vào nấu / đã ra đĩa ⇒ TÁCH thành dòng `Pending` MỚI nằm dưới,
                // KHÔNG cộng vào dòng đang nấu (giống cách bếp thật xử lý).
                //   - Món còn nằm trong một phiếu CHƯA ĐÓNG ⇒ dòng mới nằm trong chính phiếu đó,
                //     bếp thấy "Gà rán 2 (đang nấu)" rồi "Gà rán 3 (chờ nấu)" ngay dưới.
                //   - Phiếu đã Ready / đã đóng ⇒ tạo phiếu mới (giống "gọi thêm").
                //
                // Tách dòng (thay vì gộp) còn giúp phần mới vẫn giữ trạng thái `Pending`:
                // sau này huỷ/giam ⇒ phần mới còn hoàn kho được, phần đang nấu mới thành wastage.
                var reuseTicket = groupKitchenItems
                    .OrderByDescending(x => x.Id)
                    .Select(x => ticketsById.GetValueOrDefault(x.KitchenOrderId))
                    .FirstOrDefault(t => t is not null
                                          && !KitchenStatuses.IsTerminal(t.Status)
                                          && !string.Equals(t.Status, KitchenStatuses.Ready, StringComparison.OrdinalIgnoreCase));

                var newItem = OrderItemMutations.CloneLine(
                    source, added, KitchenStatuses.Pending, now);
                _db.OrderItems.Add(newItem);
                await _db.SaveChangesAsync(ct);
                await OrderItemMutations.CopyToppingsAsync(_db, source.Id, newItem.Id, ct);

                var targetTicket = reuseTicket;
                if (targetTicket is null)
                {
                    targetTicket = new KitchenOrder
                    {
                        OrderId = order.OrderId,
                        Status = KitchenStatuses.Pending,
                        SentAt = now,
                        Priority = 0
                    };
                    _db.KitchenOrders.Add(targetTicket);
                    await _db.SaveChangesAsync(ct);
                    quantityNote = "Quantity increased (new kitchen round)";
                }
                else
                {
                    quantityNote = "Quantity increased (new pending line, kitchen already started this dish)";
                }

                OrderItemMutations.RecordItemHistory(
                    _db,
                    newItem,
                    null,
                    KitchenStatuses.Pending,
                    KitchenStatuses.Pending,
                    changedBy,
                    now,
                    $"Created with quantity {newItem.Quanlity}"
                        + (reuseTicket is null ? " (new kitchen round)" : " (new pending line)"),
                    reasonCode);

                _db.KitchenOrderItems.Add(new KitchenOrderItem
                {
                    KitchenOrderId = targetTicket.KitchenOrderId,
                    OrderItemId = newItem.Id,
                    Quanlity = newItem.Quanlity,
                    Status = KitchenStatuses.Pending,
                    Note = newItem.Note
                });

                // C-23: lưu dòng bếp NGAY trước khi tính lại trạng thái phiếu.
                // Vòng tính lại đọc món của phiếu bằng truy vấn DB ⇒ món ở trạng thái
                // "Added" sẽ không xuất hiện ⇒ phiếu có thể bị chuyển "Ready"/đóng trong khi
                // vẫn còn món `Pending` mà bếp chưa nấu.
                await _db.SaveChangesAsync(ct);

                var extraRequirements = await OrderItemMutations.CalculateRequirementsForQuantityAsync(
                    _db, newItem.SizeId, added, ct);
                var extraDeltas = await PrepareStockDeltasAsync(extraRequirements, ct);
                ApplyItemStock(
                    [extraRequirements], [newItem], extraDeltas, order.OrderId, changedBy);

                affectedTicketIds.Add(targetTicket.KitchenOrderId);
            }
        }
        else
        {
            // C-13: món đang nấu đã bị "khóa" với POS — chỉ bếp (Chef) hoặc Admin mới được
            // đổi số lượng. Bếp vẫn dùng được (C-13 §2.4).
            if (inProgressItems.Count > 0 && !OrderCancellationPolicy.IsKitchenOrAdmin(_currentUser))
            {
                throw new UnauthorizedAccessException(
                    "Only the kitchen can change the quantity of a dish that is already being prepared");
            }

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

            // C-13: giảm theo mốc trạng thái.
            //   allowReturn = true  → món chưa bắt đầu (Pending): còn hoàn kho được nếu lý do là
            //                          khách đổi (CustomerChange).
            //   allowReturn = false → món đã bắt đầu (Preparing) hoặc đã ra đĩa: nguyên liệu
            //                          đã tiêu thật ⇒ ghi hao hụt, không hoàn kho.
            async Task ReduceAsync(List<OrderItem> lines, bool allowReturn, bool zeroOnFullCancel = false)
            {
                var effReason = allowReturn ? reasonCode : OrderItemMutations.KitchenError;
                var wastageDetail = zeroOnFullCancel ? (detail ?? "Reduced below prepared quantity") : detail;

                foreach (var line in lines.OrderByDescending(x => x.Id))
                {
                    if (toRemove <= 0)
                    {
                        break;
                    }

                    var take = Math.Min(line.Quanlity, toRemove);
                    kitchenByOrderItem.TryGetValue(line.Id, out var kitchenItem);
                    var oldQuantity = line.Quanlity;

                    if (take >= line.Quanlity)
                    {
                        if (allowReturn && returnsStock)
                        {
                            await OrderCancellation.ReturnStockForItemAsync(
                                _db, order.OrderId, line, remainingConsumption, changedBy, now, ct);
                        }
                        else
                        {
                            await OrderItemMutations.RecordWastageAsync(
                                _db, order.OrderId, line, line.Quanlity, changedBy, now,
                                effReason, wastageDetail, ct);
                        }

                        if (zeroOnFullCancel)
                        {
                            line.Quanlity = 0;
                        }

                        OrderItemMutations.RecordItemHistory(
                            _db,
                            line,
                            kitchenItem?.Id,
                            line.Status,
                            KitchenStatuses.Cancelled,
                            changedBy,
                            now,
                            detail ?? (zeroOnFullCancel
                                ? $"Reduced below prepared quantity (quantity {oldQuantity} -> 0)"
                                : $"Cancelled whole line (quantity {oldQuantity})"),
                            effReason);

                        OrderCancellation.MarkItemCancelled(
                            line, now, changedBy, detail ?? (zeroOnFullCancel ? "Wastage override" : reasonCode));

                        if (kitchenItem is not null)
                        {
                            if (zeroOnFullCancel)
                            {
                                kitchenItem.Quanlity = 0;
                            }
                            kitchenItem.Status = KitchenStatuses.Cancelled;
                            affectedTicketIds.Add(kitchenItem.KitchenOrderId);
                        }
                    }
                    else
                    {
                        if (allowReturn && returnsStock)
                        {
                            await OrderItemMutations.ReturnStockForPartialItemAsync(
                                _db, order.OrderId, line, take, remainingConsumption, changedBy, now, ct);
                        }
                        else
                        {
                            await OrderItemMutations.RecordWastageAsync(
                                _db, order.OrderId, line, take, changedBy, now,
                                effReason, wastageDetail, ct);
                        }

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
                                ? (zeroOnFullCancel
                                    ? $"Reduced below prepared quantity: {oldQuantity} -> {line.Quanlity}"
                                    : $"Quantity {oldQuantity} -> {line.Quanlity}")
                                : $"Quantity {oldQuantity} -> {line.Quanlity}: {detail}",
                            effReason);
                    }

                    toRemove -= take;
                }
            }

            // 1) Giảm phần CHƯA BẮT ĐẦU trước (còn hoàn kho được).
            await ReduceAsync(notStartedItems, allowReturn: true);

            // 2) Giảm phần ĐANG NẤU: nguyên liệu đã tiêu thật ⇒ chỉ ghi hao hụt.
            await ReduceAsync(inProgressItems, allowReturn: false);

            // 3) Admin override giảm xuống dưới số đã nấu: ghi wastage, không hoàn kho.
            await ReduceAsync(preparedItems, allowReturn: false, zeroOnFullCancel: true);
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
            await OrderItemMutations.RecalculateTotalsAsync(_db, order, allItems, now, settings, ct);
        }

        var statusChanged = await _lifecycle.RefreshOrderStatusFromAllItemsAsync(
            order,
            changedBy,
            quantityNote ?? "Order item quantity adjusted",
            allowReopen: targetQuantity > currentQuantity,
            cancelReason: targetQuantity == 0 ? (detail ?? reasonCode) : null,
            ct,
            reasonCode: reasonCode);

        if (!statusChanged && quantityNote is not null)
        {
            _lifecycle.RecordOrderNote(order, changedBy, quantityNote);
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
        foreach (var itemRequest in itemRequests)
        {
            if (itemRequest.Toppings is null)
                throw new ArgumentException("Order item toppings cannot be null");
        }

        var sizeIds = itemRequests.Select(x => x.SizeId).Distinct().ToList();
        // P1: chỉ kiểm FoodSize.IsActive là chưa đủ — món đã ngừng bán/soft-delete
        // vẫn có size active, nên phải join Foods và kiểm cả IsActive/IsAvailable.
        var sizesById = await (
            from fs in _db.FoodSizes.AsNoTracking()
            join f in _db.Foods.AsNoTracking() on fs.FoodId equals f.FoodId
            where sizeIds.Contains(fs.SizeId)
            select new
            {
                fs.SizeId,
                fs.FoodId,
                fs.SizePrice,
                SizeIsActive = fs.IsActive,
                FoodIsActive = f.IsActive,
                FoodIsAvailable = f.IsAvailable
            }).ToDictionaryAsync(x => x.SizeId, ct);

        var toppingIds = itemRequests
            .SelectMany(x => x.Toppings)
            .Select(x => x.ToppingId)
            .Distinct()
            .ToList();
        var foodIds = sizesById.Values.Select(x => x.FoodId).Distinct().ToList();

        // Defect #19: topping phải được gán cho đúng món qua FoodToppings.
        var toppingsByFoodAndId = toppingIds.Count == 0 || foodIds.Count == 0
            ? new Dictionary<(int FoodId, int ToppingId), decimal>()
            : (await (
                from t in _db.Toppings.AsNoTracking()
                join ft in _db.FoodToppings.AsNoTracking() on t.ToppingId equals ft.ToppingId
                where toppingIds.Contains(t.ToppingId)
                   && t.IsActive
                   && t.IsAvailable
                   && foodIds.Contains(ft.FoodId)
                select new { ft.FoodId, t.ToppingId, t.ToppingPrice }).ToListAsync(ct))
                .GroupBy(x => (x.FoodId, x.ToppingId))
                .ToDictionary(g => g.Key, g => g.First().ToppingPrice);

        var prepared = new List<PreparedOrderItem>(itemRequests.Count);

        foreach (var itemRequest in itemRequests)
        {
            if (!sizesById.TryGetValue(itemRequest.SizeId, out var size) || !size.SizeIsActive)
                throw new KeyNotFoundException($"Food size {itemRequest.SizeId} not found");

            if (!size.FoodIsActive || !size.FoodIsAvailable)
                throw new KeyNotFoundException(
                    $"Food size {itemRequest.SizeId} belongs to a food that is no longer available");

            var unitPrice = size.SizePrice;
            var toppings = new List<PreparedTopping>(itemRequest.Toppings.Count);

            foreach (var toppingRequest in itemRequest.Toppings)
            {
                if (!toppingsByFoodAndId.TryGetValue((size.FoodId, toppingRequest.ToppingId), out var toppingPrice))
                    throw new KeyNotFoundException($"Topping {toppingRequest.ToppingId} is not available for this food");

                unitPrice += toppingPrice * toppingRequest.Quanlity;
                toppings.Add(new PreparedTopping(
                    toppingRequest.ToppingId,
                    toppingRequest.Quanlity,
                    toppingPrice));
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
        var sizeIds = items.Select(x => x.SizeId).Distinct().ToList();
        var recipeRows = sizeIds.Count == 0
            ? []
            : await (
                from recipe in _db.Recipes.AsNoTracking()
                join ri in _db.RecipeIngredients.AsNoTracking()
                    on recipe.RecipeId equals ri.RecipeId
                where recipe.IsActive && sizeIds.Contains(recipe.SizeId)
                select new { recipe.SizeId, ri.IngredientId, ri.Quanlity })
                .ToListAsync(ct);

        var ingredientsBySize = recipeRows
            .GroupBy(x => x.SizeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<Dictionary<int, decimal>>(items.Count);

        foreach (var item in items)
        {
            if (!ingredientsBySize.TryGetValue(item.SizeId, out var rows) || rows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Food size {item.SizeId} does not have an active recipe");
            }

            var requirements = new Dictionary<int, decimal>();
            foreach (var row in rows)
            {
                requirements[row.IngredientId] =
                    requirements.GetValueOrDefault(row.IngredientId)
                    + row.Quanlity * item.Quanlity;
            }

            result.Add(requirements);
        }

        return result;
    }

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
        IReadOnlyDictionary<int, decimal> newRequirements,
        CancellationToken ct)
    {
        var ingredientIds = newRequirements.Keys.ToList();

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
            var delta = newRequirements.GetValueOrDefault(ingredientId);
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

            deltas.Add(new StockDelta(stock, ingredientId, delta, ingredient?.Unit));
        }

        return deltas;
    }

    private async Task<List<OrderItem>> AddOrderItemsAsync(
        int orderId,
        IReadOnlyList<PreparedOrderItem> preparedItems,
        int changedBy,
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

            // P0-04: dòng món vừa tạo cũng phải có dòng audit, nếu không lịch sử món chỉ
            // bắt đầu từ lần sửa đầu tiên (không biết món vào bếp lúc nào).
            OrderItemMutations.RecordItemHistory(
                _db,
                orderItem,
                kitchenOrderItemId: null,
                oldStatus: string.Empty,
                KitchenStatuses.Pending,
                changedBy,
                now,
                "Dish added to order");

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
        var units = stockDeltas.ToDictionary(x => x.IngredientId, x => x.Unit);

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
                    // C-17: ledger ghi kèm đơn vị tính lúc tiêu hao.
                    Unit = units.GetValueOrDefault(ingredientId),
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
        decimal Delta,
        string? Unit);

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
        // Đợt 2 / A1: cấu hình quán đọc trước transaction.
        var settings = await _settings.GetAsync(ct);
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

        // Lấy kitchen item MỚI NHẤT. Sau khi tách dòng (prepared-quantity/shortage) một
        // OrderItem có nhiều KitchenOrderItem; FirstOrDefault không ORDER BY sẽ huỷ nhầm
        // phiếu bếp. (P0-07)
        var kitchenItem = await OrderItemMutations.FindKitchenItemAsync(_db, item.Id, ct);
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

        await OrderCancellation.CancelSingleItemAsync(
            _db,
            order.OrderId,
            item,
            kitchenItem,
            reasonCode,
            reason,
            changedBy,
            now,
            ct);

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
            // Dùng chung hàm RecalculateTotalsAsync với các luồng gọi thêm / điều chỉnh SL
            // ⇒ công thức tiền và khuyến mãi chỉ có 1 nơi định nghĩa.
            await OrderItemMutations.RecalculateTotalsAsync(_db, order, allItems, now, settings, ct);

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
                    ct,
                    reasonCode);
            }
            else
            {
                await _lifecycle.SyncOrderFromItemsAsync(
                    order,
                    allItems,
                    changedBy,
                    reason,
                    ct,
                    reasonCode);
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
    /// <para>
    /// Đợt 2 / A3: đơn lưu FK <c>PromotionId</c> + snapshot <c>PromotionDiscountType</c>/
    /// <c>PromotionDiscountValue</c> ⇒ tra lại được "đơn này dùng khuyến mãi nào", và
    /// sửa/xoá khuyến mãi sau này không làm đổi nghĩa của đơn cũ.
    /// </para>
    /// </para>
    /// </summary>
    public async Task<bool> ApplyPromotionAsync(int orderId, int promotionId, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return false;

        // C-23: đơn đã Paid/Cancelled là XUNG ĐỘT NGHIỆP VỤ (409) — giống hệt
        // RemovePromotionAsync, để FE không phải đoán 400 hay 409 cho cùng một điều kiện.
        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        var now = DateTime.UtcNow;
        var promotion = await _db.Promotions.FirstOrDefaultAsync(
            x => x.Id == promotionId && x.IsActive && x.StartAt <= now && x.EndAt >= now,
            ct);
        if (promotion is null || order.Subtotal < promotion.MinimumOrderAmount) return false;

        var activeItems = await _db.OrderItems
            .AsNoTracking()
            .Where(x => x.OrderId == order.OrderId && x.Status != KitchenStatuses.Cancelled)
            .ToListAsync(ct);

        var roundedDiscount = await OrderItemMutations.ComputePromotionDiscountAsync(
            _db,
            promotion,
            activeItems,
            order.Subtotal,
            ct);
        if (roundedDiscount is null) return false;

        order.DiscountAmount = roundedDiscount.Value;
        // Đợt 2 / A3: lưu FK + snapshot khuyến mãi. Nhờ vậy tra lại được "đơn này dùng
        // khuyến mãi nào", và sửa/xoá khuyến mãi sau này không làm đổi nghĩa của đơn cũ.
        order.PromotionId = promotion.Id;
        order.PromotionDiscountType = promotion.DiscountType;
        order.PromotionDiscountValue = promotion.DiscountValue;
        OrderTotalsCalculator.Apply(order, settings);
        order.UpdatedAt = DateTime.UtcNow;

        // Đổi tiền là thay đổi quan trọng ⇒ ghi dòng audit (không đổi trạng thái đơn).
        _lifecycle.RecordOrderNote(
            order,
            _currentUser.UserId ?? order.CreatedBy,
            $"Applied promotion {promotionId}");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Chuyển đơn sang bàn khác (B1) — dùng khi khách đổi chỗ ngồi.
    ///
    /// <para>Luật đã chốt 2026-10-21 (chủ dự án):</para>
    /// <list type="bullet">
    /// <item><description>Bàn đích phải <b>đang trống</b> — nếu bàn đó đang có đơn mở thì chặn
    /// (ngoài bàn đó đang có người, không thể cho vào).</description></item>
    /// <item><description>Bàn đích phải còn hoạt động và không ở trạng thái bảo trì.</description></item>
    /// <item><description>Đơn phải chưa <c>Paid</c>/<c>Cancelled</c>.</description></item>
    /// </list>
    ///
    /// <para>Bàn cũ tự động trở lại <c>Available</c>, bàn mới thành <c>Occupied</c> (trạng thái bàn
    /// được tính tự động từ đơn mở).</para>
    /// </summary>
    public async Task<bool> ChangeTableAsync(
        int orderId,
        ChangeOrderTableRequest request,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        if (order.TableId == request.TableId)
            throw new ArgumentException("The order is already on this table");

        var target = await _db.RestaurantTables
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TableId == request.TableId, ct);

        // C-15: bàn đích không tồn tại / bảo trì ⇒ 400 (lỗi đầu vào).
        // Chỉ "bàn đích đang có đơn mở" và "đơn đã Paid/Cancelled" mới là 409 (xung đột nghiệp vụ).
        if (target is null || !target.IsActive)
            throw new ArgumentException("Table not found");

        if (string.Equals(target.Status, RestaurantTableStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Target table is under maintenance");

        // Bàn đích phải trống: nếu đang có đơn mở thì ngoài bàn đó đang có người.
        var targetHasOpenOrder = await _db.Orders.AnyAsync(
            o => o.TableId == request.TableId
              && o.OrderId != order.OrderId
              && o.Status != OrderStatuses.Paid
              && o.Status != OrderStatuses.Cancelled,
            ct);
        if (targetHasOpenOrder)
            throw new InvalidOperationException("Target table already has an open order");

        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);

        var previousTableId = order.TableId;
        order.TableId = request.TableId;
        order.UpdatedAt = DateTime.UtcNow;

        // Ghi 1 dòng audit: chuyển bàn không đổi trạng thái đơn (dùng lại đúng cơ chế
        // RecordOrderNote mà "gọi thêm tạo round mới" đang dùng).
        _lifecycle.RecordOrderNote(
            order,
            changedBy,
            $"Moved from table {previousTableId} to table {request.TableId}");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _tables.RefreshStatusForTableAsync(previousTableId, ct);
        await _tables.RefreshStatusForTableAsync(request.TableId, ct);
        return true;
    }

    /// <summary>
    /// Gỡ khuyến mãi khỏi đơn (Đợt 2 / A2).
    ///
    /// <para>
    /// Trước Đợt 2 chỉ có chiều "áp" (<c>PATCH /api/orders/{id}/promotion/{promotionId}</c>).
    /// Gỡ được chỉ gián tiếp khi sửa món, vì mọi luồng sửa đơn đều reset
    /// <c>DiscountAmount = 0</c> ⇒ không có đường gỡ tường minh.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> nếu đã gỡ; <c>false</c> nếu đơn không tồn tại hoặc đang áp khuyến mãi khác.
    /// </returns>
    public async Task<bool> RemovePromotionAsync(int orderId, int promotionId, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        // Chỉ gỡ đúng khuyến mãi đang áp dụng cho đơn này.
        if (order.PromotionId != promotionId) return false;

        order.PromotionId = null;
        order.PromotionDiscountType = null;
        order.PromotionDiscountValue = null;
        order.DiscountAmount = 0m;
        OrderTotalsCalculator.Apply(order, settings);
        order.UpdatedAt = DateTime.UtcNow;

        // Đổi tiền là thay đổi quan trọng ⇒ ghi dòng audit (không đổi trạng thái đơn).
        _lifecycle.RecordOrderNote(
            order,
            _currentUser.UserId ?? order.CreatedBy,
            $"Removed promotion {promotionId}");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
