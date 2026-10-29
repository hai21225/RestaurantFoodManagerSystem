/// <summary>
/// Body của <c>POST /api/auth/change-password</c> (P4) — tự đổi mật khẩu.
///
/// Bắt buộc nhập mật khẩu hiện tại để chặn trường hợp token bị lộ được dùng để chiếm tài khoản.
/// </summary>
public sealed class ChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmNewPassword { get; set; }
}
