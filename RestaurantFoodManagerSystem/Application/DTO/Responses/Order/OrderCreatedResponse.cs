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
    public decimal TotalAmount { get; set; }
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
        TotalAmount = x.TotalAmount,
        CreatedBy = x.CreatedBy,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}
