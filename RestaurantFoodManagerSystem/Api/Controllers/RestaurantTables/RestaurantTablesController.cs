using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    // Bếp cũng cần đọc số bàn trên phiếu; danh sách vẫn giới hạn cho POS/thu ngân.
    [Authorize(Policy = AuthPolicies.PosKitchenRead)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct = default)
    {
        var table = await _restaurantTableService
            .GetRestaurantTableByIdAsync(id, ct);
        return table is null ? NotFound() : Ok(table);
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
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "Restaurant table could not be saved due to a data conflict" }); }

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
                return NotFound(new
                {
                    message = "Restaurant table not found"
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                message = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                message = "Restaurant table could not be saved due to a data conflict"
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
                return NotFound(new
                {
                    message = "Restaurant table not found"
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
