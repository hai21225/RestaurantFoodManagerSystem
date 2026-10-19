using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class CategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db)
    {
        _db = db;
    }


    // GET BY ID
    public async Task<CategoryDTO> GetCategoryByIdAsync(int id)
    {
        var category = await _db.Categories.FindAsync(id);

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
        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.CategoryName.Contains(name))
            .ToListAsync();

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
        var categories = await _db.Categories
            .AsNoTracking()
            .ToListAsync();

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

        await _db.Categories.AddAsync(category);

        return await _db.SaveChangesAsync() > 0;
    }


    // UPDATE
    public async Task<bool> UpdateCategoryAsync(CategoryDTO categoryDTO)
    {
        if (categoryDTO == null)
        {
            throw new ArgumentNullException(nameof(categoryDTO));
        }

        var category = await _db.Categories
            .FindAsync(categoryDTO.Id);

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

        _db.Categories.Update(category);

        return await _db.SaveChangesAsync() > 0;
    }


    // DELETE - SOFT DELETE
    public async Task<bool> DeleteCategoryAsync(int id)
    {
        var category = await _db.Categories.FindAsync(id);

        if (category == null)
        {
            throw new Exception("Category not found");
        }

        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        _db.Categories.Update(category);

        return await _db.SaveChangesAsync() > 0;
    }
}
