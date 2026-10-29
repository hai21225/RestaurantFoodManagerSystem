using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Đọc/ghi cấu hình quán (Đợt 2 / A1).
///
/// <para>
/// Bảng <c>restaurant_settings</c> chỉ có một bản ghi. Nếu bản ghi đó chưa tồn tại
/// (DB cũ, hoặc test dựng schema bằng <c>EnsureCreated</c>), <see cref="GetAsync"/> trả về
/// giá trị mặc định <b>trong bộ nhớ</b> — <b>không</b> ghi DB trong đường đọc.
/// Nhờ vậy mọi luồng tính tiền luôn có cấu hình để dùng, kể cả khi admin chưa vào
/// cấu hình lần nào.
/// </para>
/// </summary>
public sealed class RestaurantSettingsService
{
    private readonly AppDbContext _db;

    public RestaurantSettingsService(AppDbContext db) => _db = db;

    /// <summary>
    /// Cấu hình hiện tại. Trả về bản ghi mặc định (không ghi DB) nếu chưa có bản ghi nào.
    /// </summary>
    public async Task<RestaurantSettings> GetAsync(CancellationToken ct = default)
    {
        var row = await _db.RestaurantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == RestaurantSettings.SingletonId, ct);

        return row is null ? Defaults() : Copy(row);
    }

    /// <summary>
    /// Lưu cấu hình. Tạo bản ghi singleton nếu chưa có.
    /// </summary>
    public async Task<RestaurantSettings> UpdateAsync(
        RestaurantSettingsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TaxRate is < 0 or > 100)
            throw new ArgumentException("TaxRate must be between 0 and 100");
        if (request.ServiceChargeRate is < 0 or > 100)
            throw new ArgumentException("ServiceChargeRate must be between 0 and 100");

        var row = await _db.RestaurantSettings
            .FirstOrDefaultAsync(x => x.Id == RestaurantSettings.SingletonId, ct);

        if (row is null)
        {
            row = new RestaurantSettings { Id = RestaurantSettings.SingletonId };
            _db.RestaurantSettings.Add(row);
        }

        row.RestaurantName = (request.RestaurantName ?? string.Empty).Trim();
        row.Address = (request.Address ?? string.Empty).Trim();
        row.Phone = (request.Phone ?? string.Empty).Trim();
        row.TaxCode = (request.TaxCode ?? string.Empty).Trim();
        row.Currency = string.IsNullOrWhiteSpace(request.Currency)
            ? "VND"
            : request.Currency.Trim();
        row.TaxRate = request.TaxRate;
        row.TaxIncluded = request.TaxIncluded;
        row.ServiceChargeRate = request.ServiceChargeRate;
        row.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>
    /// Cấu hình mặc định: không thu VAT, không thu phí dịch vụ, giá coi như đã gồm VAT.
    /// ⇒ tổng tiền = tiền hàng − giảm giá, đúng bằng hành vi của hệ thống cũ.
    /// </summary>
    private static RestaurantSettings Defaults() => new()
    {
        Id = RestaurantSettings.SingletonId,
        RestaurantName = string.Empty,
        Address = string.Empty,
        Phone = string.Empty,
        TaxCode = string.Empty,
        Currency = "VND",
        TaxRate = 0m,
        TaxIncluded = true,
        ServiceChargeRate = 0m,
        UpdatedAt = DateTime.MinValue
    };

    private static RestaurantSettings Copy(RestaurantSettings x) => new()
    {
        Id = x.Id,
        RestaurantName = x.RestaurantName,
        Address = x.Address,
        Phone = x.Phone,
        TaxCode = x.TaxCode,
        Currency = x.Currency,
        TaxRate = x.TaxRate,
        TaxIncluded = x.TaxIncluded,
        ServiceChargeRate = x.ServiceChargeRate,
        UpdatedAt = x.UpdatedAt
    };
}
