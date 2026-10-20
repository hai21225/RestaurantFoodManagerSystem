using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Phase 0: một cửa vào duy nhất cho các thay đổi trạng thái order/kitchen.
///
/// <para>
/// Coordinator này chưa thay thế toàn bộ ma trận trạng thái. Nó gom các quy tắc đang bị lặp
/// ở <see cref="OrderService"/> và <see cref="KitchenService"/> vào một chỗ:
/// guard đóng phiếu/đơn, aggregate trạng thái phiếu từ món, map phiếu sang đơn, và ghi
/// <see cref="OrderStatusHistory"/>.
/// </para>
///
/// <para>
/// Các method không tự mở transaction và không tự gọi <c>SaveChanges</c>; caller đang giữ
/// transaction chịu trách nhiệm lưu. Nhờ vậy Phase 0 không đổi API/schema hay ranh giới transaction.
/// </para>
/// </summary>
public sealed class OrderLifecycleCoordinator
{
    private readonly AppDbContext _db;

    public OrderLifecycleCoordinator(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Chặn đóng phiếu bếp (<c>Ready</c>/<c>Completed</c>) khi còn món active chưa xong.
    /// Phiếu không có món (legacy) được bỏ qua; phiếu mà mọi món đã huỷ phải đi đường
    /// <c>Cancelled</c>, không được đóng như hoàn tất.
    /// </summary>
    public static void EnsureTicketCanClose(
        string targetStatus,
        IReadOnlyCollection<string> itemStatuses)
    {
        if (!IsClosingStatus(targetStatus))
        {
            return;
        }

        // Ticket legacy không có dòng món: giữ hành vi cũ, không thể suy ra trạng thái.
        if (itemStatuses.Count == 0)
        {
            return;
        }

        var active = itemStatuses
            .Where(x => !KitchenStatuses.IsCancelled(x))
            .ToList();

        if (active.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot change kitchen order status to {targetStatus} because all dishes are cancelled");
        }

        if (active.Any(x => !KitchenStatuses.IsDone(x)))
        {
            throw new InvalidOperationException(
                $"Cannot change kitchen order status to {targetStatus} while dishes are not finished");
        }
    }

    /// <summary>
    /// Chặn POS đặt đơn sang <c>Ready</c>/<c>Completed</c> khi bếp chưa xong.
    /// Kiểm tra cả order item và kitchen item để không bỏ sót dữ liệu cũ bị lệch.
    /// Đơn legacy không có dòng món được bỏ qua (giữ hành vi cũ).
    /// </summary>
    public async Task EnsureOrderCanCloseAsync(
        int orderId,
        string targetStatus,
        CancellationToken ct)
    {
        if (!IsClosingStatus(targetStatus))
        {
            return;
        }

        var orderItemStatuses = await _db.OrderItems
            .AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .Select(x => x.Status)
            .ToListAsync(ct);

        var kitchenItemStatuses = await (
            from kitchenItem in _db.KitchenOrderItems.AsNoTracking()
            join kitchenOrder in _db.KitchenOrders.AsNoTracking()
                on kitchenItem.KitchenOrderId equals kitchenOrder.KitchenOrderId
            where kitchenOrder.OrderId == orderId
            select kitchenItem.Status).ToListAsync(ct);

        var allStatuses = orderItemStatuses
            .Concat(kitchenItemStatuses)
            .ToList();

        if (allStatuses.Count == 0)
        {
            return;
        }

        var active = allStatuses
            .Where(x => !KitchenStatuses.IsCancelled(x))
            .ToList();

        if (active.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot change order status to {targetStatus} because all dishes are cancelled");
        }

        if (active.Any(x => !KitchenStatuses.IsDone(x)))
        {
            throw new InvalidOperationException(
                $"Cannot change order status to {targetStatus} while dishes are not finished");
        }
    }

    /// <summary>
    /// Sau khi một món bếp đổi trạng thái: tính lại phiếu từ <paramref name="ticketItems"/>,
    /// rồi map phiếu sang đơn nếu transition hợp lệ.
    /// </summary>
    public async Task<bool> SyncTicketAndOrderAfterItemChangeAsync(
        Order? order,
        KitchenOrder ticket,
        IReadOnlyCollection<KitchenOrderItem> ticketItems,
        int changedItemId,
        string changedStatus,
        int changedBy,
        string? historyNote,
        string? cancelReason,
        DateTime now,
        CancellationToken ct)
    {
        var effectiveStatuses = ticketItems
            .Select(x => x.Id == changedItemId ? changedStatus : x.Status)
            .ToList();

        // Ticket đã Completed/Cancelled là terminal: không mở lại chỉ vì một món bị huỷ.
        // Trường hợp huỷ toàn bộ đơn đi qua CancelOrderAsync/OrderCancellation, nơi ticket
        // được đặt Cancelled có chủ đích.
        RecalculateTicketStatus(ticket, effectiveStatuses, now);

        if (order is null)
        {
            return false;
        }

        // C-12: trạng thái đơn phải tính từ TẤT CẢ dòng của TẤT CẢ round, không chỉ ticket
        // vừa đổi. Nếu không, huỷ dòng cuối của round mới sẽ huỷ oan cả đơn còn món cũ.
        return await RefreshOrderStatusFromAllItemsAsync(
            order,
            changedBy,
            historyNote,
            allowReopen: false,
            cancelReason,
            ct);
    }

    /// <summary>
    /// Tính lại trạng thái một phiếu bếp từ danh sách item effective (gồm thay đổi trong bộ nhớ).
    /// Phiếu terminal (Completed/Cancelled) không bao giờ bị mở lại.
    /// </summary>
    public static void RecalculateTicketStatus(
        KitchenOrder ticket,
        IReadOnlyCollection<string> itemStatuses,
        DateTime now)
    {
        if (KitchenStatuses.IsTerminal(ticket.Status))
        {
            return;
        }

        var ticketStatus = KitchenStatuses.Aggregate(ticket.Status, itemStatuses);
        ticket.Status = ticketStatus;

        if (KitchenStatuses.IsAccepted(ticketStatus) || KitchenStatuses.IsPreparing(ticketStatus))
        {
            ticket.StartedAt ??= now;
        }

        if (KitchenStatuses.IsDone(ticketStatus) || KitchenStatuses.IsCancelled(ticketStatus))
        {
            ticket.CompletedAt ??= now;
        }
    }

    /// <summary>
    /// Fallback khi order không có kitchen ticket: suy trạng thái đơn trực tiếp từ các dòng món.
    /// </summary>
    public Task<bool> SyncOrderFromItemsAsync(
        Order order,
        IReadOnlyCollection<OrderItem> orderItems,
        int changedBy,
        string? historyNote,
        CancellationToken ct) =>
        RefreshOrderStatusFromAllItemsAsync(
            order,
            changedBy,
            historyNote,
            allowReopen: false,
            cancelReason: null,
            ct,
            orderItems);

    /// <summary>
    /// Tính lại trạng thái đơn từ tất cả dòng món còn hoạt động của tất cả round.
    ///
    /// <para>
    /// Đây là nguồn sự thật cho C-12. Item bị huỷ bị loại; trạng thái kitchen item được ưu tiên
    /// nếu có để tránh projection order item bị lệch. <paramref name="allowReopen"/> chỉ dùng cho
    /// command gọi thêm/tăng số lượng: order <c>Completed/Ready</c> được phép quay về
    /// <c>SentToKitchen/Preparing</c> khi có round mới hoặc C-06 reopen món đã xong.
    /// Các luồng huỷ/giảm thì không mở lại.
    /// </para>
    /// </summary>
    public async Task<bool> RefreshOrderStatusFromAllItemsAsync(
        Order order,
        int changedBy,
        string? historyNote,
        bool allowReopen,
        string? cancelReason,
        CancellationToken ct,
        IReadOnlyCollection<OrderItem>? effectiveItems = null)
    {
        if (OrderStatuses.IsTerminal(order.Status))
        {
            return false;
        }

        var orderItems = effectiveItems?.ToList()
            ?? await _db.OrderItems
                .Where(x => x.OrderId == order.OrderId)
                .ToListAsync(ct);

        // Đơn legacy không có dòng món: giữ hành vi cũ, không tự suy trạng thái.
        if (orderItems.Count == 0)
        {
            return false;
        }

        var kitchenItems = await (
            from kitchenItem in _db.KitchenOrderItems.AsNoTracking()
            join ticket in _db.KitchenOrders.AsNoTracking()
                on kitchenItem.KitchenOrderId equals ticket.KitchenOrderId
            where ticket.OrderId == order.OrderId
            select kitchenItem).ToListAsync(ct);

        var kitchenStatusByOrderItem = kitchenItems
            .GroupBy(x => x.OrderItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First().Status);

        // Các thay đổi trong cùng transaction chưa SaveChanges sẽ không thấy qua query
        // AsNoTracking; ưu tiên trạng thái đang track trong bộ nhớ.
        var orderItemIds = orderItems.Select(x => x.Id).ToHashSet();
        foreach (var localItem in _db.KitchenOrderItems.Local
                     .Where(x => orderItemIds.Contains(x.OrderItemId)))
        {
            kitchenStatusByOrderItem[localItem.OrderItemId] = localItem.Status;
        }

        var activeStatuses = new List<string>();
        foreach (var orderItem in orderItems)
        {
            if (KitchenStatuses.IsCancelled(orderItem.Status))
            {
                continue;
            }

            var effectiveStatus = kitchenStatusByOrderItem.TryGetValue(orderItem.Id, out var kitchenStatus)
                ? kitchenStatus
                : orderItem.Status;

            if (KitchenStatuses.IsCancelled(effectiveStatus))
            {
                continue;
            }

            activeStatuses.Add(effectiveStatus);
        }

        if (activeStatuses.Count == 0)
        {
            var reason = cancelReason ?? historyNote ?? "All order items were cancelled";
            await ApplyOrderStatusAsync(
                order,
                OrderStatuses.Cancelled,
                changedBy,
                historyNote ?? reason,
                reason,
                ct);
            return true;
        }

        string nextStatus;
        if (activeStatuses.All(KitchenStatuses.IsDone))
        {
            // Completed là "món hiện tại đã phục vụ nhưng chưa thanh toán"; giữ nguyên nếu
            // mọi dòng vẫn done. Chỉ Ready khi đơn chưa từng được đánh Completed.
            nextStatus = string.Equals(order.Status, OrderStatuses.Completed, StringComparison.OrdinalIgnoreCase)
                ? OrderStatuses.Completed
                : OrderStatuses.Ready;
        }
        else if (activeStatuses.Any(KitchenStatuses.IsPreparing))
        {
            nextStatus = OrderStatuses.Preparing;
        }
        else
        {
            nextStatus = OrderStatuses.SentToKitchen;
        }

        if (string.Equals(order.Status, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var canTransition = OrderStatuses.CanTransition(order.Status, nextStatus)
            || (allowReopen && IsReopenForNewRound(order.Status, nextStatus));

        if (!canTransition)
        {
            return false;
        }

        await ApplyOrderStatusAsync(
            order,
            nextStatus,
            changedBy,
            historyNote,
            cancelReason,
            ct);
        return true;
    }

    private static bool IsReopenForNewRound(string currentStatus, string nextStatus) =>
        (string.Equals(currentStatus, OrderStatuses.Completed, StringComparison.OrdinalIgnoreCase)
         || string.Equals(currentStatus, OrderStatuses.Ready, StringComparison.OrdinalIgnoreCase))
        && (string.Equals(nextStatus, OrderStatuses.SentToKitchen, StringComparison.OrdinalIgnoreCase)
            || string.Equals(nextStatus, OrderStatuses.Preparing, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ghi một dòng audit không làm đổi trạng thái (ví dụ "gọi thêm tạo round mới").
    /// Dùng khi Refresh... không tạo history vì order vẫn ở cùng trạng thái.
    /// </summary>
    public void RecordOrderNote(Order order, int changedBy, string note)
    {
        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.OrderId,
            OldStatus = order.Status,
            NewStatus = order.Status,
            ChangedBy = changedBy,
            Note = note,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>Map trạng thái phiếu bếp sang trạng thái đơn và áp dụng nếu hợp lệ.</summary>
    public Task<bool> ApplyOrderStatusFromTicketAsync(
        Order order,
        string ticketStatus,
        int changedBy,
        string? historyNote,
        string? cancelReason,
        CancellationToken ct,
        string? reasonCode = null) =>
        ApplyOrderStatusIfAllowedAsync(
            order,
            MapTicketStatusToOrder(ticketStatus),
            changedBy,
            historyNote,
            cancelReason,
            ct,
            reasonCode);

    /// <summary>Chỉ áp dụng khi trạng thái đơn thực sự đổi và transition hợp lệ.</summary>
    public async Task<bool> ApplyOrderStatusIfAllowedAsync(
        Order order,
        string newStatus,
        int changedBy,
        string? historyNote,
        string? cancelReason,
        CancellationToken ct,
        string? reasonCode = null)
    {
        // C-07: Paid là terminal. Không được huỷ ticket/order sau khi đã thu tiền;
        // nếu nghiệp vụ cần huỷ sau Paid thì phải đi luồng refund riêng.
        if (string.Equals(newStatus, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Cannot cancel an order or kitchen ticket after the order is paid");
        }

        if (OrderStatuses.IsTerminal(order.Status))
        {
            return false;
        }

        if (string.Equals(order.Status, newStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!OrderStatuses.CanTransition(order.Status, newStatus))
        {
            return false;
        }

        await ApplyOrderStatusAsync(
            order,
            newStatus,
            changedBy,
            historyNote,
            cancelReason,
            ct,
            reasonCode);

        return true;
    }

    /// <summary>
    /// Ghi trạng thái đơn + history. Caller đã kiểm tra transition/role.
    /// Huỷ đơn dùng chung <see cref="OrderCancellation"/> để đồng bộ kho/phiếu/bàn.
    /// </summary>
    public async Task ApplyOrderStatusAsync(
        Order order,
        string newStatus,
        int changedBy,
        string? historyNote,
        string? cancelReason,
        CancellationToken ct,
        string? reasonCode = null)
    {
        var oldStatus = order.Status;
        var now = DateTime.UtcNow;

        if (string.Equals(newStatus, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            await OrderCancellation.ApplyAsync(
                _db,
                order,
                changedBy,
                ct,
                cancelReason ?? historyNote,
                reasonCode);
        }
        else
        {
            order.Status = newStatus;
            order.UpdatedAt = now;
        }

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.OrderId,
            OldStatus = oldStatus,
            NewStatus = order.Status,
            ChangedBy = changedBy,
            Note = historyNote ?? cancelReason,
            CreatedAt = now
        });
    }

    /// <summary>Huỷ toàn bộ đơn và ghi history trong cùng transaction của caller.</summary>
    public Task<bool> CancelOrderAsync(
        Order order,
        int changedBy,
        string reason,
        string? historyNote,
        CancellationToken ct,
        string? reasonCode = null) =>
        ApplyOrderStatusIfAllowedAsync(
            order,
            OrderStatuses.Cancelled,
            changedBy,
            historyNote ?? reason,
            reason,
            ct,
            reasonCode);

    /// <summary>Map trạng thái phiếu bếp sang trạng thái order hiện có.</summary>
    public static string MapTicketStatusToOrder(string kitchenStatus) =>
        kitchenStatus.Trim().ToLowerInvariant() switch
        {
            "preparing" => OrderStatuses.Preparing,
            "ready" => OrderStatuses.Ready,
            "completed" => OrderStatuses.Ready,
            "cancelled" => OrderStatuses.Cancelled,
            _ => OrderStatuses.SentToKitchen // Pending, Accepted
        };

    private static bool IsClosingStatus(string status) =>
        string.Equals(status, KitchenStatuses.Ready, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, KitchenStatuses.Completed, StringComparison.OrdinalIgnoreCase);
}
