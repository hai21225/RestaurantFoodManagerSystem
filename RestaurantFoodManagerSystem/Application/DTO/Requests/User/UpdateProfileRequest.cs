/// <summary>
/// Body của <c>PUT /api/users/me</c> (P4) — tự phục vụ hồ sơ.
///
/// Cố ý **không** có <c>roles</c>/<c>isActive</c>/<c>userName</c>: người dùng không thể
/// tự đổi quyền, tự khoá/mở tài khoản hay đổi tên đăng nhập.
/// </summary>
public sealed class UpdateProfileRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }

    /// <summary>Để trống ⇒ xoá số điện thoại (PUT thay thế toàn bộ hồ sơ).</summary>
    public string? Phone { get; set; }
}
