using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/restaurant-tables")]
public class RestaurantTablesController : ControllerBase
{
    private readonly RestaurantTableService _restaurantTableService;

    public RestaurantTablesController(
        RestaurantTableService restaurantTableService)
    {
        _restaurantTableService = restaurantTableService;
    }

    // GET: /api/restaurant-tables
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct = default)
    {
        var tables = await _restaurantTableService
            .GetAllRestaurantTablesAsync(ct);

        return Ok(tables);
    }

    // GET: /api/restaurant-tables/5
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct = default)
    {
        try
        {
            var table = await _restaurantTableService
                .GetRestaurantTableByIdAsync(id, ct);
            return table is null ? NotFound() : Ok(table);
        }
        catch (Exception ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }

    // POST: /api/restaurant-tables
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] RestaurantTableDto dto,
        CancellationToken ct = default)
    {
        if (dto == null)
        {
            return BadRequest(new
            {
                message = "Invalid restaurant table data"
            });
        }

        RestaurantTableDto created;
        try { created = await _restaurantTableService.AddRestaurantTableAsync(dto, ct); }
        catch (ConcurrencyConflictException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }

        return Ok(new
        {
            message = "Restaurant table created successfully", data = created
        });
    }

    // PUT: /api/restaurant-tables/5
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] RestaurantTableDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _restaurantTableService
                .UpdateRestaurantTableAsync(id, dto, ct);

            if (!result)
            {
                return BadRequest(new
                {
                    message = "Failed to update restaurant table"
                });
            }

            return Ok(new
            {
                message = "Restaurant table updated successfully"
            });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(new
            {
                message = ex.Message
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

    // DELETE: /api/restaurant-tables/5
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        try
        {
            var result = await _restaurantTableService
                .DeleteRestaurantTableAsync(id, ct);

            if (!result)
            {
                return BadRequest(new
                {
                    message = "Failed to delete restaurant table"
                });
            }

            return Ok(new
            {
                message = "Restaurant table deleted successfully"
            });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(new
            {
                message = ex.Message
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

    // Status is calculated by the system. This endpoint only marks/unmarks maintenance.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPatch("{id:int}/maintenance")]
    public async Task<IActionResult> Maintenance(int id, [FromQuery] bool enabled, CancellationToken ct = default)
    {
        try
        {
            return await _restaurantTableService.SetMaintenanceAsync(id, enabled, ct) ? NoContent() : NotFound();
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
