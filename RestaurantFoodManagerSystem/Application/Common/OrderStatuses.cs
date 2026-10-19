/// <summary>
/// Trạng thái đơn hàng dùng thống nhất giữa <c>OrderService</c>, <c>KitchenService</c> và
/// <c>PaymentService</c> (Nhóm B). Trước đây mỗi nơi dùng chuỗi literal rời rạc nên dễ lệch.
/// </summary>
public static class OrderStatuses
{
    public const string Pending = "Pending";
    public const string SentToKitchen = "SentToKitchen";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Completed = "Completed";
    public const string Paid = "Paid";
    public const string Cancelled = "Cancelled";

    /// <summary>
    /// Trạng thái được phép đặt qua <c>PATCH /api/orders/{id}/status</c>.
    /// <c>Paid</c> KHÔNG nằm ở đây — chỉ luồng thanh toán mới được đặt.
    /// </summary>
    public static readonly string[] Updatable =
        [Pending, SentToKitchen, Preparing, Ready, Completed, Cancelled];

    public static bool IsUpdatable(string? status) =>
        status is not null &&
        Array.Exists(Updatable, s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

    /// <summary>Paid/Cancelled là trạng thái cuối — không đổi được nữa.</summary>
    public static bool IsTerminal(string? status) =>
        status is not null &&
        (string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase));
}
