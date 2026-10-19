/// <summary>
/// Kết quả của <c>GET /api/management/kitchen/orders</c> (C4).
/// Giữ nguyên tên field của entity <see cref="KitchenOrder"/> để frontend hiện tại
/// (kitchen.js) không phải sửa.
/// </summary>
public sealed class KitchenOrderResponse
{
    public int KitchenOrderId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Priority { get; set; }

    public static KitchenOrderResponse From(KitchenOrder x) => new()
    {
        KitchenOrderId = x.KitchenOrderId,
        OrderId = x.OrderId,
        Status = x.Status,
        SentAt = x.SentAt,
        StartedAt = x.StartedAt,
        CompletedAt = x.CompletedAt,
        Priority = x.Priority
    };
}
