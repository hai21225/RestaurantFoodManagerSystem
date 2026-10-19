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
        GetToppingsByFoodIdAsync(int foodId)
    {
        var food = await _db.Foods.FindAsync(foodId);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var foodToppings = await _db.FoodToppings
            .AsNoTracking()
            .Where(x => x.FoodId == foodId)
            .ToListAsync();

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
            .ToListAsync();

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
        int toppingId)
    {
        var food = await _db.Foods.FindAsync(foodId);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var topping = await _db.Toppings
            .FindAsync(toppingId);

        if (topping == null || !topping.IsActive)
        {
            throw new Exception("Topping not found");
        }

        var exists = await _db.FoodToppings.AnyAsync(x =>
            x.FoodId == foodId &&
            x.ToppingId == toppingId);

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

        await _db.FoodToppings.AddAsync(foodTopping);

        return await _db.SaveChangesAsync() > 0;
    }


    // ============================
    // REMOVE
    // ============================

    public async Task<bool> RemoveAsync(
        int foodId,
        int toppingId)
    {
        var foodTopping = await _db.FoodToppings
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.FoodId == foodId &&
                x.ToppingId == toppingId);

        if (foodTopping == null)
        {
            return false;
        }

        _db.FoodToppings.Remove(foodTopping);

        return await _db.SaveChangesAsync() > 0;
    }
}
