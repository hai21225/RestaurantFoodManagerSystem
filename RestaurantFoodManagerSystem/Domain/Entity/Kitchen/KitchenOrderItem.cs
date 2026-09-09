public class KitchenOrderItem
{
    public int Id { get; set; }
    public int KitchenOrderId { get; set; }
    public int OrderItemId { get; set; }
    public int Quanlity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }

}