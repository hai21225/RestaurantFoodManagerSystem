using System.Text.Json.Serialization;

/// <summary>
/// Kết quả <c>GET /api/management/purchase-orders/{id}</c> (Batch A).
/// Header giữ đúng field của <see cref="PurchaseOrderResponse"/> và bổ sung tên nhà cung cấp + các dòng hàng.
/// </summary>
public sealed class PurchaseOrderDetailResponse
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime OrderedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int CreatedBy { get; set; }
    public List<PurchaseOrderItemResponse> Items { get; set; } = [];
}

/// <summary>Một dòng hàng của phiếu nhập, kèm tên/đơn vị nguyên liệu để hiển thị.</summary>
public sealed class PurchaseOrderItemResponse
{
    public int Id { get; set; }
    public int IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    // C# dùng Quanlity theo quy ước project/DB; JSON là "quantity" như các request nhập kho.
    [JsonPropertyName("quantity")]
    public decimal Quanlity { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}
