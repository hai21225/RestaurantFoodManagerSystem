public class ToppingService
{
    private readonly IRepository<Topping> _toppingRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ToppingService(
        IRepository<Topping> toppingRepository,
        IUnitOfWork unitOfWork)
    {
        _toppingRepository = toppingRepository;
        _unitOfWork = unitOfWork;
    }


    public async Task<List<ToppingResponseDto>> GetAllAsync(
        bool includeInactive = false)
    {
        List<Topping> toppings;

        if (includeInactive)
        {
            toppings = await _toppingRepository.GetAllAsync();
        }
        else
        {
            toppings = await _toppingRepository.FindAsync(
                x => x.IsActive
            );
        }

        return toppings
            .Select(MapToDto)
            .ToList();
    }


    public async Task<ToppingResponseDto?> GetByIdAsync(int id)
    {
        var topping = await _toppingRepository.GetByIdAsync(id);

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

        var exists = await _toppingRepository.ExistsAsync(
            x => x.ToppingName == name &&
                 x.IsActive
        );

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

        await _toppingRepository.AddAsync(topping);

        var result = await _unitOfWork.SaveChangesAsync();

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
        var topping = await _toppingRepository.GetByIdAsync(id);

        if (topping == null)
        {
            return null;
        }

        var name = request.ToppingName.Trim();

        var duplicate = await _toppingRepository.ExistsAsync(
            x => x.ToppingId != id &&
                 x.ToppingName == name &&
                 x.IsActive
        );

        if (duplicate)
        {
            throw new Exception("Topping name already exists");
        }

        topping.ToppingName = name;
        topping.ToppingPrice = request.ToppingPrice;
        topping.IsAvailable = request.IsAvailable;

        _toppingRepository.Update(topping);

        await _unitOfWork.SaveChangesAsync();

        return MapToDto(topping);
    }


    public async Task<bool> UpdateAvailabilityAsync(
        int id,
        bool isAvailable)
    {
        var topping = await _toppingRepository.GetByIdAsync(id);

        if (topping == null)
        {
            return false;
        }

        topping.IsAvailable = isAvailable;

        _toppingRepository.Update(topping);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }


    public async Task<bool> DeleteAsync(int id)
    {
        var topping = await _toppingRepository.GetByIdAsync(id);

        if (topping == null)
        {
            return false;
        }

        // Soft delete
        topping.IsActive = false;
        topping.IsAvailable = false;

        _toppingRepository.Update(topping);

        return await _unitOfWork.SaveChangesAsync() > 0;
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