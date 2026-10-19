using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    // Bếp cũng cần đọc chi tiết đơn để hiển thị phiếu bếp; không được sửa (các action ghi vẫn dùng policy hẹp hơn).
    [Authorize(Policy = AuthPolicies.PosKitchenRead)]
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, [FromServices] OrderService service, CancellationToken ct) => (await service.GetAsync(id, ct)) is { } x ? Ok(x) : NotFound();
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet] public async Task<IActionResult> List([FromQuery] OrderListQuery query, [FromServices] OrderService service, CancellationToken ct)
    {
        try { return Ok(await service.GetListAsync(query, ct)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPost] public async Task<IActionResult> Create(CreateOrderRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, [FromServices] OrderService service, CancellationToken ct) { try { return Ok(await service.CreateIdempotentAsync(request, idempotencyKey, ct)); } catch (KeyNotFoundException e) { return BadRequest(e.Message); } catch (ArgumentException e) { return BadRequest(e.Message); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPut("{id:int}/items")] public async Task<IActionResult> UpdateItems(int id, UpdateOrderItemsRequest request, [FromServices] OrderService service, CancellationToken ct) { try { if (!await service.UpdateItemsAsync(id, request, ct)) return NotFound(); return await service.GetAsync(id, ct) is { } detail ? Ok(detail) : NotFound(); } catch (KeyNotFoundException e) { return BadRequest(e.Message); } catch (ArgumentException e) { return BadRequest(e.Message); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("{id:int}/history")] public async Task<IActionResult> History(int id, [FromServices] OrderService service, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)) is { } x ? Ok(x) : NotFound();
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/status")] public async Task<IActionResult> Status(int id, UpdateOrderStatusRequest request, [FromServices] OrderService service, CancellationToken ct) { try { return await service.UpdateStatusAsync(id, request, ct) ? NoContent() : NotFound(); } catch (ArgumentException e) { return BadRequest(e.Message); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/promotion/{promotionId:int}")] public async Task<IActionResult> Promotion(int id, int promotionId, [FromServices] OrderService service, CancellationToken ct) => await service.ApplyPromotionAsync(id, promotionId, ct) ? NoContent() : BadRequest("Order or promotion is invalid");
}
