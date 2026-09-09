public class PurchaseOrderItem
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int IngredientId { get; set; }
    public decimal Quanlity { get; set; } // Lưu ý: theo đúng tên cột 'quanlity' trong sơ đồ
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

}