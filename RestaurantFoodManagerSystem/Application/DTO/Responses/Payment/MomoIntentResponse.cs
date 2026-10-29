/// <summary>
/// Kết quả tạo giao dịch MoMo (<c>POST /api/payments/momo</c>).
///
/// <para>
/// <b>Chế độ giả lập (<c>isMock = true</c>):</b> <c>redirectUrl</c> trỏ về endpoint giả lập
/// của chính backend — gọi nó là đóng tiền thành công, không cần mạng, không tốn tiền thật.
/// </para>
/// </summary>
public sealed class MomoIntentResponse
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;

    /// <summary>URL khách trả tiền (trang MoMo, hoặc endpoint giả lập ở chế độ Mock).</summary>
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>URL ảnh QR (chỉ có ở chế độ MoMo thật).</summary>
    public string QrCodeUrl { get; set; } = string.Empty;

    /// <summary><c>true</c> khi đang chạy chế độ giả lập (môi trường test).</summary>
    public bool IsMock { get; set; }
}
