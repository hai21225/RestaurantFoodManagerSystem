/// <summary>
/// Kết quả <c>POST /api/orders</c> (P2). Giữ đúng mọi field của entity <see cref="Order"/> mà
/// frontend đang đọc (<c>order.js</c> lưu <c>restaurant.lastOrder</c>, <c>payment.js</c> đọc lại
/// <c>orderId</c>), nhưng **không** trả <c>rowVersion</c> vì đó là concurrency token nội bộ.
/// </summary>
public sealed class OrderCreatedResponse
{
    public int OrderId { get; set; }
    public int TableId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }

    /// <summary>Phí dịch vụ của đơn (Đợt 2 / A1).</summary>
    public decimal ServiceChargeAmount { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>Ghi chú cho cả hoá đơn (Đợt 3 / B3); null = chưa có.</summary>
    public string? Note { get; set; }

    /// <summary>Khuyến mãi đang áp dụng cho đơn (Đợt 2 / A3); null = không có.</summary>
    public int? PromotionId { get; set; }

    /// <summary>Snapshot loại giảm giá của khuyến mãi tại lúc áp dụng (Đợt 2 / A3).</summary>
    public string? PromotionDiscountType { get; set; }

    /// <summary>Snapshot giá trị giảm giá của khuyến mãi tại lúc áp dụng (Đợt 2 / A3).</summary>
    public decimal? PromotionDiscountValue { get; set; }

    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static OrderCreatedResponse From(Order x) => new()
    {
        OrderId = x.OrderId,
        TableId = x.TableId,
        Status = x.Status,
        Subtotal = x.Subtotal,
        DiscountAmount = x.DiscountAmount,
        TaxAmount = x.TaxAmount,
        ServiceChargeAmount = x.ServiceChargeAmount,
        TotalAmount = x.TotalAmount,
        Note = x.Note,
        PromotionId = x.PromotionId,
        PromotionDiscountType = x.PromotionDiscountType,
        PromotionDiscountValue = x.PromotionDiscountValue,
        CreatedBy = x.CreatedBy,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}
