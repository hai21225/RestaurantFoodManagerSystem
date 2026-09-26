using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    [HttpPost] public async Task<IActionResult> Create(CreatePaymentRequest request, [FromServices] PaymentService service, CancellationToken ct) { try { var result = await service.CreateAsync(request, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [HttpGet("order/{orderId:int}")] public async Task<IActionResult> GetByOrder(int orderId, [FromServices] PaymentService service, CancellationToken ct) => Ok(await service.GetByOrderAsync(orderId, ct));
}
