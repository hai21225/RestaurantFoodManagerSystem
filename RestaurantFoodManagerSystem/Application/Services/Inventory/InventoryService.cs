using System.Data;
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
        // Defect #4: đọc/kiểm tra/ghi tồn kho phải nằm trong cùng transaction Serializable.
        // Nhờ vậy hai điều chỉnh đồng thời không cùng đọc một giá trị cũ rồi ghi đè lẫn nhau
        // (RowVersion vẫn là chốt chặn cuối ở tầng DB → 409 nếu xung đột).
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var x = await _db.Inventories.FirstOrDefaultAsync(i => i.IngredientId == ingredientId, ct);
        if (x is null) return false;

        // Cột lưu decimal(18,3) ⇒ làm tròn trước để ledger và tồn kho khớp tuyệt đối.
        var delta = Math.Round(r.Quanlity, 3, MidpointRounding.AwayFromZero);
        if (delta == 0) throw new ArgumentException("Adjustment quantity cannot be zero");

        var before = x.Quanlity;
        if (before + delta < 0) throw new InvalidOperationException("Inventory cannot become negative");

        x.Quanlity += delta;
        x.UpdatedAt = DateTime.UtcNow;
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            IngredientId = ingredientId,
            Type = delta > 0 ? "AdjustmentIn" : "AdjustmentOut",
            Quanlity = Math.Abs(delta),
            QuanlityBefore = before,
            QuanlityAfter = x.Quanlity,
            Note = r.Note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = await _currentUser.ResolveExistingActorAsync(
                _db, r.CreatedBy, _logger, nameof(r.CreatedBy), ct)
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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

        // ReferenceId là đa nghĩa (Order #7 và PurchaseOrder #7 khác nhau) ⇒ bắt buộc
        // có ReferenceType đi kèm, nếu không lọc sẽ trộn hai không gian id.
        if (referenceId.HasValue && referenceType is null)
        {
            throw new ArgumentException("referenceType is required when referenceId is provided");
        }

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
