/// <summary>
/// Tên policy + tên role dùng cho phân quyền (GĐ6).
///
/// Tên role trùng đúng 5 role được seed trong <c>Program.cs</c>:
/// <c>Admin</c>, <c>WarehouseManager</c>, <c>Chef</c>, <c>Cashier</c>, <c>Waiter</c>.
///
/// Ma trận policy theo thiết kế đã chốt (mục 4.3/4.4 của refactor-plan).
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
}
