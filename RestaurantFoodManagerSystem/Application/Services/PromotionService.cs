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
            // P0-05: client gửi giờ VN (không offset) ⇒ lưu UTC trong DB.
            StartAt = VietnamTime.ToUtcFromApi(r.StartAt),
            EndAt = VietnamTime.ToUtcFromApi(r.EndAt),
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
        // P0-05: client gửi giờ VN (không offset) ⇒ lưu UTC trong DB.
        x.StartAt = VietnamTime.ToUtcFromApi(r.StartAt);
        x.EndAt = VietnamTime.ToUtcFromApi(r.EndAt);
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

    // ==========================================================
    // C-18: TARGET (món / danh mục) của khuyến mãi
    // ==========================================================

    /// <summary>
    /// C-18: đọc target của nhiều khuyến mãi trong **2** truy vấn (không N+1).
    /// Khuyến mãi không có target ⇒ không có entry trong kết quả (áp cho toàn bộ hoá đơn).
    /// </summary>
    public async Task<Dictionary<int, PromotionTargets>> GetTargetsAsync(
        IReadOnlyCollection<int> promotionIds,
        CancellationToken ct)
    {
        var result = new Dictionary<int, PromotionTargets>();
        if (promotionIds.Count == 0)
        {
            return result;
        }

        var ids = promotionIds.ToList();

        var sizeRows = await _db.PromotionFoods
            .AsNoTracking()
            .Where(x => ids.Contains(x.PromotionId))
            .Select(x => new { x.PromotionId, x.SizeId })
            .ToListAsync(ct);

        var categoryRows = await _db.PromotionCategories
            .AsNoTracking()
            .Where(x => ids.Contains(x.PromotionId))
            .Select(x => new { x.PromotionId, x.CategoryId })
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            result[id] = new PromotionTargets(
                sizeRows.Where(x => x.PromotionId == id).Select(x => x.SizeId).Distinct().OrderBy(x => x).ToArray(),
                categoryRows.Where(x => x.PromotionId == id).Select(x => x.CategoryId).Distinct().OrderBy(x => x).ToArray());
        }

        return result;
    }

    /// <summary>
    /// C-18: **thay toàn bộ** target của khuyến mãi (idempotent).
    ///
    /// <para>
    /// Gửi danh sách rỗng ⇒ bỏ giới hạn ⇒ khuyến mãi áp cho toàn bộ hoá đơn
    /// (đúng hành vi khi chưa có target nào).
    /// </para>
    ///
    /// <para>
    /// Ghi trong transaction <see cref="System.Data.IsolationLevel.Serializable"/>: đọc–xoá–ghi
    /// phải nguyên tử, nếu không sẽ có lúc khuyến mãi mất hết target (⇒ âm thầm đổi nghĩa
    /// khuyến mãi) hoặc ghi trùng.
    /// </para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">Target không tồn tại (size/danh mục).</exception>
    public async Task<bool> SetTargetsAsync(
        int promotionId,
        PromotionTargetsRequest r,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        var promotion = await _db.Promotions.FindAsync([promotionId], ct);
        if (promotion is null) return false;

        var sizeIds = (r.SizeIds ?? []).Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        var categoryIds = (r.CategoryIds ?? []).Where(x => x > 0).Distinct().OrderBy(x => x).ToList();

        // Kiểm tra target có thật không — nếu không, để FK ném lỗi DB ⇒ 500.
        if (sizeIds.Count > 0)
        {
            var existing = await _db.FoodSizes
                .AsNoTracking()
                .Where(x => sizeIds.Contains(x.SizeId))
                .Select(x => x.SizeId)
                .ToListAsync(ct);

            var missing = sizeIds.Except(existing).ToList();
            if (missing.Count > 0)
            {
                throw new KeyNotFoundException(
                    $"Food size not found: {string.Join(", ", missing)}");
            }
        }

        if (categoryIds.Count > 0)
        {
            var existing = await _db.Categories
                .AsNoTracking()
                .Where(x => categoryIds.Contains(x.CategoryId))
                .Select(x => x.CategoryId)
                .ToListAsync(ct);

            var missing = categoryIds.Except(existing).ToList();
            if (missing.Count > 0)
            {
                throw new KeyNotFoundException(
                    $"Category not found: {string.Join(", ", missing)}");
            }
        }

        var currentFoods = await _db.PromotionFoods
            .Where(x => x.PromotionId == promotionId)
            .ToListAsync(ct);
        var currentCategories = await _db.PromotionCategories
            .Where(x => x.PromotionId == promotionId)
            .ToListAsync(ct);

        _db.PromotionFoods.RemoveRange(currentFoods);
        _db.PromotionCategories.RemoveRange(currentCategories);

        foreach (var sizeId in sizeIds)
        {
            _db.PromotionFoods.Add(new PromotionFood
            {
                PromotionId = promotionId,
                SizeId = sizeId
            });
        }

        foreach (var categoryId in categoryIds)
        {
            _db.PromotionCategories.Add(new PromotionCategory
            {
                PromotionId = promotionId,
                CategoryId = categoryId
            });
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
