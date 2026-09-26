using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/toppings")]
public class ToppingsController : ControllerBase
{
    private readonly ToppingService _toppingService;

    public ToppingsController(ToppingService toppingService)
    {
        _toppingService = toppingService;
    }


    // GET /api/toppings
    // GET /api/toppings?includeInactive=true
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeInactive = false)
    {
        var toppings = await _toppingService
            .GetAllAsync(includeInactive);

        return Ok(toppings);
    }


    // GET /api/toppings/1
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var topping = await _toppingService.GetByIdAsync(id);

        if (topping == null)
        {
            return NotFound(new
            {
                message = "Topping not found"
            });
        }

        return Ok(topping);
    }


    // POST /api/toppings
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateToppingRequest request)
    {
        try
        {
            var topping = await _toppingService
                .CreateAsync(request);

            return CreatedAtAction(
                nameof(GetById),
                new { id = topping.ToppingId },
                topping
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


    // PUT /api/toppings/1
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateToppingRequest request)
    {
        try
        {
            var topping = await _toppingService
                .UpdateAsync(id, request);

            if (topping == null)
            {
                return NotFound(new
                {
                    message = "Topping not found"
                });
            }

            return Ok(topping);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


    // PATCH /api/toppings/1/availability
    [HttpPatch("{id:int}/availability")]
    public async Task<IActionResult> UpdateAvailability(
        int id,
        [FromBody] UpdateToppingAvailabilityRequest request)
    {
        var result = await _toppingService
            .UpdateAvailabilityAsync(
                id,
                request.IsAvailable
            );

        if (!result)
        {
            return NotFound(new
            {
                message = "Topping not found"
            });
        }

        return Ok(new
        {
            message = "Topping availability updated successfully"
        });
    }


    // DELETE /api/toppings/1
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _toppingService.DeleteAsync(id);

        if (!result)
        {
            return NotFound(new
            {
                message = "Topping not found"
            });
        }

        return Ok(new
        {
            message = "Topping deleted successfully"
        });
    }
}