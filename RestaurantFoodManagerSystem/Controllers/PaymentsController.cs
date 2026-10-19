using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/payments")]
[Authorize(Policy = AuthPolicies.CashierDesk)]
public sealed class PaymentsController : ControllerBase
{
    [HttpPost] public async Task<IActionResult> Create(CreatePaymentRequest request, [FromServices] PaymentService service, CancellationToken ct) { try { var result = await service.CreateAsync(request, ct); return result is null ? NotFound() : Ok(PaymentResponse.From(result)); } catch (ArgumentException e) { return BadRequest(new { message = e.Message }); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [HttpGet] public async Task<IActionResult> List([FromQuery] PaymentListQuery query, [FromServices] PaymentService service, CancellationToken ct)
    {
        try { return Ok(await service.GetListAsync(query, ct)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [HttpGet("order/{orderId:int}")] public async Task<IActionResult> GetByOrder(int orderId, [FromServices] PaymentService service, CancellationToken ct) => Ok((await service.GetByOrderAsync(orderId, ct)).Select(PaymentResponse.From).ToList());
}
