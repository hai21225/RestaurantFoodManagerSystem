namespace RestaurantFoodManagerSystem.Infrastructure.Auth;

/// <summary>
/// Cờ bật/tắt việc enforce phân quyền (GĐ6, lát 6b).
///
/// <para>
/// <see cref="Enforce"/> = <c>false</c> (mặc định ở dev): các attribute <c>[Authorize]</c>
/// vẫn được gắn trên controller nhưng <see cref="EnforceableAuthorizationPolicyProvider"/>
/// trả policy rỗng ⇒ request không bị chặn (giữ nguyên hành vi anonymous trước GĐ6) cho tới
/// khi frontend biết gửi <c>Authorization: Bearer</c>.
/// </para>
/// <para><c>true</c>: enforce thật (401/403). Cần cấu hình khóa ký JWT.</para>
/// </summary>
public class AuthOptions
{
    public bool Enforce { get; set; }
}
