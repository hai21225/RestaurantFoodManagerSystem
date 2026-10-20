using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace RestaurantFoodManagerSystem.Infrastructure.Auth;

/// <summary>
/// Đọc actor (<c>Users.Id</c>) từ JWT của request hiện tại (GĐ6c).
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public int? UserId
    {
        get
        {
            var principal = _accessor.HttpContext?.User;
            if (principal is null)
            {
                return null;
            }

            // TokenService ghi claim "sub"; JwtBearer inbound-map "sub" → ClaimTypes.NameIdentifier.
            var raw = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // Id 0 không phải user nào ⇒ coi như chưa xác thực.
            return int.TryParse(raw, out var id) && id > 0 ? id : null;
        }
    }

    public IReadOnlyCollection<string> Roles
    {
        get
        {
            var principal = _accessor.HttpContext?.User;
            if (principal is null)
            {
                return Array.Empty<string>();
            }

            // TokenService ghi claim "role"; JwtBearer có thể map sang ClaimTypes.Role.
            return principal.FindAll("role")
                .Concat(principal.FindAll(ClaimTypes.Role))
                .Select(x => x.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
