using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class FoodToppingService
{
    private readonly AppDbContext _db;

    public FoodToppingService(AppDbContext db)
    {
        _db = db;
    }


    // ============================
    // GET TOPPINGS OF FOOD
    // ============================

    public async Task<List<ToppingResponseDto>>
        GetToppingsByFoodIdAsync(int foodId, CancellationToken ct = default)
    {
        var food = await _db.Foods.FindAsync([foodId], ct);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var foodToppings = await _db.FoodToppings
            .AsNoTracking()
            .Where(x => x.FoodId == foodId)
            .ToListAsync(ct);

        if (!foodToppings.Any())
        {
            return new List<ToppingResponseDto>();
        }

        var toppingIds = foodToppings
            .Select(x => x.ToppingId)
            .ToList();

        var toppings = await _db.Toppings
            .AsNoTracking()
            .Where(x =>
                toppingIds.Contains(x.ToppingId) &&
                x.IsActive)
            .ToListAsync(ct);

        return toppings.Select(x => new ToppingResponseDto
        {
            ToppingId = x.ToppingId,
            ToppingName = x.ToppingName,
            ToppingPrice = x.ToppingPrice,
            IsActive = x.IsActive,
            IsAvailable = x.IsAvailable

        }).ToList();
    }


    // ============================
    // ASSIGN
    // ============================

    public async Task<bool> AssignAsync(
        int foodId,
        int toppingId,
        CancellationToken ct = default)
    {
        var food = await _db.Foods.FindAsync([foodId], ct);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var topping = await _db.Toppings
            .FindAsync([toppingId], ct);

        if (topping == null || !topping.IsActive)
        {
            throw new Exception("Topping not found");
        }

        var exists = await _db.FoodToppings.AnyAsync(x =>
            x.FoodId == foodId &&
            x.ToppingId == toppingId, ct);

        if (exists)
        {
            throw new Exception(
                "Topping already assigned to this food"
            );
        }

        var foodTopping = new FoodTopping
        {
            FoodId = foodId,
            ToppingId = toppingId
        };

        await _db.FoodToppings.AddAsync(foodTopping, ct);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    // ============================
    // REMOVE
    // ============================

    public async Task<bool> RemoveAsync(
        int foodId,
        int toppingId,
        CancellationToken ct = default)
    {
        var foodTopping = await _db.FoodToppings
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.FoodId == foodId &&
                x.ToppingId == toppingId, ct);

        if (foodTopping == null)
        {
            return false;
        }

        _db.FoodToppings.Remove(foodTopping);

        return await _db.SaveChangesAsync(ct) > 0;
    }
}
