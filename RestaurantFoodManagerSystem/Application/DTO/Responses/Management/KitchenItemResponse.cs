using System.Text.Json.Serialization;

/// <summary>
/// Kết quả của <c>GET /api/management/kitchen/orders/{id}/items</c> (Nhóm C/GĐ7).
/// Giữ field <c>quanlity</c> theo quy ước của project/DB, đồng thời vẫn phát
/// alias JSON <c>quantity</c> cho frontend hiện tại.
/// </summary>
public sealed class KitchenItemResponse
{
    public int Id { get; set; }
    public int KitchenOrderId { get; set; }
    public int OrderItemId { get; set; }

    /// <summary>Tên theo quy ước project/DB.</summary>
    public int Quanlity { get; set; }

    /// <summary>Alias JSON <c>quantity</c> — frontend hiện tại đọc field này.</summary>
    [JsonPropertyName("quantity")]
    public int QuantityCompat => Quanlity;

    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string SizeName { get; set; } = string.Empty;
}
