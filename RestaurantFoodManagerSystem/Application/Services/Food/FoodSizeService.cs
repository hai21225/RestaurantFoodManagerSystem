public class FoodSizeService
{
    private readonly IRepository<FoodSize> _foodSizeRepository;
    private readonly IRepository<Food> _foodRepository;
    private readonly IUnitOfWork _unitOfWork;

    public FoodSizeService(
        IRepository<FoodSize> foodSizeRepository,
        IRepository<Food> foodRepository,
        IUnitOfWork unitOfWork)
    {
        _foodSizeRepository = foodSizeRepository;
        _foodRepository = foodRepository;
        _unitOfWork = unitOfWork;
    }


    public async Task<List<FoodSizeResponseDto>>
        GetByFoodIdAsync(int foodId)
    {
        var food = await _foodRepository.GetByIdAsync(foodId);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var sizes = await _foodSizeRepository.FindAsync(
            x => x.FoodId == foodId &&
                 x.IsActive
        );

        return sizes
            .Select(MapToDto)
            .ToList();
    }


    public async Task<FoodSizeResponseDto?> GetByIdAsync(
        int foodId,
        int sizeId)
    {
        var size = await _foodSizeRepository.FindOneAsync(
            x => x.SizeId == sizeId &&
                 x.FoodId == foodId &&
                 x.IsActive
        );

        if (size == null)
        {
            return null;
        }

        return MapToDto(size);
    }


    public async Task<FoodSizeResponseDto> CreateAsync(
        int foodId,
        CreateFoodSizeRequest request)
    {
        var food = await _foodRepository.GetByIdAsync(foodId);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var name = request.SizeName.Trim();

        var duplicate = await _foodSizeRepository.ExistsAsync(
            x => x.FoodId == foodId &&
                 x.SizeName == name &&
                 x.IsActive
        );

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
            await RemoveCurrentDefaultAsync(foodId);
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

        await _foodSizeRepository.AddAsync(size);

        var result = await _unitOfWork.SaveChangesAsync();

        if (result <= 0)
        {
            throw new Exception("Failed to create food size");
        }

        return MapToDto(size);
    }


    public async Task<FoodSizeResponseDto?> UpdateAsync(
        int foodId,
        int sizeId,
        UpdateFoodSizeRequest request)
    {
        var size = await _foodSizeRepository.FindOneAsync(
            x => x.SizeId == sizeId &&
                 x.FoodId == foodId &&
                 x.IsActive
        );

        if (size == null)
        {
            return null;
        }

        var name = request.SizeName.Trim();

        var duplicate = await _foodSizeRepository.ExistsAsync(
            x => x.FoodId == foodId &&
                 x.SizeId != sizeId &&
                 x.SizeName == name &&
                 x.IsActive
        );

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
                sizeId
            );
        }

        size.SizeName = name;
        size.SizePrice = request.SizePrice;
        size.IsDefault = request.IsDefault;
        size.UpdatedAt = DateTime.UtcNow;

        _foodSizeRepository.Update(size);

        await _unitOfWork.SaveChangesAsync();

        return MapToDto(size);
    }


    public async Task<bool> SetDefaultAsync(
        int foodId,
        int sizeId)
    {
        var size = await _foodSizeRepository.FindOneAsync(
            x => x.SizeId == sizeId &&
                 x.FoodId == foodId &&
                 x.IsActive
        );

        if (size == null)
        {
            return false;
        }

        await RemoveCurrentDefaultAsync(
            foodId,
            sizeId
        );

        size.IsDefault = true;
        size.UpdatedAt = DateTime.UtcNow;

        _foodSizeRepository.Update(size);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }


    public async Task<bool> DeleteAsync(
        int foodId,
        int sizeId)
    {
        var size = await _foodSizeRepository.FindOneAsync(
            x => x.SizeId == sizeId &&
                 x.FoodId == foodId &&
                 x.IsActive
        );

        if (size == null)
        {
            return false;
        }

        size.IsActive = false;
        size.IsDefault = false;
        size.UpdatedAt = DateTime.UtcNow;

        _foodSizeRepository.Update(size);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }


    private async Task RemoveCurrentDefaultAsync(
        int foodId,
        int? exceptSizeId = null)
    {
        var defaults = await _foodSizeRepository.FindAsync(
            x =>
                x.FoodId == foodId &&
                x.IsDefault &&
                x.IsActive &&
                (!exceptSizeId.HasValue ||
                 x.SizeId != exceptSizeId.Value)
        );

        foreach (var size in defaults)
        {
            size.IsDefault = false;
            size.UpdatedAt = DateTime.UtcNow;

            _foodSizeRepository.Update(size);
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