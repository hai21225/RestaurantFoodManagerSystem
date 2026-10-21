using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class IngredientService
{
    private readonly AppDbContext _db;

    public IngredientService(AppDbContext db) => _db = db;

    public Task<List<Ingredient>> GetAllAsync(CancellationToken ct) =>
        _db.Ingredients.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public Task<Ingredient?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Ingredients.AsNoTracking().FirstOrDefaultAsync(x => x.IngredientId == id, ct);

    public async Task<Ingredient> CreateAsync(IngredientRequest r, CancellationToken ct)
    {
        var name = r.Name.Trim();

        // Nhóm B4: ingredient + dòng tồn kho phải là nguyên tử.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // P1: kiểm trùng trước để trả 409 thay vì để unique index ném DbUpdateException (500).
        await EnsureNameAvailableAsync(name, excludeIngredientId: null, ct);

        var x = new Ingredient
        {
            Name = name,
            Unit = r.Unit.Trim(),
            MinimumStock = r.MinimumStock,
            IsActive = true
        };
        _db.Ingredients.Add(x);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Chốt chặn cuối cho race: hai request cùng tên vượt qua pre-check.
            throw new InvalidOperationException($"Ingredient '{name}' already exists", ex);
        }

        _db.Inventories.Add(new Inventory
        {
            IngredientId = x.IngredientId,
            Quanlity = 0,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return x;
    }

    public async Task<bool> UpdateAsync(int id, IngredientRequest r, CancellationToken ct)
    {
        var x = await _db.Ingredients.FindAsync([id], ct);
        if (x is null) return false;

        var name = r.Name.Trim();
        await EnsureNameAvailableAsync(name, excludeIngredientId: id, ct);

        var unit = r.Unit.Trim();

        // C-17: đơn vị là phần của dữ liệu lịch sử, không sửa tự do được.
        // Đổi "kg" → "g" khi nguyên liệu đã có công thức / ledger kho / phiếu mua
        // sẽ làm mọi số lượng cũ bị hiểu sai (âm thầm sai cả báo cáo).
        if (!string.Equals(x.Unit, unit, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureUnitChangeAllowedAsync(x.IngredientId, x.Unit, unit, ct);
            x.Unit = unit;
        }
        // Khác chữ hoa/thường thì coi như không đổi: giữ nguyên chuỗi đã lưu để
        // ledger snapshot và nguyên liệu luôn khớp tuyệt đối.

        x.Name = name;
        x.MinimumStock = r.MinimumStock;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new InvalidOperationException($"Ingredient '{name}' already exists", ex);
        }
        return true;
    }

    /// <summary>
    /// C-17: chặn đổi đơn vị khi nguyên liệu đã có dữ liệu tham chiếu.
    ///
    /// <para>
    /// Cùng một con số lượng nhưng khác đơn vị là khác hàng nghĩa, nên đổi đơn vị sau khi
    /// đã dùng sẽ làm sai lịch sử. Ba nơi tham chiếu đơn vị:
    /// công thức (<c>recipe_ingredients</c> — quyết định trừ kho),
    /// ledger kho (<c>inventory_transactions</c> — lịch sử biến động) và
    /// phiếu mua (<c>purchase_order_items</c> — số lượng nhập về).
    /// </para>
    ///
    /// <para>
    /// Nguyên liệu chưa dùng ở đâu thì vẫn sửa được (chữa typo lúc mới tạo).
    /// Muốn đổi đơn vị sau khi đã dùng thì tạo nguyên liệu mới.
    /// </para>
    /// </summary>
    private async Task EnsureUnitChangeAllowedAsync(
        int ingredientId,
        string currentUnit,
        string newUnit,
        CancellationToken ct)
    {
        var usedInRecipe = await _db.RecipeIngredients
            .AsNoTracking()
            .AnyAsync(x => x.IngredientId == ingredientId, ct);
        var hasLedger = await _db.InventoryTransactions
            .AsNoTracking()
            .AnyAsync(x => x.IngredientId == ingredientId, ct);
        var hasPurchaseHistory = await _db.PurchaseOrderItems
            .AsNoTracking()
            .AnyAsync(x => x.IngredientId == ingredientId, ct);

        if (!usedInRecipe && !hasLedger && !hasPurchaseHistory)
        {
            return;
        }

        var reasons = new List<string>();
        if (usedInRecipe) reasons.Add("recipes");
        if (hasLedger) reasons.Add("stock history");
        if (hasPurchaseHistory) reasons.Add("purchase orders");

        throw new InvalidOperationException(
            $"Cannot change unit from '{currentUnit}' to '{newUnit}' because this ingredient "
            + $"is already used in {string.Join(", ", reasons)}. Create a new ingredient instead.");
    }

    /// <summary>
    /// P1: tên nguyên liệu đang hoạt động là duy nhất (unique index lọc <c>IsActive = 1</c>).
    /// Pre-check giúp API trả 409; unique index vẫn là chốt chặn cuối cho race.
    /// SQL Server dùng collation không phân biệt hoa/thường nên phép so sánh này cũng vậy.
    /// </summary>
    private async Task EnsureNameAvailableAsync(
        string name,
        int? excludeIngredientId,
        CancellationToken ct)
    {
        var duplicate = await _db.Ingredients
            .AsNoTracking()
            .AnyAsync(
                x => x.IsActive
                     && x.Name == name
                     && (!excludeIngredientId.HasValue || x.IngredientId != excludeIngredientId.Value),
                ct);

        if (duplicate)
        {
            throw new InvalidOperationException($"Ingredient '{name}' already exists");
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var x = await _db.Ingredients.FindAsync([id], ct);
        if (x is null) return false;

        x.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Khôi phục/tắt nguyên liệu bằng soft-delete. Giữ nguyên dòng tồn kho và lịch sử
    /// giao dịch để báo cáo không bị mất dữ liệu.
    /// </summary>
    public async Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var x = await _db.Ingredients.FindAsync([id], ct);
        if (x is null) return false;

        // P1: khôi phục nguyên liệu có thể đụng tên với một nguyên liệu đang hoạt động khác.
        if (active)
        {
            await EnsureNameAvailableAsync(x.Name, excludeIngredientId: id, ct);
        }

        x.IsActive = active;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new InvalidOperationException($"Ingredient '{x.Name}' already exists", ex);
        }
        return true;
    }

    /// <summary>
    /// Kho đánh dấu trạng thái đáp ứng của nguyên liệu (C-13): <c>Available</c> /
    /// <c>OutOfStock</c> / <c>Spoiled</c>. Nguyên liệu <b>không</b> bị xoá — chỉ đổi trạng
    /// thái để kho quản lý và làm minh chứng cho báo cáo.
    /// </summary>
    public async Task<bool> SetStockStatusAsync(int id, string? stockStatus, CancellationToken ct)
    {
        var status = IngredientStockStatuses.Normalize(stockStatus);

        var x = await _db.Ingredients.FindAsync([id], ct);
        if (x is null) return false;

        x.StockStatus = status;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// SQL Server: 2601 = duplicate key, 2627 = unique constraint. Chỉ map đúng lỗi trùng tên;
    /// các DbUpdateException khác (truncation, FK, deadlock) phải nổi lên để không bị báo sai.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
