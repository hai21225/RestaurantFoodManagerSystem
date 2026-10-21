/// <summary>
/// Kết quả thanh toán trả về cho client (P1 cho danh sách, P2 cho POST/GET theo đơn).
/// Giữ nguyên tên field của entity <see cref="Payment"/> để frontend hiện tại không phải sửa.
///
/// <para>Đợt 3 / MoMo: thêm <c>provider</c>, <c>requestId</c>, <c>providerTransactionId</c>,
/// <c>note</c>, <c>createdAt</c>, <c>refundedAt</c>; <c>paidAt</c> nay có thể <c>null</c>
/// (giao dịch MoMo còn <c>Pending</c> thì chưa thu được tiền).</para>
/// </summary>
public sealed class PaymentResponse
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TransactionCode { get; set; } = string.Empty;

    /// <summary>Thời điểm thu tiền; null nếu giao dịch chưa hoàn tất.</summary>
    public DateTime? PaidAt { get; set; }

    public int CreatedBy { get; set; }

    /// <summary>Nơi thu tiền đi qua: <c>Cash</c>, <c>MoMo</c>…</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Mã request gửi lên MoMo.</summary>
    public string? RequestId { get; set; }

    /// <summary>Mã giao dịch của MoMo.</summary>
    public string? ProviderTransactionId { get; set; }

    /// <summary>Ghi chú (lý do thất bại / lý do hoàn tiền).</summary>
    public string? Note { get; set; }

    /// <summary>Body JSON thô do MoMo trả về — dùng để đối soát khi có khiếu nại.</summary>
    public string? ProviderResponse { get; set; }

    /// <summary>Thời điểm tạo bản ghi thanh toán.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Thời điểm hoàn tiền; null nếu chưa hoàn.</summary>
    public DateTime? RefundedAt { get; set; }

    public static PaymentResponse From(Payment x) => new()
    {
        PaymentId = x.PaymentId,
        OrderId = x.OrderId,
        Amount = x.Amount,
        PaymentMethod = x.PaymentMethod,
        Status = x.Status,
        TransactionCode = x.TransactionCode,
        PaidAt = x.PaidAt,
        CreatedBy = x.CreatedBy,
        Provider = x.Provider,
        RequestId = x.RequestId,
        ProviderTransactionId = x.ProviderTransactionId,
        Note = x.Note,
        ProviderResponse = x.ProviderResponse,
        CreatedAt = x.CreatedAt,
        RefundedAt = x.RefundedAt
    };
}
