using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/cashier-shifts")]
[Authorize(Policy = AuthPolicies.CashierDesk)]
public sealed class CashierShiftsController : ControllerBase
{
    [HttpPost("open")]
    public async Task<IActionResult> Open(
        OpenCashierShiftRequest request,
        [FromServices] CashierShiftService service,
        CancellationToken ct) =>
        Ok(await service.OpenAsync(request, ct));

    [HttpGet("current")]
    public async Task<IActionResult> Current(
        [FromQuery] int? cashierUserId,
        [FromServices] CashierShiftService service,
        CancellationToken ct) =>
        await service.GetCurrentAsync(cashierUserId, ct) is { } x ? Ok(x) : NotFound();

    [HttpPost("{shiftId:int}/close")]
    public async Task<IActionResult> Close(
        int shiftId,
        CloseCashierShiftRequest request,
        [FromServices] CashierShiftService service,
        CancellationToken ct) =>
        await service.CloseAsync(shiftId, request, ct) is { } x ? Ok(x) : NotFound();

    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] CashierShiftService service,
        CancellationToken ct) =>
        Ok(await service.GetListAsync(ct));

    [HttpGet("{shiftId:int}")]
    public async Task<IActionResult> Get(
        int shiftId,
        [FromServices] CashierShiftService service,
        CancellationToken ct) =>
        await service.GetByIdAsync(shiftId, ct) is { } x ? Ok(x) : NotFound();
}
