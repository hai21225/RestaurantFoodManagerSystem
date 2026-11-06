using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class MenuService
{
    private readonly AppDbContext _db;
    public MenuService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<object> GetMenuAsync(CancellationToken ct = default)
    {
        var categories = await _db.Categories.AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.CategoryName).ToListAsync(ct);
        var foods = await _db.Foods.AsNoTracking().Where(f => f.IsActive && f.IsAvailable).ToListAsync(ct);
        var sizes = await _db.FoodSizes.AsNoTracking()
            .Where(s => s.IsActive && _db.Recipes.Any(r => r.SizeId == s.SizeId && r.IsActive))
            .ToListAsync(ct);
        var sizesByFood = sizes
            .GroupBy(s => s.FoodId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var toppings = await _db.Toppings.AsNoTracking().Where(t => t.IsActive && t.IsAvailable).ToListAsync(ct);


        var toppingLinks = await _db.FoodToppings.AsNoTracking()
            .Select(ft => new { ft.FoodId, ft.ToppingId })
            .ToListAsync(ct);
        var toppingIdsByFood = toppingLinks
            .GroupBy(x => x.FoodId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ToppingId).ToHashSet());

        return categories.Select(c => new { c.CategoryId, c.CategoryName, c.CategoryDescription,
            Foods = foods.Where(f => f.CategoryId == c.CategoryId && sizesByFood.ContainsKey(f.FoodId)).Select(f => new { f.FoodId, f.FoodName, f.FoodDescription, f.FoodImageUrl, f.FoodPrice,
                Sizes = sizesByFood[f.FoodId].Select(s => new { s.SizeId, s.SizeName, s.SizePrice, s.IsDefault }),
                Toppings = toppings.Where(t =>
                        toppingIdsByFood.TryGetValue(f.FoodId, out var linkedIds) && linkedIds.Contains(t.ToppingId))
                    .Select(t => new { t.ToppingId, t.ToppingName, t.ToppingPrice }) }) });
    }
}
