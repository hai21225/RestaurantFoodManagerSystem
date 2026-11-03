/// <summary>
/// Body của <c>PUT /api/users/{id}</c> (Batch A — Admin quản lý nhân viên).
///
/// Cùng phạm vi với hồ sơ tự phục vụ: chỉ sửa <c>fullName/email/phone</c>.
/// Không có <c>userName</c>/<c>roles</c>/<c>isActive</c> để tránh mass assignment;
/// khoá/mở và gán quyền có endpoint riêng.
/// </summary>
public sealed class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

/// <summary>
/// Body của <c>POST /api/users/{id}/reset-password</c> (Batch A).
/// Admin đặt lại mật khẩu hộ nhân viên nên không cần mật khẩu hiện tại.
/// </summary>
public sealed class ResetUserPasswordRequest
{
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}
