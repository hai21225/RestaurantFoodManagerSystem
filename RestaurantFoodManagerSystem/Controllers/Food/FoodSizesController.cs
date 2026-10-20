using Microsoft.AspNetCore.Authorization;
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
    // GET /api/foods/5/sizes?includeInactive=true
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        int foodId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var sizes = await _foodSizeService
                .GetByFoodIdAsync(foodId, includeInactive, ct);

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
    // GET /api/foods/5/sizes/2?includeInactive=true
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet("{sizeId:int}")]
    public async Task<IActionResult> GetById(
        int foodId,
        int sizeId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var size = await _foodSizeService
            .GetByIdAsync(foodId, sizeId, includeInactive, ct);

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
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost]
    public async Task<IActionResult> Create(
        int foodId,
        [FromBody] CreateFoodSizeRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var size = await _foodSizeService
                .CreateAsync(foodId, request, ct);

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
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPut("{sizeId:int}")]
    public async Task<IActionResult> Update(
        int foodId,
        int sizeId,
        [FromBody] UpdateFoodSizeRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var size = await _foodSizeService
                .UpdateAsync(
                    foodId,
                    sizeId,
                    request,
                    ct
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
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("{sizeId:int}/default")]
    public async Task<IActionResult> SetDefault(
        int foodId,
        int sizeId,
        CancellationToken ct = default)
    {
        var result = await _foodSizeService
            .SetDefaultAsync(foodId, sizeId, ct);

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


    // PATCH /api/foods/5/sizes/2/active
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("{sizeId:int}/active")]
    public async Task<IActionResult> SetActive(
        int foodId,
        int sizeId,
        [FromBody] UpdateActiveRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _foodSizeService
                .SetActiveAsync(foodId, sizeId, request.IsActive, ct);

            if (!result)
            {
                return NotFound(new
                {
                    message = "Food size not found"
                });
            }

            return Ok(new
            {
                message = "Food size active status updated successfully"
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                message = ex.Message
            });
        }
    }


    // DELETE /api/foods/5/sizes/2
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpDelete("{sizeId:int}")]
    public async Task<IActionResult> Delete(
        int foodId,
        int sizeId,
        CancellationToken ct = default)
    {
        var result = await _foodSizeService
            .DeleteAsync(foodId, sizeId, ct);

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