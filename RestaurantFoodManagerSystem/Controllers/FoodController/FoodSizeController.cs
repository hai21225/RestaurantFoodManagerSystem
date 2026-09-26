using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/foods/{foodId:int}/sizes")]
public class FoodSizesController : ControllerBase
{
    private readonly FoodSizeService _foodSizeService;

    public FoodSizesController(
        FoodSizeService foodSizeService)
    {
        _foodSizeService = foodSizeService;
    }


    // GET /api/foods/5/sizes
    [HttpGet]
    public async Task<IActionResult> GetAll(int foodId)
    {
        try
        {
            var sizes = await _foodSizeService
                .GetByFoodIdAsync(foodId);

            return Ok(sizes);
        }
        catch (Exception ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }


    // GET /api/foods/5/sizes/2
    [HttpGet("{sizeId:int}")]
    public async Task<IActionResult> GetById(
        int foodId,
        int sizeId)
    {
        var size = await _foodSizeService
            .GetByIdAsync(foodId, sizeId);

        if (size == null)
        {
            return NotFound(new
            {
                message = "Food size not found"
            });
        }

        return Ok(size);
    }


    // POST /api/foods/5/sizes
    [HttpPost]
    public async Task<IActionResult> Create(
        int foodId,
        [FromBody] CreateFoodSizeRequest request)
    {
        try
        {
            var size = await _foodSizeService
                .CreateAsync(foodId, request);

            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    foodId,
                    sizeId = size.SizeId
                },
                size
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


    // PUT /api/foods/5/sizes/2
    [HttpPut("{sizeId:int}")]
    public async Task<IActionResult> Update(
        int foodId,
        int sizeId,
        [FromBody] UpdateFoodSizeRequest request)
    {
        try
        {
            var size = await _foodSizeService
                .UpdateAsync(
                    foodId,
                    sizeId,
                    request
                );

            if (size == null)
            {
                return NotFound(new
                {
                    message = "Food size not found"
                });
            }

            return Ok(size);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


    // PATCH /api/foods/5/sizes/2/default
    [HttpPatch("{sizeId:int}/default")]
    public async Task<IActionResult> SetDefault(
        int foodId,
        int sizeId)
    {
        var result = await _foodSizeService
            .SetDefaultAsync(foodId, sizeId);

        if (!result)
        {
            return NotFound(new
            {
                message = "Food size not found"
            });
        }

        return Ok(new
        {
            message = "Default size updated successfully"
        });
    }


    // DELETE /api/foods/5/sizes/2
    [HttpDelete("{sizeId:int}")]
    public async Task<IActionResult> Delete(
        int foodId,
        int sizeId)
    {
        var result = await _foodSizeService
            .DeleteAsync(foodId, sizeId);

        if (!result)
        {
            return NotFound(new
            {
                message = "Food size not found"
            });
        }

        return Ok(new
        {
            message = "Food size deleted successfully"
        });
    }
}