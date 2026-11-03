public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public int ChangedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Mã lý do (P0-12): <c>CustomerChange</c> / <c>OutOfStock</c> /
    /// <c>PhysicalShortage</c> / <c>KitchenError</c>.
    ///
    /// <para>
    /// Trước Đợt 2, cấp đơn có <c>Note</c> text tự do nhưng <b>không</b> lưu mã lý do
    /// ⇒ không phân biệt được "đơn bị huỷ vì khách đổi hay vì hết hàng", dù cấp món đã lưu.
    /// </para>
    /// </summary>
    public string? ReasonCode { get; set; }
}