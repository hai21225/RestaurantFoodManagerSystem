using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class RecipeService
{
    private readonly AppDbContext _db;

    public RecipeService(AppDbContext db) => _db = db;

    /// <summary>
    /// Danh sách công thức (Nhóm C/GĐ7). Mặc định chỉ trả công thức đang hoạt động;
    /// truyền <paramref name="includeInactive"/> = true để quản trị viên xem cả công thức
    /// đã soft-delete và khôi phục.
    /// </summary>
    public Task<List<RecipeSummaryResponse>> GetAllAsync(CancellationToken ct = default) =>
        GetAllAsync(includeInactive: false, ct);

    public Task<List<RecipeSummaryResponse>> GetAllAsync(bool includeInactive, CancellationToken ct) =>
        (from r in _db.Recipes.AsNoTracking()
         where includeInactive || r.IsActive
         select new RecipeSummaryResponse
         {
             RecipeId = r.RecipeId,
             SizeId = r.SizeId,
             Name = r.Name,
             Version = r.Version,
             IsActive = r.IsActive,
             IngredientCount = _db.RecipeIngredients.Count(ri => ri.RecipeId == r.RecipeId)
         }).ToListAsync(ct);

    public async Task<Recipe> CreateAsync(RecipeRequest r, CancellationToken ct)
    {
        if (!await _db.FoodSizes.AnyAsync(x => x.SizeId == r.SizeId && x.IsActive, ct))
            throw new KeyNotFoundException("Food size not found");
        if (r.Ingredients is null || r.Ingredients.Count == 0)
            throw new ArgumentException("Recipe must contain at least one ingredient");

        var ids = r.Ingredients.Select(i => i.IngredientId).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw new ArgumentException("An ingredient cannot be repeated in the same recipe");

        if (r.Ingredients.Any(i => i.Quanlity <= 0))
            throw new ArgumentException("Ingredient quantity must be greater than zero");

        var valid = await _db.Ingredients.CountAsync(i => ids.Contains(i.IngredientId) && i.IsActive, ct);
        if (valid != ids.Count) throw new KeyNotFoundException("One or more ingredients not found");

        // Nhóm B5: mỗi size chỉ có một công thức đang hoạt động.
        if (await _db.Recipes.AnyAsync(x => x.SizeId == r.SizeId && x.IsActive, ct))
            throw new InvalidOperationException("This size already has an active recipe");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var x = new Recipe
        {
            SizeId = r.SizeId,
            Name = (r.Name ?? string.Empty).Trim(),
            Version = string.IsNullOrWhiteSpace(r.Version) ? "1" : r.Version.Trim(),
            IsActive = true
        };
        _db.Recipes.Add(x);

        try
        {
            // Phải lưu recipe trước để có RecipeId cho các dòng nguyên liệu.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            // Unique index lọc (recipes.SizeId WHERE IsActive=1) là chốt chặn cuối khi
            // hai request tạo công thức cho cùng size chạy đồng thời.
            if (await _db.Recipes.AsNoTracking().AnyAsync(x => x.SizeId == r.SizeId && x.IsActive, ct))
                throw new InvalidOperationException("This size already has an active recipe");

            throw;
        }

        foreach (var i in r.Ingredients)
        {
            _db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = x.RecipeId,
                IngredientId = i.IngredientId,
                Quanlity = i.Quanlity
            });
        }
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return x;
    }

    public async Task<RecipeResponse?> GetDetailsAsync(int id, CancellationToken ct)
    {
        var recipe = await _db.Recipes.AsNoTracking().FirstOrDefaultAsync(x => x.RecipeId == id, ct);
        if (recipe is null) return null;

        var ingredients = await (from ri in _db.RecipeIngredients
                                 join i in _db.Ingredients on ri.IngredientId equals i.IngredientId
                                 where ri.RecipeId == id
                                 select new RecipeIngredientResponse
                                 {
                                     IngredientId = i.IngredientId,
                                     IngredientName = i.Name,
                                     Unit = i.Unit,
                                     Quanlity = ri.Quanlity
                                 }).ToListAsync(ct);

        // Nhóm C/GĐ7: join tên món/cỡ để tiêu đề chi tiết công thức hiển thị đúng.
        var sizeInfo = await (from fs in _db.FoodSizes.AsNoTracking()
                              join f in _db.Foods.AsNoTracking() on fs.FoodId equals f.FoodId
                              where fs.SizeId == recipe.SizeId
                              select new { f.FoodName, fs.SizeName }).FirstOrDefaultAsync(ct);

        return new RecipeResponse
        {
            RecipeId = recipe.RecipeId,
            SizeId = recipe.SizeId,
            Name = recipe.Name,
            Version = recipe.Version,
            IsActive = recipe.IsActive,
            FoodName = sizeInfo?.FoodName ?? string.Empty,
            SizeName = sizeInfo?.SizeName ?? string.Empty,
            Ingredients = ingredients
        };
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var x = await _db.Recipes.FindAsync([id], ct);
        if (x is null) return false;

        x.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Khôi phục/tắt công thức bằng soft-delete; định lượng nguyên liệu vẫn được giữ
    /// để lịch sử và báo cáo không bị ảnh hưởng.
    /// </summary>
    public async Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var x = await _db.Recipes.FindAsync([id], ct);
        if (x is null) return false;

        if (active &&
            await _db.Recipes.AnyAsync(r =>
                r.SizeId == x.SizeId &&
                r.RecipeId != id &&
                r.IsActive, ct))
        {
            throw new InvalidOperationException("This size already has an active recipe");
        }

        x.IsActive = active;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Cập nhật công thức (GĐ6/A3) cho <c>PUT /api/management/recipes/{id}</c>.
    /// Thay toàn bộ định lượng trong một transaction. Trả <c>false</c> nếu không tìm thấy công thức.
    /// </summary>
    public async Task<bool> UpdateAsync(int id, RecipeRequest r, CancellationToken ct)
    {
        var recipe = await _db.Recipes.FirstOrDefaultAsync(x => x.RecipeId == id, ct);
        if (recipe is null)
        {
            return false;
        }

        if (!await _db.FoodSizes.AnyAsync(x => x.SizeId == r.SizeId && x.IsActive, ct))
            throw new KeyNotFoundException("Food size not found");
        if (r.Ingredients is null || r.Ingredients.Count == 0)
            throw new ArgumentException("Recipe must contain at least one ingredient");

        var ids = r.Ingredients.Select(i => i.IngredientId).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw new ArgumentException("An ingredient cannot be repeated in the same recipe");

        if (r.Ingredients.Any(i => i.Quanlity <= 0))
            throw new ArgumentException("Ingredient quantity must be greater than zero");

        var valid = await _db.Ingredients.CountAsync(i => ids.Contains(i.IngredientId) && i.IsActive, ct);
        if (valid != ids.Count) throw new KeyNotFoundException("One or more ingredients not found");

        // Nhóm B5: không cho chuyển sang size đã có công thức đang hoạt động khác.
        if (await _db.Recipes.AnyAsync(x => x.SizeId == r.SizeId && x.IsActive && x.RecipeId != id, ct))
            throw new InvalidOperationException("This size already has an active recipe");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        recipe.SizeId = r.SizeId;
        recipe.Name = (r.Name ?? string.Empty).Trim();
        recipe.Version = string.IsNullOrWhiteSpace(r.Version) ? "1" : r.Version.Trim();

        var existing = await _db.RecipeIngredients
            .Where(x => x.RecipeId == id)
            .ToListAsync(ct);

        // Cập nhật theo chênh lệch thay vì xoá rồi chèn lại: xoá/insert cùng khóa
        // (RecipeId, IngredientId) trong một SaveChanges phụ thuộc thứ tự lệnh của EF.
        var requested = r.Ingredients.ToDictionary(i => i.IngredientId, i => i.Quanlity);

        foreach (var row in existing)
        {
            if (requested.Remove(row.IngredientId, out var quantity))
            {
                row.Quanlity = quantity;
            }
            else
            {
                _db.RecipeIngredients.Remove(row);
            }
        }

        foreach (var (ingredientId, quantity) in requested)
        {
            _db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = id,
                IngredientId = ingredientId,
                Quanlity = quantity
            });
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
