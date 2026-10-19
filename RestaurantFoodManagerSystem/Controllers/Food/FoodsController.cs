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

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? categoryId,
        [FromQuery] bool includeInactive = false)
    {
        if (categoryId.HasValue)
        {
            var foods = await _foodService
                .GetFoodsByCategoryIdAsync(categoryId.Value);

            return Ok(foods);
        }

        var result = await _foodService
            .GetAllFoodsAsync(includeInactive);

        return Ok(result);
    }


    // ========================================
    // GET /api/foods/5
    // ========================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var food = await _foodService
            .GetFoodByIdAsync(id);

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

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateFoodRequest request)
    {
        try
        {
            var food = await _foodService
                .AddFoodAsync(request);

            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    id = food.FoodId
                },
                food
            );
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


    // ========================================
    // PUT /api/foods/5
    // ========================================

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateFoodRequest request)
    {
        try
        {
            var food = await _foodService
                .UpdateFoodAsync(id, request);

            if (food == null)
            {
                return NotFound(new
                {
                    message = "Food not found"
                });
            }

            return Ok(food);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


    // ========================================
    // PATCH /api/foods/5/availability
    // ========================================

    [HttpPatch("{id:int}/availability")]
    public async Task<IActionResult> UpdateAvailability(
        int id,
        [FromBody] UpdateFoodAvailabilityRequest request)
    {
        var result = await _foodService
            .UpdateAvailabilityAsync(
                id,
                request.IsAvailable
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
    // DELETE /api/foods/5
    // ========================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _foodService
            .DeleteFoodAsync(id);

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