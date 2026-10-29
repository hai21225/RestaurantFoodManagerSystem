using System.ComponentModel.DataAnnotations;

/// <summary>
/// Tạo giao dịch thanh toán MoMo cho một đơn (<c>POST /api/payments/momo</c>).
///
/// <para>Backend tạo "intent" (bản ghi <c>Payment</c> ở trạng thái <c>Pending</c>) rồi trả
/// về URL để khách quét/thanh toán. Đơn <b>chưa</b> được đánh dấu <c>Paid</c> cho tới khi
/// MoMo gọi IPN về (hoặc ở chế độ giả lập, khi gọi endpoint giả lập).</para>
/// </summary>
public sealed class CreateMomoPaymentRequest
{
    [Range(1, int.MaxValue)] public int OrderId { get; set; }

    /// <summary>Người tạo giao dịch (thu ngân).</summary>
    [Range(1, int.MaxValue)] public int CreatedBy { get; set; }
}

/// <summary>
/// Hoàn tiền một giao dịch đã thu (<c>POST /api/payments/{paymentId}/refund</c>).
///
/// <para>Chỉ hỗ trợ hoàn <b>toàn bộ</b> (chưa cần hoàn từng phần).</para>
/// </summary>
public sealed class RefundPaymentRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Người thực hiện hoàn tiền (thu ngân).</summary>
    [Range(1, int.MaxValue)] public int CreatedBy { get; set; }
}

/// <summary>
/// Body IPN (callback) mà MoMo gửi về <c>POST /api/payments/momo/notify</c>.
///
/// <para>Đây là hợp đồng với MoMo, không phải với frontend. Tên field giữ đúng dạng
/// camelCase mà MoMo gửi lên.</para>
/// </summary>
public sealed class MomoNotificationRequest
{
    public string? PartnerCode { get; set; }
    public string? PartnerRefCode { get; set; }
    public string? Amount { get; set; }
    public string? OrderId { get; set; }
    public string? OrderInfo { get; set; }
    public string? RequestId { get; set; }
    public string? RequestType { get; set; }
    public string? NotificationUrl { get; set; }
    public string? ResultCode { get; set; }
    public string? ResponseCode { get; set; }
    public string? ExtraData { get; set; }
    public string? Message { get; set; }
    public string? TransId { get; set; }
    public string? ResponseTime { get; set; }
    public string? Signature { get; set; }

    /// <summary><c>true</c> khi MoMo báo thành công (resultCode = 9000).</summary>
    public bool IsSuccess =>
        string.Equals(ResultCode, MomoPaymentService.SuccessResultCode, StringComparison.Ordinal);
}
