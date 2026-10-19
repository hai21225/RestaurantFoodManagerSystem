using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class ToppingService
{
    private readonly AppDbContext _db;

    public ToppingService(AppDbContext db)
    {
        _db = db;
    }


    public async Task<List<ToppingResponseDto>> GetAllAsync(
        bool includeInactive = false)
    {
        List<Topping> toppings;

        if (includeInactive)
        {
            toppings = await _db.Toppings
                .AsNoTracking()
                .ToListAsync();
        }
        else
        {
            toppings = await _db.Toppings
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync();
        }

        return toppings
            .Select(MapToDto)
            .ToList();
    }


    public async Task<ToppingResponseDto?> GetByIdAsync(int id)
    {
        var topping = await _db.Toppings.FindAsync(id);

        if (topping == null)
        {
            return null;
        }

        return MapToDto(topping);
    }


    public async Task<ToppingResponseDto> CreateAsync(
        CreateToppingRequest request)
    {
        var name = request.ToppingName.Trim();

        var exists = await _db.Toppings.AnyAsync(x =>
            x.ToppingName == name &&
            x.IsActive);

        if (exists)
        {
            throw new Exception("Topping already exists");
        }

        var topping = new Topping
        {
            ToppingName = name,
            ToppingPrice = request.ToppingPrice,
            IsAvailable = request.IsAvailable,
            IsActive = true
        };

        await _db.Toppings.AddAsync(topping);

        var result = await _db.SaveChangesAsync();

        if (result <= 0)
        {
            throw new Exception("Failed to create topping");
        }

        return MapToDto(topping);
    }


    public async Task<ToppingResponseDto?> UpdateAsync(
        int id,
        UpdateToppingRequest request)
    {
        var topping = await _db.Toppings.FindAsync(id);

        if (topping == null)
        {
            return null;
        }

        var name = request.ToppingName.Trim();

        var duplicate = await _db.Toppings.AnyAsync(x =>
            x.ToppingId != id &&
            x.ToppingName == name &&
            x.IsActive);

        if (duplicate)
        {
            throw new Exception("Topping name already exists");
        }

        topping.ToppingName = name;
        topping.ToppingPrice = request.ToppingPrice;
        topping.IsAvailable = request.IsAvailable;

        _db.Toppings.Update(topping);

        await _db.SaveChangesAsync();

        return MapToDto(topping);
    }


    public async Task<bool> UpdateAvailabilityAsync(
        int id,
        bool isAvailable)
    {
        var topping = await _db.Toppings.FindAsync(id);

        if (topping == null)
        {
            return false;
        }

        topping.IsAvailable = isAvailable;

        _db.Toppings.Update(topping);

        return await _db.SaveChangesAsync() > 0;
    }


    public async Task<bool> DeleteAsync(int id)
    {
        var topping = await _db.Toppings.FindAsync(id);

        if (topping == null)
        {
            return false;
        }

        // Soft delete
        topping.IsActive = false;
        topping.IsAvailable = false;

        _db.Toppings.Update(topping);

        return await _db.SaveChangesAsync() > 0;
    }


    private static ToppingResponseDto MapToDto(Topping topping)
    {
        return new ToppingResponseDto
        {
            ToppingId = topping.ToppingId,
            ToppingName = topping.ToppingName,
            ToppingPrice = topping.ToppingPrice,
            IsActive = topping.IsActive,
            IsAvailable = topping.IsAvailable
        };
    }
}
