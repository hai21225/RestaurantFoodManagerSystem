using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/foods/{foodId:int}/toppings")]
public class FoodToppingsController : ControllerBase
{
    private readonly FoodToppingService _foodToppingService;

    public FoodToppingsController(
        FoodToppingService foodToppingService)
    {
        _foodToppingService = foodToppingService;
    }


    // GET /api/foods/5/toppings
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet]
    public async Task<IActionResult> GetAll(int foodId, CancellationToken ct = default)
    {
        try
        {
            var toppings =
                await _foodToppingService
                    .GetToppingsByFoodIdAsync(foodId, ct);

            return Ok(toppings);
        }
        catch (Exception ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }


    // POST /api/foods/5/toppings
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost]
    public async Task<IActionResult> Assign(
        int foodId,
        [FromBody] AssignFoodToppingRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _foodToppingService
                .AssignAsync(
                    foodId,
                    request.ToppingId,
                    ct
                );

            if (!result)
            {
                return BadRequest(new
                {
                    message = "Failed to assign topping"
                });
            }

            return Ok(new
            {
                message = "Topping assigned successfully"
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


    // DELETE /api/foods/5/toppings/2
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpDelete("{toppingId:int}")]
    public async Task<IActionResult> Remove(
        int foodId,
        int toppingId,
        CancellationToken ct = default)
    {
        var result = await _foodToppingService
            .RemoveAsync(
                foodId,
                toppingId,
                ct
            );

        if (!result)
        {
            return NotFound(new
            {
                message = "Food topping not found"
            });
        }

        return Ok(new
        {
            message = "Topping removed successfully"
        });
    }
}