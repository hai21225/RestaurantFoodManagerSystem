/// <summary>
/// Tên policy + tên role dùng cho phân quyền (GĐ6).
///
/// Tên role trùng đúng 5 role được seed trong <c>Program.cs</c>:
/// <c>Admin</c>, <c>WarehouseManager</c>, <c>Chef</c>, <c>Cashier</c>, <c>Waiter</c>.
///
///
/// </summary>
public static class AuthPolicies
{
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string WarehouseManager = "WarehouseManager";
        public const string Chef = "Chef";
        public const string Cashier = "Cashier";
        public const string Waiter = "Waiter";
    }

    /// <summary>Mọi role hợp lệ (đã đăng nhập).</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>Chỉ Admin.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Admin + Waiter (quầy POS).</summary>
    public const string PosOperator = "PosOperator";

    /// <summary>Admin + Cashier (quầy thu ngân).</summary>
    public const string CashierDesk = "CashierDesk";

    /// <summary>Admin + Chef (quầy bếp).</summary>
    public const string KitchenDesk = "KitchenDesk";

    /// <summary>Admin + WarehouseManager (kho).</summary>
    public const string WarehouseDesk = "WarehouseDesk";

    /// <summary>Admin + Waiter + Cashier — cho endpoint đọc mà cả POS lẫn thu ngân đều cần.</summary>
    public const string PosOrCashier = "PosOrCashier";

    /// <summary>
    /// Admin + Waiter + Cashier + Chef — đọc đơn/bàn cho POS, thu ngân và bếp.
    /// Bếp cần đọc <c>GET /orders/{id}</c> và <c>GET /restaurant-tables/{id}</c> để hiển thị
    /// mã đơn + số bàn trên phiếu bếp, nhưng không được sửa đơn/bàn.
    /// </summary>
    public const string PosKitchenRead = "PosKitchenRead";

    /// <summary>
    /// Admin + Waiter + Chef — huỷ món. Bếp phải tự huỷ được món đang nấu; luật chi tiết
    /// (món đã Ready chỉ Admin) do <c>OrderCancellationPolicy</c> áp ở tầng service.
    /// </summary>
    public const string OrderCancellation = "OrderCancellation";
}
