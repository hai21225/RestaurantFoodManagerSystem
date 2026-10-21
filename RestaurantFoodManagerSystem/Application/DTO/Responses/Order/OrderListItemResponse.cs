/// <summary>
/// Một dòng trong <c>GET /api/orders</c> (P1).
///
/// <para>
/// Bổ sung so với entity <see cref="Order"/>: <c>tableNumber</c> (join sẵn để danh sách hiển thị
/// tên bàn) và <c>itemCount</c> (số dòng món, phục vụ danh sách đơn chờ thanh toán).
/// Không trả <c>rowVersion</c> vì đó là concurrency token nội bộ.
/// </para>
/// </summary>
public sealed class OrderListItemResponse
{
    public int OrderId { get; set; }
    public int TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }

    /// <summary>Phí dịch vụ của đơn (Đợt 2 / A1).</summary>
    public decimal ServiceChargeAmount { get; set; }

    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }

    /// <summary>Ghi chú cho cả hoá đơn (Đợt 3 / B3); null = chưa có.</summary>
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
