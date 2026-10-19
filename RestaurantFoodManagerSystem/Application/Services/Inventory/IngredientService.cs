using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class IngredientService
{
    private readonly AppDbContext _db;

    public IngredientService(AppDbContext db) => _db = db;

    public Task<List<Ingredient>> GetAllAsync(CancellationToken ct) =>
        _db.Ingredients.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<Ingredient> CreateAsync(IngredientRequest r, CancellationToken ct)
    {
        var x = new Ingredient
        {
            Name = r.Name.Trim(),
            Unit = r.Unit.Trim(),
            MinimumStock = r.MinimumStock,
            IsActive = true
        };
        _db.Ingredients.Add(x);
        await _db.SaveChangesAsync(ct);

        _db.Inventories.Add(new Inventory
        {
            IngredientId = x.IngredientId,
            Quanlity = 0,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return x;
    }

    public async Task<bool> UpdateAsync(int id, IngredientRequest r, CancellationToken ct)
    {
        var x = await _db.Ingredients.FindAsync([id], ct);
        if (x is null) return false;

        x.Name = r.Name.Trim();
        x.Unit = r.Unit.Trim();
        x.MinimumStock = r.MinimumStock;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var x = await _db.Ingredients.FindAsync([id], ct);
        if (x is null) return false;

        x.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
