using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, [FromServices] OrderService service, CancellationToken ct) => (await service.GetAsync(id, ct)) is { } x ? Ok(x) : NotFound();
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPost] public async Task<IActionResult> Create(CreateOrderRequest request, [FromServices] OrderService service, CancellationToken ct) { try { return Ok(await service.CreateAsync(request, ct)); } catch (KeyNotFoundException e) { return BadRequest(e.Message); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/status")] public async Task<IActionResult> Status(int id, UpdateOrderStatusRequest request, [FromServices] OrderService service, CancellationToken ct) { try { return await service.UpdateStatusAsync(id, request, ct) ? NoContent() : NotFound(); } catch (ArgumentException e) { return BadRequest(e.Message); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/promotion/{promotionId:int}")] public async Task<IActionResult> Promotion(int id, int promotionId, [FromServices] OrderService service, CancellationToken ct) => await service.ApplyPromotionAsync(id, promotionId, ct) ? NoContent() : BadRequest("Order or promotion is invalid");
}
