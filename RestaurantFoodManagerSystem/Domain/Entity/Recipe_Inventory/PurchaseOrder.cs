public class PurchaseOrder
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime OrderedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int CreatedBy { get; set; }
}