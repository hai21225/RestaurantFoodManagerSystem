using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class KitchenService
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

    /// <summary>
    /// Danh sách phiếu bếp cho màn hình bếp.
    ///
    /// <para>
    /// <c>status=open</c> ⇒ chỉ trả phiếu đang mở (bếp poll 3–5s nên không cần phiếu đã
    /// đóng, tránh danh sách phình vô hạn). Kèm số bàn, tên phục vụ và số món để bếp
    /// nhìn 1 màn hình là đủ, không phải gọi thêm <c>GET /api/orders/{id}</c> (C-13).
    /// </para>
    ///
    /// <para>
    /// <b>Đợt 3 / B6 — <c>since</c>:</b> chỉ trả phiếu **có thay đổi** kể từ mốc đó
    /// (gửi mới / bắt đầu nấu / hoàn thành) ⇒ bếp poll liên tục mà không phải tải lại
    /// toàn bộ phiếu đang mở mỗi lần. Mẫu dùng đúng: **lần đầu** gọi **không** kèm
    /// <c>since</c> (lấy hết), các lần sau kèm mốc của lần trước.
    /// </para>
    /// </summary>
    public async Task<List<KitchenOrderResponse>> GetOrdersAsync(
        CancellationToken ct = default,
        string? status = null,
        DateTime? since = null,
        int? page = null,
        int? pageSize = null)
    {
        var openOnly = string.Equals(status, "open", StringComparison.OrdinalIgnoreCase);

        var rows = await (
            from ko in _db.KitchenOrders.AsNoTracking()
            join o in _db.Orders.AsNoTracking() on ko.OrderId equals o.OrderId
            join t in _db.RestaurantTables.AsNoTracking() on o.TableId equals t.TableId into tableGroup
            from t in tableGroup.DefaultIfEmpty()
            join u in _db.Users.AsNoTracking() on o.CreatedBy equals u.Id into userGroup
            from u in userGroup.DefaultIfEmpty()
            where !openOnly ||
                  (ko.Status != KitchenStatuses.Completed && ko.Status != KitchenStatuses.Cancelled)
            orderby ko.Priority, ko.SentAt
            select new { ko, TableNumber = t.TableNumber, WaiterName = u.UserFullName })
            .ToListAsync(ct);

        // B6: lọc theo mốc thời gian — chỉ giữ phiếu có thay đổi (gửi mới / bắt đầu / xong).
        //
        // C-21: `since` phải bắt được cả thay đổi trạng thái **MÓN**, không chỉ mốc của phiếu.
        // Trước đây phiếu chỉ có 3 mốc (Sent/Started/Completed) và mỗi mốc chỉ set **một lần** ⇒
        // món A chuyển `Ready` trong khi món B còn `Preparing` (phiếu vẫn `Preparing`, các mốc
        // không đổi) ⇒ phiếu không lọt qua `since` ⇒ màn bếp không thấy món A vừa xong.
        if (since.HasValue)
        {
            var mark = since.Value;

            var byTicketMark = rows
                .Where(r => r.ko.SentAt > mark
                         || (r.ko.StartedAt.HasValue && r.ko.StartedAt.Value > mark)
                         || (r.ko.CompletedAt.HasValue && r.ko.CompletedAt.Value > mark))
                .Select(r => r.ko.KitchenOrderId)
                .ToHashSet();

            // Thay đổi ở cấp món: lịch sử trạng thái món (order_item_status_histories).
            var ticketIds = rows.Select(r => r.ko.KitchenOrderId).ToList();
            var byItemMark = new HashSet<int>();
            if (ticketIds.Count > 0)
            {
                byItemMark = (await (
                    from history in _db.OrderItemStatusHistories.AsNoTracking()
                    join ki in _db.KitchenOrderItems.AsNoTracking() on history.OrderItemId equals ki.OrderItemId
                    where history.CreatedAt > mark
                      && ticketIds.Contains(ki.KitchenOrderId)
                    select ki.KitchenOrderId)
                    .Distinct()
                    .ToListAsync(ct)).ToHashSet();
            }

            rows = rows
                .Where(r => byTicketMark.Contains(r.ko.KitchenOrderId)
                         || byItemMark.Contains(r.ko.KitchenOrderId))
                .ToList();
        }

        // B6: phân trang tuỳ chọn. Không truyền page/pageSize ⇒ trả về toàn bộ (hành vi cũ,
        // màn hình bếp cần thấy hết phiếu đang mở).
        if (page.HasValue || pageSize.HasValue)
        {
            var skip = Paging.ResolveSkip(page ?? 1, pageSize ?? Paging.DefaultPageSize);
            rows = rows.Skip(skip).Take(pageSize ?? Paging.DefaultPageSize).ToList();
        }

        var itemCounts = await _db.KitchenOrderItems.AsNoTracking()
            .GroupBy(x => x.KitchenOrderId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return rows.Select(r =>
        {
            var dto = KitchenOrderResponse.From(r.ko);
            dto.TableNumber = r.TableNumber ?? string.Empty;
            dto.WaiterName = r.WaiterName;
            dto.ItemCount = itemCounts.GetValueOrDefault(r.ko.KitchenOrderId);
            return dto;
        }).ToList();
    }

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
         orderby ki.Id
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
        // Validate trước transaction để tránh giữ lock không cần thiết.
        var status = request.Status?.Trim() ?? string.Empty;
        if (!KitchenStatuses.AllowedTicket.Contains(status, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid kitchen order status");

        var reasonCode = string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
            ? OrderItemMutations.NormalizeReasonCode(request.ReasonCode)
            : null;

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
        var kitchenItemStatuses = await _db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
            .Select(x => x.Status)
            .ToListAsync(ct);
        OrderLifecycleCoordinator.EnsureTicketCanClose(status, kitchenItemStatuses);

        var mappedStatus = OrderLifecycleCoordinator.MapTicketStatusToOrder(status);
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);

        // C-07: order đã Paid thì không được huỷ ticket. Bếp vẫn có thể đóng ticket
        // bằng Completed, nhưng Cancelled sẽ làm báo cáo bếp lệch doanh thu.
        if (order is not null &&
            string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Cannot cancel a kitchen ticket after the order is paid");
        }

        if (order is not null &&
            string.Equals(mappedStatus, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) &&
            !OrderStatuses.IsTerminal(order.Status))
        {
            var orderItems = await _db.OrderItems
                .Where(x => x.OrderId == order.OrderId)
                .Select(x => x.Status)
                .ToListAsync(ct);
            var anyDone = orderItems.Any(KitchenStatuses.IsDone);
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

        // C-10: history order khi bếp huỷ phải giữ lý do thật, không chỉ "Updated from kitchen".
        var historyNote = string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled from kitchen" : request.Reason.Trim())
            : "Updated from kitchen";
        var cancelReason = string.Equals(status, KitchenStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
            ? historyNote
            : request.Reason;

        // Đơn đã Paid/Cancelled thì vẫn cho cập nhật phiếu bếp (bếp cần đóng ticket), nhưng
        // tuyệt đối không ghi ngược trạng thái đơn — tránh "mở lại" đơn đã thanh toán/huỷ.
        if (order is not null && !OrderStatuses.IsTerminal(order.Status))
        {
            var changedBy = await _currentUser.ResolveExistingActorAsync(
                _db, request.ChangedBy, _logger, nameof(request.ChangedBy), ct, order.CreatedBy);

            if (string.Equals(mappedStatus, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, KitchenStatuses.Accepted, StringComparison.OrdinalIgnoreCase) ||
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
                if (KitchenStatuses.IsPending(item.Status) &&
                    KitchenStatuses.IsPending(orderItem.Status))
                {
                    var effectiveReasonCode = reasonCode ?? OrderItemMutations.CustomerChange;
                    if (OrderItemMutations.ReturnsStock(effectiveReasonCode))
                    {
                        var remaining = await OrderCancellation.LoadOrderRemainingAsync(_db, order.OrderId, ct);
                        await OrderCancellation.ReturnStockForItemAsync(
                            _db, order.OrderId, orderItem, remaining, changedBy, now, ct);
                    }
                    else
                    {
                        await OrderItemMutations.RecordWastageAsync(
                            _db,
                            order.OrderId,
                            orderItem,
                            orderItem.Quanlity,
                            changedBy,
                            now,
                            effectiveReasonCode,
                            string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                            ct);
                    }
                }
                else if (KitchenStatuses.IsPreparing(item.Status) ||
                         KitchenStatuses.IsPreparing(orderItem.Status))
                {
                    // C-13: món đã bắt đầu nấu ⇒ nguyên liệu đã tiêu thật ⇒ không hoàn kho,
                    // ghi hao hụt. Lý do huỷ vẫn nằm ở item history + OrderItem.CancelReason.
                    await OrderItemMutations.RecordWastageAsync(
                        _db,
                        order.OrderId,
                        orderItem,
                        orderItem.Quanlity,
                        changedBy,
                        now,
                        OrderItemMutations.KitchenError,
                        string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                        ct);
                }

                OrderCancellation.MarkItemCancelled(
                    orderItem,
                    now,
                    changedBy,
                    string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled from kitchen" : request.Reason.Trim());

                // Món còn lại vẫn phải được tính tiền; bỏ discount cũ vì cơ sở khuyến mãi đã đổi.
                // Món cuối bị huỷ thì giữ nguyên tổng lịch sử (đơn chuyển Cancelled bên dưới),
                // khớp quy tắc của OrderService.CancelItemAsync.
                var siblingItems = await _db.OrderItems
                    .Where(x => x.OrderId == order.OrderId && x.Id != orderItem.Id)
                    .ToListAsync(ct);
                var activeSiblings = siblingItems
                    .Where(x => !KitchenStatuses.IsCancelled(x.Status))
                    .ToList();
                if (activeSiblings.Count > 0)
                {
                    order.Subtotal = activeSiblings.Sum(x => x.UnitPrice * x.Quanlity);
                    order.DiscountAmount = 0m;
                    order.PromotionId = null;
                    order.PromotionDiscountType = null;
                    order.PromotionDiscountValue = null;
                    OrderTotalsCalculator.Apply(order, settings);
                    order.UpdatedAt = now;
                }
            }
        }

        item.Status = status;
        if (orderItem is not null)
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

        var kitchenItem = await _db.KitchenOrderItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (kitchenItem is null) return false;

        var kitchenOrder = await _db.KitchenOrders
            .FirstOrDefaultAsync(x => x.KitchenOrderId == kitchenItem.KitchenOrderId, ct);
        if (kitchenOrder is null) return false;

        var order = await _db.Orders
            .FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        if (KitchenStatuses.IsTerminal(kitchenOrder.Status))
            throw new InvalidOperationException(
                $"Kitchen order is {kitchenOrder.Status} and can no longer change status");

        if (!KitchenStatuses.IsDone(kitchenItem.Status))
            throw new InvalidOperationException(
                $"Only a Ready/Completed item can be reopened; current status is {kitchenItem.Status}");

        var orderItem = await _db.OrderItems
            .FirstOrDefaultAsync(x => x.Id == kitchenItem.OrderItemId, ct);
        if (orderItem is null) return false;

        if (KitchenStatuses.IsCancelled(orderItem.Status))
            throw new InvalidOperationException("Order item is already cancelled");

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

        var kitchenItem = await _db.KitchenOrderItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (kitchenItem is null) return false;

        var kitchenOrder = await _db.KitchenOrders
            .FirstOrDefaultAsync(x => x.KitchenOrderId == kitchenItem.KitchenOrderId, ct);
        if (kitchenOrder is null) return false;

        if (KitchenStatuses.IsTerminal(kitchenOrder.Status))
            throw new InvalidOperationException(
                $"Kitchen order is {kitchenOrder.Status} and can no longer change status");

        var order = await _db.Orders
            .FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        var orderItem = await _db.OrderItems
            .FirstOrDefaultAsync(x => x.Id == kitchenItem.OrderItemId, ct);
        if (orderItem is null) return false;

        if (KitchenStatuses.IsCancelled(orderItem.Status) ||
            KitchenStatuses.IsCancelled(kitchenItem.Status))
        {
            throw new InvalidOperationException("Order item is already cancelled");
        }

        if (KitchenStatuses.IsDone(kitchenItem.Status))
            throw new InvalidOperationException("Order item is already prepared");

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

        var preparedLine = OrderItemMutations.CloneLine(
            orderItem, request.PreparedQuantity, KitchenStatuses.Completed, now);
        _db.OrderItems.Add(preparedLine);
        await _db.SaveChangesAsync(ct);
        await OrderItemMutations.CopyToppingsAsync(_db, orderItem.Id, preparedLine.Id, ct);

        OrderItemMutations.RecordItemHistory(
            _db,
            preparedLine,
            null,
            KitchenStatuses.Pending,
            KitchenStatuses.Completed,
            changedBy,
            now,
            $"Prepared portion split from order item {orderItem.Id}",
            reasonCode: null);

        var preparedKitchenItem = new KitchenOrderItem
        {
            KitchenOrderId = kitchenOrder.KitchenOrderId,
            OrderItemId = preparedLine.Id,
            Quanlity = request.PreparedQuantity,
            Status = KitchenStatuses.Completed,
            Note = orderItem.Note
        };
        _db.KitchenOrderItems.Add(preparedKitchenItem);
        kitchenItem.Quanlity = remainingQuantity;

        await OrderItemMutations.SplitItemLedgerAsync(
            _db,
            orderItem,
            preparedLine,
            originalQuantity,
            request.PreparedQuantity,
            changedBy,
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

        var kitchenItem = await _db.KitchenOrderItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (kitchenItem is null) return false;

        var kitchenOrder = await _db.KitchenOrders
            .FirstOrDefaultAsync(x => x.KitchenOrderId == kitchenItem.KitchenOrderId, ct);
        if (kitchenOrder is null) return false;

        if (KitchenStatuses.IsTerminal(kitchenOrder.Status))
            throw new InvalidOperationException(
                $"Kitchen order is {kitchenOrder.Status} and can no longer change status");

        var order = await _db.Orders
            .FirstOrDefaultAsync(x => x.OrderId == kitchenOrder.OrderId, ct);
        if (order is null) return false;

        if (OrderStatuses.IsTerminal(order.Status))
            throw new InvalidOperationException(
                $"Order is {order.Status} and can no longer change status");

        var orderItem = await _db.OrderItems
            .FirstOrDefaultAsync(x => x.Id == kitchenItem.OrderItemId, ct);
        if (orderItem is null) return false;

        if (KitchenStatuses.IsCancelled(orderItem.Status) ||
            KitchenStatuses.IsCancelled(kitchenItem.Status))
        {
            throw new InvalidOperationException("Order item is already cancelled");
        }

        if (KitchenStatuses.IsDone(kitchenItem.Status))
            throw new InvalidOperationException("Order item is already prepared");

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

            var cancelledLine = OrderItemMutations.CloneLine(
                orderItem, cancelledQuantity, KitchenStatuses.Cancelled, now);
            _db.OrderItems.Add(cancelledLine);
            await _db.SaveChangesAsync(ct);
            await OrderItemMutations.CopyToppingsAsync(_db, orderItem.Id, cancelledLine.Id, ct);

            OrderItemMutations.RecordItemHistory(
                _db,
                cancelledLine,
                null,
                KitchenStatuses.Pending,
                KitchenStatuses.Cancelled,
                changedBy,
                now,
                detail ?? $"Shortage ({reasonCode}); cancelled {cancelledQuantity} portion(s)",
                reasonCode);

            OrderCancellation.MarkItemCancelled(
                cancelledLine, now, changedBy, detail ?? reasonCode);

            var cancelledKitchenItem = new KitchenOrderItem
            {
                KitchenOrderId = kitchenOrder.KitchenOrderId,
                OrderItemId = cancelledLine.Id,
                Quanlity = cancelledQuantity,
                Status = KitchenStatuses.Cancelled,
                Note = detail
            };
            _db.KitchenOrderItems.Add(cancelledKitchenItem);

            await OrderItemMutations.SplitItemLedgerAsync(
                _db,
                orderItem,
                cancelledLine,
                originalQuantity,
                cancelledQuantity,
                changedBy,
                ct);

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
            OrderItemMutations.RecalculateTotals(order, allItems, now, settings);
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

}
