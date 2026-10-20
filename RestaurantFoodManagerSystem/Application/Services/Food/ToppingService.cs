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
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        List<Topping> toppings;

        if (includeInactive)
        {
            toppings = await _db.Toppings
                .AsNoTracking()
                .ToListAsync(ct);
        }
        else
        {
            toppings = await _db.Toppings
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(ct);
        }

        return toppings
            .Select(MapToDto)
            .ToList();
    }


    public async Task<ToppingResponseDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var topping = await _db.Toppings.FindAsync([id], ct);

        if (topping == null)
        {
            return null;
        }

        return MapToDto(topping);
    }


    public async Task<ToppingResponseDto> CreateAsync(
        CreateToppingRequest request,
        CancellationToken ct = default)
    {
        var name = request.ToppingName.Trim();

        var exists = await _db.Toppings.AnyAsync(x =>
            x.ToppingName == name &&
            x.IsActive, ct);

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

        await _db.Toppings.AddAsync(topping, ct);

        var result = await _db.SaveChangesAsync(ct);

        if (result <= 0)
        {
            throw new Exception("Failed to create topping");
        }

        return MapToDto(topping);
    }


    public async Task<ToppingResponseDto?> UpdateAsync(
        int id,
        UpdateToppingRequest request,
        CancellationToken ct = default)
    {
        var topping = await _db.Toppings.FindAsync([id], ct);

        if (topping == null)
        {
            return null;
        }

        var name = request.ToppingName.Trim();

        var duplicate = await _db.Toppings.AnyAsync(x =>
            x.ToppingId != id &&
            x.ToppingName == name &&
            x.IsActive, ct);

        if (duplicate)
        {
            throw new Exception("Topping name already exists");
        }

        topping.ToppingName = name;
        topping.ToppingPrice = request.ToppingPrice;
        topping.IsAvailable = request.IsAvailable;

        _db.Toppings.Update(topping);

        await _db.SaveChangesAsync(ct);

        return MapToDto(topping);
    }


    public async Task<bool> UpdateAvailabilityAsync(
        int id,
        bool isAvailable,
        CancellationToken ct = default)
    {
        var topping = await _db.Toppings.FindAsync([id], ct);

        if (topping == null)
        {
            return false;
        }

        topping.IsAvailable = isAvailable;

        _db.Toppings.Update(topping);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var topping = await _db.Toppings.FindAsync([id], ct);

        if (topping == null)
        {
            return false;
        }

        // Soft delete
        topping.IsActive = false;
        topping.IsAvailable = false;

        _db.Toppings.Update(topping);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    /// <summary>
    /// Bật/tắt vòng đời topping (soft-delete/restore). Khi tắt, topping cũng không còn
    /// khả dụng để chọn trong POS; dữ liệu lịch sử vẫn được giữ.
    /// </summary>
    public async Task<bool> SetActiveAsync(
        int id,
        bool active,
        CancellationToken ct = default)
    {
        var topping = await _db.Toppings.FindAsync([id], ct);

        if (topping == null)
        {
            return false;
        }

        if (active &&
            await _db.Toppings.AnyAsync(x =>
                x.ToppingId != id &&
                x.ToppingName == topping.ToppingName &&
                x.IsActive, ct))
        {
            throw new InvalidOperationException("Topping name already exists");
        }

        topping.IsActive = active;
        if (!active)
        {
            topping.IsAvailable = false;
        }

        _db.Toppings.Update(topping);
        await _db.SaveChangesAsync(ct);
        return true;
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
