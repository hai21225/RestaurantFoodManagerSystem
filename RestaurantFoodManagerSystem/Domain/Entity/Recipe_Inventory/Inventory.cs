public class Inventory
{
    public int InventoryId { get; set; }
    public int IngredientId { get; set; }
    public decimal Quanlity { get; set; } // Lưu ý: tên cột trong sơ đồ là 'quality' (hoặc quantity)
    public DateTime UpdatedAt { get; set; }


}