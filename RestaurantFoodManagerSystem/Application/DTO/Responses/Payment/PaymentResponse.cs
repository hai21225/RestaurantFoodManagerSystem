/// <summary>
/// Kết quả thanh toán trả về cho client (P1 cho danh sách, P2 cho POST/GET theo đơn).
/// Giữ nguyên tên field của entity <see cref="Payment"/> để frontend hiện tại không phải sửa.
/// </summary>
public sealed class PaymentResponse
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TransactionCode { get; set; } = string.Empty;
    public DateTime PaidAt { get; set; }
    public int CreatedBy { get; set; }

    public static PaymentResponse From(Payment x) => new()
    {
        PaymentId = x.PaymentId,
        OrderId = x.OrderId,
        Amount = x.Amount,
        PaymentMethod = x.PaymentMethod,
        Status = x.Status,
        TransactionCode = x.TransactionCode,
        PaidAt = x.PaidAt,
        CreatedBy = x.CreatedBy
    };
}
