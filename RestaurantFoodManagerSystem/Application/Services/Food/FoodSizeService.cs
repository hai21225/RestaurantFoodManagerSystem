using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class FoodSizeService
{
    private readonly AppDbContext _db;

    public FoodSizeService(AppDbContext db)
    {
        _db = db;
    }


    public async Task<List<FoodSizeResponseDto>>
        GetByFoodIdAsync(
            int foodId,
            bool includeInactive = false,
            CancellationToken ct = default)
    {
        var food = await _db.Foods.FindAsync([foodId], ct);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var query = _db.FoodSizes
            .AsNoTracking()
            .Where(x => x.FoodId == foodId);

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var sizes = await query.ToListAsync(ct);

        return sizes
            .Select(MapToDto)
            .ToList();
    }


    public async Task<FoodSizeResponseDto?> GetByIdAsync(
        int foodId,
        int sizeId,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = _db.FoodSizes
            .AsNoTracking()
            .Where(x =>
                x.SizeId == sizeId &&
                x.FoodId == foodId);

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var size = await query.FirstOrDefaultAsync(ct);

        if (size == null)
        {
            return null;
        }

        return MapToDto(size);
    }


    public async Task<FoodSizeResponseDto> CreateAsync(
        int foodId,
        CreateFoodSizeRequest request,
        CancellationToken ct = default)
    {
        // Defect #32: serialize thao tác "clear default + set default" để hai request đồng thời
        // không cùng tạo ra hai size default cho một món.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var food = await _db.Foods.FindAsync([foodId], ct);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var name = request.SizeName.Trim();

        var duplicate = await _db.FoodSizes.AnyAsync(x =>
            x.FoodId == foodId &&
            x.SizeName == name &&
            x.IsActive, ct);

        if (duplicate)
        {
            throw new Exception(
                "This size already exists for this food"
            );
        }

        // Nếu size mới là default,
        // bỏ default của size cũ
        if (request.IsDefault)
        {
            await RemoveCurrentDefaultAsync(foodId, ct: ct);
        }

        var size = new FoodSize
        {
            FoodId = foodId,
            SizeName = name,
            SizePrice = request.SizePrice,

            IsDefault = request.IsDefault,
            IsActive = true,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _db.FoodSizes.AddAsync(size, ct);

        var result = await _db.SaveChangesAsync(ct);

        if (result <= 0)
        {
            throw new Exception("Failed to create food size");
        }

        await tx.CommitAsync(ct);
        return MapToDto(size);
    }


    public async Task<FoodSizeResponseDto?> UpdateAsync(
        int foodId,
        int sizeId,
        UpdateFoodSizeRequest request,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Defect #31: dùng entity đang tracked thay vì AsNoTracking + Update(), để EF chỉ
        // UPDATE các cột thực sự thay đổi và không ghi đè thay đổi đồng thời của request khác.
        var size = await _db.FoodSizes
            .FirstOrDefaultAsync(x =>
                x.SizeId == sizeId &&
                x.FoodId == foodId &&
                x.IsActive, ct);

        if (size == null)
        {
            return null;
        }

        var name = request.SizeName.Trim();

        var duplicate = await _db.FoodSizes.AnyAsync(x =>
            x.FoodId == foodId &&
            x.SizeId != sizeId &&
            x.SizeName == name &&
            x.IsActive, ct);

        if (duplicate)
        {
            throw new Exception(
                "This size already exists for this food"
            );
        }

        if (request.IsDefault)
        {
            await RemoveCurrentDefaultAsync(
                foodId,
                sizeId,
                ct
            );
        }

        size.SizeName = name;
        size.SizePrice = request.SizePrice;
        size.IsDefault = request.IsDefault;
        size.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return MapToDto(size);
    }


    public async Task<bool> SetDefaultAsync(
        int foodId,
        int sizeId,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var size = await _db.FoodSizes
            .FirstOrDefaultAsync(x =>
                x.SizeId == sizeId &&
                x.FoodId == foodId &&
                x.IsActive, ct);

        if (size == null)
        {
            return false;
        }

        await RemoveCurrentDefaultAsync(
            foodId,
            sizeId,
            ct
        );

        size.IsDefault = true;
        size.UpdatedAt = DateTime.UtcNow;

        var saved = await _db.SaveChangesAsync(ct) > 0;
        await tx.CommitAsync(ct);
        return saved;
    }


    public async Task<bool> DeleteAsync(
        int foodId,
        int sizeId,
        CancellationToken ct = default)
    {
        var size = await _db.FoodSizes
            .FirstOrDefaultAsync(x =>
                x.SizeId == sizeId &&
                x.FoodId == foodId &&
                x.IsActive, ct);

        if (size == null)
        {
            return false;
        }

        size.IsActive = false;
        size.IsDefault = false;
        size.UpdatedAt = DateTime.UtcNow;

        return await _db.SaveChangesAsync(ct) > 0;
    }


    /// <summary>
    /// Bật/tắt vòng đời của size (soft-delete/restore). Không hard-delete để giữ lịch sử
    /// công thức và báo cáo; size bị tắt cũng không còn là default.
    /// </summary>
    public async Task<bool> SetActiveAsync(
        int foodId,
        int sizeId,
        bool active,
        CancellationToken ct = default)
    {
        var size = await _db.FoodSizes
            .FirstOrDefaultAsync(x =>
                x.SizeId == sizeId &&
                x.FoodId == foodId, ct);

        if (size == null)
        {
            return false;
        }

        if (active &&
            await _db.FoodSizes.AnyAsync(x =>
                x.FoodId == foodId &&
                x.SizeId != sizeId &&
                x.SizeName == size.SizeName &&
                x.IsActive, ct))
        {
            throw new InvalidOperationException("This size already exists for this food");
        }

        size.IsActive = active;
        if (!active)
        {
            size.IsDefault = false;
        }
        size.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }


    private async Task RemoveCurrentDefaultAsync(
        int foodId,
        int? exceptSizeId = null,
        CancellationToken ct = default)
    {
        var defaults = await _db.FoodSizes
            .Where(x =>
                x.FoodId == foodId &&
                x.IsDefault &&
                x.IsActive &&
                (!exceptSizeId.HasValue ||
                 x.SizeId != exceptSizeId.Value))
            .ToListAsync(ct);

        foreach (var size in defaults)
        {
            size.IsDefault = false;
            size.UpdatedAt = DateTime.UtcNow;
        }
    }


    private static FoodSizeResponseDto MapToDto(
        FoodSize size)
    {
        return new FoodSizeResponseDto
        {
            SizeId = size.SizeId,
            FoodId = size.FoodId,
            SizeName = size.SizeName,
            SizePrice = size.SizePrice,
            IsActive = size.IsActive,
            IsDefault = size.IsDefault
        };
    }
}
