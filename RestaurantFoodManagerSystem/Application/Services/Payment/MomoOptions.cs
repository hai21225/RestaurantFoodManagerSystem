/// <summary>
/// Tham số MoMo (Đợt 3). Nạp từ <c>Payments:MoMo</c>.
///
/// <para>
/// Ba khoá <c>PartnerCode</c> / <c>AccessKey</c> / <c>SecretKey</c> là **bí mật** ⇒ nạp qua
/// biến môi trường hoặc user-secrets, <b>không</b> commit vào repo.
/// </para>
/// </summary>
public sealed class MomoOptions
{
    /// <summary>Mã partner do MoMo cấp (ví dụ <c>MOMOBKUN202208</c>).</summary>
    public string PartnerCode { get; set; } = string.Empty;

    /// <summary>Access key — dùng trong canonical string ký HMAC.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret key — khoá HMAC-SHA256.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Tên hiển thị trên trang thanh toán MoMo.</summary>
    public string PartnerName { get; set; } = "Restaurant Food Manager";

    /// <summary>Base URL của MoMo Payment Gateway.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.momo.vn/v2/gateway/api";

    /// <summary>URL MoMo chuyển khách về sau khi thanh toán xong (trang "cảm ơn" của quán).</summary>
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>URL MoMo gọi IPN về (bắt buộc, phải truy cập được từ Internet).</summary>
    public string NotificationUrl { get; set; } = string.Empty;

    /// <summary>IP của máy chạy backend (MoMo dùng để chống gian lận).</summary>
    public string IpAddress { get; set; } = "127.0.0.1";
}
