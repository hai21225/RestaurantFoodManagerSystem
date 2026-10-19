/// <summary>
/// Kết quả của <c>GET /api/management/kitchen/orders/{id}/items</c> (Nhóm C/GĐ7).
/// Giữ nguyên các field cũ của entity <c>KitchenOrderItem</c> (kể cả typo <see cref="Quanlity"/>)
/// và bổ sung <see cref="Quantity"/> đúng chính tả + tên món/cỡ đã join.
/// </summary>
public sealed class KitchenItemResponse
{
    public int Id { get; set; }
    public int KitchenOrderId { get; set; }
    public int OrderItemId { get; set; }

    /// <summary>Tên cũ (typo) — giữ để không phá vỡ hợp đồng cũ.</summary>
    public int Quanlity { get; set; }

    /// <summary>Tên đúng chính tả, frontend đọc field này.</summary>
    public int Quantity { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string SizeName { get; set; } = string.Empty;
}
