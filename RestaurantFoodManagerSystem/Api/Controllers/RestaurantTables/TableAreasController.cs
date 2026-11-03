using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/table-areas")]
public sealed class TableAreasController : ControllerBase
{
    private readonly TableAreaService _service;

    public TableAreasController(TableAreaService service) => _service = service;

    // GET /api/table-areas
    // GET /api/table-areas?includeInactive=true
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet]
    public async Task<List<TableAreaResponse>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default) =>
        await _service.GetAllAsync(includeInactive, ct);

    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct = default)
    {
        var area = await _service.GetByIdAsync(id, ct);
        return area is null ? NotFound() : Ok(area);
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] TableAreaRequest request,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _service.CreateAsync(request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] TableAreaRequest request,
        CancellationToken ct = default)
    {
        try
        {
            return await _service.UpdateAsync(id, request, ct) ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> SetActive(
        int id,
        [FromBody] UpdateActiveRequest request,
        CancellationToken ct = default)
    {
        try
        {
            return await _service.SetActiveAsync(id, request.IsActive, ct)
                ? NoContent()
                : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default) =>
        await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
