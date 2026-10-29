/// <summary>
/// Một dòng lịch sử trạng thái đơn trong <c>GET /api/orders/{id}/history</c> (P1).
/// <c>changedByName</c> là join tiện dụng từ <c>Users.UserFullName</c>; null nếu người đổi
/// không còn trong bảng Users.
/// </summary>
public sealed class OrderStatusHistoryResponse
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public int ChangedBy { get; set; }
    public string? ChangedByName { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Mã lý do của dòng lịch sử (P0-12): <c>CustomerChange</c> / <c>OutOfStock</c> /
    /// <c>PhysicalShortage</c> / <c>KitchenError</c>. Null với các dòng không phải huỷ.
    /// </summary>
    public string? ReasonCode { get; set; }

    public DateTime CreatedAt { get; set; }
}
