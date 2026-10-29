/// <summary>
/// Trạng thái đơn hàng dùng thống nhất giữa <c>OrderService</c>, <c>KitchenService</c> và
/// <c>PaymentService</c> (Nhóm B). Trước đây mỗi nơi dùng chuỗi literal rời rạc nên dễ lệch.
///
/// <para>
/// P1d: bổ sung ma trận chuyển trạng thái. Chỉ được đi tiến theo luồng phục vụ, không được
/// quay lui (<c>Ready → Pending</c>) hay nhảy cóc (<c>Pending → Completed</c>). <c>Paid</c>
/// chỉ do luồng thanh toán đặt; <c>Paid</c>/<c>Cancelled</c> là trạng thái cuối.
/// </para>
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

    /// <summary>
    /// Các bước tiến hợp lệ. <c>Pending</c> cho phép <c>Preparing</c>/<c>Ready</c> vì đơn có thể
    /// được tạo trực tiếp ở bếp; điều cấm là quay lui và nhảy tới <c>Completed</c> khi chưa
    /// <c>Ready</c>.
    /// </summary>
    private static readonly Dictionary<string, string[]> TransitionMatrix =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Pending] = [SentToKitchen, Preparing, Ready, Cancelled],
            [SentToKitchen] = [Preparing, Ready, Cancelled],
            [Preparing] = [Ready, Completed, Cancelled],
            [Ready] = [Completed, Cancelled],
            [Completed] = [Cancelled],
        };

    public static bool IsUpdatable(string? status) =>
        status is not null &&
        Array.Exists(Updatable, s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

    /// <summary>Paid/Cancelled là trạng thái cuối — không đổi được nữa.</summary>
    public static bool IsTerminal(string? status) =>
        status is not null &&
        (string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>true</c> nếu được phép chuyển từ <paramref name="from"/> sang <paramref name="to"/>.
    /// Giữ nguyên trạng thái (no-op) cũng được coi là hợp lệ để thao tác lặp lại không lỗi.
    /// </summary>
    public static bool CanTransition(string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return false;
        }

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TransitionMatrix.TryGetValue(from, out var allowed) &&
               Array.Exists(allowed, s => string.Equals(s, to, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Đơn được phép thu tiền: bếp đã xong (Ready/Completed).</summary>
    public static bool IsPayable(string? status) =>
        status is not null &&
        (string.Equals(status, Ready, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase));

    /// <summary>Huỷ đơn sau khi món đã ra đĩa/xong chỉ Admin được phép.</summary>
    public static bool RequiresAdminToCancel(string? status) =>
        status is not null &&
        (string.Equals(status, Ready, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase));
}
