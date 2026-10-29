/// <summary>
/// Kết quả của <c>GET/POST /api/management/purchase-orders</c> (C4).
/// Giữ nguyên tên field của entity <see cref="PurchaseOrder"/> để frontend hiện tại
/// (purchases.js) không phải sửa.
/// </summary>
public sealed class PurchaseOrderResponse
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime OrderedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int CreatedBy { get; set; }

    public static PurchaseOrderResponse From(PurchaseOrder x) => new()
    {
        Id = x.Id,
        SupplierId = x.SupplierId,
        Status = x.Status,
        TotalAmount = x.TotalAmount,
        OrderedAt = x.OrderedAt,
        ReceivedAt = x.ReceivedAt,
        CreatedBy = x.CreatedBy
    };
}
