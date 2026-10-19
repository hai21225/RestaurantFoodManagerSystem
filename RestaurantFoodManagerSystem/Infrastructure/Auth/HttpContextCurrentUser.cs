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

            return int.TryParse(raw, out var id) ? id : null;
        }
    }
}
