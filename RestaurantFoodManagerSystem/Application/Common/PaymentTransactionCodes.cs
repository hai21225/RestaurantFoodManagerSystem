/// <summary>
/// C-19 (gap-review C5): mã giao dịch của khoản thu.
///
/// <para>
/// Trước đây <c>Payment.TransactionCode</c> lấy thẳng giá trị client gửi, thiếu thì để
/// <c>""</c> ⇒ nhiều dòng trùng nhau, mà index thì **không** unique ⇒ không phát hiện
/// được thu trùng (thu 2 lần cùng một mã ngân hàng).
/// </para>
///
/// <para>
/// Nay: client không gửi thì **server sinh mã** ⇒ mọi dòng đều có mã riêng và index
/// unique là đủ để chặn trùng.
/// </para>
/// </summary>
public static class PaymentTransactionCodes
{
    /// <summary>Tiền tố mã giao dịch nội bộ do server sinh.</summary>
    public const string GeneratedPrefix = "PAY";

    /// <summary>
    /// Sinh mã giao dịch nội bộ: <c>PAY{yyyyMMddHHmmss}-{8 ký tự hex}</c> (26 ký tự).
    ///
    /// <para>
    /// 8 ký tự hex = 4 tỉ tổ hợp ⇒ va chạm gần như không thể; unique index ở tầng DB là
    /// chốt chặn cuối nếu vẫn trùng.
    /// </para>
    /// </summary>
    public static string Generate(DateTime nowUtc)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{GeneratedPrefix}{nowUtc:yyyyMMddHHmmss}-{suffix}";
    }

    /// <summary>
    /// Chuẩn hoá mã do client gửi: bỏ khoảng trắng thừa; rỗng ⇒ <c>null</c> (để caller
    /// biết phải sinh mã thay vì dùng chuỗi rỗng).
    /// </summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return code.Trim();
    }
}
