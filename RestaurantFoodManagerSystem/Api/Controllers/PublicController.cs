using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/public")]
[AllowAnonymous]
public sealed class PublicController : ControllerBase
{
    private readonly TableAreaService _tableAreaService;
    private readonly RestaurantTableService _tableService;
    private readonly PromotionService _promotionService;

    public PublicController(
        TableAreaService tableAreaService,
        RestaurantTableService tableService,
        PromotionService promotionService)
    {
        _tableAreaService = tableAreaService;
        _tableService = tableService;
        _promotionService = promotionService;
    }

    [HttpGet("table-areas")]
    public async Task<IActionResult> GetTableAreas(CancellationToken ct = default)
    {
        return Ok(await _tableAreaService.GetAllAsync(includeInactive: false, ct));
    }

    [HttpGet("tables")]
    public async Task<IActionResult> GetTables(CancellationToken ct = default)
    {
        var tables = await _tableService.GetAllRestaurantTablesAsync(ct);
        var result = tables
            .Where(t => t.IsActive)
            .Select(t => new PublicTableDto
            {
                TableId = t.TableId,
                TableNumber = t.TableNumber,
                Capacity = t.Capacity,
                AreaId = t.AreaId,
                AreaName = t.AreaName,
                Status = t.Status
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("promotions")]
    public async Task<IActionResult> GetPromotions(CancellationToken ct = default)
    {
        return Ok(await _promotionService.GetActivePublicAsync(ct));
    }
}

public sealed class PublicTableDto
{
    public int TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int? AreaId { get; set; }
    public string AreaName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
