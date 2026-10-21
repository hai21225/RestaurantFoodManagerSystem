/// <summary>
/// Trạng thái thanh toán (Đợt 3 / MoMo).
///
/// <para>
/// Trước đây <c>Payment.Status</c> dùng chung hằng <c>OrderStatuses.Paid</c> — vừa gộp
/// 2 khái niệm khác nhau, vừa không có trạng thái cho thanh toán bất đồng bộ (MoMo:
/// tạo intent → khách quét QR → MoMo gọi IPN về).
/// </para>
/// </summary>
public static class PaymentStatuses
{
    /// <summary>Đã tạo intent, chờ khách thanh toán (chỉ có ở luồng provider như MoMo).</summary>
    public const string Pending = "Pending";

    /// <summary>Đã thu tiền thành công.</summary>
    public const string Paid = "Paid";

    /// <summary>Thanh toán thất bại (khách huỷ, hết hạn, MoMo từ chối).</summary>
    public const string Failed = "Failed";

    /// <summary>Đã hoàn tiền.</summary>
    public const string Refunded = "Refunded";

    /// <summary>Đã huỷ trước khi thu (ví dụ huỷ intent MoMo).</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>Các trạng thái mà đơn đã "chốt tiền" (chặn thu 2 lần).</summary>
    public static readonly string[] Settled = [Paid, Refunded];
}
