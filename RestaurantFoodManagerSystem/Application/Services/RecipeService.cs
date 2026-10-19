using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class RecipeService
{
    private readonly AppDbContext _db;

    public RecipeService(AppDbContext db) => _db = db;

    public Task<List<Recipe>> GetAllAsync(CancellationToken ct) =>
        _db.Recipes.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);

    public async Task<Recipe> CreateAsync(RecipeRequest r, CancellationToken ct)
    {
        if (!await _db.FoodSizes.AnyAsync(x => x.SizeId == r.SizeId && x.IsActive, ct))
            throw new KeyNotFoundException("Food size not found");
        if (r.Ingredients.Count == 0)
            throw new ArgumentException("Recipe must contain at least one ingredient");

        var ids = r.Ingredients.Select(i => i.IngredientId).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw new ArgumentException("An ingredient cannot be repeated in the same recipe");

        var valid = await _db.Ingredients.CountAsync(i => ids.Contains(i.IngredientId) && i.IsActive, ct);
        if (valid != ids.Count) throw new KeyNotFoundException("One or more ingredients not found");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var x = new Recipe
        {
            SizeId = r.SizeId,
            Name = r.Name.Trim(),
            Version = r.Version.Trim(),
            IsActive = true
        };
        _db.Recipes.Add(x);
        await _db.SaveChangesAsync(ct);

        foreach (var i in r.Ingredients)
        {
            _db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = x.RecipeId,
                IngredientId = i.IngredientId,
                Quanlity = i.Quantity
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
                                     Quantity = ri.Quanlity
                                 }).ToListAsync(ct);

        return new RecipeResponse
        {
            RecipeId = recipe.RecipeId,
            SizeId = recipe.SizeId,
            Name = recipe.Name,
            Version = recipe.Version,
            IsActive = recipe.IsActive,
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

        var valid = await _db.Ingredients.CountAsync(i => ids.Contains(i.IngredientId) && i.IsActive, ct);
        if (valid != ids.Count) throw new KeyNotFoundException("One or more ingredients not found");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        recipe.SizeId = r.SizeId;
        recipe.Name = (r.Name ?? string.Empty).Trim();
        recipe.Version = string.IsNullOrWhiteSpace(r.Version) ? "1" : r.Version.Trim();

        var existing = await _db.RecipeIngredients
            .Where(x => x.RecipeId == id)
            .ToListAsync(ct);
        _db.RecipeIngredients.RemoveRange(existing);

        foreach (var i in r.Ingredients)
        {
            _db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = id,
                IngredientId = i.IngredientId,
                Quanlity = i.Quantity
            });
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
