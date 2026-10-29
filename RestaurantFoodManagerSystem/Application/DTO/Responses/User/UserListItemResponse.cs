/// <summary>
/// Một dòng trong danh sách nhân viên (<c>GET /api/users</c>, GĐ6/A2).
/// Tên field khớp đúng những gì <c>frontend/js/pages/employees.js</c> đọc:
/// <c>fullName, userName, email, phone, isActive, roles[]</c>.
/// </summary>
public sealed class UserListItemResponse
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = [];
}
