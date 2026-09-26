using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Domain.AppDbContext;

public sealed class MenuService
{
    private readonly AppDbContext _db;
    public MenuService(AppDbContext db) => _db = db;

    public async Task<object> GetMenuAsync(CancellationToken ct = default)
    {
        var categories = await _db.Categories.AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.CategoryName).ToListAsync(ct);
        var foods = await _db.Foods.AsNoTracking().Where(f => f.IsActive && f.IsAvailable).ToListAsync(ct);
        var sizes = await _db.FoodSizes.AsNoTracking().Where(s => s.IsActive).ToListAsync(ct);
        var toppings = await _db.Toppings.AsNoTracking().Where(t => t.IsActive && t.IsAvailable).ToListAsync(ct);
        return categories.Select(c => new { c.CategoryId, c.CategoryName, c.CategoryDescription,
            Foods = foods.Where(f => f.CategoryId == c.CategoryId).Select(f => new { f.FoodId, f.FoodName, f.FoodDescription, f.FoodImageUrl, f.FoodPrice,
                Sizes = sizes.Where(s => s.FoodId == f.FoodId).Select(s => new { s.SizeId, s.SizeName, s.SizePrice, s.IsDefault }),
                Toppings = toppings.Where(t => _db.FoodToppings.Any(ft => ft.FoodId == f.FoodId && ft.ToppingId == t.ToppingId))
                    .Select(t => new { t.ToppingId, t.ToppingName, t.ToppingPrice }) }) });
    }
}
