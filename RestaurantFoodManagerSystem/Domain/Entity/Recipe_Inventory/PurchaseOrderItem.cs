public class PurchaseOrderItem
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int IngredientId { get; set; }
    public decimal Quanlity { get; set; } 
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

}