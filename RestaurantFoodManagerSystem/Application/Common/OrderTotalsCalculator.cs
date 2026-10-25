/// <summary>
/// Kết quả tính tiền của một đơn (Đợt 2 / A1).
/// </summary>
/// <param name="Subtotal">Tiền hàng (chưa trừ giảm giá).</param>
/// <param name="DiscountAmount">Tiền giảm giá đã áp dụng.</param>
/// <param name="TaxAmount">
/// Số tiền VAT. Nếu cấu hình <c>TaxIncluded = true</c> thì đây là phần VAT
/// <b>đã nằm sẵn</b> trong <paramref name="Subtotal"/> (tách ra để in hoá đơn),
/// không cộng thêm. Nếu <c>TaxIncluded = false</c> thì đây là khoản <b>cộng thêm</b>.
/// </param>
/// <param name="ServiceChargeAmount">Phí dịch vụ, luôn cộng thêm vào tổng.</param>
/// <param name="TotalAmount">Tổng khách phải trả.</param>
public sealed record OrderTotals(
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal ServiceChargeAmount,
    decimal TotalAmount);

/// <summary>
/// Nguồn sự thật **duy nhất** cho công thức tiền của đơn (Đợt 2 / A1).
///
/// <para>
/// Trước Đợt 2, <c>TotalAmount</c> được tính ở 6 chỗ khác nhau với 3 công thức khác nhau
/// (thiếu cả VAT lẫn phí dịch vụ), nên đơn vừa tạo ra đã sai ngay. Mọi luồng ghi tiền
/// giờ gọi <see cref="Apply"/>.
/// </para>
///
/// <para>
/// Công thức:
/// <list type="number">
/// <item><description><c>base = Subtotal − Discount</c> (không âm).</description></item>
/// <item><description>
/// Phí dịch vụ = <c>base × ServiceChargeRate%</c> (tính trên tiền hàng <b>sau</b> giảm giá).
/// </description></item>
/// <item><description>
/// VAT: nếu <c>TaxIncluded = true</c> (giá menu đã gồm VAT) thì
/// <c>TaxAmount = base × rate / (100 + rate)</c> — chỉ tách ra để in, <b>không</b> cộng thêm.
/// Nếu <c>TaxIncluded = false</c> thì <c>TaxAmount = base × rate / 100</c> và <b>cộng thêm</b>.
/// </description></item>
/// <item><description>
/// <c>Total = base + ServiceCharge + (TaxIncluded ? 0 : TaxAmount)</c>.
/// </description></item>
/// </list>
/// </para>
/// </summary>
public static class OrderTotalsCalculator
{
    /// <summary>Tính lại tiền của đơn từ <paramref name="subtotal"/> và <paramref name="discount"/>.</summary>
    public static OrderTotals Calculate(decimal subtotal, decimal discount, RestaurantSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var safeSubtotal = Round(subtotal < 0 ? 0 : subtotal);
        var safeDiscount = Round(discount < 0 ? 0 : discount);
        if (safeDiscount > safeSubtotal)
        {
            safeDiscount = safeSubtotal;
        }

        var baseAmount = Round(safeSubtotal - safeDiscount);

        var taxRate = settings.TaxRate < 0 ? 0 : settings.TaxRate;
        var serviceRate = settings.ServiceChargeRate < 0 ? 0 : settings.ServiceChargeRate;

        var taxAmount = settings.TaxIncluded
            // Giá đã gồm VAT: VAT nằm sẵn trong base, tách ra để in hoá đơn.
            ? Round(baseAmount * taxRate / (100m + taxRate))
            // Giá chưa gồm VAT: VAT cộng thêm vào tổng.
            : Round(baseAmount * taxRate / 100m);

        var serviceChargeAmount = Round(baseAmount * serviceRate / 100m);

        var totalAmount = settings.TaxIncluded
            ? Round(baseAmount + serviceChargeAmount)
            : Round(baseAmount + taxAmount + serviceChargeAmount);

        return new OrderTotals(
            safeSubtotal,
            safeDiscount,
            taxAmount,
            serviceChargeAmount,
            totalAmount);
    }

    /// <summary>
    /// Gán lại <c>TaxAmount</c>/<c>ServiceChargeAmount</c>/<c>TotalAmount</c> cho đơn.
    /// Caller chịu trách nhiệm đặt <c>Subtotal</c> và <c>DiscountAmount</c> trước.
    /// </summary>
    public static void Apply(Order order, RestaurantSettings settings)
    {
        ArgumentNullException.ThrowIfNull(order);

        var totals = Calculate(order.Subtotal, order.DiscountAmount, settings);
        order.TaxAmount = totals.TaxAmount;
        order.ServiceChargeAmount = totals.ServiceChargeAmount;
        order.TotalAmount = totals.TotalAmount;
    }

    /// <summary>Làm tròn 2 chữ số thập phân — khớp với kiểu <c>decimal(18,2)</c> của SQL Server.</summary>
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
