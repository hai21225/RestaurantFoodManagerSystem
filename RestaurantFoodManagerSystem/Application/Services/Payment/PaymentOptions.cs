/// <summary>
/// Cấu hình thanh toán (Đợt 3 / MoMo). Đọc từ section <c>Payments</c> trong <c>appsettings.json</c>.
///
/// <para>
/// <b>Không nhúng secret vào repo.</b> Ba khoá MoMo nạp qua biến môi trường
/// (<c>Payments__MoMo__PartnerCode</c> …) hoặc user-secrets. Khi chưa có khoá ⇒
/// <see cref="Provider"/> = <c>Mock</c> ⇒ hệ thống chạy được đầy đủ mà không gọi ra ngoài.
/// </para>
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// <c>Mock</c> (môi trường test — không gọi ra ngoài) hoặc <c>MoMo</c> (thật).
    /// </summary>
    public string Provider { get; set; } = PaymentProviders.Mock;

    /// <summary>Tham số kết nối MoMo.</summary>
    public MomoOptions MoMo { get; set; } = new();

    /// <summary>
    /// Base URL để FE mở trang giả lập thanh toán (chỉ dùng ở chế độ <c>Mock</c>).
    /// Rỗng ⇒ trả về đường dẫn tương đối.
    /// </summary>
    public string MockRedirectBaseUrl { get; set; } = string.Empty;
}
