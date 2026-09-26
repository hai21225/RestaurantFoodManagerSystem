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

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var categories =
                await _categoryService
                    .GetCategoriesByNameAsync(name);

            return Ok(categories);
        }

        var result =
            await _categoryService
                .GetAllCategoriesAsync();

        return Ok(result);
    }


    // ===================================================
    // GET /api/categories/5
    // ===================================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var category =
                await _categoryService
                    .GetCategoryByIdAsync(id);

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

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CategoryDTO dto)
    {
        try
        {
            var result =
                await _categoryService
                    .CreateCategoryAsync(dto);

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

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] CategoryDTO dto)
    {
        try
        {
            // ID lấy từ URL, không tin ID client gửi trong body
            dto.Id = id;

            var result =
                await _categoryService
                    .UpdateCategoryAsync(dto);

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
    // DELETE /api/categories/5
    // ===================================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result =
                await _categoryService
                    .DeleteCategoryAsync(id);

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