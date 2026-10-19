/// <summary>
/// Kết quả của <c>GET/POST /api/management/promotions</c> (C4).
/// Giữ nguyên tên field của entity <see cref="Promotion"/>.
/// </summary>
public sealed class PromotionResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public decimal MinimumOrderAmount { get; set; }
    public decimal MaxDiscountAmount { get; set; }
    public bool IsActive { get; set; }

    public static PromotionResponse From(Promotion x) => new()
    {
        Id = x.Id,
        Name = x.Name,
        Description = x.Description,
        DiscountType = x.DiscountType,
        DiscountValue = x.DiscountValue,
        StartAt = x.StartAt,
        EndAt = x.EndAt,
        MinimumOrderAmount = x.MinimumOrderAmount,
        MaxDiscountAmount = x.MaxDiscountAmount,
        IsActive = x.IsActive
    };
}
