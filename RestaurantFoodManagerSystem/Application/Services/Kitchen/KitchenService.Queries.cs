using Microsoft.EntityFrameworkCore;

public sealed partial class KitchenService
{
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
        // C-21: `since` phải bắt được cả thay đổi trạng thái MÓN, không chỉ mốc của phiếu.
        if (since.HasValue)
        {
            var mark = since.Value;

            var byTicketMark = rows
                .Where(r => r.ko.SentAt > mark
                         || (r.ko.StartedAt.HasValue && r.ko.StartedAt.Value > mark)
                         || (r.ko.CompletedAt.HasValue && r.ko.CompletedAt.Value > mark))
                .Select(r => r.ko.KitchenOrderId)
                .ToHashSet();

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
}
