using System.ComponentModel.DataAnnotations;

/// <summary>
/// Cấu hình quán (Đợt 2 / A1) — dùng cho <c>PUT /api/management/settings</c>.
///
/// <para>
/// Giá trị mặc định khi chưa cấu hình: VAT 0%, phí dịch vụ 0%, <c>taxIncluded = true</c>
/// ⇒ hành vi tiền của hệ thống cũ (tổng = tiền hàng − giảm giá) được giữ nguyên.
/// </para>
/// </summary>
public sealed class RestaurantSettingsRequest
{
    /// <summary>Tên quán (in trên hoá đơn).</summary>
    [MaxLength(200)]
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>Địa chỉ quán (in trên hoá đơn).</summary>
    [MaxLength(500)]
    public string Address { get; set; } = string.Empty;

    /// <summary>Số điện thoại quán.</summary>
    [MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>Mã số thuế (MST).</summary>
    [MaxLength(50)]
    public string TaxCode { get; set; } = string.Empty;

    /// <summary>Đơn vị tiền tệ.</summary>
    [MaxLength(10)]
    public string Currency { get; set; } = "VND";

    /// <summary>Tỉ lệ VAT (%). Ví dụ 10 = 10%.</summary>
    [Range(0, 100)]
    public decimal TaxRate { get; set; }

    /// <summary>
    /// <c>true</c> = giá menu đã bao gồm VAT (VAT chỉ tách ra để in, không cộng thêm).
    /// <c>false</c> = giá menu chưa gồm VAT (VAT cộng thêm vào tổng).
    /// </summary>
    public bool TaxIncluded { get; set; } = true;

    /// <summary>Tỉ lệ phí dịch vụ (%), tính trên tiền hàng sau giảm giá. 0 = không thu phí.</summary>
    [Range(0, 100)]
    public decimal ServiceChargeRate { get; set; }
}
