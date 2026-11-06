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
    public async Task<CategoryDTO> GetCategoryByIdAsync(int id, CancellationToken ct = default)
    {
        var category = await _db.Categories.FindAsync([id], ct);

        if (category == null)
        {
            throw new KeyNotFoundException("Category not found");
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
    public async Task<List<CategoryDTO>> GetCategoriesByNameAsync(string name, CancellationToken ct = default)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.CategoryName.Contains(name))
            .ToListAsync(ct);

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
    public async Task<List<CategoryDTO>> GetAllCategoriesAsync(CancellationToken ct = default)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .ToListAsync(ct);

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
    public async Task<bool> CreateCategoryAsync(CategoryDTO categoryDTO, CancellationToken ct = default)
    {
        if (categoryDTO == null)
        {
            throw new ArgumentNullException(nameof(categoryDTO));
        }

        var name = categoryDTO.Name?.Trim() ?? string.Empty;
        var description = categoryDTO.Description?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required");
        }

        if (await _db.Categories.AnyAsync(x => x.CategoryName == name && x.IsActive, ct))
        {
            throw new InvalidOperationException("Category name already exists");
        }

        var category = new Category
        {
            CategoryName = name,
            CategoryDescription = description,
            DisplayOrder = categoryDTO.DisplayOrder,

            // Khi tạo category nên active mặc định
            IsActive = true,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _db.Categories.AddAsync(category, ct);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    // UPDATE
    public async Task<bool> UpdateCategoryAsync(CategoryDTO categoryDTO, CancellationToken ct = default)
    {
        if (categoryDTO == null)
        {
            throw new ArgumentNullException(nameof(categoryDTO));
        }

        var category = await _db.Categories
            .FindAsync([categoryDTO.Id], ct);

        if (category == null)
        {
            throw new KeyNotFoundException("Category not found");
        }

        var name = categoryDTO.Name?.Trim() ?? string.Empty;
        var description = categoryDTO.Description?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required");
        }

        if (categoryDTO.IsActive
            && await _db.Categories.AnyAsync(x => x.CategoryId != categoryDTO.Id && x.CategoryName == name && x.IsActive, ct))
        {
            throw new InvalidOperationException("Category name already exists");
        }

        category.CategoryName = name;
        category.CategoryDescription = description;
        category.DisplayOrder = categoryDTO.DisplayOrder;
        category.IsActive = categoryDTO.IsActive;
        category.UpdatedAt = DateTime.UtcNow;

        _db.Categories.Update(category);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    // DELETE - SOFT DELETE
    public async Task<bool> DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var category = await _db.Categories.FindAsync([id], ct);

        if (category == null)
        {
            throw new KeyNotFoundException("Category not found");
        }

        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        _db.Categories.Update(category);

        return await _db.SaveChangesAsync(ct) > 0;
    }


    /// <summary>
    /// Khôi phục/tắt danh mục bằng soft-delete. Món và lịch sử báo cáo vẫn giữ CategoryId.
    /// </summary>
    public async Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var category = await _db.Categories.FindAsync([id], ct);

        if (category == null)
        {
            return false;
        }

        if (active
            && await _db.Categories.AnyAsync(x => x.CategoryId != id && x.CategoryName == category.CategoryName && x.IsActive, ct))
        {
            throw new InvalidOperationException("Category name already exists");
        }

        category.IsActive = active;
        category.UpdatedAt = DateTime.UtcNow;

        _db.Categories.Update(category);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
