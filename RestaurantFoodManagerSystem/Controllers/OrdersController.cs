using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, [FromServices] OrderService service, CancellationToken ct) => (await service.GetAsync(id, ct)) is { } x ? Ok(x) : NotFound();
    [HttpPost] public async Task<IActionResult> Create(CreateOrderRequest request, [FromServices] OrderService service, CancellationToken ct) { try { return Ok(await service.CreateAsync(request, ct)); } catch (KeyNotFoundException e) { return BadRequest(e.Message); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [HttpPatch("{id:int}/status")] public async Task<IActionResult> Status(int id, UpdateOrderStatusRequest request, [FromServices] OrderService service, CancellationToken ct) => await service.UpdateStatusAsync(id, request, ct) ? NoContent() : NotFound();
    [HttpPatch("{id:int}/promotion/{promotionId:int}")] public async Task<IActionResult> Promotion(int id, int promotionId, [FromServices] OrderService service, CancellationToken ct) => await service.ApplyPromotionAsync(id, promotionId, ct) ? NoContent() : BadRequest("Order or promotion is invalid");
}
