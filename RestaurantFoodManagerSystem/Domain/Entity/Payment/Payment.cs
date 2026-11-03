/// <summary>
/// Một giao dịch thu tiền (Đợt 3 / MoMo).
///
/// <para>
/// Trước Đợt 3, mô hình chỉ có "thu tiền tức thì": <c>POST /api/payments</c> tạo 1 dòng
/// <c>Status = Paid</c> và đóng đơn. Mô hình mới có thêm <b>intent</b> (trạng thái
/// <c>Pending</c>) cho luồng bất đồng bộ: tạo giao dịch MoMo → khách quét QR → MoMo gọi
/// IPN về → đánh dấu <c>Paid</c>.
/// </para>
/// </summary>
public class Payment
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>Trạng thái: <c>Pending</c> / <c>Paid</c> / <c>Failed</c> / <c>Refunded</c> / <c>Cancelled</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string TransactionCode { get; set; } = string.Empty;

    /// <summary>Thời điểm thực sự thu được tiền. Null khi giao dịch còn <c>Pending</c>.</summary>
    public DateTime? PaidAt { get; set; }

    public int CreatedBy { get; set; }

    // ==================== Đợt 3 / MoMo ====================

    /// <summary>Nơi thu tiền đi qua: <c>Cash</c>/<c>Card</c>/… hoặc <c>MoMo</c>.</summary>
    public string Provider { get; set; } = PaymentProviders.Direct;

    /// <summary>Mã request gửi lên MoMo — cũng là khoá tra cứu khi IPN gọi về.</summary>
    public string? RequestId { get; set; }

    /// <summary>Mã giao dịch của MoMo (<c>transId</c>).</summary>
    public string? ProviderTransactionId { get; set; }

    /// <summary>Ghi chú (lý do thất bại / lý do hoàn tiền).</summary>
    public string? Note { get; set; }

    /// <summary>Body JSON thô do MoMo trả về — giữ lại để đối soát khi có khiếu nại.</summary>
    public string? ProviderResponse { get; set; }

    /// <summary>Thời điểm tạo bản ghi thanh toán.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Thời điểm hoàn tiền (null nếu chưa hoàn).</summary>
    public DateTime? RefundedAt { get; set; }
}
