using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class FoodService
{
    private readonly AppDbContext _db;

    public FoodService(AppDbContext db)
    {
        _db = db;
    }


    // =========================
    // GET ALL
    // =========================

    public async Task<List<FoodResponseDto>> GetAllFoodsAsync(
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        List<Food> foods;

        if (includeInactive)
        {
            foods = await _db.Foods
                .AsNoTracking()
                .ToListAsync(ct);
        }
        else
        {
            foods = await _db.Foods
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(ct);
        }

        return foods.Select(MapToDto).ToList();
    }


    // =========================
    // GET BY ID
    // =========================

    public async Task<FoodResponseDto?> GetFoodByIdAsync(int id, CancellationToken ct = default)
    {
        var food = await _db.Foods.FindAsync([id], ct);

        if (food == null)
        {
            return null;
        }

        return MapToDto(food);
    }


    // =========================
    // GET BY CATEGORY
    // =========================

    public async Task<List<FoodResponseDto>>
        GetFoodsByCategoryIdAsync(
            int categoryId,
            bool includeInactive = false,
            CancellationToken ct = default)
    {
        var query = _db.Foods
            .AsNoTracking()
            .Where(x => x.CategoryId == categoryId);

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var foods = await query.ToListAsync(ct);

        return foods
            .Select(MapToDto)
            .ToList();
    }


    // =========================
    // CREATE
    // =========================

    public async Task<FoodResponseDto> AddFoodAsync(
        CreateFoodRequest request,
        CancellationToken ct = default)
    {
        var category = await _db.Categories
            .FindAsync([request.CategoryId], ct);

        if (category == null)
        {
            throw new KeyNotFoundException("Category not found");
        }

        var food = new Food
        {
            CategoryId = request.CategoryId,

            FoodName = request.FoodName.Trim(),

            FoodDescription =
                request.FoodDescription?.Trim()
                ?? string.Empty,

            FoodImageUrl =
                request.FoodImageUrl?.Trim()
                ?? string.Empty,

            FoodPrice = request.FoodPrice,

            IsAvailable = request.IsAvailable,

            // Khi tạo món mặc định đang hoạt động
            IsActive = true,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _db.Foods.AddAsync(food, ct);

        var result = await _db.SaveChangesAsync(ct);

        if (result <= 0)
        {
            throw new InvalidOperationException("Failed to create food");
        }

        return MapToDto(food);
    }


    // =========================
    // UPDATE
    // =========================

    public async Task<FoodResponseDto?> UpdateFoodAsync(
        int id,
        UpdateFoodRequest request,
        CancellationToken ct = default)
    {
        var food = await _db.Foods.FindAsync([id], ct);

        if (food == null)
        {
            return null;
        }

        var category = await _db.Categories
            .FindAsync([request.CategoryId], ct);

        if (category == null)
        {
            throw new KeyNotFoundException("Category not found");
        }

        food.CategoryId = request.CategoryId;

        food.FoodName = request.FoodName.Trim();

        food.FoodDescription =
            request.FoodDescription?.Trim()
            ?? string.Empty;

        food.FoodImageUrl =
            request.FoodImageUrl?.Trim()
            ?? string.Empty;

        food.FoodPrice = request.FoodPrice;

        food.IsAvailable = request.IsAvailable;

        food.UpdatedAt = DateTime.UtcNow;

        _db.Foods.Update(food);

        var result = await _db.SaveChangesAsync(ct);

        if (result <= 0)
        {
            return null;
        }

        return MapToDto(food);
    }


    // =========================
    // UPDATE AVAILABILITY
    // =========================

    public async Task<bool> UpdateAvailabilityAsync(
        int id,
        bool isAvailable,
        CancellationToken ct = default)
    {
        var food = await _db.Foods.FindAsync([id], ct);

        if (food == null)
        {
            return false;
        }

        food.IsAvailable = isAvailable;
        food.UpdatedAt = DateTime.UtcNow;

        _db.Foods.Update(food);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    // =========================
    // UPDATE ACTIVE (soft delete / reactivate)
    // =========================

    public async Task<bool> UpdateActiveAsync(
        int id,
        bool isActive,
        CancellationToken ct = default)
    {
        var food = await _db.Foods.FindAsync([id], ct);

        if (food == null)
        {
            return false;
        }

        food.IsActive = isActive;

        // Ngừng dùng thì cũng không cho order (đối xứng với DeleteFoodAsync).
        if (!isActive)
        {
            food.IsAvailable = false;
            food.InactiveAt = DateTime.UtcNow;
        }

        // C-13: InactiveReason/InactiveAt là lý do của lần vô hiệu hoá gần nhất — giữ lại làm
        // báo cáo, không xoá khi bật lại món.

        food.UpdatedAt = DateTime.UtcNow;

        _db.Foods.Update(food);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    // =========================
    // DELETE - SOFT DELETE
    // =========================

    public async Task<bool> DeleteFoodAsync(int id, CancellationToken ct = default)
    {
        var food = await _db.Foods.FindAsync([id], ct);

        if (food == null)
        {
            return false;
        }

        food.IsActive = false;

        // Món đã bị disable thì cũng không nên order được
        food.IsAvailable = false;
        food.InactiveAt = DateTime.UtcNow;

        food.UpdatedAt = DateTime.UtcNow;

        _db.Foods.Update(food);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    // =========================
    // PRIVATE MAPPING
    // =========================

    private static FoodResponseDto MapToDto(Food food)
    {
        return new FoodResponseDto
        {
            FoodId = food.FoodId,

            CategoryId = food.CategoryId,

            FoodName = food.FoodName,

            FoodDescription = food.FoodDescription,

            FoodImageUrl = food.FoodImageUrl,

            FoodPrice = food.FoodPrice,

            IsAvailable = food.IsAvailable,

            IsActive = food.IsActive
        };
    }
}
