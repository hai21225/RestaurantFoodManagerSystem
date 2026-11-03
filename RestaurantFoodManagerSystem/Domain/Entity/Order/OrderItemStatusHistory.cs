/// <summary>
/// C-10: audit từng thay đổi trạng thái/số lượng của một dòng món.
///
/// <para>
/// Dòng món có thể bị xoá bởi luồng replace-all cũ (<c>PUT /orders/{id}/items</c>), nên
/// <see cref="OrderItemId"/> là nullable và không đặt FK để lịch sử không bị xoá/null theo.
/// Khi đó <see cref="OrderId"/> vẫn giữ được lịch sử ở cấp đơn.
/// </para>
/// </summary>
public class OrderItemStatusHistory
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int? OrderItemId { get; set; }

    public int? KitchenOrderItemId { get; set; }

    public string OldStatus { get; set; } = string.Empty;

    public string NewStatus { get; set; } = string.Empty;

    public int ChangedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Lý do/ghi chú, gồm cả mô tả thay đổi số lượng nếu không đổi trạng thái.</summary>
    public string? Reason { get; set; }

    /// <summary>Mã lý do C-08 (<c>CustomerChange</c>, <c>OutOfStock</c>, ...) nếu có.</summary>
    public string? ReasonCode { get; set; }
}
