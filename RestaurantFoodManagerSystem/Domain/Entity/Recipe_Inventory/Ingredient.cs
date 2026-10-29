public class Ingredient
{
    public int IngredientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal MinimumStock { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Trạng thái đáp ứng: <c>Available</c> / <c>OutOfStock</c> / <c>Spoiled</c>
    /// (xem <see cref="IngredientStockStatuses"/>). Bếp báo thiếu/hư thì đổi trạng thái ở đây
    /// để kho quản lý và báo cáo; nguyên liệu vẫn được giữ, không xoá.
    /// </summary>
    public string StockStatus { get; set; } = IngredientStockStatuses.Available;

}