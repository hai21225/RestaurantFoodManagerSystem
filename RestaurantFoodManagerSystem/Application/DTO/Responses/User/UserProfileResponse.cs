/// <summary>
/// Hồ sơ của chính người dùng đang đăng nhập (<c>GET/PUT /api/users/me</c>, P4).
///
/// Không chứa mật khẩu/hash — khác với <see cref="UserDto"/> là DTO nội bộ dùng cho đăng nhập.
/// </summary>
public sealed class UserProfileResponse
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
