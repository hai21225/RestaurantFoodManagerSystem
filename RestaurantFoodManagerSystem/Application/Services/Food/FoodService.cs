public class FoodService
{
    private readonly IUnitOfWork _unitOfWork;

    private readonly IRepository<Food> _foodRepository;
    private readonly IRepository<Category> _categoryRepository;

    public FoodService(
        IUnitOfWork unitOfWork,
        IRepository<Food> foodRepository,
        IRepository<Category> categoryRepository)
    {
        _unitOfWork = unitOfWork;
        _foodRepository = foodRepository;
        _categoryRepository = categoryRepository;
    }


    // =========================
    // GET ALL
    // =========================

    public async Task<List<FoodResponseDto>> GetAllFoodsAsync(
        bool includeInactive = false)
    {
        List<Food> foods;

        if (includeInactive)
        {
            foods = await _foodRepository.GetAllAsync();
        }
        else
        {
            foods = await _foodRepository.FindAsync(
                x => x.IsActive
            );
        }

        return foods.Select(MapToDto).ToList();
    }


    // =========================
    // GET BY ID
    // =========================

    public async Task<FoodResponseDto?> GetFoodByIdAsync(int id)
    {
        var food = await _foodRepository.GetByIdAsync(id);

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
        GetFoodsByCategoryIdAsync(int categoryId)
    {
        var foods = await _foodRepository.FindAsync(
            x =>
                x.CategoryId == categoryId &&
                x.IsActive
        );

        return foods
            .Select(MapToDto)
            .ToList();
    }


    // =========================
    // CREATE
    // =========================

    public async Task<FoodResponseDto> AddFoodAsync(
        CreateFoodRequest request)
    {
        var category = await _categoryRepository
            .GetByIdAsync(request.CategoryId);

        if (category == null)
        {
            throw new Exception("Category not found");
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

        await _foodRepository.AddAsync(food);

        var result = await _unitOfWork.SaveChangesAsync();

        if (result <= 0)
        {
            throw new Exception("Failed to create food");
        }

        return MapToDto(food);
    }


    // =========================
    // UPDATE
    // =========================

    public async Task<FoodResponseDto?> UpdateFoodAsync(
        int id,
        UpdateFoodRequest request)
    {
        var food = await _foodRepository.GetByIdAsync(id);

        if (food == null)
        {
            return null;
        }

        var category = await _categoryRepository
            .GetByIdAsync(request.CategoryId);

        if (category == null)
        {
            throw new Exception("Category not found");
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

        _foodRepository.Update(food);

        var result = await _unitOfWork.SaveChangesAsync();

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
        bool isAvailable)
    {
        var food = await _foodRepository.GetByIdAsync(id);

        if (food == null)
        {
            return false;
        }

        food.IsAvailable = isAvailable;
        food.UpdatedAt = DateTime.UtcNow;

        _foodRepository.Update(food);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }


    // =========================
    // DELETE - SOFT DELETE
    // =========================

    public async Task<bool> DeleteFoodAsync(int id)
    {
        var food = await _foodRepository.GetByIdAsync(id);

        if (food == null)
        {
            return false;
        }

        food.IsActive = false;

        // Món đã bị disable thì cũng không nên order được
        food.IsAvailable = false;

        food.UpdatedAt = DateTime.UtcNow;

        _foodRepository.Update(food);

        return await _unitOfWork.SaveChangesAsync() > 0;
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