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

        // C-17: ledger ghi kèm đơn vị tính lúc phát sinh giao dịch.
        var unit = await _db.Ingredients
            .AsNoTracking()
            .Where(i => i.IngredientId == ingredientId)
            .Select(i => i.Unit)
            .FirstOrDefaultAsync(ct);

        // Cột lưu decimal(18,3) ⇒ làm tròn trước để ledger và tồn kho khớp tuyệt đối.
        var delta = Math.Round(r.Quanlity, 3, MidpointRounding.AwayFromZero);
        if (delta == 0) throw new ArgumentException("Adjustment quantity cannot be zero");

        var before = x.Quanlity;
        if (before + delta < 0) throw new InvalidOperationException("Inventory cannot become negative");

        // C-21: điều chỉnh tay BẮT BUỘC có lý do. Skill yêu cầu mọi biến động kho phải có
        // "reason"; không có thì không trả lời được "tồn giảm 3kg vì lý do gì" ⇒ báo cáo
        // biến động kho không truy được nguyên nhân.
        var note = r.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException(
                "Note (reason) is required for a manual stock adjustment");
        }

        x.Quanlity += delta;
        x.UpdatedAt = DateTime.UtcNow;
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            IngredientId = ingredientId,
            Type = delta > 0 ? "AdjustmentIn" : "AdjustmentOut",
            Quanlity = Math.Abs(delta),
            QuanlityBefore = before,
            QuanlityAfter = x.Quanlity,
            Unit = unit,
            // C-21: điều chỉnh tay không trỏ tới đơn/phiếu nào ⇒ dùng ReferenceType riêng
            // để báo cáo "biến động kho" lọc được nhóm này.
            ReferenceType = "ManualAdjustment",
            ReferenceId = null,
            Note = note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = await _currentUser.ResolveExistingActorAsync(
                _db, r.CreatedBy, _logger, nameof(r.CreatedBy), ct)
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Danh sách nguyên liệu **cần kho xử lý** — <c>GET /api/management/inventory/low-stock</c>
    /// (Đợt 3 / B4).
    ///
    /// <para>
    /// Trước Đợt 3, dashboard chỉ trả về <c>lowStockCount</c> (một con số) và
    /// <c>GET /management/inventory</c> không có <c>minimumStock</c> ⇒ frontend phải tự ghép
    /// 2 danh sách (tồn kho + nguyên liệu) để biết cái nào sắp hết. Nay server trả thẳng
    /// danh sách, kèm mức tối thiểu và số lượng còn thiếu.
    /// </para>
    ///
    /// <para>
    /// C-21: gồm cả nguyên liệu bị bếp đánh dấu <c>OutOfStock</c> / <c>Spoiled</c> **dù tồn
    /// vẫn còn nhiều** — trước đây kho không nhìn thấy tín hiệu này ở đâu cả (chỉ thấy được
    /// qua <c>GET /api/management/ingredients</c>). Mỗi dòng có <c>stockStatus</c> để FE
    /// phân biệt "sắp hết" với "hư/hết hạn".
    /// </para>
    ///
    /// <para>Sắp xếp theo số lượng thiếu **giảm dần** (gấp nhất trước) rồi theo tên.</para>
    /// </summary>
    public async Task<List<LowStockItemResponse>> GetLowStockAsync(CancellationToken ct = default)
    {
        var rows = await (
            from inventory in _db.Inventories.AsNoTracking()
            join ingredient in _db.Ingredients.AsNoTracking() on inventory.IngredientId equals ingredient.IngredientId
            where ingredient.IsActive
               && (inventory.Quanlity <= ingredient.MinimumStock
                   || ingredient.StockStatus != IngredientStockStatuses.Available)
            select new LowStockItemResponse
            {
                IngredientId = ingredient.IngredientId,
                IngredientName = ingredient.Name,
                Unit = ingredient.Unit,
                Quanlity = inventory.Quanlity,
                MinimumStock = ingredient.MinimumStock,
                Shortage = ingredient.MinimumStock - inventory.Quanlity,
                IsOutOfStock = inventory.Quanlity <= 0,
                StockStatus = ingredient.StockStatus
            }).ToListAsync(ct);

        return rows
            .OrderByDescending(x => x.Shortage)
            .ThenBy(x => x.IngredientName, StringComparer.Ordinal)
            .ToList();
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
                // C-17: ưu tiên snapshot ghi kèm lúc phát sinh; dòng cũ chưa có thì
                // lấy đơn vị hiện tại của nguyên liệu.
                Unit = t.Unit ?? (i != null ? i.Unit : null),
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
