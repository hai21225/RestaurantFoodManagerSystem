using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace RestaurantFoodManagerSystem.Infrastructure.Auth;

/// <summary>
/// Bọc <see cref="DefaultAuthorizationPolicyProvider"/> để có thể "vô hiệu hoá" toàn bộ
/// yêu cầu phân quyền khi <see cref="AuthOptions.Enforce"/> = false (lát 6b).
///
/// Khi tắt enforce, mọi policy (kể cả policy mặc định của <c>[Authorize]</c> trần) được trả về
/// dưới dạng policy rỗng không requirement ⇒ luôn cho qua, nên hành vi API không đổi so với
/// trước GĐ6. Khi bật enforce, uỷ quyền hoàn toàn cho provider mặc định.
/// </summary>
public sealed class EnforceableAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    // AuthorizationPolicy bắt buộc có ≥1 requirement, nên dùng assertion luôn đúng
    // để tạo policy "cho qua" khi tắt enforce.
    private static readonly AuthorizationPolicy DisabledPolicy =
        new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();

    private readonly DefaultAuthorizationPolicyProvider _inner;
    private readonly bool _enforce;

    public EnforceableAuthorizationPolicyProvider(
        IOptions<AuthorizationOptions> options,
        AuthOptions authOptions)
    {
        _inner = new DefaultAuthorizationPolicyProvider(options);
        _enforce = authOptions.Enforce;
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        => _enforce ? _inner.GetDefaultPolicyAsync() : Task.FromResult(DisabledPolicy);

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        => _enforce ? _inner.GetFallbackPolicyAsync() : Task.FromResult<AuthorizationPolicy?>(DisabledPolicy);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        => _enforce ? _inner.GetPolicyAsync(policyName) : Task.FromResult<AuthorizationPolicy?>(DisabledPolicy);
}
