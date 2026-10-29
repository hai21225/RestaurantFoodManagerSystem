/// <summary>
/// Cấu hình cho việc sinh JWT (GĐ6).
///
/// <para>
/// <see cref="Key"/> là khóa ký HS256 và TUYỆT ĐỐI không được commit vào repo.
/// Khóa phải nạp ngoài repo, ví dụ biến môi trường <c>Authentication__Jwt__Key</c>
/// hoặc <c>dotnet user-secrets</c>. Nếu khóa trống, <see cref="TokenService"/>
/// sẽ không sinh token (trả <c>null</c>) và ghi cảnh báo — nhờ vậy luồng login
/// hiện tại không bị vỡ khi chưa cấu hình.
/// </para>
/// </summary>
public class JwtOptions
{
    public string? Key { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    /// <summary>Thời hạn token tính theo giờ. Mặc định 8 giờ (theo quyết định GĐ6).</summary>
    public int ExpiryHours { get; set; } = 8;

    /// <summary>Thời hạn hiệu lực sau khi chuẩn hoá (luôn &gt; 0).</summary>
    public int EffectiveExpiryHours => ExpiryHours > 0 ? ExpiryHours : 8;
}
