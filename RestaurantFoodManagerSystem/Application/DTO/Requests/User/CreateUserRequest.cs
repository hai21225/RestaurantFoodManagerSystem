/// <summary>
/// Body của <c>POST /api/users</c> (GĐ6/A2) — khớp payload
/// <c>frontend/js/pages/employees.js</c> gửi: fullName, roleName, userName,
/// email, phone, password, confirmPassword.
/// </summary>
public sealed class CreateUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Phone { get; set; }

    /// <summary>Role gán ngay khi tạo. Để trống ⇒ tạo user chưa có role.</summary>
    public string? RoleName { get; set; }
}
