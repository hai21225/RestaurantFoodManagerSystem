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

    [HttpGet("tables/resolve-qr")]
    public async Task<IActionResult> ResolveQr([FromQuery] string? qrToken, CancellationToken ct = default)
    {
        var token = qrToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { message = "Vui lòng nhập mã QR bàn." });
        }

        var tables = await _tableService.GetAllRestaurantTablesAsync(ct);
        var match = tables.FirstOrDefault(t =>
            t.IsActive
            && !string.Equals(t.Status, RestaurantTableStatuses.Maintenance, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.QrToken, token, StringComparison.Ordinal));

        if (match is null)
        {
            return NotFound(new { message = "Mã QR bàn không hợp lệ hoặc bàn đang tạm ngừng phục vụ." });
        }

        return Ok(new PublicTableDto
        {
            TableId = match.TableId,
            TableNumber = match.TableNumber,
            Capacity = match.Capacity,
            AreaId = match.AreaId,
            AreaName = match.AreaName,
            Status = match.Status
        });
    }

    [HttpGet("promotions")]
    public async Task<IActionResult> GetPromotions(CancellationToken ct = default)
    {
        var promotions = await _promotionService.GetActivePublicAsync(ct);
        var targets = await _promotionService.GetTargetsAsync(
            promotions.Select(x => x.Id).ToList(), ct);
        var result = promotions
            .Select(x => PromotionResponse.From(x, targets.GetValueOrDefault(x.Id)))
            .ToList();
        return Ok(result);
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
