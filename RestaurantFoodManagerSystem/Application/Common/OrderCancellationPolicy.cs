/// <summary>
/// Luật ai được huỷ món/đơn (P1b/P1d).
///
/// <para>
/// Bếp phải tự huỷ được món đang nấu (hết nguyên liệu, khách đổi món, hỏng) vì Admin không
/// phải lúc nào cũng có mặt ở bếp. Ngược lại, sau khi món đã ra đĩa thì chỉ Admin được huỷ
/// để tránh thất thoát doanh thu.
/// </para>
///
/// <para>
/// Nếu request không xác thực (<see cref="ICurrentUser.Roles"/> rỗng) thì bỏ qua kiểm tra ở
/// tầng service và để policy của controller quyết định — giữ tương thích với các luồng nội bộ
/// và test cũ chưa gắn role.
/// </para>
/// </summary>
internal static class OrderCancellationPolicy
{
    public static bool CanCancelItem(ICurrentUser user, string? itemStatus)
    {
        if (user.Roles.Count == 0)
        {
            return true;
        }

        if (HasRole(user, AuthPolicies.Roles.Admin))
        {
            return true;
        }

        if (KitchenStatuses.IsPending(itemStatus))
        {
            return HasRole(user, AuthPolicies.Roles.Waiter) ||
                   HasRole(user, AuthPolicies.Roles.Chef);
        }

        // Đang nấu: bếp được huỷ, phục vụ phải nhờ bếp.
        if (KitchenStatuses.IsPreparing(itemStatus))
        {
            return HasRole(user, AuthPolicies.Roles.Chef);
        }

        // Ready/Completed: chỉ Admin.
        return false;
    }

    public static bool CanCancelOrder(ICurrentUser user, bool anyItemReadyOrCompleted)
    {
        if (user.Roles.Count == 0)
        {
            return true;
        }

        if (HasRole(user, AuthPolicies.Roles.Admin))
        {
            return true;
        }

        if (anyItemReadyOrCompleted)
        {
            return false;
        }

        // Chưa có món nào ra đĩa: cả phục vụ và bếp đều được huỷ đơn.
        return HasRole(user, AuthPolicies.Roles.Waiter) ||
               HasRole(user, AuthPolicies.Roles.Chef);
    }

    public static bool HasRole(ICurrentUser user, string role) =>
        user.Roles.Any(x => string.Equals(x, role, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Bếp (Chef) hoặc Admin. Dùng cho món đã bắt đầu nấu (<c>Preparing</c>): món đó bị
    /// "khóa" với POS nhưng bếp vẫn dùng được (huỷ / báo thiếu nguyên liệu).
    ///
    /// <para>
    /// Khi request chưa xác thực (<c>Roles</c> rỗng) thì cho qua — đồng bộ với
    /// <see cref="CanCancelItem"/> để các luồng nội bộ và test cũ chưa gắn role vẫn chạy được.
    /// Khi bật <c>Auth:Enforce</c> thì mọi request đều có role ⇒ khoá thật.
    /// </para>
    /// </summary>
    public static bool IsKitchenOrAdmin(ICurrentUser user) =>
        user.Roles.Count == 0
        || HasRole(user, AuthPolicies.Roles.Admin)
        || HasRole(user, AuthPolicies.Roles.Chef);
}
