/// <summary>
/// Nguồn thu tiền của một giao dịch thanh toán (Đợt 3 / MoMo).
///
/// <para>
/// Tách khái niệm "provider" (nơi tiền đi qua) khỏi "phương thức" (<c>Payment.PaymentMethod</c>:
/// tiền mặt / thẻ / chuyển khoản / ví điện tử). Provider = <c>Direct</c> thì thu tiền tức thì;
/// provider = <c>MoMo</c> thì bất đồng bộ (intent → QR → IPN).
/// </para>
/// </summary>
public static class PaymentProviders
{
    /// <summary>Thu tiền trực tiếp, không qua bên thứ ba (tiền mặt, thẻ, chuyển khoản nội bộ).</summary>
    public const string Direct = "Direct";

    /// <summary>Cổng thanh toán MoMo (bất đồng bộ: intent → QR/redirect → IPN).</summary>
    public const string MoMo = "MoMo";

    /// <summary>
    /// Chế độ **giả lập** — chỉ dùng ở môi trường test, là giá trị của
    /// <c>Payments:Provider</c> (không lưu vào cột <c>payments.Provider</c>).
    /// Không gọi ra ngoài; backend tự sinh payload IPN **đã ký chữ ký đúng** nên vẫn chạy
    /// đúng đường verify thật.
    /// </summary>
    public const string Mock = "Mock";
}
