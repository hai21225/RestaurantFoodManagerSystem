/// <summary>
/// Một nguyên liệu sắp hết — <c>GET /api/management/inventory/low-stock</c> (Đợt 3 / B4).
///
/// <para>
/// Trước Đợt 3, backend chỉ đếm được <c>lowStockCount</c> trên dashboard (một con số), còn
/// danh sách nguyên liệu sắp hết thì FE phải tự ghép <c>GET /management/inventory</c>
/// (tồn kho) với <c>GET /management/ingredients</c> (mức tối thiểu) — dễ lệch, dễ sót.
/// </para>
///
/// <para>
/// Giữ tên <c>quanlity</c> theo đúng quy ước project/DB (giống
/// <see cref="InventoryResponse"/>) để frontend đọc thống nhất.
/// </para>
/// </summary>
public sealed class LowStockItemResponse
{
    public int IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;

    /// <summary>Đơn vị tính của nguyên liệu (snapshot hiện tại).</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Tồn kho hiện tại.</summary>
    public decimal Quanlity { get; set; }

    /// <summary>Mức tối thiểu kho phải có.</summary>
    public decimal MinimumStock { get; set; }

    /// <summary>Còn thiếu bao nhiêu để đạt mức tối thiểu (0 nếu đã đủ).</summary>
    public decimal Shortage { get; set; }

    /// <summary>Nguyên liệu đã hết hàng hoàn toàn (tồn &lt;= 0).</summary>
    public bool IsOutOfStock { get; set; }

    /// <summary>
    /// Trạng thái đáp ứng của nguyên liệu: <c>Available</c> / <c>OutOfStock</c> / <c>Spoiled</c>
    /// (C-21). Bếp đánh dấu <c>Spoiled</c> / <c>OutOfStock</c> thì nguyên liệu xuất hiện trong
    /// danh sách này **dù tồn vẫn còn nhiều** — trước đây kho không nhìn thấy tín hiệu này.
    /// </summary>
    public string StockStatus { get; set; } = IngredientStockStatuses.Available;
}
