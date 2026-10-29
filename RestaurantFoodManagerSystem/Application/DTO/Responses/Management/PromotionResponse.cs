/// <summary>
/// Kết quả của <c>GET/POST /api/management/promotions</c> (C4).
/// Giữ nguyên tên field của entity <see cref="Promotion"/>.
///
/// <para>
/// <b>Quy ước múi giờ (Đợt 2 / P0-05):</b> <c>StartAt</c>/<c>EndAt</c> trả về theo
/// <b>giờ Việt Nam (UTC+7)</b> — cùng quy ước với đầu vào, để FE gửi lại nguyên vẹn khi sửa.
/// <c>StartAtUtc</c>/<c>EndAtUtc</c> là mốc UTC thật (đúng thứ đang lưu trong DB).
/// </para>
/// </summary>
public sealed class PromotionResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }

    /// <summary>Mốc bắt đầu theo giờ Việt Nam (UTC+7).</summary>
    public DateTime StartAt { get; set; }

    /// <summary>Mốc kết thúc theo giờ Việt Nam (UTC+7).</summary>
    public DateTime EndAt { get; set; }

    /// <summary>Mốc bắt đầu UTC (đúng giá trị đang lưu trong DB).</summary>
    public DateTime StartAtUtc { get; set; }

    /// <summary>Mốc kết thúc UTC (đúng giá trị đang lưu trong DB).</summary>
    public DateTime EndAtUtc { get; set; }

    public decimal MinimumOrderAmount { get; set; }
    public decimal MaxDiscountAmount { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// C-18: các size (món cụ thể) mà khuyến mãi được áp. Rỗng = áp cho toàn bộ hoá đơn.
    /// </summary>
    public int[] SizeIds { get; set; } = [];

    /// <summary>
    /// C-18: các danh mục mà khuyến mãi được áp. Rỗng = áp cho toàn bộ hoá đơn.
    /// </summary>
    public int[] CategoryIds { get; set; } = [];

    public static PromotionResponse From(Promotion x) => From(x, null);

    /// <summary>
    /// C-18: kèm target của khuyến mãi. <paramref name="targets"/> null = khuyến mãi chưa có
    /// target nào (áp cho toàn bộ hoá đơn).
    /// </summary>
    public static PromotionResponse From(Promotion x, PromotionTargets? targets) => new()
    {
        Id = x.Id,
        Name = x.Name,
        Description = x.Description,
        DiscountType = x.DiscountType,
        DiscountValue = x.DiscountValue,
        StartAt = VietnamTime.ToVn(x.StartAt),
        EndAt = VietnamTime.ToVn(x.EndAt),
        StartAtUtc = x.StartAt,
        EndAtUtc = x.EndAt,
        MinimumOrderAmount = x.MinimumOrderAmount,
        MaxDiscountAmount = x.MaxDiscountAmount,
        IsActive = x.IsActive,
        SizeIds = targets?.SizeIds ?? [],
        CategoryIds = targets?.CategoryIds ?? []
    };
}

/// <summary>
/// C-18: target của một khuyến mãi — món theo size + danh mục.
/// Hai danh sách rỗng = khuyến mãi áp cho toàn bộ hoá đơn.
/// </summary>
public sealed record PromotionTargets(int[] SizeIds, int[] CategoryIds);
