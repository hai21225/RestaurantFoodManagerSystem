public class CategoryService
{
    private readonly IRepository<Category> _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(
        IRepository<Category> categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }


    // GET BY ID
    public async Task<CategoryDTO> GetCategoryByIdAsync(int id)
    {
        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
        {
            throw new Exception("Category not found");
        }

        return new CategoryDTO
        {
            Id = category.CategoryId,
            Name = category.CategoryName,
            Description = category.CategoryDescription,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive
        };
    }


    // SEARCH BY NAME
    public async Task<List<CategoryDTO>> GetCategoriesByNameAsync(string name)
    {
        var categories = await _categoryRepository.FindAsync(
            c => c.CategoryName.Contains(name)
        );

        return categories.Select(category => new CategoryDTO
        {
            Id = category.CategoryId,
            Name = category.CategoryName,
            Description = category.CategoryDescription,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive

        }).ToList();
    }


    // GET ALL
    public async Task<List<CategoryDTO>> GetAllCategoriesAsync()
    {
        var categories = await _categoryRepository.GetAllAsync();

        return categories.Select(category => new CategoryDTO
        {
            Id = category.CategoryId,
            Name = category.CategoryName,
            Description = category.CategoryDescription,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive

        }).ToList();
    }


    // CREATE
    public async Task<bool> CreateCategoryAsync(CategoryDTO categoryDTO)
    {
        if (categoryDTO == null)
        {
            throw new ArgumentNullException(nameof(categoryDTO));
        }

        var category = new Category
        {
            CategoryName = categoryDTO.Name ?? "",
            CategoryDescription = categoryDTO.Description ?? "",
            DisplayOrder = categoryDTO.DisplayOrder,

            // Khi tạo category nên active mặc định
            IsActive = true,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _categoryRepository.AddAsync(category);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }


    // UPDATE
    public async Task<bool> UpdateCategoryAsync(CategoryDTO categoryDTO)
    {
        if (categoryDTO == null)
        {
            throw new ArgumentNullException(nameof(categoryDTO));
        }

        var category = await _categoryRepository
            .GetByIdAsync(categoryDTO.Id);

        if (category == null)
        {
            throw new Exception("Category not found");
        }

        category.CategoryName = categoryDTO.Name ?? "";

        category.CategoryDescription =
            categoryDTO.Description ?? "";

        category.DisplayOrder = categoryDTO.DisplayOrder;

        category.IsActive = categoryDTO.IsActive;

        category.UpdatedAt = DateTime.UtcNow;

        _categoryRepository.Update(category);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }


    // DELETE - SOFT DELETE
    public async Task<bool> DeleteCategoryAsync(int id)
    {
        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
        {
            throw new Exception("Category not found");
        }

        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        _categoryRepository.Update(category);

        return await _unitOfWork.SaveChangesAsync() > 0;
    }
}