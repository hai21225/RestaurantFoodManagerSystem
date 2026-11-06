using Microsoft.EntityFrameworkCore;

public sealed partial class OrderService
{
    /// <summary>
    /// Chi tiết đơn hàng cho <c>GET /api/orders/{id}</c> (Nhóm C/GĐ7).
    /// Bổ sung <c>items[]</c> (kèm tên món/cỡ) so với entity <c>Order</c> thô; mọi field cũ được giữ nguyên.
    /// </summary>
    public async Task<OrderDetailResponse?> GetAsync(int id, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == id, ct);
        if (order is null) return null;

        // LEFT JOIN: nếu món/size bị xoá cứng thì dòng món vẫn phải hiện trong chi tiết đơn
        // (thiếu món ⇒ POS hiển thị sai, tổng tiền lệch). (P0: GetAsync dùng INNER JOIN)
        var items = await (from oi in _db.OrderItems.AsNoTracking()
                           join fs in _db.FoodSizes.AsNoTracking() on oi.SizeId equals fs.SizeId into sizeGroup
                           from fs in sizeGroup.DefaultIfEmpty()
                           join f in _db.Foods.AsNoTracking() on fs.FoodId equals f.FoodId into foodGroup
                           from f in foodGroup.DefaultIfEmpty()
                           where oi.OrderId == id
                           orderby oi.Id
                           select new OrderItemDetailResponse
                           {
                               Id = oi.Id,
                               SizeId = oi.SizeId,
                               FoodName = f.FoodName ?? string.Empty,
                               SizeName = fs.SizeName ?? string.Empty,
                               Quanlity = oi.Quanlity,
                               UnitPrice = oi.UnitPrice,
                               Note = oi.Note,
                               Status = oi.Status,
                               OrderItemGroupId = oi.OrderItemGroupId,
                               CancelReason = oi.CancelReason,
                               CancelledAt = oi.CancelledAt,
                               CancelledBy = oi.CancelledBy
                           }).ToListAsync(ct);

