public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public int ChangedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
}