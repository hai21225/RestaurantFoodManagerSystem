/// <summary>
/// Kết quả của <c>GET /api/management/inventory</c> (C4).
///
/// <para>
/// Giữ tên <c>quanlity</c> theo đúng quy ước hiện có của project/DB (cột
/// <c>inventory.Quanlity</c>) và frontend <c>inventory.js</c> đang đọc field này.
/// Không trả <c>RowVersion</c> vì đây là concurrency token nội bộ, không có client nào dùng.
/// </para>
/// </summary>
public sealed class InventoryResponse
{
    public int InventoryId { get; set; }
    public int IngredientId { get; set; }
    public decimal Quanlity { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static InventoryResponse From(Inventory x) => new()
    {
        InventoryId = x.InventoryId,
        IngredientId = x.IngredientId,
        Quanlity = x.Quanlity,
        UpdatedAt = x.UpdatedAt
    };
}
