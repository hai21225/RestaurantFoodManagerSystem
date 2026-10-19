using System.Text.Json.Serialization;

/// <summary>
/// Kết quả của <c>GET /api/orders/{id}</c> (Nhóm C/GĐ7).
/// Bổ sung <see cref="Items"/> so với entity <c>Order</c> thô, để hoá đơn hiển thị được
/// chi tiết từng món. Mọi field cũ của <c>Order</c> được giữ nguyên (thay đổi chỉ mang tính bổ sung).
/// </summary>
public sealed class OrderDetailResponse
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
    public List<OrderItemDetailResponse> Items { get; set; } = [];
}

/// <summary>
/// Một dòng món trong <see cref="OrderDetailResponse"/>, kèm tên món/cỡ đã join
/// để frontend không phải tra thêm (frontend đọc <c>foodName</c>, <c>sizeName</c>, <c>quantity</c>, <c>unitPrice</c>).
/// </summary>
public sealed class OrderItemDetailResponse
{
    public int Id { get; set; }
    public int SizeId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string SizeName { get; set; } = string.Empty;
    // C# dùng Quanlity theo quy ước project/DB; JSON vẫn là "quantity" cho frontend hiện tại.
    [JsonPropertyName("quantity")]
    public int Quanlity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
}
