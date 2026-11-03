/// <summary>
/// Trạng thái phiếu bếp / món trong bếp và quy tắc gộp trạng thái (P1c, P1d).
///
/// <para>
/// P1c: trước đây món <c>Cancelled</c> vẫn bị tính khi gộp trạng thái phiếu, nên khi mọi món
/// bị huỷ phiếu bị kẹt ở <c>Preparing</c>/<c>Pending</c>. Nay món đã huỷ bị loại khỏi phép gộp;
/// nếu không còn món hoạt động nào thì phiếu chuyển <c>Cancelled</c>.
/// </para>
/// <para>
/// P1d: chỉ cho đi tiến trong thao tác thường; không cho quay lui bằng
/// <c>PATCH status</c>. C-06 có command reopen riêng cho món đã xong trên phiếu chưa đóng.
/// </para>
/// </summary>
public static class KitchenStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    /// <summary>
    /// C-11: <c>Accepted</c> chỉ là trạng thái của phiếu bếp. Ở cấp món, bếp dùng
    /// <c>Pending → Preparing → Ready/Completed</c>; nhận phiếu không làm từng món đổi trạng thái.
    /// </summary>
    public static readonly string[] AllowedTicket =
        [Pending, Accepted, Preparing, Ready, Completed, Cancelled];

    /// <summary>C-11: trạng thái hợp lệ cho một món bếp (không có <c>Accepted</c>).</summary>
    public static readonly string[] AllowedItem =
        [Pending, Preparing, Ready, Completed, Cancelled];

    public static bool IsCancelled(string? status) =>
        string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase);

    public static bool IsPending(string? status) =>
        string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase);

    public static bool IsAccepted(string? status) =>
        string.Equals(status, Accepted, StringComparison.OrdinalIgnoreCase);

    /// <summary>Món đã xong phần bếp (ra đĩa hoặc hoàn tất).</summary>
    public static bool IsDone(string? status) =>
        string.Equals(status, Ready, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase);

    public static bool IsPreparing(string? status) =>
        string.Equals(status, Preparing, StringComparison.OrdinalIgnoreCase);

    /// <summary>Phiếu/món đã đóng: không được đổi trạng thái nữa (Completed/Cancelled).</summary>
    public static bool IsTerminal(string? status) =>
        string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase) ||
        IsCancelled(status);

    public static bool CanTransitionOrder(string? from, string? to) =>
        CanTransition(from, to, TicketMatrix);

    public static bool CanTransitionItem(string? from, string? to) =>
        CanTransition(from, to, ItemMatrix);

    /// <summary>Tập trạng thái món → trạng thái phiếu bếp.</summary>
    public static string Aggregate(string current, IReadOnlyCollection<string> itemStatuses)
    {
        var active = itemStatuses.Where(x => !IsCancelled(x)).ToList();

        // Tất cả món đã huỷ ⇒ phiếu cũng huỷ, không treo ở trạng thái cũ.
        if (itemStatuses.Count > 0 && active.Count == 0)
        {
            return Cancelled;
        }

        if (active.Count > 0 && active.All(IsDone))
        {
            return Ready;
        }

        if (active.Any(IsPreparing))
        {
            return Preparing;
        }

        return current;
    }

    private static readonly Dictionary<string, string[]> TicketMatrix =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Pending] = [Accepted, Preparing, Ready, Completed, Cancelled],
            [Accepted] = [Preparing, Ready, Completed, Cancelled],
            [Preparing] = [Ready, Completed, Cancelled],
            [Ready] = [Completed, Cancelled],
        };

    private static readonly Dictionary<string, string[]> ItemMatrix =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Pending] = [Preparing, Ready, Completed, Cancelled],
            [Preparing] = [Ready, Completed, Cancelled],
            [Ready] = [Completed, Cancelled],
        };

    private static bool CanTransition(
        string? from,
        string? to,
        IReadOnlyDictionary<string, string[]> matrix)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return false;
        }

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return matrix.TryGetValue(from, out var allowed) &&
               Array.Exists(allowed, s => string.Equals(s, to, StringComparison.OrdinalIgnoreCase));
    }
}
