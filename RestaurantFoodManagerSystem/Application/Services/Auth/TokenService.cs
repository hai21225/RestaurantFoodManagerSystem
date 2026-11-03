using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Sinh JWT cho luồng đăng nhập (GĐ6).
///
/// Claims theo thiết kế đã chốt (mục 4.2 của refactor-plan):
/// <c>sub</c> = <c>Users.Id</c>, <c>unique_name</c>, <c>name</c>, nhiều claim <c>role</c>,
/// <c>jti</c>, <c>iat</c>, <c>exp</c>.
///
/// Lớp này CHƯA gắn validation vào pipeline (việc đó là lát 6b); ở lát 6a nó chỉ
/// sinh token và trả về kèm trong response login nên không phá vỡ hành vi cũ.
/// </summary>
public class TokenService
{
    /// <summary>Tên claim role trong token (tên ngắn "role" theo thiết kế GĐ6).</summary>
    public const string RoleClaimName = "role";

    private readonly JwtOptions _options;
    private readonly ILogger<TokenService> _logger;

    public TokenService(JwtOptions options, ILogger<TokenService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Sinh token cho <paramref name="user"/> với danh sách <paramref name="roles"/>.
    /// Trả <c>null</c> (không ném lỗi) khi chưa cấu hình khóa ký, để luồng login
    /// vẫn chạy được ở giai đoạn frontend chưa gửi Bearer token.
    /// </summary>
    public string? CreateToken(UserDto user, IEnumerable<string>? roles)
    {
        if (string.IsNullOrWhiteSpace(_options.Key))
        {
            _logger.LogWarning(
                "JWT signing key chưa được cấu hình (Authentication:Jwt:Key); bỏ qua việc sinh token.");
            return null;
        }

        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.FullName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64),
        };

        if (roles != null)
        {
            foreach (var role in roles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // Ghi claim tên ngắn "role" (đúng thiết kế); JwtBearer inbound-map vẫn
                // dịch "role" về ClaimTypes.Role để [Authorize(Roles=...)] hoạt động.
                claims.Add(new Claim(RoleClaimName, role));
            }
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: string.IsNullOrWhiteSpace(_options.Issuer) ? null : _options.Issuer,
            audience: string.IsNullOrWhiteSpace(_options.Audience) ? null : _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(_options.EffectiveExpiryHours),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
