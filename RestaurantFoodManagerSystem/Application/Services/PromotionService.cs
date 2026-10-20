using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class PromotionService
{
    private readonly AppDbContext _db;

    public PromotionService(AppDbContext db) => _db = db;

    public Task<List<Promotion>> GetAllAsync(CancellationToken ct) =>
        _db.Promotions.AsNoTracking().OrderByDescending(x => x.StartAt).ToListAsync(ct);

    public Task<Promotion?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Promotions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Promotion> CreateAsync(PromotionRequest r, CancellationToken ct)
    {
        if (r.EndAt <= r.StartAt) throw new ArgumentException("EndAt must be after StartAt");
        var discount = ValidateDiscount(r);

        var x = new Promotion
        {
            Name = (r.Name ?? string.Empty).Trim(),
            Description = (r.Description ?? string.Empty).Trim(),
            DiscountType = discount.Type,
            DiscountValue = discount.Value,
            StartAt = r.StartAt,
            EndAt = r.EndAt,
            MinimumOrderAmount = Round(r.MinimumOrderAmount),
            MaxDiscountAmount = Round(r.MaxDiscountAmount),
            IsActive = true
        };
        _db.Promotions.Add(x);
        await _db.SaveChangesAsync(ct);
        return x;
    }

    public async Task<bool> UpdateAsync(int id, PromotionRequest r, CancellationToken ct)
    {
        var x = await _db.Promotions.FindAsync([id], ct);
        if (x is null) return false;
        if (r.EndAt <= r.StartAt) throw new ArgumentException("EndAt must be after StartAt");
        var discount = ValidateDiscount(r);

        x.Name = (r.Name ?? string.Empty).Trim();
        x.Description = (r.Description ?? string.Empty).Trim();
        x.DiscountType = discount.Type;
        x.DiscountValue = discount.Value;
        x.StartAt = r.StartAt;
        x.EndAt = r.EndAt;
        x.MinimumOrderAmount = Round(r.MinimumOrderAmount);
        x.MaxDiscountAmount = Round(r.MaxDiscountAmount);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Batch A2 (15.4/15.5): chỉ nhận <c>Percent</c>/<c>Fixed</c> và giá trị hợp lệ.
    /// Trước đây mọi giá trị khác bị hiểu nhầm là phần trăm, còn <c>EndAt</c> sai gây 500 ở controller.
    /// </summary>
    private static (string Type, decimal Value) ValidateDiscount(PromotionRequest r)
    {
        var rawType = (r.DiscountType ?? string.Empty).Trim();
        var type = rawType.Equals("Percent", StringComparison.OrdinalIgnoreCase)
            ? "Percent"
            : rawType.Equals("Fixed", StringComparison.OrdinalIgnoreCase)
                ? "Fixed"
                : throw new ArgumentException("Discount type must be Percent or Fixed");

        if (r.DiscountValue <= 0)
            throw new ArgumentException("Discount value must be greater than zero");
        if (type == "Percent" && r.DiscountValue > 100)
            throw new ArgumentException("Percent discount cannot exceed 100");

        // Cột DB là decimal(18,2) ⇒ phải tròn trước khi lưu, nếu không API trả về
        // 10.555 nhưng đơn thực tế áp dụng 10.56 ⇒ số liệu khuyến mãi lệch nhau.
        return (type, Round(r.DiscountValue));
    }

    /// <summary>Làm tròn 2 chữ số thập phân (giống cách SQL Server lưu decimal(18,2)).</summary>
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct)
    {
        var x = await _db.Promotions.FindAsync([id], ct);
        if (x is null) return false;

        x.IsActive = active;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var x = await _db.Promotions.FindAsync([id], ct);
        if (x is null) return false;

        x.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
