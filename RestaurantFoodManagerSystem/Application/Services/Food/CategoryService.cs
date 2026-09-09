public class CategoryService
{
    private readonly IRepository<Category> _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    public CategoryService(IRepository<Category> categoryRepository, IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<CategoryDTO> GetCategoryByIdAsync(int id)
    {
        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            throw new Exception("Category not found");
        }
        var categoryDTO = new CategoryDTO
        {
            Id = category.CategoryId,
            Name = category.CategoryName,
            Description = category.CategoryDescription,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive
        };
        return categoryDTO;
    }

    public async Task<List<CategoryDTO>> GetCategoriesByNameAsync()
    {
        var categories = await _categoryRepository.GetAllAsync();
        var categoryDTOs = categories.Select(category => new CategoryDTO
        {
            Id = category.CategoryId,
            Name = category.CategoryName,
            Description = category.CategoryDescription,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive
        }).ToList();
        return categoryDTOs;
    }

    public async Task<List<CategoryDTO>> GetAllCategoriesAsync()
    {
        var categories = await _categoryRepository.GetAllAsync();
        var categoryDTOs = categories.Select(category => new CategoryDTO
        {
            Id = category.CategoryId,
            Name = category.CategoryName,
            Description = category.CategoryDescription,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive
        }).ToList();
        return categoryDTOs;
    }

    public async Task<bool> CreateCategoryAsync(CategoryDTO categoryDTO)
    {
        if (categoryDTO == null)
        {
            throw new ArgumentNullException(nameof(categoryDTO));
        }
        var category = new Category
        {
            CategoryName = categoryDTO.Name??"",
            CategoryDescription = categoryDTO.Description ?? "",
            DisplayOrder = categoryDTO.DisplayOrder,
            IsActive = categoryDTO.IsActive
        };
        await _categoryRepository.AddAsync(category);
        return await _unitOfWork.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateCategoryAsync(CategoryDTO categoryDTO)
    {
        if (categoryDTO == null)
        {
            throw new ArgumentNullException(nameof(categoryDTO));
        }
        var category = await _categoryRepository.GetByIdAsync(categoryDTO.Id);
        if (category == null)
        {
            throw new Exception("Category not found");
        }
        category.CategoryName = categoryDTO.Name ?? "";
        category.CategoryDescription = categoryDTO.Description??"";
        category.DisplayOrder = categoryDTO.DisplayOrder;
        category.IsActive = categoryDTO.IsActive;
        _categoryRepository.Update(category);
        return await _unitOfWork.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            throw new Exception("Category not found");
        }
        _categoryRepository.Delete(category);
        return await _unitOfWork.SaveChangesAsync() > 0;
    }
}