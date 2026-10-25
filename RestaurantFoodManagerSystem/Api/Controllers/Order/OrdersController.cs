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
    [HttpPost] public async Task<IActionResult> Create(CreateOrderRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, [FromServices] OrderService service, CancellationToken ct) { try { return Ok(await service.CreateIdempotentAsync(request, idempotencyKey, ct)); } catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); } catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); } catch (ArgumentException e) { return BadRequest(new { message = e.Message }); } catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); } }
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
    public async Task<IActionResult> AddItems(int id, AddOrderItemsRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            return await service.AddItemsIdempotentAsync(id, request, idempotencyKey, ct) is { } detail ? Ok(detail) : NotFound();
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{orderId:int}/items/{itemId:int}/quantity")]
    public async Task<IActionResult> AdjustQuantity(int orderId, int itemId, AdjustOrderItemQuantityRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            return await service.AdjustItemQuantityIdempotentAsync(orderId, itemId, request, idempotencyKey, ct) is { } detail ? Ok(detail) : NotFound();
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id, [FromServices] OrderService service, CancellationToken ct)
    {
        return (await service.GetHistoryAsync(id, ct)) is { } x ? Ok(x) : NotFound();
    }

    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("{id:int}/item-history")]
    public async Task<IActionResult> ItemHistory(int id, [FromServices] OrderService service, CancellationToken ct)
    {
        return (await service.GetItemHistoryAsync(id, ct)) is { } x ? Ok(x) : NotFound();
    }

    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/status")] public async Task<IActionResult> Status(int id, UpdateOrderStatusRequest request, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            return await service.UpdateStatusAsync(id, request, ct) ? NoContent() : NotFound(); 
        } 
        catch (ArgumentException e) 
        { 
            return BadRequest(new { message = e.Message }); 
        } 
        catch (UnauthorizedAccessException e) 
        { 
            return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); 
        } 
        catch (InvalidOperationException e) 
        { 
            return Conflict(new { message = e.Message }); 
        } 
    }
    [Authorize(Policy = AuthPolicies.OrderCancellation)]
    [HttpPost("{orderId:int}/items/{itemId:int}/cancel")]
    public async Task<IActionResult> CancelItem(int orderId, int itemId, CancelOrderItemRequest request, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            if (!await service.CancelItemAsync(orderId, itemId, request, ct)) return NotFound();
            return await service.GetAsync(orderId, ct) is { } detail ? Ok(detail) : NotFound();
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }
    // B1: chuyển đơn sang bàn khác (khách đổi chỗ ngồi). Waiter + Admin.
    /// <summary>
    /// Đợt 3 / B2 — hoá đơn **gộp theo bàn**: gom mọi đơn đang mở của bàn thành 1 bản tổng hợp
    /// (tiền hàng, giảm giá, VAT, phí dịch vụ, tổng + danh sách món đã gộp).
    /// Trả 404 khi bàn không có đơn đang mở.
    /// </summary>
    [Authorize(Policy = AuthPolicies.PosOrCashier)]
    [HttpGet("table/{tableId:int}/bill")]
    public async Task<IActionResult> TableBill(int tableId, [FromServices] OrderService service, CancellationToken ct)
    {
        return await service.GetTableBillAsync(tableId, ct) is { } bill ? Ok(bill) : NotFound();
    }

    /// <summary>
    /// Đợt 3 / B3 — ghi chú cho **cả hoá đơn** ("xuất hoá đơn công ty ABC", "chia đôi với bàn 6"…).
    /// Gửi chuỗi rỗng để xoá ghi chú.
    /// </summary>
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/note")]
    public async Task<IActionResult> UpdateNote(int id, UpdateOrderNoteRequest request, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            return await service.UpdateNoteAsync(id, request, ct) ? NoContent() : NotFound();
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/table")]
    public async Task<IActionResult> ChangeTable(int id, ChangeOrderTableRequest request, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            return await service.ChangeTableAsync(id, request, ct) ? NoContent() : NotFound();
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpPatch("{id:int}/promotion/{promotionId:int}")]
    public async Task<IActionResult> Promotion(int id, int promotionId, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            return await service.ApplyPromotionAsync(id, promotionId, ct) ? NoContent() : BadRequest(new { message = "Order or promotion is invalid" });
        }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }
    // Đợt 2 / A2: gỡ khuyến mãi khỏi đơn (trước đây chỉ gỡ được gián tiếp khi sửa món).
    [Authorize(Policy = AuthPolicies.PosOperator)]
    [HttpDelete("{id:int}/promotion/{promotionId:int}")]
    public async Task<IActionResult> RemovePromotion(int id, int promotionId, [FromServices] OrderService service, CancellationToken ct)
    {
        try
        {
            return await service.RemovePromotionAsync(id, promotionId, ct)
                ? NoContent()
                : BadRequest(new { message = "Order or promotion is invalid" });
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }
}
