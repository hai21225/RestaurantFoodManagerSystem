using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed partial class KitchenService
{
    private readonly AppDbContext _db;
    private readonly RestaurantTableService _tables;
    private readonly ICurrentUser _currentUser;
    private readonly OrderLifecycleCoordinator _lifecycle;
    private readonly ILogger<KitchenService> _logger;
    private readonly RestaurantSettingsService _settings;

    public KitchenService(
        AppDbContext db,
        RestaurantTableService tables,
        ICurrentUser currentUser,
        OrderLifecycleCoordinator lifecycle,
        ILogger<KitchenService> logger,
        RestaurantSettingsService settings)
    {
        _db = db;
        _tables = tables;
        _currentUser = currentUser;
        _lifecycle = lifecycle;
        _logger = logger;
        _settings = settings;
    }

    public async Task<bool> UpdateOrderStatusAsync(int id, KitchenStatusRequest request, CancellationToken ct)
    {
        // Validate trước transaction để tránh giữ lock không cần thiết.
        var status = request.Status?.Trim() ?? string.Empty;
        if (!KitchenStatuses.AllowedTicket.Contains(status, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid kitchen order status");

        var isCancelling = string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
        var reasonCode = isCancelling
            ? OrderItemMutations.NormalizeReasonCode(request.ReasonCode)
            : null;
        var settings = isCancelling ? await _settings.GetAsync(ct) : null;

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var kitchenOrder = await _db.KitchenOrders.FirstOrDefaultAsync(x => x.KitchenOrderId == id, ct);
        if (kitchenOrder is null) return false;

        // P1d: phiếu đã Completed/Cancelled là trạng thái cuối; chỉ cho đi tiến, không mở lại.
        if (KitchenStatuses.IsTerminal(kitchenOrder.Status))
            throw new InvalidOperationException(
                $"Kitchen order is {kitchenOrder.Status} and can no longer change status");

        if (!KitchenStatuses.CanTransitionOrder(kitchenOrder.Status, status))
            throw new InvalidOperationException(
                $"Cannot change kitchen order status from {kitchenOrder.Status} to {status}");

        // C-02: không đóng phiếu Ready/Completed khi còn món chưa xong.
        var kitchenItems = await _db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
            .ToListAsync(ct);
        OrderLifecycleCoordinator.EnsureTicketCanClose(status, kitchenItems.Select(x => x.Status).ToList());

        var ticketOrderItemIds = kitchenItems.Select(x => x.OrderItemId).Distinct().ToList();
        var ticketOrderItems = ticketOrderItemIds.Count == 0
            ? new List<OrderItem>()
            : await _db.OrderItems
                .Where(x => ticketOrderItemIds.Contains(x.Id))
                .ToListAsync(ct);

        var mappedStatus = OrderLifecycleCoordinator.MapTicketStatusToOrder(status);
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);

        // C-07: order đã Paid thì không được huỷ ticket. Bếp vẫn có thể đóng ticket
        // bằng Completed, nhưng Cancelled sẽ làm báo cáo bếp lệch doanh thu.
        if (order is not null &&
            string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase) &&
            isCancelling)
        {
            throw new InvalidOperationException(
                "Cannot cancel a kitchen ticket after the order is paid");
        }

        if (order is not null &&
            string.Equals(mappedStatus, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) &&
            !OrderStatuses.IsTerminal(order.Status))
        {
            var anyDone = kitchenItems.Any(x => KitchenStatuses.IsDone(x.Status)) ||
                          ticketOrderItems.Any(x => KitchenStatuses.IsDone(x.Status));
            if (!OrderCancellationPolicy.CanCancelOrder(_currentUser, anyDone))
                throw new UnauthorizedAccessException(
                    "Only an administrator can cancel an order after a dish is ready");
        }

        kitchenOrder.Status = status;
        var now = DateTime.UtcNow;
        if (status.Equals(KitchenStatuses.Accepted, StringComparison.OrdinalIgnoreCase) ||
            status.Equals(KitchenStatuses.Preparing, StringComparison.OrdinalIgnoreCase))
            kitchenOrder.StartedAt ??= now;
        if (status.Equals(KitchenStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
            status.Equals(KitchenStatuses.Ready, StringComparison.OrdinalIgnoreCase) ||
            status.Equals(KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            kitchenOrder.CompletedAt ??= now;

        if (status.Equals(KitchenStatuses.Preparing, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var ki in kitchenItems.Where(x => KitchenStatuses.IsPending(x.Status)))
            {
                ki.Status = KitchenStatuses.Preparing;
            }
        }

        // C-10: history order khi bếp huỷ phải giữ lý do thật, không chỉ "Updated from kitchen".
        var historyNote = isCancelling
            ? (string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled from kitchen" : request.Reason.Trim())
            : "Updated from kitchen";
        var cancelReason = isCancelling
            ? historyNote
            : request.Reason;

        // Đơn đã Paid/Cancelled thì vẫn cho cập nhật phiếu bếp (bếp cần đóng ticket), nhưng
        // tuyệt đối không ghi ngược trạng thái đơn — tránh "mở lại" đơn đã thanh toán/huỷ.
        if (order is not null && !OrderStatuses.IsTerminal(order.Status))
        {
            var changedBy = await _currentUser.ResolveExistingActorAsync(
                _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);

            if (status.Equals(KitchenStatuses.Preparing, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var oi in ticketOrderItems.Where(x => KitchenStatuses.IsPending(x.Status)))
                {
                    var oldItemStatus = oi.Status;
                    oi.Status = KitchenStatuses.Preparing;
                    var linkedKi = kitchenItems.FirstOrDefault(x => x.OrderItemId == oi.Id);
                    OrderItemMutations.RecordItemHistory(
                        _db, oi, linkedKi?.Id, oldItemStatus, KitchenStatuses.Preparing, changedBy, now, historyNote);
                }
            }

            if (isCancelling)
            {
                var effectiveReasonCode = reasonCode ?? OrderItemMutations.CustomerChange;
                var remainingConsumption = await OrderCancellation.LoadOrderRemainingAsync(_db, order.OrderId, ct);

                foreach (var oi in ticketOrderItems.Where(x => !KitchenStatuses.IsCancelled(x.Status)))
                {
                    var linkedKi = kitchenItems.FirstOrDefault(x => x.OrderItemId == oi.Id);
                    await OrderCancellation.CancelSingleItemAsync(
                        _db,
                        order.OrderId,
                        oi,
                        linkedKi,
                        effectiveReasonCode,
                        cancelReason,
                        changedBy,
                        now,
                        ct,
                        remainingConsumption);
                }

                foreach (var ki in kitchenItems.Where(x => !KitchenStatuses.IsTerminal(x.Status)))
                {
                    ki.Status = KitchenStatuses.Cancelled;
                }

                var allOrderItems = await _db.OrderItems
                    .Where(x => x.OrderId == order.OrderId)
                    .ToListAsync(ct);

                if (allOrderItems.Count == 0)
                {
                    await _lifecycle.ApplyOrderStatusFromTicketAsync(
                        order, status, changedBy, historyNote, cancelReason, ct, reasonCode);
                }
                else
                {
                    if (allOrderItems.Any(x => !KitchenStatuses.IsCancelled(x.Status)))
                    {
                        await OrderItemMutations.RecalculateTotalsAsync(_db, order, allOrderItems, now, settings!, ct);
                    }

                    await _lifecycle.RefreshOrderStatusFromAllItemsAsync(
                        order,
                        changedBy,
                        historyNote,
                        allowReopen: false,
                        cancelReason,
                        ct,
                        allOrderItems,
                        reasonCode);
                }
            }
            else if (string.Equals(status, KitchenStatuses.Accepted, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(status, KitchenStatuses.Preparing, StringComparison.OrdinalIgnoreCase))
            {
                await _lifecycle.ApplyOrderStatusFromTicketAsync(
                    order,
                    status,
                    changedBy,
                    historyNote,
                    cancelReason,
                    ct,
                    reasonCode);
            }
            else
            {
                // C-12: Ready/Completed của một ticket chỉ được nâng order lên Ready khi
                // TẤT CẢ round đã xong. Nếu còn round mới Pending, order không được đóng.
                await _lifecycle.RefreshOrderStatusFromAllItemsAsync(
                    order,
                    changedBy,
                    historyNote,
                    allowReopen: false,
                    cancelReason,
                    ct,
                    reasonCode: reasonCode);
            }
        }
        else if (isCancelling)
        {
            foreach (var ki in kitchenItems.Where(x => !KitchenStatuses.IsTerminal(x.Status)))
            {
                ki.Status = KitchenStatuses.Cancelled;
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (order is not null &&
            string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        }

        return true;
    }

    public async Task<bool> UpdateItemStatusAsync(int id, KitchenItemStatusRequest request, CancellationToken ct)
    {
        // Validate trước transaction để tránh giữ lock không cần thiết.
        var status = request.Status?.Trim() ?? string.Empty;
        if (!KitchenStatuses.AllowedItem.Contains(status, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid kitchen item status");

        if (request.Reason is { Length: > 500 })
            throw new ArgumentException("Reason must be at most 500 characters");

        var reasonCode = string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
            ? OrderItemMutations.NormalizeReasonCode(request.ReasonCode)
            : null;

        // Đợt 2 / A1: cấu hình quán đọc trước transaction.
        var settings = await _settings.GetAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var item = await _db.KitchenOrderItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return false;

        // Defect #25: kiểm phiếu bếp cha trước khi sửa item; thiếu cha ⇒ trả false (404)
        // thay vì FirstAsync ném lỗi 500.
        var kitchenOrder = await _db.KitchenOrders.FirstOrDefaultAsync(x => x.KitchenOrderId == item.KitchenOrderId, ct);
        if (kitchenOrder is null) return false;

        // P1d: không sửa món khi phiếu cha đã kết thúc.
        if (KitchenStatuses.IsTerminal(kitchenOrder.Status))
            throw new InvalidOperationException(
                $"Kitchen order is {kitchenOrder.Status} and can no longer change status");

        if (!KitchenStatuses.CanTransitionItem(item.Status, status))
            throw new InvalidOperationException(
                $"Cannot change kitchen item status from {item.Status} to {status}");

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);
        var orderItem = await _db.OrderItems.FirstOrDefaultAsync(x => x.Id == item.OrderItemId, ct);
        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order?.CreatedBy ?? 0);
        var now = DateTime.UtcNow;
        var oldItemStatus = item.Status;
        var historyReason = string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled from kitchen" : request.Reason.Trim())
            : "Updated from kitchen";

        // P1a/P1b: huỷ món phải trả kho đúng phần của món đó và lưu lý do.
        if (status.Equals(KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            // Đơn đã thu tiền: huỷ món phải đi luồng hoàn tiền, không hoàn kho ở đây.
            if (order is not null &&
                string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cannot cancel an item of a paid order");
            }

            if (!OrderCancellationPolicy.CanCancelItem(_currentUser, item.Status))
                throw new UnauthorizedAccessException(
                    "Only the kitchen can cancel a dish that is being prepared; only an administrator can cancel a served dish");

            if (orderItem is not null && order is not null)
            {
                await OrderCancellation.CancelSingleItemAsync(
                    _db,
                    order.OrderId,
                    orderItem,
                    item,
                    reasonCode,
                    historyReason,
                    changedBy,
                    now,
                    ct);

                // Món còn lại vẫn phải được tính tiền; món cuối bị huỷ thì giữ nguyên tổng lịch sử
                // (đơn chuyển Cancelled bên dưới), khớp quy tắc của OrderService.CancelItemAsync.
                var orderItems = await _db.OrderItems
                    .Where(x => x.OrderId == order.OrderId)
                    .ToListAsync(ct);
                if (orderItems.Any(x => !KitchenStatuses.IsCancelled(x.Status)))
                {
                    await OrderItemMutations.RecalculateTotalsAsync(_db, order, orderItems, now, settings, ct);
                }
            }
        }

        item.Status = status;
        if (orderItem is not null &&
            !(status.Equals(KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) && order is not null))
        {
            orderItem.Status = status;
            OrderItemMutations.RecordItemHistory(
                _db,
                orderItem,
                item.Id,
                oldItemStatus,
                status,
                changedBy,
                now,
                historyReason,
                string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                    ? reasonCode
                    : null);
        }

        // Tính trạng thái phiếu sau khi đổi món (bỏ qua món đã huỷ — P1c) và đồng bộ đơn.
        var allItems = await _db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == item.KitchenOrderId)
            .ToListAsync(ct);
        var note = historyReason;

        await _lifecycle.SyncTicketAndOrderAfterItemChangeAsync(
            order,
            kitchenOrder,
            allItems,
            item.Id,
            status,
            changedBy,
            note,
            string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ? note : null,
            now,
            ct,
            string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                ? reasonCode
                : null);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (order is not null &&
            string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        }

        return true;
    }

    /// <summary>
    /// C-06: mở lại một món đã ra đĩa (<c>Ready/Completed</c>) để làm lại.
    ///
    /// <para>
    /// Chỉ cho phép khi đơn chưa <c>Paid/Cancelled</c> và phiếu bếp chưa
    /// <c>Completed/Cancelled</c>. Món quay về <c>Preparing</c>; nếu phiếu/đơn đang
    /// <c>Ready/Completed</c> thì được hạ về <c>Preparing</c> (reopen có kiểm soát),
    /// bàn vẫn giữ <c>Occupied</c>.
    /// </para>
    /// </summary>
    public async Task<bool> ReopenItemAsync(
        int id,
        ReopenKitchenItemRequest request,
        CancellationToken ct)
    {
        if (request.Reason is { Length: > 500 })
            throw new ArgumentException("Reason must be at most 500 characters");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var ctx = await LoadMutableItemContextAsync(id, requireDone: true, ct);
        if (ctx is null) return false;
        var (kitchenItem, kitchenOrder, order, orderItem) = ctx;

        var oldStatus = kitchenItem.Status;
        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);
        var now = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Item reopened by kitchen"
            : request.Reason.Trim();

        kitchenItem.Status = KitchenStatuses.Preparing;
        orderItem.Status = KitchenStatuses.Preparing;

        OrderItemMutations.RecordItemHistory(
            _db,
            orderItem,
            kitchenItem.Id,
            oldStatus,
            KitchenStatuses.Preparing,
            changedBy,
            now,
            reason,
            reasonCode: null);

        var ticketItems = await _db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
            .ToListAsync(ct);

        await _lifecycle.SyncTicketAndOrderAsync(
            order,
            kitchenOrder,
            ticketItems.Select(x => x.Status).ToList(),
            changedBy,
            reason,
            cancelReason: null,
            now,
            allowReopen: true,
            ct);

        // Phiếu từng Ready có CompletedAt; khi quay lại Preparing thì mốc hoàn tất cũ
        // không còn đúng. Chỉ xoá khi phiếu thực sự đang Preparing.
        if (KitchenStatuses.IsPreparing(kitchenOrder.Status))
        {
            kitchenOrder.CompletedAt = null;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Bếp báo đã nấu một phần. Cách A: tách dòng hiện tại thành phần đã nấu
    /// (<c>Completed</c>) và phần còn lại (giữ trạng thái cũ). Không trừ/hoàn kho vì
    /// nguyên liệu đã được trừ đủ khi tạo đơn; ledger được tách theo tỷ lệ để lần giảm
    /// phần chưa nấu sau đó chỉ hoàn đúng phần đó.
    /// </summary>
    public async Task<bool> UpdatePreparedQuantityAsync(
        int id,
        PreparedQuantityRequest request,
        CancellationToken ct)
    {
        if (request.PreparedQuantity <= 0)
            throw new ArgumentException("Prepared quantity must be greater than zero");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var ctx = await LoadMutableItemContextAsync(id, requireDone: false, ct);
        if (ctx is null) return false;
        var (kitchenItem, kitchenOrder, order, orderItem) = ctx;

        if (request.PreparedQuantity > kitchenItem.Quanlity)
            throw new ArgumentException("Prepared quantity cannot exceed item quantity");

        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);
        var now = DateTime.UtcNow;

        if (request.PreparedQuantity == kitchenItem.Quanlity)
        {
            var allPreparedOldStatus = orderItem.Status;
            kitchenItem.Status = KitchenStatuses.Completed;
            orderItem.Status = KitchenStatuses.Completed;

            OrderItemMutations.RecordItemHistory(
                _db,
                orderItem,
                kitchenItem.Id,
                allPreparedOldStatus,
                KitchenStatuses.Completed,
                changedBy,
                now,
                string.IsNullOrWhiteSpace(request.Reason)
                    ? "Kitchen reported all portions prepared"
                    : request.Reason.Trim(),
                reasonCode: null);

            var ticketItems = await _db.KitchenOrderItems
                .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
                .ToListAsync(ct);

            await _lifecycle.SyncTicketAndOrderAsync(
                order,
                kitchenOrder,
                ticketItems.Select(x => x.Status).ToList(),
                changedBy,
                "Kitchen reported all portions prepared",
                cancelReason: null,
                now,
                allowReopen: false,
                ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        var originalQuantity = orderItem.Quanlity;
        var remainingQuantity = originalQuantity - request.PreparedQuantity;
        if (remainingQuantity <= 0)
            throw new ArgumentException("Prepared quantity cannot exceed item quantity");

        OrderItemMutations.EnsureGroupId(orderItem);
        var splitOldStatus = orderItem.Status;
        orderItem.Quanlity = remainingQuantity;
        kitchenItem.Quanlity = remainingQuantity;

        OrderItemMutations.RecordItemHistory(
            _db,
            orderItem,
            kitchenItem.Id,
            splitOldStatus,
            orderItem.Status,
            changedBy,
            now,
            string.IsNullOrWhiteSpace(request.Reason)
                ? $"Prepared {request.PreparedQuantity}; quantity {originalQuantity} -> {remainingQuantity}"
                : $"Prepared {request.PreparedQuantity}; quantity {originalQuantity} -> {remainingQuantity}: {request.Reason.Trim()}",
            reasonCode: null);

        var (_, preparedKitchenItem) = await SplitKitchenOrderItemLineAsync(
            kitchenOrder,
            orderItem,
            request.PreparedQuantity,
            KitchenStatuses.Completed,
            $"Prepared portion split from order item {orderItem.Id}",
            splitReasonCode: null,
            kitchenNote: orderItem.Note,
            originalQuantity,
            changedBy,
            now,
            ct);

        var ticketItemsAfterSplit = await _db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
            .ToListAsync(ct);
        ticketItemsAfterSplit.Add(preparedKitchenItem);

        // Tách phần đã nấu không đổi tổng số lượng/đơn giá, nên không reset discount.
        // Chỉ cập nhật thời điểm để audit.
        order.UpdatedAt = now;

        await _lifecycle.SyncTicketAndOrderAsync(
            order,
            kitchenOrder,
            ticketItemsAfterSplit.Select(x => x.Status).ToList(),
            changedBy,
            "Kitchen reported part of the item prepared",
            cancelReason: null,
            now,
            allowReopen: false,
            ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Bếp báo thiếu nguyên liệu thực tế. Phần còn làm được giữ lại; phần thiếu bị huỷ và
    /// ghi <c>Wastage</c> (không hoàn kho ảo). Không bao giờ hard-block bếp.
    /// </summary>
    public async Task<bool> ReportShortageAsync(
        int id,
        ReportShortageRequest request,
        CancellationToken ct)
    {
        var reasonCode = OrderItemMutations.NormalizeReasonCode(
            request.ReasonCode, OrderItemMutations.PhysicalShortage);

        if (!string.Equals(reasonCode, OrderItemMutations.OutOfStock, StringComparison.Ordinal) &&
            !string.Equals(reasonCode, OrderItemMutations.PhysicalShortage, StringComparison.Ordinal))
        {
            throw new ArgumentException("Shortage reason must be OutOfStock or PhysicalShortage");
        }

        // Đợt 2 / A1: cấu hình quán đọc trước transaction.
        var settings = await _settings.GetAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var ctx = await LoadMutableItemContextAsync(id, requireDone: false, ct);
        if (ctx is null) return false;
        var (kitchenItem, kitchenOrder, order, orderItem) = ctx;

        if (request.PreparedQuantity < 0 ||
            request.PreparedQuantity >= kitchenItem.Quanlity)
        {
            throw new ArgumentException("Prepared quantity must be smaller than item quantity");
        }

        var detail = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason.Trim();
        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);
        var now = DateTime.UtcNow;
        var originalQuantity = orderItem.Quanlity;
        var cancelledQuantity = originalQuantity - request.PreparedQuantity;

        // P0-06: orderItem.Quanlity và kitchenItem.Quanlity có thể lệch nhau (dữ liệu cũ).
        // Không để cancelledQuantity âm ⇒ dòng "Cancelled" số lượng âm + ledger sai.
        if (cancelledQuantity < 0)
        {
            throw new ArgumentException(
                "Prepared quantity cannot exceed the order item quantity");
        }

        // C-13: bếp chỉ định nguyên liệu nào thực sự hư/hết ⇒ hao hụt chỉ ghi cho nguyên
        // liệu đó, và trạng thái nguyên liệu được đổi để kho quản lý + báo cáo.
        if (request.IngredientId is int ingredientId)
        {
            var ingredient = await _db.Ingredients
                .FirstOrDefaultAsync(x => x.IngredientId == ingredientId, ct);
            if (ingredient is null)
                throw new ArgumentException($"Ingredient {ingredientId} not found");
            if (!ingredient.IsActive)
                throw new InvalidOperationException("Cannot report shortage for an inactive ingredient");

            ingredient.StockStatus = string.Equals(
                reasonCode, OrderItemMutations.OutOfStock, StringComparison.Ordinal)
                ? IngredientStockStatuses.OutOfStock
                : IngredientStockStatuses.Spoiled;
        }

        OrderItemMutations.EnsureGroupId(orderItem);

        if (request.PreparedQuantity == 0)
        {
            await OrderItemMutations.RecordWastageAsync(
                _db, order.OrderId, orderItem, cancelledQuantity,
                changedBy, now, reasonCode, detail, ct,
                request.IngredientId);

            OrderItemMutations.RecordItemHistory(
                _db,
                orderItem,
                kitchenItem.Id,
                orderItem.Status,
                KitchenStatuses.Cancelled,
                changedBy,
                now,
                detail ?? $"Shortage ({reasonCode}); cancelled all {cancelledQuantity} portion(s)",
                reasonCode);

            OrderCancellation.MarkItemCancelled(
                orderItem, now, changedBy, detail ?? reasonCode);
            kitchenItem.Status = KitchenStatuses.Cancelled;
        }
        else
        {
            var oldStatus = orderItem.Status;
            orderItem.Quanlity = request.PreparedQuantity;
            orderItem.Status = KitchenStatuses.Completed;
            kitchenItem.Quanlity = request.PreparedQuantity;
            kitchenItem.Status = KitchenStatuses.Completed;

            OrderItemMutations.RecordItemHistory(
                _db,
                orderItem,
                kitchenItem.Id,
                oldStatus,
                KitchenStatuses.Completed,
                changedBy,
                now,
                detail is null
                    ? $"Shortage ({reasonCode}); kept {request.PreparedQuantity} of {originalQuantity}"
                    : $"Shortage ({reasonCode}); kept {request.PreparedQuantity} of {originalQuantity}: {detail}",
                reasonCode);

            var (cancelledLine, _) = await SplitKitchenOrderItemLineAsync(
                kitchenOrder,
                orderItem,
                cancelledQuantity,
                KitchenStatuses.Cancelled,
                detail ?? $"Shortage ({reasonCode}); cancelled {cancelledQuantity} portion(s)",
                reasonCode,
                detail,
                originalQuantity,
                changedBy,
                now,
                ct);

            OrderCancellation.MarkItemCancelled(
                cancelledLine, now, changedBy, detail ?? reasonCode);

            await OrderItemMutations.RecordWastageAsync(
                _db, order.OrderId, cancelledLine, cancelledQuantity,
                changedBy, now, reasonCode, detail, ct,
                request.IngredientId);
        }

        // C-13: lô nguyên liệu không còn đảm bảo ⇒ chấm dứt luôn món này (món biến mất khỏi
        // menu POS) và lưu lý do để làm báo cáo. Nguyên liệu vẫn giữ nguyên làm minh chứng.
        if (request.DeactivateDish)
        {
            var foodId = await _db.FoodSizes.AsNoTracking()
                .Where(x => x.SizeId == orderItem.SizeId)
                .Select(x => x.FoodId)
                .FirstOrDefaultAsync(ct);
            if (foodId <= 0)
                throw new InvalidOperationException("Cannot deactivate the dish: food size not found");

            var food = await _db.Foods.FirstOrDefaultAsync(x => x.FoodId == foodId, ct);
            if (food is null)
                throw new InvalidOperationException("Cannot deactivate the dish: food not found");

            food.IsActive = false;
            food.IsAvailable = false;
            food.InactiveReason = string.IsNullOrWhiteSpace(request.InactiveReason)
                ? detail ?? $"Shortage ({reasonCode})"
                : request.InactiveReason.Trim();
            food.InactiveAt = now;
            food.UpdatedAt = now;
        }

        var ticketItems = await _db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
            .ToListAsync(ct);
        var cancelledKitchen = _db.KitchenOrderItems.Local
            .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId
                     && !ticketItems.Any(t => t.Id == x.Id))
            .ToList();
        ticketItems.AddRange(cancelledKitchen);

        var allItems = await _db.OrderItems
            .Where(x => x.OrderId == order.OrderId)
            .ToListAsync(ct);

        // Nếu shortage huỷ luôn dòng cuối cùng, giữ tổng tiền lịch sử của đơn đã huỷ.
        if (allItems.Any(x => !KitchenStatuses.IsCancelled(x.Status)))
        {
            await OrderItemMutations.RecalculateTotalsAsync(_db, order, allItems, now, settings, ct);
        }

        await _lifecycle.SyncTicketAndOrderAsync(
            order,
            kitchenOrder,
            ticketItems.Select(x => x.Status).ToList(),
            changedBy,
            $"Shortage reported ({reasonCode})",
            detail ?? reasonCode,
            now,
            allowReopen: false,
            ct,
            reasonCode);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            await _tables.RefreshStatusForTableAsync(order.TableId, ct);

        return true;
    }

    private sealed record MutableKitchenItemContext(
        KitchenOrderItem KitchenItem,
        KitchenOrder KitchenOrder,
        Order Order,
        OrderItem OrderItem);

    private async Task<MutableKitchenItemContext?> LoadMutableItemContextAsync(
        int id,
        bool requireDone,
        CancellationToken ct)
    {
        var kitchenItem = await _db.KitchenOrderItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (kitchenItem is null) return null;

        var kitchenOrder = await _db.KitchenOrders
            .FirstOrDefaultAsync(x => x.KitchenOrderId == kitchenItem.KitchenOrderId, ct);
        if (kitchenOrder is null) return null;

        var order = await _db.Orders
            .FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);
        if (order is null) return null;

        if (KitchenStatuses.IsTerminal(kitchenOrder.Status))
            throw new InvalidOperationException(
                $"Kitchen order is {kitchenOrder.Status} and can no longer change status");

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        if (requireDone && !KitchenStatuses.IsDone(kitchenItem.Status))
            throw new InvalidOperationException(
                $"Only a Ready/Completed item can be reopened; current status is {kitchenItem.Status}");

        var orderItem = await _db.OrderItems
            .FirstOrDefaultAsync(x => x.Id == kitchenItem.OrderItemId, ct);
        if (orderItem is null) return null;

        if (KitchenStatuses.IsCancelled(orderItem.Status) ||
            KitchenStatuses.IsCancelled(kitchenItem.Status))
        {
            throw new InvalidOperationException("Order item is already cancelled");
        }

        if (!requireDone && KitchenStatuses.IsDone(kitchenItem.Status))
            throw new InvalidOperationException("Order item is already prepared");

        return new MutableKitchenItemContext(kitchenItem, kitchenOrder, order, orderItem);
    }

    private async Task<(OrderItem SplitLine, KitchenOrderItem SplitKitchenItem)> SplitKitchenOrderItemLineAsync(
        KitchenOrder kitchenOrder,
        OrderItem orderItem,
        int splitQuantity,
        string splitStatus,
        string splitHistoryReason,
        string? splitReasonCode,
        string? kitchenNote,
        int originalQuantity,
        int changedBy,
        DateTime now,
        CancellationToken ct)
    {
        var splitLine = OrderItemMutations.CloneLine(orderItem, splitQuantity, splitStatus, now);
        _db.OrderItems.Add(splitLine);
        await _db.SaveChangesAsync(ct);
        await OrderItemMutations.CopyToppingsAsync(_db, orderItem.Id, splitLine.Id, ct);

        OrderItemMutations.RecordItemHistory(
            _db,
            splitLine,
            null,
            KitchenStatuses.Pending,
            splitStatus,
            changedBy,
            now,
            splitHistoryReason,
            splitReasonCode);

        var splitKitchenItem = new KitchenOrderItem
        {
            KitchenOrderId = kitchenOrder.KitchenOrderId,
            OrderItemId = splitLine.Id,
            Quanlity = splitQuantity,
            Status = splitStatus,
            Note = kitchenNote
        };
        _db.KitchenOrderItems.Add(splitKitchenItem);

        await OrderItemMutations.SplitItemLedgerAsync(
            _db,
            orderItem,
            splitLine,
            originalQuantity,
            splitQuantity,
            changedBy,
            ct);

        return (splitLine, splitKitchenItem);
    }
}
