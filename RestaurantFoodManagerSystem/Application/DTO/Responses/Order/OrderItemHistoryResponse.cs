/// <summary>
/// Một dòng audit thay đổi trạng thái/số lượng của dòng món (C-10).
/// <c>OrderItemId</c>/<c>KitchenOrderItemId</c> có thể null nếu dòng gốc đã bị xoá bởi
/// luồng replace-all cũ; lịch sử vẫn giữ <c>OrderId</c>, trạng thái và lý do.
/// </summary>
public sealed class OrderItemHistoryResponse
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? OrderItemId { get; set; }
    public int? KitchenOrderItemId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public int ChangedBy { get; set; }
    public string? ChangedByName { get; set; }
    public string? Reason { get; set; }
    public string? ReasonCode { get; set; }
    public DateTime CreatedAt { get; set; }
}
