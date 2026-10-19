/// <summary>
/// Vỏ phân trang cho các endpoint danh sách MỚI (P1: <c>GET /api/orders</c>, <c>GET /api/payments</c>).
///
/// <para>
/// Chỉ áp cho endpoint mới. Các endpoint danh sách cũ (foods, inventory, purchase-orders…)
/// vẫn trả mảng trực tiếp để không phá frontend đang chạy; việc chuyển chúng sang vỏ này
/// là một bước riêng cần phối hợp frontend (xem mục P5 kế hoạch).
/// </para>
/// </summary>
public sealed class PagedResponse<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public List<T> Items { get; set; } = [];
}
