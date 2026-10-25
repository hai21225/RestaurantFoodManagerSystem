using System.Globalization;
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
    ///
    /// <para>
    /// <b>C-19 (gap-review C3): sửa công thức = tạo phiên bản mới, không ghi đè bản cũ.</b>
    /// Bản cũ được chuyển sang <c>IsActive = false</c> (giữ nguyên định lượng để làm
    /// minh chứng lịch sử) và một bản mới <c>IsActive = true</c> được tạo. Unique index
    /// <c>(SizeId) WHERE IsActive = 1</c> đã có sẵn nên không cần đổi schema, và luôn
    /// bảo đảm chỉ có đúng một bản đang chạy cho mỗi size.
    /// </para>
    ///
    /// <para>
    /// Phiên bản: nếu client gửi <b>đúng** version đang chạy thì server **tự tăng**
    /// (1 → 2) — tránh tạo hai bản trùng tên phiên bản. Nếu client gửi version khác
    /// thì giữ nguyên version đó.
    /// </para>
    /// </summary>
    public async Task<bool> UpdateAsync(int id, RecipeRequest r, CancellationToken ct)
    {
        var recipe = await _db.Recipes.FirstOrDefaultAsync(x => x.RecipeId == id, ct);
        if (recipe is null)
        {
            return false;
        }

        // C-19: chỉ sửa được bản đang chạy. Bản đã bị thay thế thì phải sửa bản mới nhất.
        if (!recipe.IsActive)
        {
            throw new InvalidOperationException(
                "This recipe version is no longer active; update the current version instead");
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

        // Serializable: đọc–ghi–ghi phải nguyên tử, nếu không sẽ có lúc size không còn
        // công thức đang chạy (⇒ trừ kho sai) hoặc hai bản cùng active.
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        // Bước 1: chuyển bản cũ sang không hoạt động. SaveChanges riêng để lệnh UPDATE
        // chạy TRƯỚC lệnh INSERT của bản mới — unique index (SizeId) WHERE IsActive=1
        // sẽ chặn INSERT nếu bản cũ chưa được chuyển sang inactive.
        recipe.IsActive = false;
        await _db.SaveChangesAsync(ct);

        // Bước 2: tạo bản mới.
        var version = ResolveVersion(r.Version, recipe.Version);
        var created = new Recipe
        {
            SizeId = r.SizeId,
            Name = (r.Name ?? string.Empty).Trim(),
            Version = version,
            IsActive = true
        };
        _db.Recipes.Add(created);
        await _db.SaveChangesAsync(ct);

        foreach (var i in r.Ingredients)
        {
            _db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = created.RecipeId,
                IngredientId = i.IngredientId,
                Quanlity = i.Quanlity
            });
        }
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// C-19: chốt phiên bản của bản công thức mới.
    /// Client gửi version rỗng hoặc **đúng bằng** version đang chạy ⇒ server tự tăng
    /// (1 → 2) để không có hai bản cùng tên phiên bản. Ngược lại giữ nguyên version client gửi.
    /// </summary>
    private static string ResolveVersion(string? requested, string currentVersion)
    {
        var trimmed = (requested ?? string.Empty).Trim();

        if (trimmed.Length == 0
            || string.Equals(trimmed, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return NextVersion(currentVersion);
        }

        return trimmed;
    }

    /// <summary>Phiên bản kế tiếp: "1" → "2"; "1.5" → "1.5-2" (giới hạn 50 ký tự của cột).</summary>
    private static string NextVersion(string currentVersion)
    {
        var trimmed = (currentVersion ?? string.Empty).Trim();

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return (number + 1).ToString(CultureInfo.InvariantCulture);
        }

        return trimmed.Length >= 48 ? trimmed : trimmed + "-2";
    }
}
