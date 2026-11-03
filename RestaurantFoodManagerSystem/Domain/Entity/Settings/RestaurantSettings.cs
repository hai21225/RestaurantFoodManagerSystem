/// <summary>
/// Cấu hình chung của quán (Đợt 2 / A1).
///
/// <para>
/// Bảng này chỉ chứa <b>một</b> bản ghi duy nhất (<see cref="SingletonId"/>) vì các giá trị
/// ở đây là cấu hình của cả quán, không phải của từng đơn: tỉ lệ VAT, tỉ lệ phí dịch vụ,
/// tên quán, MST...
/// </para>
///
/// <para>
/// Trước Đợt 2, <c>Order.TaxAmount</c> <b>luôn bằng 0</c> (không có câu lệnh gán nào trong
/// toàn repo) và <c>TotalAmount</c> bị tính ở 6 chỗ với 3 công thức khác nhau. Bảng này là
/// nơi duy nhất để cấu hình, và <see cref="OrderTotalsCalculator"/> là nơi duy nhất để tính.
/// </para>
/// </summary>
public class RestaurantSettings
{
    /// <summary>Khóa cố định: bảng <c>restaurant_settings</c> chỉ có một bản ghi.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Tên quán (in trên hoá đơn).</summary>
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>Địa chỉ quán (in trên hoá đơn).</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Số điện thoại quán.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Mã số thuế (MST) in trên hoá đơn.</summary>
    public string TaxCode { get; set; } = string.Empty;

    /// <summary>Đơn vị tiền tệ (mặc định VND).</summary>
    public string Currency { get; set; } = "VND";

    /// <summary>
    /// Tỉ lệ VAT (%). Ví dụ <c>10</c> = 10%.
    /// </summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// <c>true</c> = giá trong menu <b>đã bao gồm</b> VAT (thường gặp ở Việt Nam).
    /// Khi đó VAT chỉ được <b>tách ra</b> để in hoá đơn, <b>không</b> cộng thêm vào tổng tiền.
    /// <c>false</c> = giá menu chưa gồm VAT, VAT được cộng thêm vào tổng.
    /// </summary>
    public bool TaxIncluded { get; set; } = true;

    /// <summary>
    /// Tỉ lệ phí dịch vụ (%), tính trên <b>tiền hàng sau giảm giá</b>.
    /// Đặt <c>0</c> để tắt phí dịch vụ.
    /// </summary>
    public decimal ServiceChargeRate { get; set; }

    public DateTime UpdatedAt { get; set; }
}
