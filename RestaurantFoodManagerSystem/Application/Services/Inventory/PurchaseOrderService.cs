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

    public async Task<PurchaseOrder> CreateAsync(PurchaseOrderRequest r, CancellationToken ct)
    {
        if (!await _db.Suppliers.AnyAsync(x => x.SupplierId == r.SupplierId && x.IsActive, ct))
            throw new KeyNotFoundException("Supplier not found");

        // Nhóm B4: purchase order + các dòng phải là nguyên tử.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var p = new PurchaseOrder
        {
            SupplierId = r.SupplierId,
            CreatedBy = _currentUser.ResolveActorId(r.CreatedBy, _logger, nameof(r.CreatedBy)),
            Status = "Draft",
            OrderedAt = DateTime.UtcNow
        };

        foreach (var i in r.Items)
        {
            if (!await _db.Ingredients.AnyAsync(x => x.IngredientId == i.IngredientId && x.IsActive, ct))
                throw new KeyNotFoundException("Ingredient not found");
            p.TotalAmount += i.Quantity * i.UnitPrice;
        }

        _db.PurchaseOrders.Add(p);
        await _db.SaveChangesAsync(ct);

        foreach (var i in r.Items)
        {
            _db.PurchaseOrderItems.Add(new PurchaseOrderItem
            {
                PurchaseOrderId = p.Id,
                IngredientId = i.IngredientId,
                Quanlity = i.Quantity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.Quantity * i.UnitPrice
            });
        }
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return p;
    }

    public async Task<bool> ReceiveAsync(int id, ReceivePurchaseRequest r, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var p = await _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null || p.Status == "Received") return false;

        var items = await _db.PurchaseOrderItems.Where(x => x.PurchaseOrderId == id).ToListAsync(ct);
        foreach (var item in items)
        {
            var stock = await _db.Inventories.FirstAsync(x => x.IngredientId == item.IngredientId, ct);
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
                ReferenceType = "PurchaseOrder",
                ReferenceId = id,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.ResolveActorId(r.ReceivedBy, _logger, nameof(r.ReceivedBy))
            });
        }

        p.Status = "Received";
        p.ReceivedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
