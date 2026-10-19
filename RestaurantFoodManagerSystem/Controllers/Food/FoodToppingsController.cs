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
    [HttpGet]
    public async Task<IActionResult> GetAll(int foodId)
    {
        try
        {
            var toppings =
                await _foodToppingService
                    .GetToppingsByFoodIdAsync(foodId);

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
    [HttpPost]
    public async Task<IActionResult> Assign(
        int foodId,
        [FromBody] AssignFoodToppingRequest request)
    {
        try
        {
            var result = await _foodToppingService
                .AssignAsync(
                    foodId,
                    request.ToppingId
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
    [HttpDelete("{toppingId:int}")]
    public async Task<IActionResult> Remove(
        int foodId,
        int toppingId)
    {
        var result = await _foodToppingService
            .RemoveAsync(
                foodId,
                toppingId
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