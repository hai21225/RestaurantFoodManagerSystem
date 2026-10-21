using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/foods")]
public class FoodsController : ControllerBase
{
    private readonly FoodService _foodService;

    public FoodsController(FoodService foodService)
    {
        _foodService = foodService;
    }


    // ========================================
    // GET /api/foods
    //
    // GET /api/foods?categoryId=1
    //
    // GET /api/foods?includeInactive=true
    // ========================================

    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? categoryId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        if (categoryId.HasValue)
        {
            var foods = await _foodService
                .GetFoodsByCategoryIdAsync(categoryId.Value, includeInactive, ct);

            return Ok(foods);
        }

        var result = await _foodService
            .GetAllFoodsAsync(includeInactive, ct);

        return Ok(result);
    }


    // ========================================
    // GET /api/foods/5
    // ========================================

    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct = default)
    {
        var food = await _foodService
            .GetFoodByIdAsync(id, ct);

        if (food == null)
        {
            return NotFound(new
            {
                message = "Food not found"
            });
        }

        return Ok(food);
    }


    // ========================================
    // POST /api/foods
    // ========================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateFoodRequest request,
        CancellationToken ct = default)
    {
        // Danh mục không tồn tại ⇒ KeyNotFoundException ⇒ 404; trùng tên ⇒ 409.
        var food = await _foodService
            .AddFoodAsync(request, ct);

        return CreatedAtAction(
            nameof(GetById),
            new
            {
                id = food.FoodId
            },
            food
        );
    }


    // ========================================
    // PUT /api/foods/5
    // ========================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateFoodRequest request,
        CancellationToken ct = default)
    {
        var food = await _foodService
            .UpdateFoodAsync(id, request, ct);

        if (food == null)
        {
            return NotFound(new
            {
                message = "Food not found"
            });
        }

        return Ok(food);
    }


    // ========================================
    // PATCH /api/foods/5/availability
    // ========================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("{id:int}/availability")]
    public async Task<IActionResult> UpdateAvailability(
        int id,
        [FromBody] UpdateFoodAvailabilityRequest request,
        CancellationToken ct = default)
    {
        var result = await _foodService
            .UpdateAvailabilityAsync(
                id,
                request.IsAvailable,
                ct
            );

        if (!result)
        {
            return NotFound(new
            {
                message = "Food not found"
            });
        }

        return Ok(new
        {
            message = "Food availability updated successfully"
        });
    }


    // ========================================
    // PATCH /api/foods/5/active
    //
    // Kích hoạt lại (hoặc ngừng dùng) món — GĐ6/A4.
    // Khác /availability (IsAvailable).
    // ========================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> UpdateActive(
        int id,
        [FromBody] UpdateFoodActiveRequest request,
        CancellationToken ct = default)
    {
        var result = await _foodService
            .UpdateActiveAsync(
                id,
                request.IsActive,
                ct
            );

        if (!result)
        {
            return NotFound(new
            {
                message = "Food not found"
            });
        }

        return Ok(new
        {
            message = "Food active status updated successfully"
        });
    }


    // ========================================
    // DELETE /api/foods/5
    // ========================================

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var result = await _foodService
            .DeleteFoodAsync(id, ct);

        if (!result)
        {
            return NotFound(new
            {
                message = "Food not found"
            });
        }

        return Ok(new
        {
            message = "Food deleted successfully"
        });
    }
}
