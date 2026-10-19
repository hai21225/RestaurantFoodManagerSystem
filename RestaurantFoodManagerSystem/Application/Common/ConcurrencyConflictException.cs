/// <summary>
/// Xung đột ghi đồng thời (Nhóm B7). Kế thừa <see cref="InvalidOperationException"/> để các
/// controller đang map <c>InvalidOperationException</c> → 409 giữ nguyên hành vi.
///
/// Phát sinh khi hai request cùng sửa một <c>Order</c>/<c>Inventory</c>/<c>RestaurantTable</c>:
/// request đến sau mang <c>RowVersion</c> cũ nên bị DB từ chối thay vì ghi đè im lặng.
/// </summary>
public sealed class ConcurrencyConflictException : InvalidOperationException
{
    public ConcurrencyConflictException()
        : base("Dữ liệu vừa bị thay đổi bởi thao tác khác. Vui lòng tải lại và thử lại.")
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
