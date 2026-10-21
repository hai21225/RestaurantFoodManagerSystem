using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/payments")]
[Authorize(Policy = AuthPolicies.CashierDesk)]
public sealed class PaymentsController : ControllerBase
{
    /// <summary>
    /// Ghi nhận thanh toán. Hỗ trợ header <c>Idempotency-Key</c> (Đợt 2 / A5) để bấm
    /// "thu tiền" 2 lần chỉ thu một lần.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] PaymentService service,
        CancellationToken ct)
    {
        try
        {
            var result = await service.CreateIdempotentAsync(request, idempotencyKey, ct);
            return result is null ? NotFound() : Ok(PaymentResponse.From(result));
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    /// <summary>
    /// Tạo giao dịch MoMo cho một đơn. Trả về URL để khách trả (QR/redirect).
    /// Đơn **chưa** được đánh dấu <c>Paid</c> cho tới khi MoMo gọi IPN về.
    /// </summary>
    [HttpPost("momo")]
    public async Task<IActionResult> CreateMomo(
        CreateMomoPaymentRequest request,
        [FromServices] PaymentService service,
        CancellationToken ct)
    {
        try
        {
            var result = await service.CreateMomoIntentAsync(request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    /// <summary>
    /// IPN (callback) của MoMo. Endpoint này **không** yêu cầu token — MoMo không đăng nhập.
    /// An toàn nằm ở chữ ký HMAC + kiểm tra số tiền, không nằm ở xác thực.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("momo/notify")]
    public async Task<IActionResult> MomoNotify(
        MomoNotificationRequest request,
        [FromServices] PaymentService service,
        CancellationToken ct)
    {
        var outcome = await service.ApplyMomoNotificationAsync(request, ct);

        if (!outcome.Accepted)
        {
            // Trả 200 cho MoMo để nó không gửi lại vô hạn, nhưng body nói rõ đã từ chối.
            return Ok(new { accepted = false, message = outcome.Message });
        }

        return Ok(new
        {
            accepted = true,
            applied = outcome.WasApplied,
            paymentId = outcome.PaymentId,
            message = outcome.Message
        });
    }

    /// <summary>
    /// Giả lập kết quả thanh toán MoMo — **chỉ** hoạt động khi
    /// <c>Payments:Provider = Mock</c> (môi trường test). Không gọi ra ngoài, không tốn tiền thật.
    ///
    /// <para>Payload IPN được sinh **đã ký chữ ký đúng** nên vẫn chạy đúng đường verify thật.</para>
    /// </summary>
    [HttpPost("momo/mock/{paymentId:int}")]
    public async Task<IActionResult> MomoMock(
        int paymentId,
        [FromQuery] bool success,
        [FromServices] PaymentService service,
        CancellationToken ct)
    {
        try
        {
            var outcome = await service.SimulateMomoPaymentAsync(paymentId, success, ct);
            if (!outcome.Accepted)
            {
                return BadRequest(new { message = outcome.Message });
            }

            return Ok(new
            {
                accepted = true,
                applied = outcome.WasApplied,
                paymentId = outcome.PaymentId,
                message = outcome.Message
            });
        }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    /// <summary>Hoàn tiền một giao dịch đã thu (chỉ hoàn toàn bộ).</summary>
    [HttpPost("{paymentId:int}/refund")]
    public async Task<IActionResult> Refund(
        int paymentId,
        RefundPaymentRequest request,
        [FromServices] PaymentService service,
        CancellationToken ct)
    {
        try
        {
            var result = await service.RefundAsync(paymentId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaymentListQuery query, [FromServices] PaymentService service, CancellationToken ct)
    {
        try
        {
            return Ok(await service.GetListAsync(query, ct));
        }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [HttpGet("order/{orderId:int}")]
    public async Task<IActionResult> GetByOrder(int orderId, [FromServices] PaymentService service, CancellationToken ct) => Ok((await service.GetByOrderAsync(orderId, ct)).Select(PaymentResponse.From).ToList());
}
