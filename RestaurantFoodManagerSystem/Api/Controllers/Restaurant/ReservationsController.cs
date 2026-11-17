using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/reservations")]
public sealed class ReservationsController : ControllerBase
{
    [Authorize(Policy = AuthPolicies.CustomerOrPos)]
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateReservationRequest request,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    [Authorize(Policy = AuthPolicies.CustomerOnly)]
    [HttpGet("my")]
    public async Task<IActionResult> My(
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        Ok(await service.GetMyAsync(ct));

    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ReservationListQuery query,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        Ok(await service.GetListAsync(query, ct));

    [Authorize(Policy = AuthPolicies.CustomerOrPos)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(
        int id,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        await service.GetByIdAsync(id, ct) is { } x ? Ok(x) : NotFound();

    [Authorize(Policy = AuthPolicies.CustomerOrPos)]
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(
        int id,
        [FromBody] CancelReservationRequest? request,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        await service.CancelAsync(id, request, ct) is { } x ? Ok(x) : NotFound();

    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(
        int id,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        await service.ConfirmAsync(id, ct) is { } x ? Ok(x) : NotFound();

    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPost("{id:int}/seat")]
    public async Task<IActionResult> Seat(
        int id,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        await service.SeatAsync(id, ct) is { } x ? Ok(x) : NotFound();

    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPost("{id:int}/no-show")]
    public async Task<IActionResult> NoShow(
        int id,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        await service.NoShowAsync(id, ct) is { } x ? Ok(x) : NotFound();

    [Authorize(Policy = AuthPolicies.CashierDesk)]
    [HttpPost("{id:int}/deposit/confirm")]
    public async Task<IActionResult> ConfirmDeposit(
        int id,
        [FromBody] ConfirmDepositRequest? request,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        await service.ConfirmDepositAsync(id, request, ct) is { } x ? Ok(x) : NotFound();

    [Authorize(Policy = AuthPolicies.CustomerOrPos)]
    [HttpPost("{id:int}/deposit/momo")]
    public async Task<IActionResult> CreateMomoDeposit(
        int id,
        [FromServices] ReservationService service,
        CancellationToken ct) =>
        await service.CreateMomoDepositAsync(id, ct) is { } x ? Ok(x) : NotFound();

    [AllowAnonymous]
    [HttpPost("deposit/momo/notify")]
    public async Task<IActionResult> MomoDepositNotify(
        MomoNotificationRequest request,
        [FromServices] ReservationService service,
        CancellationToken ct)
    {
        var outcome = await service.ApplyMomoDepositNotificationAsync(request, ct);
        if (!outcome.Accepted)
            return Ok(new { accepted = false, message = outcome.Message });

        return Ok(new
        {
            accepted = true,
            applied = outcome.WasApplied,
            reservationId = outcome.PaymentId,
            message = outcome.Message
        });
    }
}
