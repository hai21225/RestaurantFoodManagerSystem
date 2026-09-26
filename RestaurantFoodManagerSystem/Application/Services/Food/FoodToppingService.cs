public class FoodToppingService
{
    private readonly IRepository<FoodTopping> _foodToppingRepository;
    private readonly IRepository<Food> _foodRepository;
    private readonly IRepository<Topping> _toppingRepository;
    private readonly IUnitOfWork _unitOfWork;

    public FoodToppingService(
        IRepository<FoodTopping> foodToppingRepository,
        IRepository<Food> foodRepository,
        IRepository<Topping> toppingRepository,
        IUnitOfWork unitOfWork)
    {
        _foodToppingRepository = foodToppingRepository;
        _foodRepository = foodRepository;
        _toppingRepository = toppingRepository;
        _unitOfWork = unitOfWork;
    }


    // ============================
    // GET TOPPINGS OF FOOD
    // ============================

    public async Task<List<ToppingResponseDto>>
        GetToppingsByFoodIdAsync(int foodId)
    {
        var food = await _foodRepository.GetByIdAsync(foodId);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var foodToppings =
            await _foodToppingRepository.FindAsync(
                x => x.FoodId == foodId
            );

        if (!foodToppings.Any())
        {
            return new List<ToppingResponseDto>();
        }

        var toppingIds = foodToppings
            .Select(x => x.ToppingId)
            .ToList();

        var toppings = await _toppingRepository.FindAsync(
            x =>
                toppingIds.Contains(x.ToppingId) &&
                x.IsActive
        );

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
        var food = await _foodRepository.GetByIdAsync(foodId);

        if (food == null)
        {
            throw new Exception("Food not found");
        }

        var topping = await _toppingRepository
            .GetByIdAsync(toppingId);

        if (topping == null || !topping.IsActive)
        {
            throw new Exception("Topping not found");
        }

        var exists =
            await _foodToppingRepository.ExistsAsync(
                x =>
                    x.FoodId == foodId &&
                    x.ToppingId == toppingId
            );

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

        await _foodToppingRepository.AddAsync(foodTopping);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }


    // ============================
    // REMOVE
    // ============================

    public async Task<bool> RemoveAsync(
        int foodId,
        int toppingId)
    {
        var foodTopping =
            await _foodToppingRepository.FindOneAsync(
                x =>
                    x.FoodId == foodId &&
                    x.ToppingId == toppingId
            );

        if (foodTopping == null)
        {
            return false;
        }

        _foodToppingRepository.Delete(foodTopping);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }
}