        if (items.Count > 0)
        {
            var toppingsByItem = await LoadToppingsByItemIdAsync(items.Select(x => x.Id).ToList(), ct);
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
            ServiceChargeAmount = order.ServiceChargeAmount,
            TotalAmount = order.TotalAmount,
            PromotionId = order.PromotionId,
            PromotionDiscountType = order.PromotionDiscountType,
            PromotionDiscountValue = order.PromotionDiscountValue,
            Note = order.Note,
            CreatedBy = order.CreatedBy,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Items = items
        };
    }

    /// <summary>
    /// Hoá đơn gộp theo bàn — <c>GET /api/orders/table/{tableId}/bill</c> (Đợt 3 / B2).
    /// Trả <c>null</c> khi bàn không tồn tại hoặc không có đơn đang mở (controller → 404).
    /// </summary>
    public async Task<TableBillResponse?> GetTableBillAsync(int tableId, CancellationToken ct = default)
    {
        var table = await _db.RestaurantTables.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TableId == tableId, ct);
        if (table is null)
        {
            return null;
        }

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.TableId == tableId
                     && o.Status != OrderStatuses.Paid
                     && o.Status != OrderStatuses.Cancelled)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.OrderId)
            .ToListAsync(ct);

        if (orders.Count == 0)
        {
            return null;
        }

        var orderIds = orders.Select(x => x.OrderId).ToList();

        var itemRows = await (from oi in _db.OrderItems.AsNoTracking()
                              join fs in _db.FoodSizes.AsNoTracking() on oi.SizeId equals fs.SizeId into sizeGroup
                              from fs in sizeGroup.DefaultIfEmpty()
                              join f in _db.Foods.AsNoTracking() on fs.FoodId equals f.FoodId into foodGroup
                              from f in foodGroup.DefaultIfEmpty()
                              where orderIds.Contains(oi.OrderId)
                                && oi.Status != KitchenStatuses.Cancelled
                              select new
                              {
                                  oi.Id,
                                  oi.OrderId,
                                  oi.SizeId,
                                  FoodName = f.FoodName ?? string.Empty,
                                  SizeName = fs.SizeName ?? string.Empty,
                                  oi.Quanlity,
                                  oi.UnitPrice,
                                  oi.Note
                              }).ToListAsync(ct);

        var toppingsByItem = itemRows.Count == 0
            ? new Dictionary<int, List<OrderItemToppingDetailResponse>>()
            : await LoadToppingsByItemIdAsync(itemRows.Select(x => x.Id).ToList(), ct);

        static string BuildToppingKey(List<OrderItemToppingDetailResponse>? toppings) =>
            toppings is null || toppings.Count == 0
                ? string.Empty
                : string.Join("|", toppings
                    .OrderBy(t => t.ToppingId)
                    .ThenBy(t => t.UnitPrice)
                    .Select(t => $"{t.ToppingId}:{t.Quanlity}:{t.UnitPrice}"));

        // Gộp dòng: cùng món + cùng đơn giá + cùng ghi chú + cùng bộ topping thì gộp làm một dòng.
        var lines = itemRows
            .GroupBy(x => new
            {
                x.SizeId,
                x.UnitPrice,
                x.Note,
                ToppingKey = BuildToppingKey(toppingsByItem.GetValueOrDefault(x.Id))
            })
            .Select(g => new TableBillLineResponse
            {
                SizeId = g.Key.SizeId,
                FoodName = g.First().FoodName,
                SizeName = g.First().SizeName,
                Quanlity = g.Sum(x => x.Quanlity),
                UnitPrice = g.Key.UnitPrice,
                LineTotal = g.Sum(x => x.UnitPrice * x.Quanlity),
                Note = g.Key.Note,
                Toppings = toppingsByItem.GetValueOrDefault(g.First().Id) ?? []
            })
            .OrderBy(x => x.FoodName)
            .ThenBy(x => x.SizeName)
            .ToList();

        return new TableBillResponse
        {
            TableId = table.TableId,
            TableNumber = table.TableNumber,
            OrderIds = orderIds.ToArray(),
            OrderCount = orders.Count,
            Subtotal = orders.Sum(x => x.Subtotal),
            DiscountAmount = orders.Sum(x => x.DiscountAmount),
            TaxAmount = orders.Sum(x => x.TaxAmount),
            ServiceChargeAmount = orders.Sum(x => x.ServiceChargeAmount),
            TotalAmount = orders.Sum(x => x.TotalAmount),
            Items = lines,
            Notes = orders
                .Select(x => x.Note)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Orders = orders.Select(x => new TableBillOrderResponse
            {
                OrderId = x.OrderId,
                Status = x.Status,
                Subtotal = x.Subtotal,
                DiscountAmount = x.DiscountAmount,
                TotalAmount = x.TotalAmount,
                PromotionId = x.PromotionId,
                Note = x.Note,
                CreatedAt = x.CreatedAt
            }).ToList()
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
                ServiceChargeAmount = o.ServiceChargeAmount,
                TotalAmount = o.TotalAmount,
                ItemCount = _db.OrderItems.Count(x => x.OrderId == o.OrderId),
                Note = o.Note,
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

    /// <summary>
    /// Lịch sử trạng thái đơn cho <c>GET /api/orders/{id}/history</c> (P1), sắp xếp theo thời gian tăng dần.
    /// Trả <c>null</c> khi đơn không tồn tại để controller map thành 404 (đơn rỗng cũng là 200 `[]`).
    /// </summary>
    public async Task<List<OrderStatusHistoryResponse>?> GetHistoryAsync(int orderId, CancellationToken ct = default)
    {
        if (!await OrderExistsAsync(orderId, ct))
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
                ReasonCode = h.ReasonCode,
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
        if (!await OrderExistsAsync(orderId, ct))
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

    private Task<bool> OrderExistsAsync(int orderId, CancellationToken ct) =>
        _db.Orders.AsNoTracking().AnyAsync(x => x.OrderId == orderId, ct);

    private async Task<Dictionary<int, List<OrderItemToppingDetailResponse>>> LoadToppingsByItemIdAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken ct)
    {
        var toppingRows = await (
            from ot in _db.OrderItemToppings.AsNoTracking()
            join t in _db.Toppings.AsNoTracking() on ot.ToppingId equals t.ToppingId into toppingGroup
            from t in toppingGroup.DefaultIfEmpty()
            where itemIds.Contains(ot.OrderItemId)
            orderby ot.ToppingId, ot.Id
            select new
            {
                ot.OrderItemId,
                Dto = new OrderItemToppingDetailResponse
                {
                    Id = ot.Id,
                    ToppingId = ot.ToppingId,
                    ToppingName = t != null ? (t.ToppingName ?? string.Empty) : string.Empty,
                    Quanlity = ot.Quanlity,
                    UnitPrice = ot.UnitPrice
                }
            }).ToListAsync(ct);

        return toppingRows
            .GroupBy(x => x.OrderItemId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());
    }
}
