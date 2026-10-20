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
    // C-09: route "thay toàn bộ dòng món" (replace-all) đã bị chặn.
    //
    // Lý do bỏ:
    //   - xoá hẳn OrderItem cũ ⇒ mất OrderItemId ⇒ mất liên kết ledger theo dòng và mất
    //     dấu vết huỷ (CancelReason/CancelledAt/CancelledBy);
    //   - InventoryTransaction ghi ở cấp đơn (không có OrderItemId) ⇒ huỷ món sau đó phải
    //     ước tính lại từ công thức, dễ lệch tồn kho;
    //   - không ghi history món (C-10) cho dòng bị gỡ.
    //
    // Các thao tác mà replace-all từng gộp đã có route lệnh riêng:
    //   - gọi thêm:      POST  /api/orders/{id}/items
    //   - đổi số lượng: PATCH /api/orders/{id}/items/{itemId}/quantity
    //   - hủy 1 món:    POST  /api/orders/{id}/items/{itemId}/cancel
    //
    // Route cũ trả 410 Gone để client biết chính xác phải dùng route nào thay vì trả
    // 404 mù. Logic cũ (OrderService.UpdateItemsAsync) chỉ còn lại cho test, không được
    // expose nữa. Xoá hẳn action + service method khi không còn client nào gọi.
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPut("{id:int}/items")]
    public IActionResult UpdateItemsRetired() => StatusCode(
        StatusCodes.Status410Gone,
        new
        {
            message = "PUT /api/orders/{id}/items is retired (C-09). Use POST /api/orders/{id}/items to add items, PATCH /api/orders/{id}/items/{itemId}/quantity to change quantity, or POST /api/orders/{id}/items/{itemId}/cancel to cancel a dish."
        });
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddItems(int id, AddOrderItemsRequest request, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            if (!await service.AddItemsAsync(id, request, ct)) return NotFound();
            return await service.GetAsync(id, ct) is { } detail ? Ok(detail) : NotFound();
        }
        catch (KeyNotFoundException e) { return BadRequest(e.Message); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (InvalidOperationException e) { return Conflict(e.Message); }
    }
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{orderId:int}/items/{itemId:int}/quantity")]
    public async Task<IActionResult> AdjustQuantity(int orderId, int itemId, AdjustOrderItemQuantityRequest request, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            if (!await service.AdjustItemQuantityAsync(orderId, itemId, request, ct)) return NotFound();
            return await service.GetAsync(orderId, ct) is { } detail ? Ok(detail) : NotFound();
        }
        catch (KeyNotFoundException e) { return BadRequest(e.Message); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(e.Message); }
    }
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("{id:int}/history")] public async Task<IActionResult> History(int id, [FromServices] OrderService service, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)) is { } x ? Ok(x) : NotFound();
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("{id:int}/item-history")] public async Task<IActionResult> ItemHistory(int id, [FromServices] OrderService service, CancellationToken ct) => (await service.GetItemHistoryAsync(id, ct)) is { } x ? Ok(x) : NotFound();
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/status")] public async Task<IActionResult> Status(int id, UpdateOrderStatusRequest request, [FromServices] OrderService service, CancellationToken ct) { try { return await service.UpdateStatusAsync(id, request, ct) ? NoContent() : NotFound(); } catch (ArgumentException e) { return BadRequest(e.Message); } catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); } catch (InvalidOperationException e) { return Conflict(e.Message); } }
    [Authorize(Policy = AuthPolicies.OrderCancellation)]
    [HttpPost("{orderId:int}/items/{itemId:int}/cancel")]
    public async Task<IActionResult> CancelItem(int orderId, int itemId, CancelOrderItemRequest request, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            if (!await service.CancelItemAsync(orderId, itemId, request, ct)) return NotFound();
            return await service.GetAsync(orderId, ct) is { } detail ? Ok(detail) : NotFound();
        }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(e.Message); }
    }
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/promotion/{promotionId:int}")] public async Task<IActionResult> Promotion(int id, int promotionId, [FromServices] OrderService service, CancellationToken ct) => await service.ApplyPromotionAsync(id, promotionId, ct) ? NoContent() : BadRequest("Order or promotion is invalid");
}
