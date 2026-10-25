using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class PurchaseOrderService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PurchaseOrderService> _logger;

    public PurchaseOrderService(AppDbContext db, ICurrentUser currentUser, ILogger<PurchaseOrderService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<List<PurchaseOrder>> GetAllAsync(CancellationToken ct) =>
        _db.PurchaseOrders.AsNoTracking().OrderByDescending(x => x.OrderedAt).ToListAsync(ct);

    /// <summary>
    /// Chi tiết phiếu nhập kèm tên nhà cung cấp và các dòng hàng (Batch A).
    /// Trả <c>null</c> khi phiếu không tồn tại để controller map thành 404.
    /// </summary>
    public async Task<PurchaseOrderDetailResponse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var detail = await (
            from p in _db.PurchaseOrders.AsNoTracking()
            join s in _db.Suppliers.AsNoTracking() on p.SupplierId equals s.SupplierId into suppliers
            from s in suppliers.DefaultIfEmpty()
            where p.Id == id
            select new PurchaseOrderDetailResponse
            {
                Id = p.Id,
                SupplierId = p.SupplierId,
                SupplierName = s != null ? s.Name : string.Empty,
                Status = p.Status,
                TotalAmount = p.TotalAmount,
                OrderedAt = p.OrderedAt,
                ReceivedAt = p.ReceivedAt,
                CreatedBy = p.CreatedBy
            }).FirstOrDefaultAsync(ct);

        if (detail is null)
        {
            return null;
        }

        detail.Items = await (
            from i in _db.PurchaseOrderItems.AsNoTracking()
            join ing in _db.Ingredients.AsNoTracking() on i.IngredientId equals ing.IngredientId into ingredients
            from ing in ingredients.DefaultIfEmpty()
            where i.PurchaseOrderId == id
            orderby i.Id
            select new PurchaseOrderItemResponse
            {
                Id = i.Id,
                IngredientId = i.IngredientId,
                IngredientName = ing != null ? ing.Name : string.Empty,
                Unit = ing != null ? ing.Unit : string.Empty,
                Quanlity = i.Quanlity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.TotalPrice
            }).ToListAsync(ct);

        return detail;
    }

    /// <summary>
    /// Huỷ phiếu nhập (Batch A). Chỉ phiếu chưa nhận hàng mới huỷ được; gọi lại trên phiếu
    /// đã huỷ là idempotent. Phiếu đã nhận giữ nguyên lịch sử kho nên không được huỷ.
    /// </summary>
    public async Task<bool> CancelAsync(int id, CancellationToken ct = default)
    {
        // Serializable để cancel và receive trên cùng phiếu không chen vào nhau:
        // một bên thấy trạng thái mới nhất sau khi bên kia commit, thay vì cùng đọc "Draft".
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var p = await _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null)
        {
            return false;
        }

        if (p.Status.Equals("Received", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Received purchase order cannot be cancelled");
        }

        if (p.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            await tx.CommitAsync(ct);
            return true;
        }

        p.Status = "Cancelled";
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<PurchaseOrder> CreateAsync(PurchaseOrderRequest r, CancellationToken ct)
    {
        // Batch A2 (15.5): JSON `items: null` lọt qua model binding; `[]` tạo phiếu 0 dòng vô nghĩa.
        if (r.Items is null || r.Items.Count == 0)
            throw new ArgumentException("Purchase order must contain at least one item");

        if (!await _db.Suppliers.AnyAsync(x => x.SupplierId == r.SupplierId && x.IsActive, ct))
            throw new KeyNotFoundException("Supplier not found");

        // Nhóm B4: purchase order + các dòng phải là nguyên tử.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var p = new PurchaseOrder
        {
            SupplierId = r.SupplierId,
            CreatedBy = 0,
            Status = "Draft",
            OrderedAt = DateTime.UtcNow
        };

        // Tính tròn TIỀN DÒNG một lần rồi dùng lại cho cả TotalPrice lẫn TotalAmount.
        // Nếu không, DB lưu TotalPrice/decimal(18,2) đã tròn còn TotalAmount thì chưa
        // ⇒ tổng phiếu lệch với tổng dòng hàng.
        var lines = new List<(int IngredientId, decimal Quanlity, decimal UnitPrice, decimal TotalPrice)>();

        foreach (var i in r.Items)
        {
            if (!await _db.Ingredients.AnyAsync(x => x.IngredientId == i.IngredientId && x.IsActive, ct))
                throw new KeyNotFoundException("Ingredient not found");

            var lineTotal = Math.Round(i.Quanlity * i.UnitPrice, 2, MidpointRounding.AwayFromZero);
            lines.Add((i.IngredientId, i.Quanlity, i.UnitPrice, lineTotal));
            p.TotalAmount += lineTotal;
        }

        p.TotalAmount = Math.Round(p.TotalAmount, 2, MidpointRounding.AwayFromZero);
        p.CreatedBy = await _currentUser.ResolveExistingActorAsync(
            _db, r.CreatedBy, _logger, nameof(r.CreatedBy), ct);

        _db.PurchaseOrders.Add(p);
        await _db.SaveChangesAsync(ct);

        foreach (var line in lines)
        {
            _db.PurchaseOrderItems.Add(new PurchaseOrderItem
            {
                PurchaseOrderId = p.Id,
                IngredientId = line.IngredientId,
                Quanlity = line.Quanlity,
                UnitPrice = line.UnitPrice,
                TotalPrice = line.TotalPrice
            });
        }
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return p;
    }

    public async Task<bool> ReceiveAsync(int id, ReceivePurchaseRequest r, CancellationToken ct)
    {
        // Defect #5: đọc trạng thái phiếu + cộng kho phải nằm trong cùng transaction Serializable.
        // Nhờ vậy hai lần receive đồng thời được tuần tự hoá: lần sau thấy "Received" và trả false,
        // thay vì cả hai cùng thấy "Draft" rồi cộng kho hai lần.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var p = await _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return false;

        // Phiếu đã huỷ không được cộng kho; phiếu đã nhận vẫn trả false như baseline
        // (controller map thành 404) để không cộng kho hai lần.
        if (p.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cancelled purchase order cannot be received");
        if (p.Status.Equals("Received", StringComparison.OrdinalIgnoreCase))
            return false;

        // Khoá các dòng tồn kho theo một thứ tự cố định để hai phiếu nhập chung nguyên liệu
        // không tạo vòng chờ chéo nhau (deadlock) khi chạy Serializable.
        var items = await _db.PurchaseOrderItems
            .Where(x => x.PurchaseOrderId == id)
            .OrderBy(x => x.IngredientId)
            .ToListAsync(ct);

        var receivedBy = await _currentUser.ResolveExistingActorAsync(
            _db, r.ReceivedBy, _logger, nameof(r.ReceivedBy), ct);

        // C-21: tên nhà cung cấp để ghi vào note của dòng ledger (truy nguồn gốc khi nhập kho).
        var supplierName = await _db.Suppliers
            .AsNoTracking()
            .Where(s => s.SupplierId == p.SupplierId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);

        // C-17: ledger ghi kèm đơn vị tính của nguyên liệu lúc nhập kho.
        var units = await IngredientUnits.LoadAsync(
            _db, items.Select(x => x.IngredientId), ct);

        foreach (var item in items)
        {
            // Defect #26: nguyên liệu thiếu dòng tồn kho là lỗi dữ liệu nghiệp vụ,
            // không để FirstAsync ném unhandled → 500.
            var stock = await _db.Inventories.FirstOrDefaultAsync(x => x.IngredientId == item.IngredientId, ct)
                ?? throw new InvalidOperationException($"No inventory record exists for ingredient {item.IngredientId}");
            var before = stock.Quanlity;
            stock.Quanlity += item.Quanlity;
            stock.UpdatedAt = DateTime.UtcNow;
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                IngredientId = item.IngredientId,
                Type = "Purchase",
                Quanlity = item.Quanlity,
                QuanlityBefore = before,
                QuanlityAfter = stock.Quanlity,
                Unit = units.GetValueOrDefault(item.IngredientId),
                ReferenceType = "PurchaseOrder",
                ReferenceId = id,
                // C-21: dòng nhập phiếu mua trước đây KHÔNG có note ⇒ báo cáo "biến động
                // kho" không truy được nguồn gốc. Ghi rõ phiếu mua + nhà cung cấp.
                Note = $"Nhập phiếu mua #{id}"
                       + (supplierName is null ? "" : $" từ {supplierName}"),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = receivedBy
            });
        }

        p.Status = "Received";
        p.ReceivedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
