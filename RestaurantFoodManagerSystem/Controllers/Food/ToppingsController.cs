using Microsoft.AspNetCore.Authorization;
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
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var toppings = await _toppingService
            .GetAllAsync(includeInactive, ct);

        return Ok(toppings);
    }


    // GET /api/toppings/1
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct = default)
    {
        var topping = await _toppingService.GetByIdAsync(id, ct);

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
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateToppingRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var topping = await _toppingService
                .CreateAsync(request, ct);

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
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateToppingRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var topping = await _toppingService
                .UpdateAsync(id, request, ct);

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
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("{id:int}/availability")]
    public async Task<IActionResult> UpdateAvailability(
        int id,
        [FromBody] UpdateToppingAvailabilityRequest request,
        CancellationToken ct = default)
    {
        var result = await _toppingService
            .UpdateAvailabilityAsync(
                id,
                request.IsAvailable,
                ct
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


    // PATCH /api/toppings/1/active
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> SetActive(
        int id,
        [FromBody] UpdateActiveRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _toppingService
                .SetActiveAsync(id, request.IsActive, ct);

            if (!result)
            {
                return NotFound(new
                {
                    message = "Topping not found"
                });
            }

            return Ok(new
            {
                message = "Topping active status updated successfully"
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


    // DELETE /api/toppings/1
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var result = await _toppingService.DeleteAsync(id, ct);

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