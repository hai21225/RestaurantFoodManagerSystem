using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class InventoryService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(AppDbContext db, ICurrentUser currentUser, ILogger<InventoryService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<List<Inventory>> GetAllAsync(CancellationToken ct) =>
        _db.Inventories.AsNoTracking().OrderBy(x => x.IngredientId).ToListAsync(ct);

    public async Task<bool> AdjustAsync(int ingredientId, InventoryAdjustmentRequest r, CancellationToken ct)
    {
        var x = await _db.Inventories.FirstOrDefaultAsync(i => i.IngredientId == ingredientId, ct);
        if (x is null) return false;

        var before = x.Quanlity;
        if (before + r.Quanlity < 0) throw new InvalidOperationException("Inventory cannot become negative");

        x.Quanlity += r.Quanlity;
        x.UpdatedAt = DateTime.UtcNow;
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            IngredientId = ingredientId,
            Type = r.Quanlity >= 0 ? "AdjustmentIn" : "AdjustmentOut",
            Quanlity = Math.Abs(r.Quanlity),
            QuanlityBefore = before,
            QuanlityAfter = x.Quanlity,
            Note = r.Note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.ResolveActorId(r.CreatedBy, _logger, nameof(r.CreatedBy))
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Danh sách biến động kho cho <c>GET /api/management/inventory-transactions</c> (P1),
    /// mới nhất trước; lọc theo nguyên liệu/loại/tham chiếu/khoảng thời gian.
    /// </summary>
    public async Task<PagedResponse<InventoryTransactionResponse>> GetTransactionsAsync(InventoryTransactionQuery query, CancellationToken ct = default)
    {
        var skip = Paging.ResolveSkip(query.Page, query.PageSize);
        var type = string.IsNullOrWhiteSpace(query.Type) ? null : query.Type.Trim().ToLowerInvariant();
        var referenceType = string.IsNullOrWhiteSpace(query.ReferenceType) ? null : query.ReferenceType.Trim().ToLowerInvariant();
        var ingredientId = query.IngredientId;
        var referenceId = query.ReferenceId;
        var fromDate = query.From;
        var toDate = query.To;

        var filtered =
            from t in _db.InventoryTransactions.AsNoTracking()
            join i in _db.Ingredients.AsNoTracking() on t.IngredientId equals i.IngredientId into ingredients
            from i in ingredients.DefaultIfEmpty()
            where (ingredientId == null || t.IngredientId == ingredientId)
               && (type == null || t.Type.ToLower() == type)
               && (referenceType == null || (t.ReferenceType != null && t.ReferenceType.ToLower() == referenceType))
               && (referenceId == null || t.ReferenceId == referenceId)
               && (fromDate == null || t.CreatedAt >= fromDate)
               && (toDate == null || t.CreatedAt <= toDate)
            select new InventoryTransactionResponse
            {
                Id = t.Id,
                IngredientId = t.IngredientId,
                IngredientName = i != null ? i.Name : null,
                Type = t.Type,
                Quanlity = t.Quanlity,
                QuanlityBefore = t.QuanlityBefore,
                QuanlityAfter = t.QuanlityAfter,
                ReferenceType = t.ReferenceType,
                ReferenceId = t.ReferenceId,
                Note = t.Note,
                CreatedAt = t.CreatedAt,
                CreatedBy = t.CreatedBy
            };

        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResponse<InventoryTransactionResponse>
        {
            Page = query.Page,
            PageSize = query.PageSize,
            Total = total,
            Items = items
        };
    }
}
