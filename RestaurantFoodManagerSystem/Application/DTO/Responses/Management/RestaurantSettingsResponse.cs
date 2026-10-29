/// <summary>
/// Cấu hình quán trả về cho <c>GET /api/management/settings</c> (Đợt 2 / A1).
/// </summary>
public sealed class RestaurantSettingsResponse
{
    public int Id { get; set; }
    public string RestaurantName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string Currency { get; set; } = "VND";

    /// <summary>Tỉ lệ VAT (%).</summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// <c>true</c> = giá menu đã bao gồm VAT (VAT chỉ tách ra để in, không cộng thêm vào tổng).
    /// </summary>
    public bool TaxIncluded { get; set; }

    /// <summary>Tỉ lệ phí dịch vụ (%), tính trên tiền hàng sau giảm giá.</summary>
    public decimal ServiceChargeRate { get; set; }

    public DateTime UpdatedAt { get; set; }

    public static RestaurantSettingsResponse From(RestaurantSettings x) => new()
    {
        Id = x.Id,
        RestaurantName = x.RestaurantName,
        Address = x.Address,
        Phone = x.Phone,
        TaxCode = x.TaxCode,
        Currency = x.Currency,
        TaxRate = x.TaxRate,
        TaxIncluded = x.TaxIncluded,
        ServiceChargeRate = x.ServiceChargeRate,
        UpdatedAt = x.UpdatedAt
    };
}
