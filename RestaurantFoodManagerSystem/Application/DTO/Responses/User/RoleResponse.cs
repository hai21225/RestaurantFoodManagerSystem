/// <summary>
/// Một role trong <c>GET /api/roles</c> (P4).
///
/// Chỉ đọc: 5 role cố định được seed sẵn, ma trận quyền là policy trong code
/// (<see cref="AuthPolicies"/>) nên API không trả/nhận ma trận quyền.
/// </summary>
public sealed class RoleResponse
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string RoleDescription { get; set; } = string.Empty;
}
