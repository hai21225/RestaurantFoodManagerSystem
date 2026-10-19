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
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tables = await _restaurantTableService
            .GetAllRestaurantTablesAsync();

        return Ok(tables);
    }

    // GET: /api/restaurant-tables/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var table = await _restaurantTableService
                .GetRestaurantTableByIdAsync(id);
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
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] RestaurantTableDto dto)
    {
        if (dto == null)
        {
            return BadRequest(new
            {
                message = "Invalid restaurant table data"
            });
        }

        RestaurantTableDto created;
        try { created = await _restaurantTableService.AddRestaurantTableAsync(dto); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }

        return Ok(new
        {
            message = "Restaurant table created successfully", data = created
        });
    }

    // PUT: /api/restaurant-tables/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] RestaurantTableDto dto)
    {
        try
        {
            var result = await _restaurantTableService
                .UpdateRestaurantTableAsync(id, dto);

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
        catch (Exception ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }

    // DELETE: /api/restaurant-tables/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _restaurantTableService
                .DeleteRestaurantTableAsync(id);

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
        catch (Exception ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }

    // Status is calculated by the system. This endpoint only marks/unmarks maintenance.
    [HttpPatch("{id:int}/maintenance")]
    public async Task<IActionResult> Maintenance(int id, [FromQuery] bool enabled)
        => await _restaurantTableService.SetMaintenanceAsync(id, enabled) ? NoContent() : NotFound();
}
