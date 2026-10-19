using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class PromotionService
{
    private readonly AppDbContext _db;

    public PromotionService(AppDbContext db) => _db = db;

    public Task<List<Promotion>> GetAllAsync(CancellationToken ct) =>
        _db.Promotions.AsNoTracking().OrderByDescending(x => x.StartAt).ToListAsync(ct);

    public async Task<Promotion> CreateAsync(PromotionRequest r, CancellationToken ct)
    {
        if (r.EndAt <= r.StartAt) throw new ArgumentException("EndAt must be after StartAt");

        var x = new Promotion
        {
            Name = r.Name.Trim(),
            Description = r.Description.Trim(),
            DiscountType = r.DiscountType.Trim(),
            DiscountValue = r.DiscountValue,
            StartAt = r.StartAt,
            EndAt = r.EndAt,
            MinimumOrderAmount = r.MinimumOrderAmount,
            MaxDiscountAmount = r.MaxDiscountAmount,
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

        x.Name = r.Name.Trim();
        x.Description = r.Description.Trim();
        x.DiscountType = r.DiscountType.Trim();
        x.DiscountValue = r.DiscountValue;
        x.StartAt = r.StartAt;
        x.EndAt = r.EndAt;
        x.MinimumOrderAmount = r.MinimumOrderAmount;
        x.MaxDiscountAmount = r.MaxDiscountAmount;
        await _db.SaveChangesAsync(ct);
        return true;
    }

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
