using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly CategoryService _categoryService;

    public CategoriesController(CategoryService categoryService)
    {
        _categoryService = categoryService;
    }


    // ===================================================
    // GET /api/categories
    //
    // GET /api/categories?name=drink
    // ===================================================

    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? name,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var categories =
                await _categoryService
                    .GetCategoriesByNameAsync(name, ct);

            return Ok(categories);
        }

        var result =
            await _categoryService
                .GetAllCategoriesAsync(ct);

        return Ok(result);
    }


    // ===================================================
    // GET /api/categories/5
    // ===================================================

    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct = default)
    {
        try
        {
            var category =
                await _categoryService
                    .GetCategoryByIdAsync(id, ct);

            return Ok(category);
        }
        catch (Exception ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }


    // ===================================================
    // POST /api/categories
    // ===================================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CategoryDTO dto,
        CancellationToken ct = default)
    {
        try
        {
            var result =
                await _categoryService
                    .CreateCategoryAsync(dto, ct);

            if (!result)
            {
                return BadRequest(new
                {
                    message = "Failed to create category"
                });
            }

            return Ok(new
            {
                message = "Category created successfully"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


    // ===================================================
    // PUT /api/categories/5
    // ===================================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] CategoryDTO dto,
        CancellationToken ct = default)
    {
        try
        {
            // ID lấy từ URL, không tin ID client gửi trong body
            dto.Id = id;

            var result =
                await _categoryService
                    .UpdateCategoryAsync(dto, ct);

            if (!result)
            {
                return BadRequest(new
                {
                    message = "Failed to update category"
                });
            }

            return Ok(new
            {
                message = "Category updated successfully"
            });
        }
        catch (Exception ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }


    // ===================================================
    // PATCH /api/categories/5/active
    // ===================================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> SetActive(
        int id,
        [FromBody] UpdateActiveRequest request,
        CancellationToken ct = default)
    {
        var result = await _categoryService
            .SetActiveAsync(id, request.IsActive, ct);

        if (!result)
        {
            return NotFound(new
            {
                message = "Category not found"
            });
        }

        return Ok(new
        {
            message = "Category active status updated successfully"
        });
    }


    // ===================================================
    // DELETE /api/categories/5
    // ===================================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        try
        {
            var result =
                await _categoryService
                    .DeleteCategoryAsync(id, ct);

            if (!result)
            {
                return BadRequest(new
                {
                    message = "Failed to delete category"
                });
            }

            return Ok(new
            {
                message = "Category deleted successfully"
            });
        }
        catch (Exception ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }
}